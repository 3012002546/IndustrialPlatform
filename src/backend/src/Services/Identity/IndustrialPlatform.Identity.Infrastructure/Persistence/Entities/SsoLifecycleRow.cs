namespace IndustrialPlatform.Identity.Infrastructure.Persistence.Entities;

/// <summary>
/// SSO 表公共生命周期行契约:供 <c>SsoStore</c> 通用双版本原子更新
/// (WHERE id + is_deleted=false + optimistic_version + concurrency_version)。
/// </summary>
internal interface ISsoLifecycleRow
{
    Guid Id { get; set; }

    bool IsDeleted { get; set; }

    long OptimisticVersion { get; set; }

    Guid ConcurrencyVersion { get; set; }
}
