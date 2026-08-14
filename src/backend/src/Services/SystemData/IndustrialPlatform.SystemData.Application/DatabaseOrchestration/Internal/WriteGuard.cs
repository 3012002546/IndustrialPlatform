using IndustrialPlatform.SharedKernel.Exceptions;

namespace IndustrialPlatform.SystemData.Application.DatabaseOrchestration.Internal;

/// <summary>写操作守卫:将存储层并发冲突映射为 SD_DB_OPERATION_CONFLICT(409)。</summary>
internal static class WriteGuard
{
    /// <summary>执行写操作,捕获 <see cref="ConcurrencyException"/> 映射为 <see cref="OperationConflictException"/>。</summary>
    public static async Task ExecuteAsync(Func<Task> write)
    {
        try
        {
            await write();
        }
        catch (ConcurrencyException)
        {
            throw new OperationConflictException("数据已被其他操作修改,请刷新后重试。");
        }
    }
}
