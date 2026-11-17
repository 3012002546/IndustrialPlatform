using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using IndustrialPlatform.PlatformStudio.Domain;

namespace IndustrialPlatform.PlatformStudio.Application.Report;

/// <summary>Export-only scalar worksheet snapshot. Never updates a dataset or report definition.</summary>
public static class ReportQuerySnapshot
{
    public static void Validate(JsonObject snapshot)
    {
        if (snapshot.Any(p => p.Key is not ("columns" or "rows")) || snapshot["columns"] is not JsonArray columns || snapshot["rows"] is not JsonArray rows
            || columns.Count is < 1 or > 1000 || rows.Count > 10000 || (long)rows.Count * columns.Count > 500000 || Encoding.UTF8.GetByteCount(snapshot.ToJsonString()) > 20 * 1024 * 1024)
            throw new StudioException("STUDIO_LIMIT", "临时工作表结构无效或超过容量。", 422);
        foreach (var column in columns)
            if (column is not JsonValue title || !title.TryGetValue<string>(out var text) || string.IsNullOrWhiteSpace(text) || text.Length > 256)
                throw new StudioException("STUDIO_VALIDATION", "临时列标题无效。", 422);
        foreach (var row in rows)
        {
            if (row is not JsonArray cells || cells.Count != columns.Count) throw new StudioException("STUDIO_VALIDATION", "临时行字段数不一致。", 422);
            foreach (var cell in cells)
                if (cell is not null && (cell is not JsonValue || cell.GetValueKind() is not (JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False)))
                    throw new StudioException("STUDIO_VALIDATION", "临时工作表仅接受计算后的标量值。", 422);
        }
    }

    public static bool PaperFormat(string? format) => format is "Pdf" or "Html" or "Docx" or "Rtf" or "Mht" or "Png" or "Jpeg";

    // At most nine columns on paper: the identifier plus eight data columns.
    // Transform only the ephemeral query flow, never spreadsheet/data exports.
    public static ReportContentFlow PaginateColumns(ReportContentFlow flow, string format)
    {
        if (!PaperFormat(format) || flow.Blocks.Count == 0) return flow;
        var count = flow.Blocks[0].Definition["table"]?["columns"]?.AsArray().Count ?? 0;
        if (count <= 9) return flow;
        var blocks = new List<ReportFlowBlock>();
        for (var start = 1; start < count; start += 8)
        {
            var indices = Enumerable.Range(start, Math.Min(8, count - start)).Prepend(0).ToArray();
            var suffix = "/columns-" + start.ToString(System.Globalization.CultureInfo.InvariantCulture);
            foreach (var block in flow.Blocks)
            {
                var definition = block.Definition.DeepClone().AsObject();
                var table = definition["table"]!.AsObject();
                var columns = table["columns"]!.AsArray();
                var width = definition["widthMm"]!.GetValue<double>() / indices.Length;
                table["columns"] = new JsonArray(indices.Select(i => { var c = columns[i]!.DeepClone().AsObject(); c["widthMm"] = width; return (JsonNode)c; }).ToArray());
                definition["headerGroupKey"] = StudioJson.Text(definition["headerGroupKey"]) + suffix;
                if (start > 1 && block == flow.Blocks[0]) definition["bandBreakBefore"] = "Page";
                var rows = new JsonArray((block.Value as JsonArray ?? []).Select(row => (JsonNode)new JsonArray(indices.Select(i => row![i]?.DeepClone()).ToArray())).ToArray());
                blocks.Add(block with { Marker = block.Marker + suffix, ParentInstanceKey = block.ParentInstanceKey + suffix, Definition = definition, Value = rows });
            }
        }
        return new(blocks, blocks.Where(b => !b.Repeat).Select(b => b.Marker).ToArray());
    }

    public static JsonObject Document(JsonObject original, JsonObject snapshot, string? format = null)
    {
        if (StudioJson.Text(original["mode"]) != "Query") throw new StudioException("TYPE_MISMATCH", "临时工作表仅用于查询报表。", 422);
        Validate(snapshot);
        var columns = snapshot["columns"]!.AsArray(); var rows = new JsonArray();
        foreach (var row in snapshot["rows"]!.AsArray())
        {
            var item = new JsonObject(); for (var c = 0; c < columns.Count; c++) item["c" + c] = row![c]?.DeepClone(); rows.Add(item);
        }
        var paper = original["paper"]!.DeepClone().AsObject();
        if (PaperFormat(format)) { paper["size"] = "A4"; paper["orientation"] = "Landscape"; }
        var bodyWidth = StudioDocuments.ReportPaper(paper).BodyWidth;
        var tableColumns = new JsonArray();
        for (var c = 0; c < columns.Count; c++) tableColumns.Add(new JsonObject { ["id"] = "c" + c, ["label"] = columns[c]!.DeepClone(), ["widthMm"] = bodyWidth / columns.Count, ["value"] = new JsonObject { ["from"] = "Row", ["path"] = new JsonArray("c" + c), ["nullPolicy"] = "Null" } });
        return new JsonObject {
            ["schemaVersion"] = 1, ["mode"] = "Fixed", ["parameters"] = new JsonArray(), ["styles"] = new JsonArray(), ["assets"] = new JsonArray(), ["paper"] = paper,
            ["instances"] = new JsonArray(new JsonObject { ["instanceId"] = "temporary", ["sourceType"] = "Constant", ["parameters"] = new JsonArray(), ["value"] = rows }),
            ["bands"] = new JsonArray(new JsonObject { ["id"] = "temporary-band", ["kind"] = "Detail", ["name"] = "Query", ["heightMm"] = 10,
                ["data"] = new JsonObject { ["mode"] = "RootPath", ["instanceId"] = "temporary", ["path"] = new JsonArray() },
                ["controls"] = new JsonArray(new JsonObject { ["id"] = "temporary-table", ["type"] = "Table", ["xMm"] = 0, ["yMm"] = 0, ["widthMm"] = bodyWidth, ["heightMm"] = 8, ["canGrow"] = true,
                    ["table"] = new JsonObject { ["rowMode"] = "BandRow", ["columns"] = tableColumns, ["headerMode"] = "OncePerParent", ["repeatHeader"] = true } }) })
        };
    }
}
