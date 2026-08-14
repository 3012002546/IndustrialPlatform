namespace IndustrialPlatform.SystemData.Infrastructure.Persistence.Migrations;

/// <summary>
/// 执行 SystemData 库未应用迁移的契约。
/// </summary>
public interface ISchemaMigrationRunner
{
    /// <summary>
    /// 应用所有尚未记录的迁移步骤;幂等,重复调用不会重复应用已应用步骤。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    Task ApplyPendingAsync(CancellationToken cancellationToken = default);
}
