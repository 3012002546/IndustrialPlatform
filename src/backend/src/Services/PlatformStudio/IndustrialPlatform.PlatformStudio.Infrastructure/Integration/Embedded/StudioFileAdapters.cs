using IndustrialPlatform.PlatformStudio.Application;
using IndustrialPlatform.PlatformStudio.Domain;
using IndustrialPlatform.RuntimeSupport.Files;
using IndustrialPlatform.SystemData.Application.Files;
using IndustrialPlatform.SystemData.Contracts.Files;

namespace IndustrialPlatform.PlatformStudio.Infrastructure.Integration.Embedded;

/// <summary>台账只能使用当前操作者持有的个人引用，不从台账grant推导文件权限。</summary>
public sealed class StudioFiles(StudioDeploymentProfile profile, IFileStore? store = null, IStudioIdentity? identity = null) : IStudioFiles
{
    public bool Enabled => profile.Files.Provider != "Disabled";
    public async Task ValidateReferenceAsync(StudioPrincipal principal, string fileNId, string referenceNId, CancellationToken cancellationToken)
    {
        if (!Enabled || store is null) throw new StudioException("STUDIO_FILES_UNAVAILABLE", "文件能力不可用。", 503);
        if (identity is not null) principal = await identity.RefreshAsync(principal, cancellationToken);
        if (profile.DeploymentMode == "PlatformNative" && !principal.HasPermission("systemdata.file.read")) throw new StudioException("STUDIO_FILE_FORBIDDEN", "缺少平台文件读取权限。", 403);
        var reference = await store.GetReferenceAsync(principal.TenantNId, referenceNId, cancellationToken);
        var file = await store.GetFileAsync(principal.TenantNId, fileNId, cancellationToken);
        if (reference is null || reference.OwnerUserNId != principal.SubjectNId || reference.FileNId != fileNId || reference.DeletedOn is not null
            || file is null || file.ScanStatus != "Clean" || file.Restricted || file.DeletionStatus != "Active") throw new StudioException("STUDIO_FILE_FORBIDDEN", "附件不存在或当前用户无个人文件引用权限。", 403);
    }
}

/// <summary>每个传输步骤从最新主体重新计算模块权限及资源grant，撤权后旧上传会话不可续用。</summary>
public sealed class StudioFileScopeAuthorizer(IStudioIdentity identity, IStudioResourceStore store, IStudioAuthorization authorization, StudioDeploymentProfile profile) : IStudioFileScopeAuthorizer
{
    public Task AuthorizeAsync(TrustedServiceContext context, StudioFileUsageV1 usage, CancellationToken cancellationToken) => AuthorizeCore(context, usage, false, cancellationToken);
    public Task AuthorizeReadAsync(TrustedServiceContext context, StudioFileUsageV1 usage, CancellationToken cancellationToken) => AuthorizeCore(context, usage, true, cancellationToken);
    private async Task AuthorizeCore(TrustedServiceContext context, StudioFileUsageV1 usage, bool readOnly, CancellationToken cancellationToken)
    {
        var principal = await identity.RefreshAsync(await identity.GetCurrentAsync(cancellationToken), cancellationToken);
        if (principal.TenantNId != context.TenantNId || principal.SubjectNId != context.ActorUserNId || context.ServiceKey != "platform-studio") throw new StudioException("STUDIO_FORBIDDEN", "文件操作者不匹配。", 403);
        if (profile.Files.Provider == "Disabled") throw new StudioException("STUDIO_FILES_UNAVAILABLE", "文件能力未启用。", 503);
        if (!profile.ActiveModules.Contains(usage.ModuleKey, StringComparer.Ordinal)) throw new StudioException("STUDIO_MODULE_DISABLED", "用途所属模块未启用。", 503);
        var permission = usage.Purpose switch
        {
            "LedgerField" when usage.ModuleKey == "LowCode" => readOnly ? "studio.ledger.read" : "studio.ledger.write",
            "WorkbookImport" when usage.ModuleKey == "Report" => readOnly ? "studio.workbook.view" : "studio.workbook.edit",
            "TemplateAsset" or "TemplatePackage" => (usage.ModuleKey switch { "LowCode" => "studio.lowcode", "Dashboard" => "studio.dashboard", "Report" => "studio.report", "Traceability" => "studio.trace", _ => throw Invalid() }) + (readOnly ? ".view" : ".design"),
            _ => throw Invalid(),
        };
        authorization.RequirePermission(principal, permission);
        if (usage.Purpose == "LedgerField" && string.IsNullOrWhiteSpace(usage.ResourceNId)) throw Invalid();
        if (!string.IsNullOrWhiteSpace(usage.ResourceNId))
        {
            var resource = await store.FindResourceAsync(principal.TenantNId, usage.ResourceNId, cancellationToken) ?? throw new StudioException("STUDIO_NOT_FOUND", "用途资源不存在。", 404);
            if (!StudioKinds.Modules.TryGetValue(resource.Kind, out var module) || module != usage.ModuleKey || resource.IsDeleted) throw Invalid();
            if (usage.Purpose == "LedgerField" && resource.Kind != "LedgerModel") throw Invalid();
            await authorization.RequireAsync(principal, resource, permission, usage.Purpose == "LedgerField" ? "Run" : readOnly ? "View" : "Design", cancellationToken);
        }
    }
    private static StudioException Invalid() => new("STUDIO_FILE_USAGE_INVALID", "上传用途与模块或资源不匹配。", 422);
}

/// <summary>浏览器API使用此门面，统一从已验证Studio身份生成服务上下文。</summary>
public sealed class StudioFileTransfer(IStudioIdentity identity, StudioDeploymentProfile profile, IStudioFileTransfer? transfer = null)
{
    private async Task<TrustedServiceContext> Context(CancellationToken cancellationToken, string permission = "systemdata.file.upload")
    {
        if (profile.Files.Provider == "Disabled" || transfer is null) throw new StudioException("STUDIO_FILES_UNAVAILABLE", "文件能力未启用。", 503);
        var principal = await identity.RefreshAsync(await identity.GetCurrentAsync(cancellationToken), cancellationToken);
        if (profile.DeploymentMode == "PlatformNative" && !principal.HasPermission(permission)) throw new StudioException("STUDIO_FORBIDDEN", "缺少平台文件权限。", 403);
        return new(principal.TenantNId, "platform-studio", principal.SubjectNId);
    }
    public async Task<UploadSessionV1> CreateSessionAsync(CreateUploadSessionRequest request, StudioFileUsageV1 usage, CancellationToken cancellationToken) => await transfer!.CreateScopedSessionAsync(await Context(cancellationToken), request, usage, cancellationToken);
    public async Task<UploadSessionV1> GetSessionAsync(string id, CancellationToken cancellationToken) => await transfer!.GetSessionAsync(await Context(cancellationToken), id, cancellationToken);
    public async Task<UploadSessionV1> GetByTransportAsync(string id, CancellationToken cancellationToken) => await transfer!.GetByTransportAsync(await Context(cancellationToken), id, cancellationToken);
    public async Task<UploadSessionV1> SetContentHashAsync(string id, ContentHashRequest request, CancellationToken cancellationToken) => await transfer!.SetContentHashAsync(await Context(cancellationToken), id, request, cancellationToken);
    public async Task<UploadSessionV1> ResumeProofAsync(string id, ResumeProofRequest request, CancellationToken cancellationToken) => await transfer!.ResumeProofAsync(await Context(cancellationToken), id, request, cancellationToken);
    public async Task<UploadSessionV1> ResumeAsync(string id, ResumeProofRequest request, CancellationToken cancellationToken) => await transfer!.ResumeAsync(await Context(cancellationToken), id, request, cancellationToken);
    public async Task<UploadSessionV1> PauseAsync(string id, CancellationToken cancellationToken) => await transfer!.PauseAsync(await Context(cancellationToken), id, cancellationToken);
    public async Task<UploadSessionV1> CancelAsync(string id, SetUploadStateRequest request, CancellationToken cancellationToken) => await transfer!.CancelAsync(await Context(cancellationToken), id, request, cancellationToken);
    public async Task<UploadSessionV1> AppendAsync(string id, long offset, int epoch, Stream content, string? ticket, CancellationToken cancellationToken) => await transfer!.AppendAsync(await Context(cancellationToken), id, offset, epoch, content, ticket, cancellationToken);
    public async Task<FileObjectV1> CompleteAsync(string id, CancellationToken cancellationToken) => await transfer!.CompleteAsync(await Context(cancellationToken), id, cancellationToken);
    public async Task<FileObjectV1> GetFileAsync(string id, CancellationToken cancellationToken) => await transfer!.GetFileAsync(await Context(cancellationToken, "systemdata.file.read"), id, cancellationToken);
    public async Task<Stream> OpenContentAsync(string id, string? reference, CancellationToken cancellationToken) => await transfer!.OpenContentAsync(await Context(cancellationToken, "systemdata.file.download"), id, reference, cancellationToken);
    public async Task<FileReferenceRecord> AddReferenceAsync(string id, string reference, FileReferenceRequest request, CancellationToken cancellationToken) => await transfer!.AddReferenceAsync(await Context(cancellationToken, "systemdata.file.manage"), id, reference, request, cancellationToken);
    public async Task DeleteReferenceAsync(string id, string reference, CancellationToken cancellationToken) => await transfer!.DeleteReferenceAsync(await Context(cancellationToken, "systemdata.file.manage"), id, reference, cancellationToken);
}
