using IndustrialPlatform.SystemData.Application.Administration;
using IndustrialPlatform.SystemData.Contracts.Administration;

namespace IndustrialPlatform.SystemData.Application.IdentityDirectory;

/// <summary>
/// 用户目录默认实现(fail-closed):真实 Identity 目录适配器由后续任务(契约稳定后)接入。
/// 当前状态下一切新写入走 503 SD_IDENTITY_DIRECTORY_UNAVAILABLE(05 方案 §11.2 不允许降级为
/// 手输未知 UserNId),读取沿用既有显示快照不受影响。测试经 DI 以 <see cref="IIdentityUserDirectory"/> 替身替换。
/// </summary>
public sealed class UnavailableIdentityUserDirectory : IIdentityUserDirectory
{
    /// <inheritdoc />
    public Task<IdentityUserDirectoryEntryV1?> GetAsync(string tenantNId, string userNId, CancellationToken cancellationToken)
    {
        throw new IdentityDirectoryUnavailableException("身份用户目录暂不可用,禁止新建任职。");
    }
}
