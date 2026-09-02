using System.IO.Pipelines;
using Microsoft.AspNetCore.Mvc;

namespace IndustrialPlatform.SystemData.Api.Export;

internal static class StreamingXlsxExport
{
    internal const string ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    internal static int ParseQuantity(string quantity) => string.Equals(quantity, "all", StringComparison.OrdinalIgnoreCase) ? int.MaxValue : int.TryParse(quantity, out var value) && value > 0 ? value : 10000;

    internal sealed record Column<T>(string Field, string Title, Func<T, string?> Value);

    internal static IReadOnlyList<Column<T>> SelectColumns<T>(string? requested, params Column<T>[] defaults)
    {
        if (string.IsNullOrWhiteSpace(requested)) return defaults;
        var fields = requested.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var selected = defaults.Where(column => fields.Contains(column.Field, StringComparer.OrdinalIgnoreCase)).ToArray();
        return selected.Length == 0 ? defaults : selected;
    }

    internal static FileStreamResult Start(string fileName, Func<Stream, CancellationToken, Task> producer, CancellationToken cancellationToken)
    {
        var pipe = new Pipe(new PipeOptions(pauseWriterThreshold: 1024 * 1024, resumeWriterThreshold: 512 * 1024));
        _ = ProduceAsync(pipe, producer, cancellationToken);
        return new FileStreamResult(pipe.Reader.AsStream(leaveOpen: false), ContentType) { FileDownloadName = fileName };
    }

    private static async Task ProduceAsync(Pipe pipe, Func<Stream, CancellationToken, Task> producer, CancellationToken cancellationToken)
    {
        Exception? failure = null;
        try { await using var output = pipe.Writer.AsStream(leaveOpen: true); await producer(output, cancellationToken); }
        catch (Exception ex) { failure = ex; }
        finally { await pipe.Writer.CompleteAsync(failure); }
    }
}
