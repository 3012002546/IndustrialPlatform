using System.IO.Compression;
using System.Security;
using System.Text;

namespace IndustrialPlatform.Identity.Api.Export;

/// <summary>最小 OpenXML 工作簿写出器，按服务端分页结果生成真实 xlsx。</summary>
internal sealed class StreamingXlsxWriter : IAsyncDisposable
{
    private readonly ZipArchive _archive;
    private readonly StreamWriter _sheet;

    private StreamingXlsxWriter(ZipArchive archive, StreamWriter sheet)
    {
        _archive = archive;
        _sheet = sheet;
    }

    public static async Task<StreamingXlsxWriter> CreateAsync(Stream output, CancellationToken cancellationToken)
    {
        var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
        await WriteTextAsync(archive, "[Content_Types].xml", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/><Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/></Types>", cancellationToken);
        await WriteTextAsync(archive, "_rels/.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>", cancellationToken);
        await WriteTextAsync(archive, "xl/_rels/workbook.xml.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/></Relationships>", cancellationToken);
        await WriteTextAsync(archive, "xl/workbook.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets><sheet name=\"Export\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>", cancellationToken);
        var entry = archive.CreateEntry("xl/worksheets/sheet1.xml", CompressionLevel.Fastest);
        var sheet = new StreamWriter(entry.Open(), new UTF8Encoding(false), leaveOpen: false);
        await sheet.WriteAsync("<?xml version=\"1.0\" encoding=\"UTF-8\"?><worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>");
        await sheet.FlushAsync(cancellationToken);
        return new StreamingXlsxWriter(archive, sheet);
    }

    public async Task WriteRowAsync(IEnumerable<string?> values, CancellationToken cancellationToken)
    {
        await _sheet.WriteAsync("<row>");
        foreach (var value in values)
        {
            await _sheet.WriteAsync("<c t=\"inlineStr\"><is><t>");
            await _sheet.WriteAsync(SecurityElement.Escape(value ?? string.Empty) ?? string.Empty);
            await _sheet.WriteAsync("</t></is></c>");
        }
        await _sheet.WriteAsync("</row>");
        await _sheet.FlushAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await _sheet.WriteAsync("</sheetData></worksheet>");
        await _sheet.FlushAsync();
        await _sheet.DisposeAsync();
        _archive.Dispose();
    }

    private static async Task WriteTextAsync(ZipArchive archive, string name, string text, CancellationToken cancellationToken)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Fastest);
        await using var stream = entry.Open();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        await writer.WriteAsync(text);
        await writer.FlushAsync(cancellationToken);
    }

}
