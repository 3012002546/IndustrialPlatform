using IndustrialPlatform.SharedKernel.Exceptions;

namespace IndustrialPlatform.SystemData.Application.Administration;

/// <summary>
/// 管理用例写操作守卫:将存储层双版本并发冲突映射为
/// <see cref="AdministrationConcurrencyConflictException"/>(§9.9 SD_CONCURRENCY_CONFLICT)。
/// 领域层 <see cref="BusinessException"/> 作为兜底映射同一状态冲突码(移动预览过期、
/// 状态不允许等由领域不变量校验),<see cref="ValidationException"/> 映射为 400。
/// </summary>
internal static class AdministrationWriteGuard
{
    /// <summary>执行写操作并统一映射领域/存储并发异常。</summary>
    public static async Task<T> ExecuteAsync<T>(Func<Task<T>> write)
    {
        try
        {
            return await write();
        }
        catch (ConcurrencyException)
        {
            throw new AdministrationConcurrencyConflictException("数据已被其他操作修改,请刷新后重试。");
        }
    }

    /// <summary>执行写操作并统一映射领域/存储并发异常(无返回值)。</summary>
    public static async Task ExecuteAsync(Func<Task> write)
    {
        try
        {
            await write();
        }
        catch (ConcurrencyException)
        {
            throw new AdministrationConcurrencyConflictException("数据已被其他操作修改,请刷新后重试。");
        }
    }
}
