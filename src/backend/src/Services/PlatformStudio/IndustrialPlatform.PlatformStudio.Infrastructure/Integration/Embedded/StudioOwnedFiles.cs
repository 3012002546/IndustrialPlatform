using IndustrialPlatform.PlatformStudio.Application;
using IndustrialPlatform.PlatformStudio.Domain;
using IndustrialPlatform.RuntimeSupport.Files;

namespace IndustrialPlatform.PlatformStudio.Infrastructure.Integration.Embedded;

/// <summary>PF08模板资产消费端口。业务归属固定为实际已授权资源，不能用referenceNId直接跳过资源grant。</summary>
public interface IStudioOwnedFiles
{
    bool Enabled { get; }
    Task<OwnedFilePreparedV1> PrepareAsync(StudioPrincipal principal, string resourceNId, OwnedFilePrepareV1 request, CancellationToken cancellationToken);
    Task<OwnedFileStatusV1> CommitAsync(StudioPrincipal principal, string resourceNId, OwnedFileCommitV1 request, CancellationToken cancellationToken);
    Task<OwnedFileStatusV1> GetStatusAsync(StudioPrincipal principal, string resourceNId, string referenceNId, CancellationToken cancellationToken);
    Task<OwnedFileReadTicketV1> GetReadTicketAsync(StudioPrincipal principal, string resourceNId, string referenceNId, CancellationToken cancellationToken);
    Task<Stream> OpenReadAsync(StudioPrincipal principal, string resourceNId, OwnedFileReadV1 request, CancellationToken cancellationToken);
    Task<OwnedFileStatusV1> ReleaseAsync(StudioPrincipal principal, string resourceNId, OwnedFileReleaseV1 request, CancellationToken cancellationToken);
    Task<OwnedFileStatusV1> SetRetentionAsync(StudioPrincipal principal, string resourceNId, OwnedFileRetentionV1 request, CancellationToken cancellationToken);
    Task<OwnedFileListV1> ListAsync(StudioPrincipal principal, string resourceNId, string? cursor, CancellationToken cancellationToken);
}

public sealed class StudioOwnedFiles(StudioDeploymentProfile profile, IStudioIdentity identity, IStudioResourceStore store, IStudioAuthorization authorization, IOwnedFileService? files = null) : IStudioOwnedFiles
{
    public bool Enabled => profile.Files.Provider != "Disabled";
    public async Task<OwnedFilePreparedV1> PrepareAsync(StudioPrincipal principal, string resourceNId, OwnedFilePrepareV1 request, CancellationToken cancellationToken)
    {
        var context = await Authorize(principal, resourceNId, true, cancellationToken);
        if (request.OwnerService != "platform-studio" || request.OwnerKind != "StudioAsset" || request.OwnerNId != resourceNId) throw new StudioException("STUDIO_FILE_OWNER_INVALID", "资产必须归属当前授权资源。", 422);
        return await files!.PrepareAsync(context, request, cancellationToken);
    }
    public async Task<OwnedFileStatusV1> CommitAsync(StudioPrincipal principal, string resourceNId, OwnedFileCommitV1 request, CancellationToken cancellationToken) => await files!.CommitAsync(await Reference(principal, resourceNId, request.ReferenceNId, true, cancellationToken), request, cancellationToken);
    public async Task<OwnedFileStatusV1> GetStatusAsync(StudioPrincipal principal, string resourceNId, string referenceNId, CancellationToken cancellationToken) => await files!.GetStatusAsync(await Reference(principal, resourceNId, referenceNId, false, cancellationToken), referenceNId, cancellationToken);
    public async Task<OwnedFileReadTicketV1> GetReadTicketAsync(StudioPrincipal principal, string resourceNId, string referenceNId, CancellationToken cancellationToken) => await files!.GetReadTicketAsync(await Reference(principal, resourceNId, referenceNId, false, cancellationToken), referenceNId, cancellationToken);
    public async Task<Stream> OpenReadAsync(StudioPrincipal principal, string resourceNId, OwnedFileReadV1 request, CancellationToken cancellationToken) => await files!.ReadAsync(await Reference(principal, resourceNId, request.ReferenceNId, false, cancellationToken), request, cancellationToken);
    public async Task<OwnedFileStatusV1> ReleaseAsync(StudioPrincipal principal, string resourceNId, OwnedFileReleaseV1 request, CancellationToken cancellationToken) => await files!.ReleaseAsync(await Reference(principal, resourceNId, request.ReferenceNId, true, cancellationToken), request, cancellationToken);
    public async Task<OwnedFileStatusV1> SetRetentionAsync(StudioPrincipal principal, string resourceNId, OwnedFileRetentionV1 request, CancellationToken cancellationToken)
    {
        principal = await identity.RefreshAsync(principal, cancellationToken);
        authorization.RequirePermission(principal, "studio.resource.grant");
        return await files!.SetRetentionAsync(await Reference(principal, resourceNId, request.ReferenceNId, true, cancellationToken), request, cancellationToken);
    }
    public async Task<OwnedFileListV1> ListAsync(StudioPrincipal principal, string resourceNId, string? cursor, CancellationToken cancellationToken) => await files!.ListByOwnerAsync(await Authorize(principal, resourceNId, true, cancellationToken), "StudioAsset", resourceNId, cursor, cancellationToken);
    private async Task<TrustedServiceContext> Authorize(StudioPrincipal principal, string resourceNId, bool write, CancellationToken cancellationToken)
    {
        if (!Enabled || files is null) throw new StudioException("STUDIO_FILES_UNAVAILABLE", "文件能力不可用。", 503);
        principal = await identity.RefreshAsync(principal, cancellationToken);
        var resource = await store.FindResourceAsync(principal.TenantNId, resourceNId, cancellationToken) ?? throw new StudioException("STUDIO_NOT_FOUND", "资源不存在。", 404);
        if (resource.IsDeleted || !StudioKinds.Modules.TryGetValue(resource.Kind, out var module) || !profile.ActiveModules.Contains(module, StringComparer.Ordinal)) throw new StudioException("STUDIO_NOT_FOUND", "资源不可用。", 404);
        await authorization.RequireAsync(principal, resource, StudioKinds.PermissionPrefix(resource.Kind) + (write ? ".design" : ".view"), write ? "Design" : "View", cancellationToken);
        return new(principal.TenantNId, "platform-studio", principal.SubjectNId);
    }
    private async Task<TrustedServiceContext> Reference(StudioPrincipal principal, string resourceNId, string referenceNId, bool write, CancellationToken cancellationToken)
    {
        var context = await Authorize(principal, resourceNId, write, cancellationToken);
        string? cursor = null;
        do
        {
            var page = await files!.ListByOwnerAsync(context, "StudioAsset", resourceNId, cursor, cancellationToken);
            if (page.Items.Any(item => item.ReferenceNId == referenceNId)) return context;
            cursor = page.NextCursor;
        } while (cursor is not null);
        throw new StudioException("STUDIO_NOT_FOUND", "该资源不存在此文件引用。", 404);
    }
}
