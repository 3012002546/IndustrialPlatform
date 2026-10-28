using IndustrialPlatform.PlatformStudio.Application.Dashboard;
using IndustrialPlatform.PlatformStudio.Domain;

namespace IndustrialPlatform.PlatformStudio.Infrastructure.Persistence;

public sealed class StudioScreenStore(StudioDatabase database) : IStudioScreenStore
{
    public async Task InsertAsync(StudioScreenLink link, CancellationToken ct) =>
        _ = await database.Client.Insertable(link).ExecuteCommandAsync(ct);
    public Task<StudioScreenLink?> FindAsync(Guid id, CancellationToken ct) =>
        database.Client.Queryable<StudioScreenLink>().Where(x => x.Id == id).FirstUtcAsync(database.Provider, ct)!;
    public Task<bool> IsActiveAsync(Guid id, DateTimeOffset now, CancellationToken ct) =>
        database.Client.Queryable<StudioScreenLink>()
            .Where(x => x.Id == id && x.RevokedOn == null && (x.NeverExpires || x.ExpiresOn > now)).AnyAsync(ct);
    public async Task<IReadOnlyList<StudioScreenLink>> ListAsync(string tenant, string resource, CancellationToken ct) =>
        await database.Client.Queryable<StudioScreenLink>().Where(x => x.TenantNId == tenant && x.ResourceNId == resource)
            .OrderByDescending(x => x.CreatedOn).Take(100).ToUtcListAsync(database.Provider, ct);
    public async Task RevokeAsync(Guid id, string tenant, string resource, DateTimeOffset now, CancellationToken ct) =>
        _ = await database.Client.Updateable<StudioScreenLink>().SetColumns(x => new StudioScreenLink { RevokedOn = now })
            .Where(x => x.Id == id && x.TenantNId == tenant && x.ResourceNId == resource && x.RevokedOn == null).ExecuteCommandAsync(ct);
}
