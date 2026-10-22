using System.Text.Json.Nodes;
using IndustrialPlatform.PlatformStudio.Api;
using IndustrialPlatform.PlatformStudio.Application;
using IndustrialPlatform.PlatformStudio.Application.Data;
using IndustrialPlatform.PlatformStudio.Application.Ledger;
using IndustrialPlatform.PlatformStudio.Application.Publishing;
using IndustrialPlatform.PlatformStudio.Contracts;
using IndustrialPlatform.PlatformStudio.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace IndustrialPlatform.PlatformStudio.Tests;

public sealed class StudioActionRegressionTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;
    private static readonly string[] ReaderCapabilities = ["View", "Run"];

    [Fact]
    public async Task Direct_ledger_receipt_cannot_be_reused_as_another_page_actions_success()
    {
        await using var harness = await Harness.CreateAsync();
        var first = await harness.PublishAsync("LedgerModel", "ledger-a", StudioPublishingTests.Model());
        var second = await harness.PublishAsync("LedgerModel", "ledger-b", StudioPublishingTests.Model());
        await harness.Ledger.WriteAsync(harness.Principal, "ledger-a", null, new(new() { ["name"] = "direct" }, first.NId, NId: "direct-row"), "shared-key", Ct);
        var page = await harness.PublishAsync("LowCodePage", "page-b", LedgerPage("ledger-b", second.NId, "page-row", "page-value"));
        var result = await harness.Pages.ExecuteActionAsync(harness.Principal, "page-b", "save", new(page.NId), "shared-key", Ct);
        Assert.Equal("Succeeded", result["status"]!.GetValue<string>());
        Assert.Equal("page-row", result["result"]!["nId"]!.GetValue<string>());
        Assert.Equal("page-value", (await harness.Ledger.GetAsync(harness.Principal, "ledger-b", "page-row", second.NId, Ct)).Values["name"]!.GetValue<string>());
        var receipt = (await harness.Fixture.Store.FindReceiptAsync(harness.Principal.TenantNId, "PageAction", "shared-key", Ct))!;
        Assert.NotNull(await harness.Fixture.Store.FindReceiptAsync(harness.Principal.TenantNId, "LedgerWrite", "page-" + receipt.NId, Ct));
    }

    [Fact]
    public async Task Pending_executor_dispatches_once_and_every_lookup_uses_the_same_persistent_child_key()
    {
        var action = new DeferredAction();
        await using var harness = await Harness.CreateAsync(action);
        var page = await harness.PublishAsync("LowCodePage", "pending-page", ActionPage(action.Key));
        var result = await harness.Pages.ExecuteActionAsync(harness.Principal, "pending-page", "save", new(page.NId), "pending-key", Ct);
        Assert.Equal("Pending", result["status"]!.GetValue<string>()); Assert.Null(result["errorCode"]);
        var repeated = await Assert.ThrowsAsync<StudioException>(() => harness.Pages.ExecuteActionAsync(harness.Principal, "pending-page", "save", new(page.NId), "pending-key", Ct));
        Assert.Equal("STUDIO_OPERATION_UNKNOWN", repeated.Code);
        Assert.Equal("Pending", (await harness.Pages.GetOperationAsync(harness.Principal, "pending-key", Ct))["status"]!.GetValue<string>());
        var child = Assert.Single(action.DispatchedKeys);
        Assert.StartsWith("page-receipt-", child, StringComparison.Ordinal); Assert.NotEqual("pending-key", child);
        Assert.True(action.LookupKeys.Count >= 3); Assert.All(action.LookupKeys, key => Assert.Equal(child, key));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Completed_action_rechecks_the_model_grant_and_ledger_read_permission(bool revokeGrant)
    {
        await using var harness = await Harness.CreateAsync();
        var model = await harness.PublishAsync("LedgerModel", "protected-model", StudioPublishingTests.Model());
        var page = await harness.PublishAsync("LowCodePage", "protected-page", LedgerPage("protected-model", model.NId, "protected-row", "sensitive-value"));
        var secondary = harness.Principal with { SubjectNId = "secondary-user" };
        await harness.GrantRunAsync("protected-model", secondary.SubjectNId);
        await harness.GrantRunAsync("protected-page", secondary.SubjectNId);
        harness.Identity.Principal = secondary;
        Assert.Equal("Succeeded", (await harness.Pages.ExecuteActionAsync(secondary, "protected-page", "save", new(page.NId), "protected-key", Ct))["status"]!.GetValue<string>());
        if (revokeGrant)
        {
            var resource = (await harness.Fixture.Store.FindResourceAsync(secondary.TenantNId, "protected-model", Ct))!;
            var grants = await harness.Fixture.Store.GetGrantsAsync(secondary.TenantNId, resource.Id, Ct);
            await harness.Fixture.Store.ReplaceGrantsAsync(secondary.TenantNId, resource.Id, grants.Where(grant => grant.SubjectNId != secondary.SubjectNId).ToArray(), Ct);
        }
        else harness.Identity.Principal = secondary with { Permissions = secondary.Permissions.Where(permission => permission != "studio.ledger.read").ToArray() };
        var denied = await Assert.ThrowsAsync<StudioException>(() => harness.Pages.GetOperationAsync(harness.Identity.Principal, "protected-key", Ct));
        Assert.Equal(revokeGrant ? 404 : 403, denied.StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("99")]
    public async Task Real_execute_cannot_bypass_required_when_or_range_validation(string? invalidValue)
    {
        var action = new DeferredAction { ExecuteStatus = "Succeeded" };
        await using var harness = await Harness.CreateAsync(action);
        var document = ActionPage(action.Key);
        document["layout"]!["children"] = new JsonArray(new JsonObject { ["id"] = "quantity", ["type"] = "FormField", ["registryVersion"] = "1", ["props"] = new JsonObject { ["fieldId"] = "quantity", ["control"] = "Number" } });
        document["fields"] = new JsonArray(new JsonObject
        {
            ["componentId"] = "quantity", ["property"] = "value", ["source"] = Form("quantity"), ["writeTarget"] = "quantity", ["readOnly"] = false,
            ["requiredWhen"] = new JsonObject { ["op"] = "Eq", ["left"] = Constant(JsonValue.Create(true)), ["right"] = Constant(JsonValue.Create(true)) },
            ["validators"] = new JsonArray(new JsonObject { ["kind"] = "Range", ["options"] = new JsonObject { ["min"] = "1", ["max"] = "10" } }),
        });
        document["actions"]![0]!["inputMap"]![0]!["source"] = Form("quantity");
        var page = await harness.PublishAsync("LowCodePage", "validated-page", document);
        var invalidForm = new JsonObject(); if (invalidValue is not null) invalidForm["quantity"] = invalidValue;
        var invalid = new StudioPageInput(page.NId, Context: new() { ["form"] = invalidForm });
        var error = await Assert.ThrowsAsync<StudioException>(() => harness.Pages.ExecuteActionAsync(harness.Principal, "validated-page", "save", invalid, "invalid-value", Ct));
        Assert.Equal("STUDIO_VALIDATION", error.Code); Assert.Equal("context.form.quantity", error.Path); Assert.Empty(action.DispatchedKeys);
        Assert.Null(await harness.Fixture.Store.FindReceiptAsync(harness.Principal.TenantNId, "PageAction", "invalid-value", Ct));
        var valid = new StudioPageInput(page.NId, Context: new() { ["form"] = new JsonObject { ["quantity"] = "5" } });
        Assert.Equal("Succeeded", (await harness.Pages.ExecuteActionAsync(harness.Principal, "validated-page", "save", valid, "valid-value", Ct))["status"]!.GetValue<string>());
        Assert.Single(action.DispatchedKeys);
    }

    [Fact]
    public async Task Late_pending_reconciliation_cannot_overwrite_a_succeeded_receipt()
    {
        var action = new DeferredAction { ExecuteStatus = "Succeeded" };
        await using var harness = await Harness.CreateAsync(action);
        var page = await harness.PublishAsync("LowCodePage", "terminal-page", ActionPage(action.Key));
        var first = await harness.Pages.ExecuteActionAsync(harness.Principal, "terminal-page", "save", new(page.NId), "terminal-key", Ct);
        Assert.Equal("Succeeded", first["status"]!.GetValue<string>());
        action.Known = new("Pending", new JsonObject { ["value"] = "stale" });
        var reconciled = await harness.Pages.GetOperationAsync(harness.Principal, "terminal-key", Ct);
        Assert.Equal("Succeeded", reconciled["status"]!.GetValue<string>());
        Assert.Equal("stable", reconciled["result"]!["value"]!.GetValue<string>());
        var stored = (await harness.Fixture.Store.FindReceiptAsync(harness.Principal.TenantNId, "PageAction", "terminal-key", Ct))!;
        Assert.Equal("Succeeded", stored.Status); Assert.Contains("stable", stored.ResponseJson, StringComparison.Ordinal); Assert.Single(action.DispatchedKeys);
    }

    private static JsonObject Constant(JsonNode? value) => new() { ["from"] = "Constant", ["value"] = value?.DeepClone(), ["nullPolicy"] = "Error" };
    private static JsonObject Form(string field) => new() { ["from"] = "Form", ["path"] = new JsonArray(field), ["nullPolicy"] = "Null" };
    private static JsonObject Map(string target, JsonNode? value) => new() { ["target"] = target, ["source"] = Constant(value) };
    private static JsonObject ActionPage(string key) => new()
    {
        ["schemaVersion"] = 1, ["mode"] = "Compose", ["parameters"] = new JsonArray(), ["instances"] = new JsonArray(), ["fields"] = new JsonArray(),
        ["layout"] = new JsonObject { ["id"] = "root", ["type"] = "Container", ["registryVersion"] = "1", ["props"] = new JsonObject(), ["children"] = new JsonArray() },
        ["actions"] = new JsonArray(new JsonObject { ["id"] = "save", ["label"] = "save", ["event"] = "Submit", ["actionKey"] = key, ["executorVersion"] = "1", ["inputMap"] = new JsonArray(Map("value", JsonValue.Create("input"))) }),
    };
    private static JsonObject LedgerPage(string model, string revision, string nId, string value)
    {
        var document = ActionPage("studio.ledger.save");
        document["actions"]![0]!["inputMap"] = new JsonArray(Map("modelNId", JsonValue.Create(model)), Map("revisionNId", JsonValue.Create(revision)), Map("nId", JsonValue.Create(nId)), Map("values", new JsonObject { ["name"] = value }));
        return document;
    }
    private sealed class DeferredAction : IStudioAction
    {
        public string Key => "test.deferred";
        public string ExecutorVersion => "1";
        public string PermissionKey => "studio.ledger.write";
        public bool Mutating => true;
        public JsonObject InputSchema => JsonNode.Parse("""{"type":"object","required":["value"],"properties":{"value":{"type":"string"}},"additionalProperties":false}""")!.AsObject();
        public JsonObject OutputSchema => new() { ["type"] = "object" };
        public string ExecuteStatus { get; init; } = "Pending";
        public StudioActionResult? Known { get; set; }
        public List<string> DispatchedKeys { get; } = [];
        public List<string> LookupKeys { get; } = [];
        public Task<JsonNode?> PreviewAsync(StudioPrincipal principal, JsonObject input, CancellationToken cancellationToken) => Task.FromResult<JsonNode?>(new JsonObject { ["simulated"] = true });
        public Task<StudioActionResult> ExecuteAsync(StudioPrincipal principal, string operationKey, JsonObject input, CancellationToken cancellationToken) { DispatchedKeys.Add(operationKey); return Task.FromResult(new StudioActionResult(ExecuteStatus, new JsonObject { ["value"] = "stable" })); }
        public Task<StudioActionResult?> GetByOperationAsync(StudioPrincipal principal, string operationKey, CancellationToken cancellationToken) { LookupKeys.Add(operationKey); return Task.FromResult(Known); }
    }
    private sealed class Identity(StudioPrincipal principal) : IStudioIdentity
    {
        public StudioPrincipal Principal { get; set; } = principal;
        public Task<StudioPrincipal> GetCurrentAsync(CancellationToken cancellationToken) => Task.FromResult(Principal);
        public Task<StudioPrincipal> RefreshAsync(StudioPrincipal principal, CancellationToken cancellationToken) => Task.FromResult(Principal);
    }
    private sealed class Files : IStudioFiles
    {
        public bool Enabled => false;
        public Task ValidateReferenceAsync(StudioPrincipal principal, string fileNId, string referenceNId, CancellationToken cancellationToken) => throw new InvalidOperationException("This fixture does not upload files.");
    }
    private sealed class Harness : IAsyncDisposable
    {
        public StudioTestContext Fixture { get; }
        public Identity Identity { get; }
        private ServiceProvider Services { get; }
        public StudioPrincipal Principal => Identity.Principal;
        public StudioPublishing Publishing => Services.GetRequiredService<StudioPublishing>();
        public StudioLedger Ledger => Services.GetRequiredService<StudioLedger>();
        public StudioPages Pages => Services.GetRequiredService<StudioPages>();
        private Harness(StudioTestContext fixture, IStudioAction? extra)
        {
            Fixture = fixture; Identity = new(fixture.Principal);
            var services = new ServiceCollection();
            services.AddSingleton<IStudioResourceStore>(fixture.Store); services.AddSingleton<IStudioAuthorization>(fixture.Authorization); services.AddSingleton(fixture.Commands);
            services.AddSingleton<IStudioIdentity>(Identity); services.AddSingleton<IStudioAction, StudioLedgerSaveAction>();
            if (extra is not null) services.AddSingleton(extra);
            services.AddSingleton<StudioPublishing>(sp => new(fixture.Store, fixture.Authorization, fixture.Commands, fixture.Profile, TimeProvider.System, sp.GetServices<IStudioAction>(), fixture.SourceExecutor));
            services.AddSingleton<StudioLedger>(sp => new(fixture.Store, sp.GetRequiredService<StudioPublishing>(), fixture.Authorization, fixture.Commands, new Files(), TimeProvider.System));
            services.AddSingleton<DatasetExecution>(sp => new(fixture.Store, sp.GetRequiredService<StudioPublishing>(), fixture.Authorization, fixture.SourceExecutor, new(), TimeProvider.System));
            services.AddSingleton<StudioPages>(sp => new(fixture.Store, sp.GetRequiredService<StudioPublishing>(), sp.GetRequiredService<DatasetExecution>(), fixture.Authorization, Identity, sp.GetServices<IStudioAction>(), TimeProvider.System));
            Services = services.BuildServiceProvider();
        }
        public static async Task<Harness> CreateAsync(IStudioAction? extra = null) => new(await StudioTestContext.CreateAsync(), extra);
        public async Task<RevisionDto> PublishAsync(string kind, string nId, JsonObject document)
        {
            var created = await Publishing.CreateAsync(Principal, new(nId, kind, nId, null, null, document), "create-" + nId, Ct);
            var plan = await Publishing.ValidateAsync(Principal, nId, document, Ct);
            Assert.True(plan.Valid, string.Join(";", plan.Diagnostics.Select(diagnostic => diagnostic.Path + ":" + diagnostic.Message)));
            return await Publishing.PublishAsync(Principal, nId, new(created.OptimisticVersion, created.ConcurrencyVersion, "test publication", true, plan.DependencyPlanHash), "publish-" + nId, Ct);
        }
        public async Task GrantRunAsync(string resourceNId, string subject)
        {
            var resource = (await Fixture.Store.FindResourceAsync(Principal.TenantNId, resourceNId, Ct))!;
            var previous = await Fixture.Store.GetGrantsAsync(Principal.TenantNId, resource.Id, Ct);
            await Fixture.Store.ReplaceGrantsAsync(Principal.TenantNId, resource.Id, previous.Concat(ReaderCapabilities.Select(capability => new StudioGrant { NId = "grant-" + Guid.NewGuid().ToString("N"), TenantNId = Principal.TenantNId, ResourceId = resource.Id, SubjectNId = subject, Capability = capability, GrantedBy = Principal.SubjectNId, CreatedOn = DateTimeOffset.UtcNow })).ToArray(), Ct);
        }
        public async ValueTask DisposeAsync() { await Services.DisposeAsync(); await Fixture.DisposeAsync(); }
    }
}
