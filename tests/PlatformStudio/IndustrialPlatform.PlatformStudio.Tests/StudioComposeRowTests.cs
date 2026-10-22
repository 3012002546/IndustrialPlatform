using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using IndustrialPlatform.PlatformStudio.Application;
using IndustrialPlatform.PlatformStudio.Application.Data;
using IndustrialPlatform.PlatformStudio.Application.Publishing;
using IndustrialPlatform.PlatformStudio.Contracts;
using IndustrialPlatform.PlatformStudio.Domain;
using IndustrialPlatform.PlatformStudio.Infrastructure.Data;
using Microsoft.Extensions.Configuration;

namespace IndustrialPlatform.PlatformStudio.Tests;

public sealed class StudioComposeRowTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    [Fact]
    public async Task Initial_runtime_defers_row_dependencies_and_selection_resolves_nested_authorized_rows_before_detail()
    {
        await using var harness = await Harness.CreateAsync();
        var initial = await harness.RuntimeAsync();
        Assert.Equal(1, harness.Count("master")); Assert.Equal(0, harness.Count("detail")); Assert.Equal(0, harness.Count("follow"));
        Assert.Equal("AwaitingRow", initial["instances"]!["detail"]!["status"]!.GetValue<string>());
        Assert.Equal("AwaitingRow", initial["instances"]!["follow"]!["status"]!.GetValue<string>());
        Assert.All(initial["fields"]!.AsArray(), field =>
        {
            Assert.Equal("AwaitingRow", field!["status"]!.GetValue<string>());
            Assert.Equal("ROW_REQUIRED", field["reasonCode"]!.GetValue<string>());
            Assert.StartsWith("fields[", field["path"]!.GetValue<string>(), StringComparison.Ordinal);
            Assert.Null(field["value"]); Assert.True(field["readOnly"]!.GetValue<bool>());
        });
        var selected = await harness.RuntimeAsync(new() { ["rowKey"] = new JsonObject { ["instanceId"] = "master", ["key"] = "WO1" } });
        Assert.Equal(2, harness.Count("master")); Assert.Equal(1, harness.Count("detail")); Assert.Equal(1, harness.Count("follow"));
        Assert.Equal("WO1", Assert.Single(harness.Requests,request => request.Operation == "detail").Key);
        Assert.Equal("visible row", selected["fields"]![0]!["value"]!.GetValue<string>());
        Assert.Equal("conditional", selected["fields"]![1]!["value"]!.GetValue<string>());
        Assert.Equal("detail WO1", selected["fields"]![2]!["value"]!.GetValue<string>());
        Assert.Null(selected["instances"]!["detail"]!["status"]);
    }

    [Fact]
    public async Task Forged_invisible_row_is_rejected_before_detail_and_bare_client_row_is_never_trusted()
    {
        await using var harness = await Harness.CreateAsync();
        var missing = await Assert.ThrowsAsync<StudioException>(() => harness.RuntimeAsync(new() { ["rowKey"] = new JsonObject { ["instanceId"] = "master", ["key"] = "HIDDEN" } }));
        Assert.Equal(404, missing.StatusCode); Assert.Equal("STUDIO_NOT_FOUND", missing.Code);
        Assert.Equal(1, harness.Count("master")); Assert.Equal(0, harness.Count("detail"));
        var forged = await Assert.ThrowsAsync<StudioException>(() => harness.RuntimeAsync(new() { ["row"] = new JsonObject { ["nId"] = "HIDDEN", ["name"] = "forged" } }));
        Assert.Equal("STUDIO_VALIDATION", forged.Code); Assert.Equal("context.row", forged.Path);
        Assert.Equal(1, harness.Count("master")); Assert.Equal(0, harness.Count("detail"));
    }

    [Fact]
    public async Task Duplicate_authorized_row_keys_return_a_controlled_conflict_before_detail()
    {
        await using var harness = await Harness.CreateAsync(); harness.DuplicateMasterKey = true;
        var duplicate = await Assert.ThrowsAsync<StudioException>(() => harness.RuntimeAsync(new() { ["rowKey"] = new JsonObject { ["instanceId"] = "master", ["key"] = "WO1" } }));
        Assert.Equal("STUDIO_ROW_KEY_AMBIGUOUS", duplicate.Code); Assert.Equal(409, duplicate.StatusCode);
        Assert.Equal("context.rowKey.key", duplicate.Path); Assert.Equal(0, harness.Count("detail"));
    }

    private sealed class Identity(StudioPrincipal principal) : IStudioIdentity
    {
        public Task<StudioPrincipal> GetCurrentAsync(CancellationToken cancellationToken) => Task.FromResult(principal);
        public Task<StudioPrincipal> RefreshAsync(StudioPrincipal current, CancellationToken cancellationToken) => Task.FromResult(principal);
    }

    private sealed class Harness : IAsyncDisposable
    {
        private readonly StudioTestContext _fixture;
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new(TimeSpan.FromSeconds(60));
        private readonly HttpClient _http = new();
        private readonly RegisteredSourceExecutor _executor;
        private readonly StudioPublishing _publishing;
        private readonly StudioPages _pages;
        private readonly Task _server;
        private string _pageRevision = "";
        public ConcurrentQueue<(string Operation, string? Key)> Requests { get; } = new();
        public bool DuplicateMasterKey { get; set; }
        private Harness(StudioTestContext fixture)
        {
            _fixture = fixture; _listener.Start();
            _server = ServeAsync();
            _executor = new(fixture.Profile, new ConfigurationBuilder().Build(), _http, TimeProvider.System, allowLoopbackHttp: true);
            _publishing = new(fixture.Store, fixture.Authorization, fixture.Commands, fixture.Profile, TimeProvider.System, [], _executor);
            var datasets = new DatasetExecution(fixture.Store, _publishing, fixture.Authorization, _executor, new(), TimeProvider.System);
            _pages = new(fixture.Store, _publishing, datasets, fixture.Authorization, new Identity(fixture.Principal), [], TimeProvider.System);
        }
        public static async Task<Harness> CreateAsync()
        {
            var harness = new Harness(await StudioTestContext.CreateAsync());
            try { await harness.InitializeAsync(); return harness; }
            catch { await harness.DisposeAsync(); throw; }
        }
        public int Count(string operation) => Requests.Count(request => request.Operation == operation);
        public Task<JsonObject> RuntimeAsync(JsonObject? context = null) => _pages.RuntimeAsync(_fixture.Principal, "compose-row", new(_pageRevision, Context: context), Ct);
        private async Task InitializeAsync()
        {
            var endpoint = new StudioSourceEndpoint
            {
                BaseAddress = "http://127.0.0.1:" + ((IPEndPoint)_listener.LocalEndpoint).Port.ToString(CultureInfo.InvariantCulture) + "/",
                ReadOnlyVerified = true, ProbeOperationKey = "probe", RowsPath = ["payload", "rows"], FactoryFieldPath = ["factory"],
                Operations = new() { ["probe"] = new() { Path = "probe" }, ["master"] = new() { Path = "master" }, ["detail"] = new() { Path = "detail", RequiredParameters = ["rowId"] }, ["follow"] = new() { Path = "follow", RequiredParameters = ["rowId"] } },
            };
            _fixture.Profile.SourceRegistry["row-api"] = endpoint;
            var admin = new SourceAdministration(_fixture.Store, _fixture.Authorization, _fixture.Commands, _executor, _fixture.Profile, new(), TimeProvider.System);
            var allowed = new JsonArray(endpoint.Operations.Keys.Select(key => (JsonNode)new JsonObject { ["kind"] = "ApiOperation", ["qualifiedName"] = key }).ToArray());
            var source = await admin.CreateAsync(_fixture.Principal, new("row-source", "row source", null, "RegisteredApi", "row-api", null, allowed), "create-source", Ct);
            await admin.TestAsync(_fixture.Principal, source.NId, new(source.OptimisticVersion, source.ConcurrencyVersion), Ct);
            source = await admin.SetEnabledAsync(_fixture.Principal, source.NId, true, new(source.OptimisticVersion, source.ConcurrencyVersion), Ct);
            var master = await PublishAsync("Dataset", "master-data", Dataset(source, "master"));
            var detail = await PublishAsync("Dataset", "detail-data", Dataset(source, "detail"));
            var follow = await PublishAsync("Dataset", "follow-data", Dataset(source, "follow"));
            var document = new JsonObject
            {
                ["schemaVersion"] = 1, ["mode"] = "Compose", ["parameters"] = new JsonArray(), ["actions"] = new JsonArray(),
                // Deliberately place detail first: the DAG, rather than document order, controls execution.
                ["instances"] = new JsonArray(Instance("detail", "detail-data", detail.NId, "master", Binding("Row", "nId")), Instance("follow", "follow-data", follow.NId, "detail", InstanceBinding("detail", "payload", "rows", "0", "nId")), Instance("master", "master-data", master.NId)),
                ["layout"] = new JsonObject
                {
                    ["id"] = "root", ["type"] = "Container", ["registryVersion"] = "1", ["props"] = new JsonObject(),
                    ["children"] = new JsonArray(new JsonObject { ["id"] = "table", ["type"] = "Table", ["registryVersion"] = "1", ["props"] = new JsonObject { ["instanceId"] = "master", ["collectionPath"] = new JsonArray("payload", "rows"), ["rowKeyPath"] = new JsonArray("nId") } }, Text("row-name"), Text("condition"), Text("detail-name")),
                },
                ["fields"] = new JsonArray(
                    new JsonObject { ["componentId"] = "row-name", ["property"] = "text", ["source"] = Binding("Row", "name") },
                    new JsonObject { ["componentId"] = "condition", ["property"] = "text", ["source"] = Constant("conditional"), ["visibleWhen"] = new JsonObject { ["op"] = "Eq", ["left"] = Binding("Row", "nId"), ["right"] = Constant("WO1") } },
                    new JsonObject { ["componentId"] = "detail-name", ["property"] = "text", ["source"] = InstanceBinding("detail", "payload", "rows", "0", "name") }),
            };
            _pageRevision = (await PublishAsync("LowCodePage", "compose-row", document)).NId;
        }
        private async Task<RevisionDto> PublishAsync(string kind, string nId, JsonObject document)
        {
            var created = await _publishing.CreateAsync(_fixture.Principal, new(nId, kind, nId, null, null, document), "create-" + nId, Ct);
            var plan = await _publishing.ValidateAsync(_fixture.Principal, nId, document, Ct);
            Assert.True(plan.Valid, string.Join(";", plan.Diagnostics.Select(diagnostic => diagnostic.Path + ":" + diagnostic.Message)));
            return await _publishing.PublishAsync(_fixture.Principal, nId, new(created.OptimisticVersion, created.ConcurrencyVersion, "row regression", true, plan.DependencyPlanHash), "publish-" + nId, Ct);
        }
        private static JsonObject Dataset(SourceDto source, string operation) => new()
        {
            ["schemaVersion"] = 1, ["sourceNId"] = source.NId, ["sourceConfigurationVersion"] = source.ConfigurationVersion,
            ["query"] = new JsonObject { ["kind"] = "Api", ["operationKey"] = operation, ["method"] = "GET" },
            ["parameters"] = operation == "master" ? new JsonArray() : new JsonArray(new JsonObject { ["name"] = "rowId", ["type"] = "String", ["required"] = true, ["nullable"] = false, ["source"] = "Caller" }),
            ["result"] = JsonNode.Parse("""{"id":"root","name":"root","type":"Object","nullable":false,"children":[{"id":"payload","name":"payload","type":"Object","nullable":false,"children":[{"id":"rows","name":"rows","type":"Array","nullable":false,"item":{"id":"row","name":"row","type":"Object","nullable":false,"children":[{"id":"nId","name":"nId","type":"String","nullable":false},{"id":"name","name":"name","type":"String","nullable":false},{"id":"factory","name":"factory","type":"String","nullable":false}]}}]}]}"""),
            ["rowPolicyKey"] = "authorized-factories", ["readableFieldIds"] = new JsonArray("nId", "name"), ["timeoutSeconds"] = 10, ["maxRows"] = 100, ["maxBytes"] = "1048576", ["cacheSeconds"] = 0, ["freshness"] = new JsonObject { ["mode"] = "AcquisitionOnly" },
        };
        private static JsonObject Instance(string id, string dataset, string revision, string? dependency = null, JsonObject? source = null) => new()
        {
            ["instanceId"] = id, ["datasetNId"] = dataset, ["revisionNId"] = revision, ["dependsOn"] = dependency is null ? new JsonArray() : new JsonArray(dependency),
            ["parameters"] = source is null ? new JsonArray() : new JsonArray(new JsonObject { ["target"] = "rowId", ["source"] = source }),
        };
        private static JsonObject Text(string id) => new() { ["id"] = id, ["type"] = "Text", ["registryVersion"] = "1", ["props"] = new JsonObject() };
        private static JsonObject Constant(string value) => new() { ["from"] = "Constant", ["value"] = value, ["nullPolicy"] = "Error" };
        private static JsonObject Binding(string from, params string[] path) => new() { ["from"] = from, ["path"] = new JsonArray(path.Select(segment => (JsonNode)JsonValue.Create(segment)!).ToArray()), ["nullPolicy"] = "Error" };
        private static JsonObject InstanceBinding(string instance, params string[] path) { var binding = Binding("Instance", path); binding["instanceId"] = instance; return binding; }
        private async Task ServeAsync()
        {
            try
            {
                while (!_stop.IsCancellationRequested)
                {
                    using var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                    await using var stream = client.GetStream(); using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
                    var line = await reader.ReadLineAsync(_stop.Token) ?? throw new IOException("Missing HTTP request.");
                    while (await reader.ReadLineAsync(_stop.Token) is { Length: > 0 }) { }
                    var uri = new Uri("http://localhost" + line.Split(' ')[1]); var operation = uri.AbsolutePath.Trim('/');
                    var key = uri.Query.Length == 0 ? null : Uri.UnescapeDataString(uri.Query.Split('=', 2)[1]);
                    Requests.Enqueue((operation, key));
                    var rows = operation == "master"
                        ? new JsonArray(Row("WO1", "visible row", "factory-a"), Row("HIDDEN", "private row", "factory-b"))
                        : new JsonArray(Row(key ?? "probe", operation + " " + key, "factory-a"));
                    if (operation == "master" && DuplicateMasterKey) rows.Add(Row("WO1", "duplicate", "factory-a"));
                    var body = Encoding.UTF8.GetBytes(new JsonObject { ["payload"] = new JsonObject { ["rows"] = rows } }.ToJsonString());
                    var header = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: " + body.Length.ToString(CultureInfo.InvariantCulture) + "\r\nConnection: close\r\n\r\n");
                    await stream.WriteAsync(header, _stop.Token); await stream.WriteAsync(body, _stop.Token);
                }
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
            catch (SocketException) when (_stop.IsCancellationRequested) { }
        }
        private static JsonObject Row(string nId, string name, string factory) => new() { ["nId"] = nId, ["name"] = name, ["factory"] = factory };
        public async ValueTask DisposeAsync()
        {
            await _stop.CancelAsync(); _listener.Stop(); await _server;
            _executor.Dispose(); _http.Dispose(); _stop.Dispose(); await _fixture.DisposeAsync();
        }
    }
}
