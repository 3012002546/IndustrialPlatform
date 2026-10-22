using System.Text.Json;
using System.Text.Json.Nodes;
using IndustrialPlatform.PlatformStudio.Domain;
using IndustrialPlatform.PlatformStudio.Infrastructure.Initialization;
using IndustrialPlatform.PlatformStudio.Infrastructure.Persistence;

namespace IndustrialPlatform.PlatformStudio.Host;

/// <summary>独立宿主受控运维入口；只输出脱敏检查，不启动HTTP或后台处理器。</summary>
public sealed class StudioCommandLine(StudioDeploymentProfile profile, StudioDatabase database, StudioServiceInitializer initializer, TimeProvider clock)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static readonly string[] Commands = ["--studio-inspect", "--studio-initialize", "--studio-check-change"];
    public sealed record Check(string Key, string Status, string? ReasonCode, DateTimeOffset CheckedOn);
    public sealed record MigrationStep(string ModuleKey, string StepId, string Checksum, string Status);
    public sealed record Result(bool Ok, string ProfileVersion, string[] Modules, Check[] Checks, MigrationStep[] MigrationPlan);

    public async Task<int?> ExecuteAsync(string[] args, TextWriter output, CancellationToken cancellationToken)
    {
        var selected = args.Where(argument => Commands.Contains(argument, StringComparer.Ordinal)).ToArray();
        if (selected.Length == 0) return null;
        var checks = new List<Check>();
        var plan = initializer.Migrations().Select(step => new MigrationStep(Module(step.Id), step.Id, step.Checksum, "Unknown")).ToArray();
        try
        {
            if (selected.Length != 1 || args.Any(argument => argument is "--studio-create-admin" or "--studio-reset-admin" or "--studio-disable-admin"))
                throw new StudioException("STUDIO_VALIDATION", "一次只允许一个运维命令。");
            if (selected[0] == "--studio-initialize") await initializer.InitializeLocalAsync(cancellationToken);
            var state = await initializer.InspectLocalAsync(cancellationToken);
            checks.Add(new("schema", state.Ready ? "Ready" : "Unavailable", state.Ready ? null : "STUDIO_NOT_READY", clock.GetUtcNow()));
            if (state.Ready) plan = plan.Select(step => step with { Status = "Applied" }).ToArray();
            if (selected[0] == "--studio-check-change")
            {
                var index = Array.IndexOf(args, selected[0]);
                if (index + 1 >= args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal)) throw new StudioException("STUDIO_VALIDATION", "缺少新配置文件。");
                if (state.Ready) await CheckChangeAsync(await ReadProfileAsync(args[index + 1], cancellationToken), cancellationToken);
                checks.Add(new("change", state.Ready ? "Ready" : "Unavailable", state.Ready ? null : "STUDIO_NOT_READY", clock.GetUtcNow()));
            }
            return await WriteAsync(output, new(state.Ready, profile.ProfileVersion, profile.ActiveModules, checks.ToArray(), plan));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            checks.Add(new("command", "Unavailable", exception is StudioException studio ? studio.Code : "STUDIO_COMMAND_FAILED", clock.GetUtcNow()));
            return await WriteAsync(output, new(false, profile.ProfileVersion, profile.ActiveModules, checks.ToArray(), plan));
        }
    }

    public static async Task<StudioDeploymentProfile> ReadProfileAsync(string path, CancellationToken cancellationToken)
    {
        var file = new FileInfo(path);
        if (!file.Exists || file.Length > 4 * 1024 * 1024) throw new StudioException("STUDIO_VALIDATION", "配置文件不存在或超预算。");
        await using var stream = file.OpenRead();
        var node = await JsonNode.ParseAsync(stream, cancellationToken: cancellationToken);
        return (node?["PlatformStudio"] ?? node)?.Deserialize<StudioDeploymentProfile>(Json) ?? throw new StudioException("STUDIO_VALIDATION", "配置结构非法。");
    }

    public async Task CheckChangeAsync(StudioDeploymentProfile next, CancellationToken cancellationToken)
    {
        var validation = JsonSerializer.Deserialize<StudioDeploymentProfile>(Serialize(next), Json)!;
        validation.Enabled = true;
        validation.Validate();
        if (next.Enabled && next.ActiveModules.Any(module => module != "LowCode")) throw new StudioException("STUDIO_MODULE_UNAVAILABLE", "当前交付物只提供LowCode。");
        if (StudioJson.Version(next.ProfileVersion) <= StudioJson.Version(profile.ProfileVersion)) throw new StudioException("STUDIO_VERSION_CONFLICT", "新部署版本必须递增。", 409);
        if (next.DeploymentMode != profile.DeploymentMode || next.Identity.Provider != profile.Identity.Provider || Serialize(next.Database) != Serialize(profile.Database))
            throw new StudioException("STUDIO_DEPLOYMENT_MIGRATION_REQUIRED", "模式和持久目标改变需要显式迁移。", 409);
        if (Serialize(next.Identity) != Serialize(profile.Identity) && StudioJson.Version(next.Identity.PolicyVersion) <= StudioJson.Version(profile.Identity.PolicyVersion))
            throw new StudioException("STUDIO_VERSION_CONFLICT", "身份策略改变必须递增PolicyVersion。", 409);
        if (next.Files.Provider != profile.Files.Provider || next.Files.Root != profile.Files.Root || next.Audit.Provider != profile.Audit.Provider)
            throw new StudioException("STUDIO_DEPLOYMENT_MIGRATION_REQUIRED", "文件或审计持久装配改变需要显式迁移。", 409);
        var removed = profile.ActiveModules.Except(next.Enabled ? next.ActiveModules : [], StringComparer.Ordinal).ToArray();
        if (removed.Length == 0) return;
        if (await database.Client.Queryable<StudioOperationReceipt>().Where(receipt => receipt.Status == "Pending" || receipt.Status == "Unknown").AnyAsync(cancellationToken))
            throw new StudioException("STUDIO_MODULE_BUSY", "存在未终结业务动作。", 409);
        // 仅核验已持久事实；不注册或模拟尚未交付的Report/Office处理器。
        if (removed.Contains("Report", StringComparer.Ordinal))
        {
            if (await database.Client.Ado.GetIntAsync($"SELECT COUNT(*) FROM {database.Table("studio_report_run")} WHERE status IN ('PendingDispatch','Queued','Running','CancelRequested','Unknown')", new { }, cancellationToken) > 0
                || await database.Client.Ado.GetIntAsync($"SELECT COUNT(*) FROM {database.Table("studio_workbook_session")} WHERE mode='Edit' AND status IN ('Active','Saving','Conflict')", new { }, cancellationToken) > 0)
                throw new StudioException("STUDIO_MODULE_BUSY", "存在未终结报告或写会话。", 409);
        }
        var resources = await database.Client.Queryable<StudioResource>().Where(resource => !resource.IsDeleted && resource.IsEnabled && resource.PublishedRevisionId != null).ToListAsync(cancellationToken);
        var revisions = await database.Client.Queryable<StudioRevision>().ToListAsync(cancellationToken);
        foreach (var resource in resources.Where(resource => next.Enabled && next.IsKindEnabled(resource.Kind)))
        {
            var revision = revisions.SingleOrDefault(item => item.Id == resource.PublishedRevisionId && item.ResourceId == resource.Id && item.TenantNId == resource.TenantNId)
                ?? throw new StudioException("STUDIO_SCHEMA_DRIFT", "发布指向缺失。", 409);
            var dependencies = JsonNode.Parse(revision.DependenciesJson) as JsonArray ?? throw new StudioException("STUDIO_SCHEMA_DRIFT", "依赖结构非法。", 409);
            foreach (var dependency in dependencies)
            {
                if (dependency is not JsonObject item) throw new StudioException("STUDIO_SCHEMA_DRIFT", "依赖结构非法。", 409);
                var target = resources.SingleOrDefault(candidate => candidate.TenantNId == resource.TenantNId && candidate.NId == StudioJson.RequiredString(item, "resourceNId"));
                if (target is null || !next.IsKindEnabled(target.Kind)) throw new StudioException("STUDIO_MODULE_BUSY", "仍启用资源引用将停用的模块。", 409);
            }
        }
    }

    private static string Serialize<T>(T value) => StudioJson.Canonicalize(JsonSerializer.SerializeToNode(value, Json));
    private static string Module(string step) => step.StartsWith("STF-", StringComparison.Ordinal) ? "studio-files" : step.StartsWith("STA-", StringComparison.Ordinal) ? "studio-audit" : "studio-core";
    private static async Task<int> WriteAsync(TextWriter output, Result result)
    {
        await output.WriteLineAsync(JsonSerializer.Serialize(result, Json));
        return result.Ok ? 0 : 1;
    }
}
