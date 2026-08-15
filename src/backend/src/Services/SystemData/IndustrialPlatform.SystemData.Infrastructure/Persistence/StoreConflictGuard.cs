using IndustrialPlatform.SharedKernel.Exceptions;
using SqlSugar;

namespace IndustrialPlatform.SystemData.Infrastructure.Persistence;

/// <summary>
/// 仓储写冲突守卫(TASK-SD-005):唯一键冲突映射并发异常、双版本原子更新影响行数校验、
/// 沿内部异常链判定唯一约束冲突。必须用具体表类型调用 <see cref="ISqlSugarClient.Insertable"/>
/// (泛型 <c>object</c> 会被 SqlSugar 拒绝);底层驱动可能抛裸 <see cref="System.Data.Common.DbException"/>
/// (SQLite/PostgreSQL)而非 <c>SqlSugarException</c>,故用带过滤器捕获。
/// </summary>
internal static class StoreConflictGuard
{
    /// <summary>插入并守卫唯一键冲突:唯一约束错误映射为并发异常,其余原样抛出。</summary>
    public static async Task InsertWithConflictGuardAsync<T>(
        ISqlSugarClient sugar,
        T row,
        CancellationToken cancellationToken)
        where T : class, new()
    {
        try
        {
            await sugar.Insertable(row).ExecuteCommandAsync(cancellationToken);
        }
        catch (Exception ex) when (IsUniqueConstraintViolation(ex))
        {
            throw new ConcurrencyException("唯一键冲突:记录已存在。", ex);
        }
    }

    /// <summary>双版本原子更新守卫:影响行数非 1 抛并发异常(版本不匹配或记录不存在)。</summary>
    public static void EnsureSingleRowAffected(int affected, string entityType, string operation)
    {
        if (affected != 1)
        {
            throw new ConcurrencyException($"实体 {entityType} {operation}失败:并发版本不匹配或记录不存在。");
        }
    }

    /// <summary>沿内部异常链判定是否为唯一约束/主键冲突(SQLite "UNIQUE constraint failed"、PostgreSQL "duplicate key"/SQLSTATE 23505)。</summary>
    public static bool IsUniqueConstraintViolation(Exception exception)
    {
        for (var ex = exception; ex is not null; ex = ex.InnerException)
        {
            var message = ex.Message;
            if (message.Contains("UNIQUE constraint failed", StringComparison.OrdinalIgnoreCase)
                || message.Contains("duplicate key", StringComparison.OrdinalIgnoreCase)
                || message.Contains("23505", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
