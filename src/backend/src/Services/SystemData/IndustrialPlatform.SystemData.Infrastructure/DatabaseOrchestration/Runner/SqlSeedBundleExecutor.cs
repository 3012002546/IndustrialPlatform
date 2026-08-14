using IndustrialPlatform.SystemData.Application.DatabaseOrchestration.Runner;
using IndustrialPlatform.SystemData.Domain.DatabaseOrchestration;

namespace IndustrialPlatform.SystemData.Infrastructure.DatabaseOrchestration.Runner;

/// <summary>
/// 签名 SQL 种子包执行器(TASK-SD-004,蓝图 §5.2/§5.3):在目标库事务内按序执行种子包步骤并记账。
/// 账本幂等:同 (module_key, seed_key, seed_version) 已应用且校验和一致 → 幂等成功;
/// 校验和不同 → drift 拒绝(返回失败不盲重);未应用 → 事务执行+记账,失败整体回滚(可验证边界重试)。
/// 产物已由 Runner 解析并校验,本执行器只执行请求携带的已校验步骤。
/// </summary>
public sealed class SqlSeedBundleExecutor : ISeedExecutor
{
    /// <inheritdoc />
    public SeedExecutorKind Kind => SeedExecutorKind.SqlBundle;

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

        var write = new SeedLedgerWrite(
            request.Seed.SeedKey,
            request.Seed.SeedVersion,
            request.Seed.SeedChecksum,
            request.Seed.Scope,
            SeedStatus.Applied,
            DateTimeOffset.UtcNow,
            request.OperationNId,
            request.TraceId);
        var appliedOn = await TargetSeedLedger.ApplySqlBundleAsync(
            request.Connection,
            request.ModuleKey,
            request.Artifact.Steps,
            write,
            cancellationToken);
        return new SeedExecutionResult(true, SeedStatus.Applied, appliedOn, null, "种子已执行并记账。");
    }
}
