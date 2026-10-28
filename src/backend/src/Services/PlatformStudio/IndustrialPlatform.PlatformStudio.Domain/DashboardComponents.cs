using System.Text.Json.Nodes;

namespace IndustrialPlatform.PlatformStudio.Domain;

/// <summary>
/// Dashboard 的代码注册表。每个概念入口都有独立的可编辑注册项；运行时只接受这里登记的键和版本。
/// </summary>
public sealed record DashboardComponentRegistration(
    string Key,
    string Version,
    string Label,
    string Category,
    JsonObject PropsSchema,
    IReadOnlyList<string> BindableProperties,
    IReadOnlyList<string> Events);

public static class DashboardComponentRegistry
{
    private static JsonObject Schema(params string[] properties)
    {
        var propertySchema = new JsonObject();
        foreach (var key in properties)
        {
            var type = key is "min" or "max" or "digits" or "intervalSeconds" or "autoSwitchSeconds" or "speedPxPerSecond" or "pageSeconds" or "maxItems" or "diameterPx" or "rowHeight"
                ? "number"
                : key is "clipChildren" or "disabled" or "wrap" or "legend" or "animate" or "pauseOnHover" or "showDate" or "showLastSuccess" or "showSourceTime"
                    ? "boolean"
                    : key is "assetIds" or "cases" or "items" or "series" or "columns" or "points" or "thresholds" or "colorStops" or "parameterMap" or "collectionPath" or "rowKeyPath"
                        ? "array"
                        : "string";
            var schema = new JsonObject { ["type"] = type };
            if (key is "intervalSeconds" or "pageSeconds" or "autoSwitchSeconds") { schema["minimum"] = 3; schema["maximum"] = 3600; }
            if (key == "speedPxPerSecond") { schema["minimum"] = 10; schema["maximum"] = 100; }
            if (key == "diameterPx") { schema["minimum"] = 2; schema["maximum"] = 40; }
            if (key == "rowHeight") { schema["minimum"] = 16; schema["maximum"] = 200; }
            if (key == "digits") { schema["minimum"] = 0; schema["maximum"] = 6; }
            if (key == "maxItems") { schema["minimum"] = 1; schema["maximum"] = 100; }
            if (key == "orientation") schema["enum"] = new JsonArray("Horizontal", "Vertical");
            if (key == "presentation") schema["enum"] = new JsonArray("Table", "List");
            if (key == "kind") schema["enum"] = new JsonArray("Bar", "StackedBar", "Line", "Area", "Pie", "Donut", "Gauge", "Scatter", "WaterLevel", "Ranking", "Heatmap");
            propertySchema[key] = schema;
        }
        propertySchema["appearance"] = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["color"] = new JsonObject { ["type"] = "string" },
                ["background"] = new JsonObject { ["type"] = "string" },
                ["borderColor"] = new JsonObject { ["type"] = "string" },
                ["borderWidth"] = new JsonObject { ["type"] = "number", ["minimum"] = 0, ["maximum"] = 20 },
                ["fontKey"] = new JsonObject { ["type"] = "string" },
                ["fontSizePx"] = new JsonObject { ["type"] = "number", ["minimum"] = 8, ["maximum"] = 128 },
                ["opacity"] = new JsonObject { ["type"] = "number", ["minimum"] = 0, ["maximum"] = 1 },
                ["paddingPx"] = new JsonObject { ["type"] = "number", ["minimum"] = 0, ["maximum"] = 48 },
            },
        };
        return new JsonObject
        {
            ["type"] = "object",
            ["properties"] = propertySchema,
            ["additionalProperties"] = false,
        };
    }

    private static DashboardComponentRegistration Entry(
        string key,
        string label,
        string category,
        string[] properties,
        string[] bindings = null!,
        string[] events = null!) => new(
        key,
        "1",
        label,
        category,
        Schema(properties),
        bindings ?? [],
        events ?? []);

    public static IReadOnlyList<DashboardComponentRegistration> Entries { get; } =
    [
        Entry("Container", "容器", "基础", ["clipChildren"], events: []),
        Entry("Text", "文字", "基础", ["text", "align", "wrap"], ["text"]),
        Entry("Button", "按钮", "基础", ["text", "disabled"], ["disabled"], ["Click"]),
        Entry("Image", "图片", "基础", ["assetId", "fit", "alt"]),
        Entry("StateImage", "状态图片", "基础", ["cases", "defaultAssetId"], ["state"]),
        Entry("ImageCarousel", "图片轮播", "基础", ["assetIds", "intervalSeconds", "pauseOnHover"]),
        Entry("Number", "数值 / 翻牌", "基础", ["unit", "digits", "prefix", "suffix", "display", "animate"], ["value"]),
        Entry("Progress", "进度", "基础", ["min", "max", "unit", "orientation"], ["value"]),
        Entry("Clock", "时间 / 班次", "基础", ["pattern", "timeZone", "showDate"], ["shiftLabel"]),
        Entry("ScrollingText", "滚动文字", "基础", ["speedPxPerSecond", "pauseOnHover"], ["text"]),
        Entry("ContentSwitch", "内容切换", "基础", ["items", "activeItemId", "autoSwitchSeconds"], ["activeItemId"]),
        Entry("EmbeddedPage", "嵌入页", "基础", ["registeredPageKey", "parameterMap", "sandboxPolicyKey"]),
        Entry("Chart.Bar", "柱状图", "图表", ["kind", "instanceId", "collectionPath", "categoryField", "xField", "series", "legend", "unit"], ["rows"]),
        Entry("Chart.Line", "折线 / 多序列", "图表", ["kind", "instanceId", "collectionPath", "categoryField", "xField", "yField", "series", "legend", "unit"], ["rows"]),
        Entry("Chart.Area", "面积图", "图表", ["kind", "instanceId", "collectionPath", "categoryField", "xField", "series", "legend", "unit"], ["rows"]),
        Entry("Chart.Pie", "饼图 / 环图", "图表", ["kind", "instanceId", "collectionPath", "categoryField", "xField", "series", "legend", "unit"], ["rows"]),
        Entry("Chart.WaterLevel", "水位图", "图表", ["kind", "instanceId", "min", "max", "unit"], ["value"]),
        Entry("Chart.Gauge", "仪表盘", "图表", ["kind", "instanceId", "min", "max", "unit"], ["value"]),
        Entry("Table.Scroll", "滚动表格", "图表", ["presentation", "instanceId", "collectionPath", "rowKeyPath", "columns", "rowHeight", "scrollMode", "pageSeconds"], ["rows"], ["RowClick"]),
        Entry("List.Scroll", "滚动列表", "图表", ["presentation", "instanceId", "collectionPath", "rowKeyPath", "columns", "rowHeight", "scrollMode", "pageSeconds"], ["rows"], ["RowClick"]),
        Entry("Chart.Ranking", "排名", "图表", ["kind", "instanceId", "collectionPath", "categoryField", "valueField", "maxItems", "order"], ["rows"]),
        Entry("Chart.Heatmap", "状态矩阵", "图表", ["kind", "instanceId", "collectionPath", "xField", "yField", "valueField", "colorStops"], ["rows"]),
        Entry("Tank", "罐", "工业", ["shape", "min", "max", "unit", "label"], ["level", "temperature", "state"]),
        Entry("Pump", "泵", "工业", ["label", "flowUnit"], ["running", "flow", "state"]),
        Entry("Valve", "阀", "工业", ["label"], ["open", "opening", "state"]),
        Entry("Pipe", "管道", "工业", ["points", "diameterPx"], ["flow", "direction", "state"]),
        Entry("Level", "液位尺", "工业", ["min", "max", "unit", "orientation"], ["value"]),
        Entry("StatusLamp", "状态灯", "工业", ["cases", "unknownColor"], ["state"]),
        Entry("Conveyor", "输送", "工业", ["label", "orientation"], ["speed", "direction", "state"]),
        Entry("Thermometer", "温度", "工业", ["min", "max", "unit", "thresholds"], ["value"]),
        Entry("Motor", "电机 / 风机", "工业", ["variant", "label"], ["running", "speed", "state"]),
        Entry("ProcessSteps", "工艺步骤", "工业", ["instanceId", "collectionPath", "keyField", "labelField", "stateField", "direction"], ["rows", "currentKey"]),
        Entry("DataSource", "页面数据源", "数据", ["instanceId"]),
        Entry("PageParameter", "页面参数", "数据", ["parameterName"]),
        Entry("DataQuality", "数据质量", "数据", ["instanceId", "showLastSuccess", "showSourceTime"]),
    ];

    private static readonly Dictionary<string, DashboardComponentRegistration> ByKey =
        Entries.ToDictionary(entry => entry.Key, StringComparer.Ordinal);

    public static bool TryGet(string? key, out DashboardComponentRegistration registration) =>
        ByKey.TryGetValue(key switch { "Chart.StackedBar" => "Chart.Bar", "Chart.Donut" => "Chart.Pie", "Chart.Scatter" => "Chart.Line", _ => key ?? string.Empty }, out registration!);

    public static DashboardComponentRegistration Get(string key) =>
        TryGet(key, out var value)
            ? value
            : throw new StudioException("STUDIO_VALIDATION", "看板组件未在代码注册表中登记。", path: "components.type");
}
