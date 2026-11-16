using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using IndustrialPlatform.PlatformStudio.Domain;
using IndustrialPlatform.PlatformStudio.Infrastructure.Report;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using ExcelDataReader;
using W = DocumentFormat.OpenXml.Wordprocessing;
using Xunit;

namespace IndustrialPlatform.PlatformStudio.Tests;

public sealed class ReportExportTests
{
    static ReportExportTests() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    private static string DocumentText(DocumentFormat.OpenXml.OpenXmlElement element) =>
        string.Concat(element.Descendants().Select(node => node switch
        {
            W.Text text => text.Text,
            W.TabChar => "\t",
            W.Break => "\n",
            _ => ""
        }));

    [Fact]
    public async Task Editable_and_data_exports_preserve_literals_rows_and_unicode()
    {
        var directory = Path.Combine(Path.GetTempPath(), "pf09-export-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        try
        {
            var rows = new JsonArray(new JsonArray("工序", "批次", "说明"), new JsonArray("001", "9007199254740993.001", "光刻胶,\"测试\"\n下一行"), new JsonArray("=1+1", "", "终行"));
            var blocks = new JsonArray(new JsonObject { ["type"] = "paragraph", ["text"] = "光刻胶😀\r中行\r\n尾行\t末列", ["heading"] = true, ["bold"] = true }, new JsonObject { ["type"] = "table", ["rows"] = rows.DeepClone(), ["headerRows"] = 1 });
            var content = Encoding.UTF8.GetBytes(new JsonObject { ["rows"] = rows, ["blocks"] = blocks }.ToJsonString());
            await File.WriteAllBytesAsync(Path.Combine(directory, "result.export.json"), content);
            var rendered = new ReportRenderedOutput(directory, "unused", 1, new JsonObject { ["exportSha256"] = Convert.ToHexStringLower(SHA256.HashData(content)) }, new());
            var draftCsv = await File.ReadAllTextAsync(await ReportExporter.WriteAsync(rendered, "Csv", default, preview: true));
            Assert.Contains("PREVIEW / 草稿试运行", draftCsv);
            using (var stream = File.OpenRead(await ReportExporter.WriteAsync(rendered, "Docx", default, preview: true)))
            using (var draftDoc = WordprocessingDocument.Open(stream, false)) Assert.StartsWith("PREVIEW /", draftDoc.MainDocumentPart!.Document!.Body!.Elements<W.Paragraph>().First().InnerText);
            // A subsequent formal conversion uses the same frozen model without the draft label.
            Assert.Equal(content, await File.ReadAllBytesAsync(Path.Combine(directory, "result.export.json")));
            var xls = await ReportExporter.WriteAsync(rendered, "Xls", default);
            Assert.Equal(new byte[] { 0xd0, 0xcf, 0x11, 0xe0 }, (await File.ReadAllBytesAsync(xls))[..4]);
            using (var stream = File.OpenRead(xls)) using (var reader = ExcelReaderFactory.CreateBinaryReader(stream))
            {
                Assert.Equal("报告正文", reader.Name);
                Assert.Equal(3, reader.RowCount);
                Assert.True(reader.Read()); Assert.True(reader.Read());
                Assert.Equal("001", reader.GetString(0));
                Assert.Equal("9007199254740993.001", reader.GetString(1));
                Assert.True(reader.Read()); Assert.Equal(typeof(string), reader.GetFieldType(0));
                Assert.Equal("=1+1", reader.GetString(0)); Assert.Equal("终行", reader.GetString(2));
                Assert.False(reader.Read()); Assert.False(reader.NextResult());
            }
            var docx = await ReportExporter.WriteAsync(rendered, "Docx", default);
            using (var doc = WordprocessingDocument.Open(docx, false))
            {
                Assert.Empty(new OpenXmlValidator().Validate(doc));
                var body = doc.MainDocumentPart!.Document!.Body!;
                var paragraph = Assert.Single(body.Elements<W.Paragraph>());
                Assert.Equal("光刻胶😀\n中行\n尾行\t末列", DocumentText(paragraph));
                Assert.NotNull(paragraph.ParagraphProperties?.KeepNext);
                var table = Assert.Single(body.Elements<W.Table>());
                var tableRows = table.Elements<W.TableRow>().ToArray(); Assert.Equal(3, tableRows.Length);
                Assert.NotNull(tableRows[0].TableRowProperties?.GetFirstChild<W.TableHeader>());
                Assert.Equal("终行", DocumentText(tableRows[2].Elements<W.TableCell>().ElementAt(2)));
                Assert.Equal("光刻胶,\"测试\"\n下一行", DocumentText(tableRows[1].Elements<W.TableCell>().ElementAt(2)));
            }
            var csv = await File.ReadAllTextAsync(await ReportExporter.WriteAsync(rendered, "Csv", default));
            Assert.Contains("\"光刻胶,\"\"测试\"\"\n下一行\"", csv, StringComparison.Ordinal);
            Assert.Contains("\"'=1+1\"", csv, StringComparison.Ordinal);
            var text = await File.ReadAllTextAsync(await ReportExporter.WriteAsync(rendered, "Text", default));
            Assert.Contains("测试\"\\n下一行", text, StringComparison.Ordinal); Assert.Equal(3, text.Split("\r\n", StringSplitOptions.RemoveEmptyEntries).Length);
            var rtf = await File.ReadAllTextAsync(await ReportExporter.WriteAsync(rendered, "Rtf", default));
            Assert.StartsWith(@"{\rtf1", rtf, StringComparison.Ordinal); Assert.Contains(@"\trhdr", rtf, StringComparison.Ordinal); Assert.Contains(@"\u20809?", rtf, StringComparison.Ordinal); Assert.Contains(@"\keepn", rtf, StringComparison.Ordinal);
            Assert.Equal(3, System.Text.RegularExpressions.Regex.Count(rtf, @"\\line "));
            Assert.Contains(@"\tab ", rtf, StringComparison.Ordinal);
            if (Environment.GetEnvironmentVariable("PF09_R5_OUTPUT") is { Length: > 0 } folder)
                foreach (var format in new[] { "Xls", "Xlsx", "Docx", "Rtf", "Csv", "Text" })
                {
                    var file = await ReportExporter.WriteAsync(rendered, format, default);
                    File.Copy(file, Path.Combine(folder, "literals" + Path.GetExtension(file)), true);
                }
            rendered.Result["exportSha256"] = new string('0', 64);
            Assert.Equal("REPORT_OUTPUT_INVALID", (await Assert.ThrowsAsync<StudioException>(() => ReportExporter.WriteAsync(rendered, "Xls", default))).Code);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task Mht_embeds_and_deduplicates_resources_without_preview_urls()
    {
        var directory = Path.Combine(Path.GetTempPath(), "pf09-export-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        try
        {
            const string image = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4z8DwHwAFAAH/iZk9HQAAAABJRU5ErkJggg==";
            await File.WriteAllTextAsync(Path.Combine(directory, "result.html"), $"<!doctype html><html><meta charset=\"utf-8\"><body>光刻胶<img src=\"{image}\"><img src=\"{image}\"></body></html>");
            var rendered = new ReportRenderedOutput(directory, "unused", 1, new(), new());
            var file = await ReportExporter.WriteAsync(rendered, "Mht", default);
            var mht = await File.ReadAllTextAsync(file);
            Assert.Contains("Content-Type: multipart/related", mht, StringComparison.Ordinal);
            Assert.Equal(2, System.Text.RegularExpressions.Regex.Count(mht, "Content-ID:"));
            Assert.DoesNotContain("/previews/", mht, StringComparison.Ordinal);
            if (Environment.GetEnvironmentVariable("PF09_R5_OUTPUT") is { Length: > 0 } folder) File.Copy(file, Path.Combine(folder, "image-resources.mht"), true);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData(65537, 1, 1)]
    [InlineData(1, 257, 1)]
    [InlineData(1, 1, 32768)]
    public async Task Xls_rejects_limits_without_truncating(int rowCount, int columns, int cellLength)
    {
        var directory = Path.Combine(Path.GetTempPath(), "pf09-export-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        try
        {
            var rows = new JsonArray(Enumerable.Range(0, rowCount).Select(_ => (JsonNode)new JsonArray(Enumerable.Range(0, columns).Select(_ => (JsonNode)JsonValue.Create(new string('a', cellLength))!).ToArray())).ToArray());
            var content = Encoding.UTF8.GetBytes(new JsonObject { ["rows"] = rows, ["blocks"] = new JsonArray() }.ToJsonString());
            await File.WriteAllBytesAsync(Path.Combine(directory, "result.export.json"), content);
            var rendered = new ReportRenderedOutput(directory, "unused", 1, new JsonObject { ["exportSha256"] = Convert.ToHexStringLower(SHA256.HashData(content)) }, new());
            Assert.Equal("REPORT_WORKBOOK_LIMIT", (await Assert.ThrowsAsync<StudioException>(() => ReportExporter.WriteAsync(rendered, "Xls", default))).Code);
            Assert.False(File.Exists(Path.Combine(directory, "result.xls")));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task Raster_archive_rejects_wrong_physical_page_order()
    {
        var directory = Path.Combine(Path.GetTempPath(), "pf09-export-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        try
        {
            var rendered = new ReportRenderedOutput(directory, "unused", 1, new JsonObject { ["rasterPages"] = new JsonArray(new JsonObject { ["pageNo"] = 2, ["file"] = "page-0002.png" }) }, new());
            Assert.Equal("REPORT_OUTPUT_INVALID", (await Assert.ThrowsAsync<StudioException>(() => ReportExporter.WriteAsync(rendered, "Png", default))).Code);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task Independent_document_tables_keep_separate_headers()
    {
        var directory = Path.Combine(Path.GetTempPath(), "pf09-tables-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        try
        {
            var blocks = new JsonArray("FIRST", "SECOND").Select(label => (JsonNode)new JsonObject { ["type"] = "table", ["headerRows"] = 1, ["rows"] = new JsonArray(new JsonArray(label!.GetValue<string>() + "-HEADER"), new JsonArray(label.GetValue<string>() + "-ROW")) }).ToArray();
            var content = Encoding.UTF8.GetBytes(new JsonObject { ["rows"] = new JsonArray(), ["blocks"] = new JsonArray(blocks) }.ToJsonString());
            await File.WriteAllBytesAsync(Path.Combine(directory, "result.export.json"), content);
            var rendered = new ReportRenderedOutput(directory, "unused", 1, new JsonObject { ["exportSha256"] = Convert.ToHexStringLower(SHA256.HashData(content)) }, new());
            using (var doc = WordprocessingDocument.Open(await ReportExporter.WriteAsync(rendered, "Docx", default), false))
            {
                Assert.Empty(new OpenXmlValidator().Validate(doc));
                var body = doc.MainDocumentPart!.Document!.Body!;
                var tables = body.Elements<W.Table>().ToArray();
                Assert.Equal(2, tables.Length); Assert.Equal(3, body.ChildElements.Count);
                Assert.IsType<W.Table>(body.ChildElements[0]); Assert.IsType<W.Paragraph>(body.ChildElements[1]); Assert.IsType<W.Table>(body.ChildElements[2]);
                Assert.All(tables, table => Assert.NotNull(table.Elements<W.TableRow>().First().TableRowProperties?.GetFirstChild<W.TableHeader>()));
                Assert.Equal("FIRST-ROW", tables[0].Elements<W.TableRow>().ElementAt(1).InnerText);
                Assert.Equal("SECOND-ROW", tables[1].Elements<W.TableRow>().ElementAt(1).InnerText);
            }
            var rtf = await File.ReadAllTextAsync(await ReportExporter.WriteAsync(rendered, "Rtf", default));
            Assert.Contains(@"\row \pard\fs2\sl20\slmult0\sa0\sb0\par\pard\f0\fs20 \trowd", rtf, StringComparison.Ordinal);
            Assert.Equal(2, System.Text.RegularExpressions.Regex.Count(rtf, @"\\trhdr"));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData("Docx")]
    [InlineData("Rtf")]
    public async Task Document_tables_accept_63_columns_and_reject_64_without_truncation(string format)
    {
        var directory = Path.Combine(Path.GetTempPath(), "pf09-columns-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        try
        {
            foreach (var count in new[] { 64, 63 })
            {
                var row = new JsonArray(Enumerable.Range(0, count).Select(i => (JsonNode)JsonValue.Create($"COL-{i:00}")!).ToArray());
                var content = Encoding.UTF8.GetBytes(new JsonObject { ["rows"] = new JsonArray(), ["blocks"] = new JsonArray(new JsonObject { ["type"] = "table", ["headerRows"] = 1, ["rows"] = new JsonArray(row) }) }.ToJsonString());
                await File.WriteAllBytesAsync(Path.Combine(directory, "result.export.json"), content);
                var rendered = new ReportRenderedOutput(directory, "unused", 1, new JsonObject { ["exportSha256"] = Convert.ToHexStringLower(SHA256.HashData(content)) }, new());
                if (count == 64)
                {
                    Assert.Equal("REPORT_DOCUMENT_TABLE_LIMIT", (await Assert.ThrowsAsync<StudioException>(() => ReportExporter.WriteAsync(rendered, format, default))).Code);
                    Assert.False(File.Exists(Path.Combine(directory, "result." + format.ToLowerInvariant())));
                }
                else
                {
                    var file = await ReportExporter.WriteAsync(rendered, format, default);
                    if (format == "Docx")
                    {
                        using var doc = WordprocessingDocument.Open(file, false);
                        Assert.Empty(new OpenXmlValidator().Validate(doc));
                        var cells = Assert.Single(doc.MainDocumentPart!.Document!.Body!.Elements<W.Table>()).Elements<W.TableRow>().First().Elements<W.TableCell>().ToArray();
                        Assert.Equal(63, cells.Length);
                        Assert.Equal("COL-62", cells[62].InnerText);
                    }
                    else Assert.Contains("COL-62", await File.ReadAllTextAsync(file), StringComparison.Ordinal);
                }
            }
        }
        finally { Directory.Delete(directory, true); }
    }
    [Theory]
    [InlineData(65536, 1, 16)]
    [InlineData(2, 256, 16)]
    [InlineData(2, 2, 32767)]
    public async Task Xls_boundaries_round_trip_with_independent_reader(int rowCount, int columns, int cellLength)
    {
        var directory = Path.Combine(Path.GetTempPath(), "pf09-xls-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        try
        {
            // Mixed Unicode crosses BIFF SST continuation boundaries; all values must remain text.
            var value = "001=😀" + new string('胶', cellLength - 6);
            var rows = new JsonArray(Enumerable.Range(0, rowCount).Select(i => (JsonNode)new JsonArray(
                Enumerable.Range(0, columns).Select(c => (JsonNode)JsonValue.Create(c == 0 ? value : $"行{i}-列{c}")!).ToArray())).ToArray());
            var content = Encoding.UTF8.GetBytes(new JsonObject { ["rows"] = rows, ["blocks"] = new JsonArray() }.ToJsonString());
            await File.WriteAllBytesAsync(Path.Combine(directory, "result.export.json"), content);
            var rendered = new ReportRenderedOutput(directory, "unused", 1, new JsonObject { ["exportSha256"] = Convert.ToHexStringLower(SHA256.HashData(content)) }, new());
            using var input = File.OpenRead(await ReportExporter.WriteAsync(rendered, "Xls", default));
            using var reader = ExcelReaderFactory.CreateBinaryReader(input);
            Assert.Equal(rowCount, reader.RowCount); Assert.Equal(columns, reader.FieldCount);
            for (var i = 0; i < rowCount; i++)
            {
                Assert.True(reader.Read()); Assert.Equal(value, reader.GetString(0));
                for (var c = 1; c < columns; c++) Assert.Equal($"行{i}-列{c}", reader.GetString(c));
            }
            Assert.False(reader.Read());
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task Docx_embeds_images_with_unique_ids_and_keeps_text_and_headers()
    {
        var directory = Path.Combine(Path.GetTempPath(), "pf09-docx-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        try
        {
            const string png = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4z8DwHwAFAAH/iZk9HQAAAABJRU5ErkJggg==";
            var blocks = new JsonArray(
                new JsonObject { ["type"] = "paragraph", ["text"] = " 光刻胶😀 ", ["bold"] = true, ["heading"] = true },
                new JsonObject { ["type"] = "table", ["headerRows"] = 2, ["rows"] = new JsonArray(new JsonArray("工序", "数据"), new JsonArray("编号", "内容"), new JsonArray("001"), new JsonArray("002", "末行")) });
            for (var i = 0; i < 2; i++)
                blocks.Add(new JsonObject { ["type"] = "image", ["data"] = "data:image/png;base64," + png, ["widthMm"] = 320, ["heightMm"] = 460 });
            var content = Encoding.UTF8.GetBytes(new JsonObject { ["rows"] = new JsonArray(), ["blocks"] = blocks }.ToJsonString());
            await File.WriteAllBytesAsync(Path.Combine(directory, "result.export.json"), content);
            var rendered = new ReportRenderedOutput(directory, "unused", 1, new JsonObject { ["exportSha256"] = Convert.ToHexStringLower(SHA256.HashData(content)) }, new());
            using var doc = WordprocessingDocument.Open(await ReportExporter.WriteAsync(rendered, "Docx", default), false);
            Assert.Empty(new OpenXmlValidator().Validate(doc));
            var main = doc.MainDocumentPart!; var body = main.Document!.Body!;
            var heading = body.Elements<W.Paragraph>().First();
            Assert.Equal(" 光刻胶😀 ", heading.InnerText);
            Assert.NotNull(heading.ParagraphProperties?.KeepNext); Assert.NotNull(heading.Descendants<W.Bold>().SingleOrDefault());
            var rows = Assert.Single(body.Elements<W.Table>()).Elements<W.TableRow>().ToArray();
            Assert.Equal(4, rows.Length); Assert.All(rows.Take(2), row => Assert.NotNull(row.TableRowProperties?.GetFirstChild<W.TableHeader>()));
            Assert.Equal("", rows[2].Elements<W.TableCell>().ElementAt(1).InnerText);
            Assert.Equal("末行", rows[3].Elements<W.TableCell>().ElementAt(1).InnerText);
            Assert.Equal(2, main.ImageParts.Count());
            foreach (var part in main.ImageParts)
            {
                using var input = part.GetStream(); using var output = new MemoryStream(); input.CopyTo(output);
                Assert.Equal(Convert.FromBase64String(png), output.ToArray());
            }
            var drawings = body.Descendants<DocumentFormat.OpenXml.Drawing.Wordprocessing.Inline>().ToArray();
            Assert.Equal(2, drawings.Length);
            Assert.Equal(2, drawings.Select(d => d.DocProperties!.Id!.Value).Distinct().Count());
            Assert.All(drawings, drawing =>
            {
                Assert.Equal(160L * 36000, drawing.Extent!.Cx!.Value);
                Assert.Equal(230L * 36000, drawing.Extent.Cy!.Value);
                var blip = drawing.Descendants<DocumentFormat.OpenXml.Drawing.Blip>().Single();
                Assert.IsType<ImagePart>(main.GetPartById(blip.Embed!));
            });
        }
        finally { Directory.Delete(directory, true); }
    }

}
