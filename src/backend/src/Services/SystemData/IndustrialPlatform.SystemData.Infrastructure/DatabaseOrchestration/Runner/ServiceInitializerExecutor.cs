using IndustrialPlatform.SystemData.Application.DatabaseOrchestration.Runner;
using IndustrialPlatform.SystemData.Domain.DatabaseOrchestration;

namespace IndustrialPlatform.SystemData.Infrastructure.DatabaseOrchestration.Runner;

/// <summary>
/// 服务 initializer 包执行器(TASK-SD-004,蓝图 §5.2/§5.3):只传非敏感上下文的受控适配器,
/// 禁止任意 SQL/路径/命令。每个步骤必须是 <see cref="AllowedStepKinds"/> 白名单指令;
/// 现仅支持 <c>bootstrap-secret</c>(校验请求中已解析的 Secret 值存在,绝不持久化 Secret 值),
/// 其余指令一律失败(SD_INIT_INITIALIZER_FAILED)。记账语义同 SQL 包:
/// 同版本同校验和幂等成功、校验和漂移拒绝、升级追加新版本。
/// 服务自有 initializer 逻辑(如真实 Secret Provider 解析)在后续服务侧扩展,本实现只保证账本边界与白名单。
/// </summary>
public sealed class ServiceInitializerExecutor : ISeedExecutor
{
    /// <summary>白名单指令集(禁止任意 SQL/路径/命令/可执行内容)。</summary>
    private static readonly HashSet<string> AllowedStepKinds = new(StringComparer.Ordinal)
    {
        "bootstrap-secret",
    };

    /// <inheritdoc />
    public SeedExecutorKind Kind => SeedExecutorKind.ServiceInitializer;

    /// <inheritdoc />
    public Task<SeedLedgerEntry?> ReadLedgerAsync(SeedLedgerQuery query, CancellationToken cancellationToken) =>
        TargetSeedLedger.FindAsync(query.Connection, query.ModuleKey, query.SeedKey, query.SeedVersion, cancellationToken);

    /// <inheritdoc />
    public async Task<SeedExecutionResult> ExecuteAsync(SeedExecutionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

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

            return new SeedExecutionResult(
                false,
                SeedStatus.Failed,
                null,
                DatabaseOrchestrationRunnerErrors.SeedChecksumDrift,
                $"种子 {request.Seed.SeedKey} 同版本校验和漂移,拒绝执行。");
        }

        // 白名单校验:每个步骤必须是被允许的 initializer 指令;bootstrap-secret 校验 Secret 已解析。
        foreach (var step in request.Artifact.Steps.OrderBy(step => step.Sequence))
        {
            if (!AllowedStepKinds.Contains(step.StepId))
            {
                return new SeedExecutionResult(
                    false,
                    SeedStatus.Failed,
                    null,
                    DatabaseOrchestrationRunnerErrors.SeedFailed,
                    $"initializer 步骤 {step.StepId} 不在白名单,拒绝执行。");
            }

            if (string.Equals(step.StepId, "bootstrap-secret", StringComparison.Ordinal)
                && string.IsNullOrWhiteSpace(request.SecretValue))
            {
                return new SeedExecutionResult(
                    false,
                    SeedStatus.Failed,
                    null,
                    DatabaseOrchestrationRunnerErrors.BootstrapSecretMissing,
                    $"bootstrap 种子 {request.Seed.SeedKey} 缺少已解析的 Secret 值。");
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
