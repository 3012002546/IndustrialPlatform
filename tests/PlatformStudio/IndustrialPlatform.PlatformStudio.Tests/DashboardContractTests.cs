using System.Text.Json.Nodes;
using IndustrialPlatform.PlatformStudio.Domain;

namespace IndustrialPlatform.PlatformStudio.Tests;

public sealed class DashboardContractTests
{
    [Fact]
    public void Dashboard_registry_contains_all_concept_entries_with_versions_and_bindings()
    {
        Assert.Equal(35, DashboardComponentRegistry.Entries.Count);
        Assert.Equal(35, DashboardComponentRegistry.Entries.Select(item => item.Key).Distinct(StringComparer.Ordinal).Count());
        Assert.All(DashboardComponentRegistry.Entries, item =>
        {
            Assert.Equal("1", item.Version);
            Assert.NotEmpty(item.Label);
            Assert.NotNull(item.PropsSchema);
        });
        Assert.Contains(DashboardComponentRegistry.Entries, item => item.Key == "Table.Scroll" && item.Events.Contains("RowClick"));
        Assert.Contains(DashboardComponentRegistry.Entries, item => item.Key == "Tank" && item.BindableProperties.Contains("level"));
    }

    [Fact]
    public void Valid_dashboard_requires_fixed_dataset_revision_and_canvas_contract()
    {
        var document = new JsonObject
        {
            ["schemaVersion"] = 1,
            ["canvas"] = new JsonObject { ["width"] = 1920, ["height"] = 1080, ["fit"] = "Contain", ["background"] = "#081525" },
            ["parameters"] = new JsonArray(),
            ["instances"] = new JsonArray
            {
                new JsonObject { ["instanceId"] = "orders", ["datasetNId"] = "dataset-orders", ["revisionNId"] = "rev-1", ["parameters"] = new JsonArray() },
            },
            ["components"] = new JsonArray
            {
                new JsonObject
                {
                    ["id"] = "orders-table", ["type"] = "Table", ["registryVersion"] = "1", ["name"] = "订单",
                    ["x"] = 24, ["y"] = 24, ["width"] = 760, ["height"] = 320, ["rotation"] = 0, ["zIndex"] = 1,
                    ["locked"] = false, ["visible"] = true,
                    ["props"] = new JsonObject { ["presentation"] = "Table", ["instanceId"] = "orders", ["rowKeyPath"] = new JsonArray("nId"), ["columns"] = new JsonArray() },
                    ["bindings"] = new JsonArray(), ["events"] = new JsonArray("RowClick"), ["children"] = new JsonArray(),
                },
            },
            ["navigation"] = new JsonArray(),
        };

        Assert.DoesNotContain(StudioDocuments.Validate("Dashboard", document), item => item.Severity == "Error");
    }

    [Fact]
    public void Dashboard_rejects_unbounded_canvas_and_unregistered_component()
    {
        var document = new JsonObject
        {
            ["schemaVersion"] = 1,
            ["canvas"] = new JsonObject { ["width"] = 200, ["height"] = 200, ["fit"] = "Stretch" },
            ["parameters"] = new JsonArray(), ["instances"] = new JsonArray(),
            ["components"] = new JsonArray
            {
                new JsonObject { ["id"] = "x", ["type"] = "ForeignWidget", ["registryVersion"] = "9", ["x"] = 0, ["y"] = 0, ["width"] = 10, ["height"] = 10, ["rotation"] = 0, ["zIndex"] = 1, ["props"] = new JsonObject(), ["bindings"] = new JsonArray(), ["events"] = new JsonArray() },
            },
            ["navigation"] = new JsonArray(),
        };

        var errors = StudioDocuments.Validate("Dashboard", document).Where(item => item.Severity == "Error").ToArray();
        Assert.Contains(errors, item => item.Path == "canvas.width");
        Assert.Contains(errors, item => item.Path == "canvas.fit");
        Assert.Contains(errors, item => item.Path == "components.x.type");
    }

    [Fact]
    public void Dashboard_accepts_canonical_instance_rows_binding_and_navigation_rule_ids()
    {
        var document = new JsonObject
        {
            ["schemaVersion"] = 1,
            ["canvas"] = new JsonObject { ["width"] = 1920, ["height"] = 1080, ["fit"] = "Contain" },
            ["parameters"] = new JsonArray
            {
                new JsonObject { ["name"] = "lineId", ["type"] = "String", ["required"] = true, ["nullable"] = false, ["source"] = "Caller", ["maxLength"] = 40 },
            },
            ["instances"] = new JsonArray
            {
                new JsonObject { ["instanceId"] = "lines", ["datasetNId"] = "dataset-lines", ["revisionNId"] = "revision-lines", ["refreshSeconds"] = 5, ["staleAfterSeconds"] = 15, ["dependsOn"] = new JsonArray() },
            },
            ["components"] = new JsonArray
            {
                new JsonObject
                {
                    ["id"] = "lines-table", ["type"] = "Table", ["registryVersion"] = "1", ["name"] = "产线",
                    ["x"] = 0, ["y"] = 0, ["width"] = 600, ["height"] = 300, ["rotation"] = 0, ["zIndex"] = 1,
                    ["props"] = new JsonObject { ["presentation"] = "Table", ["rowKeyPath"] = new JsonArray("id"), ["columns"] = new JsonArray() },
                    ["bindings"] = new JsonArray { new JsonObject { ["property"] = "rows", ["source"] = new JsonObject { ["from"] = "Instance", ["instanceId"] = "lines", ["path"] = new JsonArray(), ["nullPolicy"] = "Error" } } },
                    ["events"] = new JsonArray("line-click"),
                },
            },
            ["navigation"] = new JsonArray
            {
                new JsonObject
                {
                    ["id"] = "line-click", ["componentId"] = "lines-table", ["event"] = "RowClick",
                    ["target"] = new JsonObject { ["kind"] = "Dashboard", ["resourceNId"] = "dashboard-detail" },
                    ["parameterMap"] = new JsonArray { new JsonObject { ["target"] = "lineId", ["source"] = new JsonObject { ["from"] = "Row", ["path"] = new JsonArray("line_code"), ["nullPolicy"] = "Error" } } },
                    ["openMode"] = "Current",
                },
            },
        };

        Assert.DoesNotContain(StudioDocuments.Validate("Dashboard", document), item => item.Severity == "Error");
    }

    [Theory]
    [InlineData(32, 40, 60, true)]
    [InlineData(16, 25, 0, true)]
    [InlineData(15, 40, 60, false)]
    [InlineData(201, 40, 60, false)]
    [InlineData(32, 70, 40, false)]
    [InlineData(32, -1, 40, false)]
    public void Table_layout_validates_row_height_and_percentage_widths(double height, double first, double second, bool valid)
    {
        var columns = new JsonArray(new JsonObject { ["field"]="name", ["widthPercent"]=first });
        columns.Add(second == 0 ? new JsonObject { ["field"]="value" } : new JsonObject { ["field"]="value", ["widthPercent"]=second });
        var document = new JsonObject
        {
            ["schemaVersion"]=1, ["canvas"]=new JsonObject { ["width"]=1920,["height"]=1080,["fit"]="Contain" },
            ["parameters"]=new JsonArray(), ["instances"]=new JsonArray(), ["navigation"]=new JsonArray(),
            ["components"]=new JsonArray(new JsonObject
            {
                ["id"]="table", ["type"]="Table",["registryVersion"]="1", ["name"]="Table",
                ["x"]=0,["y"]=0,["width"]=600,["height"]=300,["rotation"]=0,["zIndex"]=0,
                ["props"]=new JsonObject { ["presentation"]="Table",["rowHeight"]=height,["columns"]=columns },
                ["bindings"]=new JsonArray(),["events"]=new JsonArray(),
            }),
        };
        Assert.Equal(valid, !StudioDocuments.Validate("Dashboard", document).Any(item=>item.Severity=="Error"));
    }

    [Fact]
    public void Playlist_contract_limits_preload_and_failure_policy()
    {
        var document = new JsonObject
        {
            ["schemaVersion"] = 1,
            ["parameters"] = new JsonArray(),
            ["items"] = new JsonArray
            {
                new JsonObject { ["id"] = "one", ["dashboardNId"] = "dashboard-1", ["revisionNId"] = "rev-1", ["parameterMap"] = new JsonArray(), ["staySeconds"] = 20, ["enabled"] = true },
            },
            ["loop"] = true, ["onFailure"] = "Pause", ["preloadCount"] = 1,
        };

        Assert.DoesNotContain(StudioDocuments.Validate("Playlist", document), item => item.Severity == "Error");
        document["preloadCount"] = 2;
        Assert.Contains(StudioDocuments.Validate("Playlist", document), item => item.Path == "preloadCount");
    }
}
