using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using IndustrialPlatform.PlatformStudio.Domain;
using NPOI.HSSF.UserModel;
using NPOI.XWPF.UserModel;

namespace IndustrialPlatform.PlatformStudio.Infrastructure.Report;

/// <summary>Converts the renderer's frozen content, never calls a dataset or bypasses owned-file confirmation.</summary>
public static class ReportExporter
{
    public static async Task<string> WriteAsync(ReportRenderedOutput rendered, string format, CancellationToken token, bool preview = false)
    {
        if (!ReportFormats.Supports(format)) throw Error("REPORT_FORMAT_UNSUPPORTED", "输出格式未登记。");
        if (format == "Pdf") return rendered.PdfPath;
        if (format == "Html") return Path.Combine(rendered.Directory, "result.html");
        var path = Path.Combine(rendered.Directory, "result" + ReportFormats.Extension(ReportFormats.ContentType(format)));
        if (format == "Mht")
        {
            var html = await File.ReadAllTextAsync(Path.Combine(rendered.Directory, "result.html"), token);
            var boundary = "report-" + Guid.NewGuid().ToString("N");
            var assets = new Dictionary<string, (string Name, string Type, string Data)>();
            html = Regex.Replace(html, "data:(image/(?:png|jpeg|gif|webp));base64,([A-Za-z0-9+/=]+)", match =>
            {
                if (!assets.TryGetValue(match.Value, out var asset)) assets[match.Value] = asset = ($"asset-{assets.Count}", match.Groups[1].Value, match.Groups[2].Value);
                return "cid:" + asset.Name;
            });
            var mht = new StringBuilder("MIME-Version: 1.0\r\nContent-Type: multipart/related; type=\"text/html\"; boundary=\"").Append(boundary).Append("\"\r\n\r\n");
            void Part(string type, string id, string base64) => mht.Append("--").Append(boundary).Append("\r\nContent-Type: ").Append(type).Append("\r\nContent-ID: <").Append(id).Append(">\r\nContent-Transfer-Encoding: base64\r\n\r\n").Append(Regex.Replace(base64, ".{1,76}", "$0\r\n"));
            Part("text/html; charset=utf-8", "report", Convert.ToBase64String(Encoding.UTF8.GetBytes(html)));
            foreach (var asset in assets.Values) Part(asset.Type, asset.Name, asset.Data);
            mht.Append("--").Append(boundary).Append("--\r\n");
            await File.WriteAllTextAsync(path, mht.ToString(), Encoding.ASCII, token);
            return path;
        }
        if (format is "Png" or "Jpeg")
        {
            var pages = rendered.Result["rasterPages"] as JsonArray ?? throw Error("REPORT_OUTPUT_INVALID", "图像页清单缺失。");
            if (pages.Count != rendered.PageCount) throw Error("REPORT_OUTPUT_INVALID", "图像页数不完整。");
            await using var output = File.Create(path);
            using var zip = new ZipArchive(output, ZipArchiveMode.Create, true);
            for (var i = 0; i < pages.Count; i++)
            {
                var item = pages[i]!.AsObject(); var file = StudioJson.RequiredString(item, "file");
                if (item["pageNo"]?.GetValue<int>() != i + 1 || file != $"page-{i + 1:D4}." + (format == "Png" ? "png" : "jpg")) throw Error("REPORT_OUTPUT_INVALID", "图像物理页顺序无效。");
                var bytes = await VerifiedAsync(Path.Combine(rendered.Directory, file), StudioJson.RequiredString(item, "sha256"), token);
                await using var entry = zip.CreateEntry(file, CompressionLevel.NoCompression).Open(); await entry.WriteAsync(bytes, token);
            }
            await using var manifest = zip.CreateEntry("pages.json").Open();
            await manifest.WriteAsync(Encoding.UTF8.GetBytes(pages.ToJsonString()), token);
            return path;
        }
        var bytesJson = await VerifiedAsync(Path.Combine(rendered.Directory, "result.export.json"), StudioJson.RequiredString(rendered.Result, "exportSha256"), token);
        var model = JsonNode.Parse(bytesJson)!.AsObject();
        if (preview)
        {
            // 连续文档/数据格式没有纸张水印，保留可读的试运行标识；正式导出不增加此行。
            const string notice = "PREVIEW / 草稿试运行（非正式归档）";
            (model["rows"] as JsonArray)?.Insert(0, new JsonArray(JsonValue.Create(notice)));
            (model["blocks"] as JsonArray)?.Insert(0, new JsonObject { ["type"] = "paragraph", ["text"] = notice, ["bold"] = true });
        }
        var rows = (model["rows"] as JsonArray ?? throw Error("REPORT_OUTPUT_INVALID", "正文数据缺失。")).OfType<JsonArray>().Select(r => r.Select(c => StudioJson.Text(c) ?? "").ToArray()).ToArray();
        var blocks = (model["blocks"] as JsonArray ?? throw Error("REPORT_OUTPUT_INVALID", "文档正文缺失。")).OfType<JsonObject>().ToArray();
        if (format is "Docx" or "Rtf" && blocks.Where(block => StudioJson.Text(block["type"]) == "table").Any(block => block["rows"]!.AsArray().OfType<JsonArray>().Any(row => row.Count > 63)))
            throw Error("REPORT_DOCUMENT_TABLE_LIMIT", "Word 文档表格最多支持 63 列，未截断内容。");
        if (format is "Xls" or "Xlsx")
        {
            var maxRows = format == "Xls" ? 65536 : 1048576; var maxColumns = format == "Xls" ? 256 : 16384;
            if (rows.Length > maxRows || rows.Any(r => r.Length > maxColumns || r.Any(c => c.Length > 32767))) throw Error("REPORT_WORKBOOK_LIMIT", "工作簿行、列或单元格超限，未截断内容。");
            await using var stream = File.Create(path);
            if (format == "Xls")
            {
                using var workbook = new HSSFWorkbook(); var sheet = workbook.CreateSheet("报告正文");
                for (var r = 0; r < rows.Length; r++) { token.ThrowIfCancellationRequested(); var row = sheet.CreateRow(r); for (var c = 0; c < rows[r].Length; c++) row.CreateCell(c).SetCellValue(rows[r][c]); }
                workbook.Write(stream, true);
            }
            else
            {
                await using var writer = await IndustrialPlatform.RuntimeSupport.Exports.StreamingXlsxWriter.CreateAsync(stream, token);
                try { foreach (var row in rows) await writer.WriteRowAsync(row, token); }
                catch (Exception error) when (error is ArgumentOutOfRangeException or System.Xml.XmlException) { throw Error("REPORT_XLSX_CELL_UNSUPPORTED", "单元格含不支持的字符，未截断内容。"); }
            }
        }
        else if (format is "Text" or "Csv")
        {
            await using var writer = new StreamWriter(path, false, new UTF8Encoding(true));
            foreach (var row in rows)
            {
                token.ThrowIfCancellationRequested();
                // CSV stays literal when opened by spreadsheet software; quoting alone does not stop formulas.
                static string Csv(string value) => "\"" + (Regex.IsMatch(value, @"^\s*[=+@-]") ? "'" : "") + value.Replace("\"", "\"\"") + "\"";
                static string Text(string value) => value.Replace("\\", "\\\\").Replace("\t", "\\t").Replace("\r", "\\r").Replace("\n", "\\n");
                await writer.WriteAsync(string.Join(format == "Csv" ? "," : "\t", row.Select(value => format == "Csv" ? Csv(value) : Text(value))) + "\r\n");
            }
        }
        else if (format == "Docx")
        {
            using var doc = new XWPFDocument();
            foreach (var block in blocks)
            {
                token.ThrowIfCancellationRequested();
                switch (StudioJson.Text(block["type"]))
                {
                    case "paragraph":
                        var paragraph = doc.CreateParagraph(); if (block["heading"]?.GetValue<bool>() == true) paragraph.GetCTP().AddNewPPr().keepNext = new() { val = true };
                        SetText(paragraph, StudioJson.Text(block["text"]) ?? "", block["bold"]?.GetValue<bool>() == true); break;
                    case "table":
                        var tableRows = block["rows"]!.AsArray().OfType<JsonArray>().ToArray(); if (tableRows.Length == 0) break;
                        // Adjacent same-style OOXML tables are otherwise treated as one table by Word.
                        if (doc.BodyElements.LastOrDefault() is XWPFTable)
                        {
                            var separator = doc.CreateParagraph(); separator.SpacingBefore = 0; separator.SpacingAfter = 0;
                            separator.SetSpacingBetween(1, LineSpacingRule.EXACT);
                        }
                        var table = doc.CreateTable(tableRows.Length, tableRows.Max(r => r.Count)); table.Width = 9000;
                        for (var r = 0; r < tableRows.Length; r++)
                        {
                            var row = table.GetRow(r); if (r < (block["headerRows"]?.GetValue<int>() ?? 0)) row.SetRepeatHeader(true);
                            for (var c = 0; c < tableRows[r].Count; c++) SetText(row.GetCell(c).Paragraphs[0], StudioJson.Text(tableRows[r][c]) ?? "");
                        }
                        break;
                    case "image":
                        var image = Image(block); using (var stream = new MemoryStream(image.Bytes))
                            doc.CreateParagraph().CreateRun().AddPicture(stream, (int)PictureType.PNG, "report.png", (int)Math.Round(image.Width * 36000), (int)Math.Round(image.Height * 36000));
                        break;
                    default: throw Error("REPORT_OUTPUT_INVALID", "未知文档正文类型。");
                }
            }
            await using var output = File.Create(path); doc.Write(output);
        }
        else if (format == "Rtf")
        {
            var previousTable = false;
            var text = new StringBuilder(@"{\rtf1\ansi\deff0{\fonttbl{\f0 Microsoft YaHei;}}\uc1\f0\fs20 ");
            foreach (var block in blocks)
            {
                token.ThrowIfCancellationRequested();
                switch (StudioJson.Text(block["type"]))
                {
                    case "paragraph":
                        text.Append(@"\pard "); if (block["heading"]?.GetValue<bool>() == true) text.Append(@"\keepn ");
                        text.Append(block["bold"]?.GetValue<bool>() == true ? @"\b " : @"\b0 ").Append(RtfText(StudioJson.Text(block["text"]) ?? "")).Append(@"\par "); break;
                    case "table":
                        if (previousTable) text.Append(@"\pard\fs2\sl20\slmult0\sa0\sb0\par\pard\f0\fs20 ");
                        var r = 0;
                        foreach (var row in block["rows"]!.AsArray().OfType<JsonArray>())
                        {
                            text.Append(@"\trowd\trgaph100 "); if (r++ < (block["headerRows"]?.GetValue<int>() ?? 0)) text.Append(@"\trhdr ");
                            for (var c = 1; c <= row.Count; c++) text.Append(@"\cellx").Append(9000 * c / row.Count).Append(' ');
                            foreach (var cell in row) text.Append(@"\pard\intbl\b0 ").Append(RtfText(StudioJson.Text(cell) ?? "")).Append(@"\cell ");
                            text.Append(@"\row ");
                        }
                        break;
                    case "image":
                        var image = Image(block); text.Append(@"\pard {\pict\pngblip\picwgoal").Append((int)Math.Round(image.Width * 1440 / 25.4)).Append(@"\pichgoal").Append((int)Math.Round(image.Height * 1440 / 25.4)).Append(' ').Append(Convert.ToHexString(image.Bytes)).Append(@"}\par "); break;
                    default: throw Error("REPORT_OUTPUT_INVALID", "未知文档正文类型。");
                }
                previousTable = StudioJson.Text(block["type"]) == "table";
            }
            text.Append('}'); await File.WriteAllTextAsync(path, text.ToString(), Encoding.ASCII, token);
        }
        return path;
    }
    private static void SetText(XWPFParagraph paragraph, string text, bool bold = false)
    {
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var tabs = lines[i].Split('\t');
            for (var j = 0; j < tabs.Length; j++)
            {
                // SetText replaces a run's text; separate runs preserve every segment and its order.
                var run = paragraph.CreateRun(); run.FontFamily = "Microsoft YaHei"; run.FontSize = 10; run.IsBold = bold; run.SetText(tabs[j]);
                if (j < tabs.Length - 1) run.AddTab();
                else if (i < lines.Length - 1) run.AddBreak(BreakType.TEXTWRAPPING);
            }
        }
    }
    private static string RtfText(string value)
    {
        var text = new StringBuilder();
        foreach (var c in value.Replace("\r\n", "\n").Replace('\r', '\n')) if (c is '\\' or '{' or '}') text.Append('\\').Append(c); else if (c == '\n') text.Append(@"\line "); else if (c == '\t') text.Append(@"\tab "); else if (c > 127) text.Append(@"\u").Append(unchecked((short)c).ToString(CultureInfo.InvariantCulture)).Append('?'); else if (c >= 32) text.Append(c);
        return text.ToString();
    }
    private static (byte[] Bytes, double Width, double Height) Image(JsonObject block)
    {
        var data = StudioJson.RequiredString(block, "data", 50 * 1024 * 1024);
        if (!data.StartsWith("data:image/png;base64,", StringComparison.Ordinal)) throw Error("REPORT_OUTPUT_INVALID", "文档图像类型无效。");
        var width = block["widthMm"]!.GetValue<double>(); var height = block["heightMm"]!.GetValue<double>();
        var scale = Math.Min(1, Math.Min(160 / width, 230 / height));
        return (Convert.FromBase64String(data[22..]), width * scale, height * scale);
    }
    private static async Task<byte[]> VerifiedAsync(string path, string hash, CancellationToken token)
    {
        if (!File.Exists(path) || new FileInfo(path).Length > 50 * 1024 * 1024) throw Error("REPORT_OUTPUT_INVALID", "导出内容缺失或超限。");
        var bytes = await File.ReadAllBytesAsync(path, token);
        if (!string.Equals(Convert.ToHexStringLower(SHA256.HashData(bytes)), hash, StringComparison.OrdinalIgnoreCase)) throw Error("REPORT_OUTPUT_INVALID", "导出内容哈希不匹配。");
        return bytes;
    }
    private static StudioException Error(string code, string message) => new(code, message, 422);
}
