using System.Text.Json.Nodes;
using IndustrialPlatform.PlatformStudio.Domain;

namespace IndustrialPlatform.PlatformStudio.Application.Report;

public static class ReportTableTemplates
{
    // Legacy documents have one row defined by column.value. Query projection uses the first template row.
    public static JsonObject Binding(JsonObject table, JsonObject column, int row = 0) =>
        table["rows"] is JsonArray rows
            ? rows[row]!["cells"]!.AsArray().OfType<JsonObject>().Single(c => StudioJson.Text(c["columnId"]) == StudioJson.Text(column["id"]))["value"]!.AsObject()
            : column["value"]!.AsObject();
}
