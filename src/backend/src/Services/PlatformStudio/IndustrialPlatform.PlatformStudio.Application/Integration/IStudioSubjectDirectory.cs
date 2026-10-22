using IndustrialPlatform.PlatformStudio.Domain;
namespace IndustrialPlatform.PlatformStudio.Application.Integration;

/// <summary>授予前核验真实受信主体，不允许任意未验证用户标识。</summary>
public interface IStudioSubjectDirectory
{
    Task<bool> ExistsAsync(StudioPrincipal principal,string subjectKind,string subjectNId,CancellationToken cancellationToken);
}

/// <summary>平台可信工厂范围的公开消费端口；未接入提供者时范围为空。</summary>
public interface IStudioFactoryScope
{
    Task<IReadOnlyList<string>> GetAsync(string tenantNId,string userNId,CancellationToken cancellationToken);
}
public sealed class EmptyStudioFactoryScope : IStudioFactoryScope
{
    public Task<IReadOnlyList<string>> GetAsync(string tenantNId,string userNId,CancellationToken cancellationToken)=>Task.FromResult<IReadOnlyList<string>>([]);
}
