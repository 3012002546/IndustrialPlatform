using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using IndustrialPlatform.PlatformStudio.Domain;
using IndustrialPlatform.PlatformStudio.Infrastructure.Report;
using NPOI.HSSF.UserModel;
using NPOI.XWPF.UserModel;
using Xunit;

namespace IndustrialPlatform.PlatformStudio.Tests;

public sealed class ReportExportTests
{
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
            using (var draftDoc = new XWPFDocument(stream)) Assert.StartsWith("PREVIEW /", draftDoc.Paragraphs[0].Text);
            // A subsequent formal conversion uses the same frozen model without the draft label.
            Assert.Equal(content, await File.ReadAllBytesAsync(Path.Combine(directory, "result.export.json")));
            var xls = await ReportExporter.WriteAsync(rendered, "Xls", default);
            Assert.Equal(new byte[] { 0xd0, 0xcf, 0x11, 0xe0 }, (await File.ReadAllBytesAsync(xls))[..4]);
            using (var stream = File.OpenRead(xls)) using (var workbook = new HSSFWorkbook(stream))
            {
                var sheet = workbook.GetSheetAt(0); Assert.Equal(3, sheet.PhysicalNumberOfRows);
                Assert.Equal("001", sheet.GetRow(1).GetCell(0).StringCellValue);
                Assert.Equal("9007199254740993.001", sheet.GetRow(1).GetCell(1).StringCellValue);
                Assert.Equal(NPOI.SS.UserModel.CellType.String, sheet.GetRow(2).GetCell(0).CellType);
            }
            var docx = await ReportExporter.WriteAsync(rendered, "Docx", default);
            using (var stream = File.OpenRead(docx)) using (var doc = new XWPFDocument(stream))
            {
                Assert.Equal("光刻胶😀\n中行\n尾行\t末列", Assert.Single(doc.Paragraphs).Text);
                Assert.NotNull(doc.Paragraphs[0].GetCTP().pPr.keepNext);
                var table = Assert.Single(doc.Tables); Assert.Equal(3, table.Rows.Count);
                Assert.True(table.GetRow(0).IsRepeatHeader); Assert.Equal("终行", table.GetRow(2).GetCell(2).GetText());
                Assert.Equal("光刻胶,\"测试\"\n下一行", table.GetRow(1).GetCell(2).GetText());
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
            using (var stream = File.OpenRead(await ReportExporter.WriteAsync(rendered, "Docx", default))) using (var doc = new XWPFDocument(stream))
            {
                Assert.Equal(2, doc.Tables.Count); Assert.Equal(3, doc.BodyElements.Count);
                Assert.IsType<XWPFTable>(doc.BodyElements[0]); Assert.IsType<XWPFParagraph>(doc.BodyElements[1]); Assert.IsType<XWPFTable>(doc.BodyElements[2]);
                Assert.All(doc.Tables, table => Assert.True(table.GetRow(0).IsRepeatHeader));
                Assert.Equal("FIRST-ROW", doc.Tables[0].GetRow(1).GetCell(0).GetText());
                Assert.Equal("SECOND-ROW", doc.Tables[1].GetRow(1).GetCell(0).GetText());
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
                        using var stream = File.OpenRead(file); using var doc = new XWPFDocument(stream);
                        Assert.Equal(63, Assert.Single(doc.Tables).GetRow(0).GetTableCells().Count);
                        Assert.Equal("COL-62", doc.Tables[0].GetRow(0).GetCell(62).GetText());
                    }
                    else Assert.Contains("COL-62", await File.ReadAllTextAsync(file), StringComparison.Ordinal);
                }
            }
        }
        finally { Directory.Delete(directory, true); }
    }
}
