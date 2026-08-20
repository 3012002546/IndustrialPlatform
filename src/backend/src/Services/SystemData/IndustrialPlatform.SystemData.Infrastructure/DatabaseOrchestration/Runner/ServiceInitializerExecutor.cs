using IndustrialPlatform.Application.Abstractions.Initialization;
using IndustrialPlatform.SystemData.Application.DatabaseOrchestration.Initialization;
using IndustrialPlatform.SystemData.Application.DatabaseOrchestration.Runner;
using IndustrialPlatform.SystemData.Domain.DatabaseOrchestration;

namespace IndustrialPlatform.SystemData.Infrastructure.DatabaseOrchestration.Runner;

/// <summary>
/// SystemData Runner 的服务初始化适配器。生产 DI 路径只调用初始化端口并写入脱敏目标账本；
/// 无端口的无参构造仅保留旧测试夹具的白名单兼容行为。
/// </summary>
public sealed class ServiceInitializerExecutor : ISeedExecutor
{
    private static readonly HashSet<string> LegacyAllowedStepKinds = new(StringComparer.Ordinal)
    {
        "bootstrap-secret",
    };

    private readonly IServiceInitializationInvoker? _invoker;

    public ServiceInitializerExecutor()
    {
    }

    public ServiceInitializerExecutor(IServiceInitializationInvoker invoker)
    {
        _invoker = invoker ?? throw new ArgumentNullException(nameof(invoker));
    }

    public SeedExecutorKind Kind => SeedExecutorKind.ServiceInitializer;

    public Task<SeedLedgerEntry?> ReadLedgerAsync(SeedLedgerQuery query, CancellationToken cancellationToken) =>
        TargetSeedLedger.FindAsync(query.Connection, query.ModuleKey, query.SeedKey, query.SeedVersion, cancellationToken);

    public Task<SeedExecutionResult> ExecuteAsync(SeedExecutionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return _invoker is null
            ? ExecuteLegacyAsync(request, cancellationToken)
            : ExecuteThroughPortAsync(request, cancellationToken);
    }

    private async Task<SeedExecutionResult> ExecuteThroughPortAsync(
        SeedExecutionRequest request,
        CancellationToken cancellationToken)
    {
        await TargetSeedLedger.EnsureTableAsync(request.Connection, request.ModuleKey, cancellationToken);
        var existing = await TargetSeedLedger.FindAsync(
            request.Connection,
            request.ModuleKey,
            request.Seed.SeedKey,
            request.Seed.SeedVersion,
            cancellationToken);
        if (existing is not null)
        {
            return string.Equals(existing.Checksum, request.Seed.SeedChecksum, StringComparison.Ordinal)
                ? new SeedExecutionResult(true, SeedStatus.Applied, existing.AppliedOn, null, "服务初始化已记账,幂等成功。")
                : new SeedExecutionResult(false, SeedStatus.Failed, null, DatabaseOrchestrationRunnerErrors.SeedChecksumDrift, "服务初始化账本校验和漂移,拒绝执行。");
        }

        var context = new ServiceInitializationContext(
            request.EnvironmentNId,
            request.TenantNId,
            request.OperationNId,
            request.Target.ServiceKey,
            request.ModuleKey,
            request.Target,
            request.Seed.SeedVersion,
            ServiceInitializationPolicy.Standard,
            request.TraceId);
        try
        {
            var inspection = await _invoker!.InspectAsync(context, cancellationToken);
            var plan = await _invoker.PlanAsync(context, inspection, cancellationToken);
            var applied = plan.RequiresApply
                ? await _invoker.ApplyAsync(context, plan, cancellationToken)
                : inspection;
            var verified = await _invoker.VerifyAsync(context, cancellationToken);
            if (!verified.Ready || !applied.Ready)
            {
                return new SeedExecutionResult(false, SeedStatus.Failed, null, DatabaseOrchestrationRunnerErrors.SeedFailed, verified.Reason ?? "服务初始化未达到 Ready。");
            }

            var write = new SeedLedgerWrite(
                request.Seed.SeedKey,
                request.Seed.SeedVersion,
                request.Seed.SeedChecksum,
                request.Seed.Scope,
                SeedStatus.Applied,
                DateTimeOffset.UtcNow,
                request.OperationNId,
                request.TraceId);
            await TargetSeedLedger.InsertAsync(request.Connection, request.ModuleKey, write, cancellationToken);
            return new SeedExecutionResult(true, SeedStatus.Applied, write.AppliedOn, null, "服务初始化已执行并记账。");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return new SeedExecutionResult(false, SeedStatus.Failed, null, DatabaseOrchestrationRunnerErrors.SeedFailed, "服务初始化调用失败。");
        }
    }

    private static async Task<SeedExecutionResult> ExecuteLegacyAsync(
        SeedExecutionRequest request,
        CancellationToken cancellationToken)
    {
        await TargetSeedLedger.EnsureTableAsync(request.Connection, request.ModuleKey, cancellationToken);
        var existing = await TargetSeedLedger.FindAsync(
            request.Connection,
            request.ModuleKey,
            request.Seed.SeedKey,
            request.Seed.SeedVersion,
            cancellationToken);
        if (existing is not null)
        {
            if (string.Equals(existing.Checksum, request.Seed.SeedChecksum, StringComparison.Ordinal))
            {
                return new SeedExecutionResult(true, SeedStatus.Applied, existing.AppliedOn, null, "种子已应用,幂等成功。");
            }

            return new SeedExecutionResult(false, SeedStatus.Failed, null, DatabaseOrchestrationRunnerErrors.SeedChecksumDrift, "种子同版本校验和漂移,拒绝执行。");
        }

        foreach (var step in request.Artifact.Steps.OrderBy(step => step.Sequence))
        {
            if (!LegacyAllowedStepKinds.Contains(step.StepId))
            {
                return new SeedExecutionResult(false, SeedStatus.Failed, null, DatabaseOrchestrationRunnerErrors.SeedFailed, $"initializer 步骤 {step.StepId} 不在白名单,拒绝执行。");
            }

            if (string.Equals(step.StepId, "bootstrap-secret", StringComparison.Ordinal)
                && string.IsNullOrWhiteSpace(request.SecretValue))
            {
                return new SeedExecutionResult(false, SeedStatus.Failed, null, DatabaseOrchestrationRunnerErrors.BootstrapSecretMissing, "bootstrap 种子缺少已解析的 Secret 值。");
            }
        }

        var write = new SeedLedgerWrite(
            request.Seed.SeedKey,
            request.Seed.SeedVersion,
            request.Seed.SeedChecksum,
            request.Seed.Scope,
            SeedStatus.Applied,
            DateTimeOffset.UtcNow,
            request.OperationNId,
            request.TraceId);
        await TargetSeedLedger.InsertAsync(request.Connection, request.ModuleKey, write, cancellationToken);
        return new SeedExecutionResult(true, SeedStatus.Applied, write.AppliedOn, null, "initializer 已执行并记账。");
    }
}
