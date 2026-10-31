using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml;
using IndustrialPlatform.PlatformStudio.Application;
using IndustrialPlatform.PlatformStudio.Application.Publishing;
using IndustrialPlatform.PlatformStudio.Contracts;
using IndustrialPlatform.PlatformStudio.Domain;
using IndustrialPlatform.PlatformStudio.Infrastructure.Persistence;
using IndustrialPlatform.RuntimeSupport.Files;
using IndustrialPlatform.SystemData.Application.Files;
using Microsoft.AspNetCore.DataProtection;

namespace IndustrialPlatform.PlatformStudio.Infrastructure.Report;

public sealed record WorkbookDto(string NId, string Name, string ContentVersion, bool IsArchived, string OptimisticVersion, Guid ConcurrencyVersion, string HistoryVersion = "1", bool HasWorkingCopy = false);
public sealed record WorkbookVersionDto(string NId, string ContentVersion, string Sha256, string ContentLength, DateTimeOffset CreatedOn, string? Note = null);
public sealed record WorkbookSessionDto(string SessionNId, string EditorUrl, string AccessToken, DateTimeOffset ExpiresOn, string ContentVersion, string Mode, bool SupportsTokenRefresh = false, bool SlidingSession = true, string HistoryVersion = "1", bool HasWorkingCopy = false);
public sealed record WorkbookTokenDto(string? AccessToken, DateTimeOffset ExpiresOn);
public sealed record WorkbookCommitResult(bool Saved, string ContentVersion, string FileNId, string Sha256, string? RecoveryFileNId = null);
public sealed class WopiLockConflictException(string currentLock) : Exception("Workbook lock conflict") { public string CurrentLock { get; } = currentLock; }

/// <summary>Office-independent persistent WOPI host. File confirmation precedes immutable content versions and CAS.</summary>
public sealed partial class StudioWorkbooks(StudioDatabase database, IStudioResourceStore resources, IStudioIdentity identity, IStudioAuthorization authorization,
    IOwnedFileService owned, IFileService files, IDataProtectionProvider protection, TimeProvider clock, StudioDeploymentProfile profile)
{
    private const string XlsxType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    private const long MaxBytes = 50 * 1024 * 1024;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly IDataProtector _locks = protection.CreateProtector("Studio.Workbook.WopiLock.v1");
    private static TrustedServiceContext Context(StudioPrincipal p) => new(p.TenantNId, "platform-studio", p.SubjectNId);
    public async Task<IReadOnlyList<WorkbookDto>> ListAsync(StudioPrincipal principal, CancellationToken token)
    {
        principal = await identity.RefreshAsync(principal, token); authorization.RequirePermission(principal, "studio.workbook.view");
        var rows = await database.Client.Queryable<StudioWorkbook>().Where(w => w.TenantNId == principal.TenantNId && w.OwnerUserNId == principal.SubjectNId && !w.IsDeleted && w.CurrentVersionId != null).OrderByDescending(w => w.LastUpdatedOn).Take(200).ToListAsync(token);
        return rows.Select(Dto).ToArray();
    }
    public async Task<WorkbookDto> ImportAsync(StudioPrincipal principal, string name, string sourceFileNId, string operationKey, CancellationToken token)
    {
        principal = await identity.RefreshAsync(principal, token); authorization.RequirePermission(principal, "studio.workbook.edit"); StudioCommands.ValidateKey(operationKey);
        if (string.IsNullOrWhiteSpace(name) || name.Length > 128) throw new StudioException("STUDIO_VALIDATION", "工作簿名称无效。");
        var file = await files.GetFileAsync(principal.TenantNId, sourceFileNId, token) ?? throw Missing();
        await using var source = await files.OpenFileContentAsync(principal.TenantNId, principal.SubjectNId, sourceFileNId, null, token);
        using var content = await ReadBoundedAsync(source, token);
        return await ImportContentAsync(principal, name, content, operationKey, null, file.FileNId, token);
    }
    public async Task<WorkbookDto> ImportArtifactAsync(StudioPrincipal principal, string name, string artifactNId, string operationKey, ReportOutputs reports, CancellationToken token)
    {
        principal = await identity.RefreshAsync(principal, token); authorization.RequirePermission(principal, "studio.workbook.edit");
        await using var source = await reports.OpenWorkbookSourceAsync(principal, artifactNId, token);
        using var content = await ReadBoundedAsync(source, token);
        return await ImportContentAsync(principal, name, content, operationKey, artifactNId, null, token);
    }
    public async Task<WorkbookDto> CopyAsync(StudioPrincipal p, string nId, string name, string baseContentVersion, string operationKey, CancellationToken token)
    {
        var (principal, workbook) = await RequireAsync(p, nId, "export", token); authorization.RequirePermission(principal, "studio.workbook.edit");
        var version = await VersionAsync(workbook.CurrentVersionId!.Value, principal.TenantNId, token);
        if (version.ContentVersion != StudioJson.Version(baseContentVersion)) throw Conflict();
        await using var source = await ReadOwnedAsync(principal, version, token); using var content = await ReadBoundedAsync(source, token);
        return await ImportContentAsync(principal, name, content, operationKey, workbook.SourceArtifactNId, version.FileNId, token);
    }
    private async Task<WorkbookDto> ImportContentAsync(StudioPrincipal principal, string name, MemoryStream content, string operationKey, string? artifactNId, string? sourceFileNId, CancellationToken token)
    {
        StudioCommands.ValidateKey(operationKey);
        if (string.IsNullOrWhiteSpace(name) || name.Length > 128) throw new StudioException("STUDIO_VALIDATION", "工作簿名称无效。");
        ValidateXlsx(content);
        var nId = "workbook-" + StudioJson.Hash(principal.TenantNId + "/" + principal.SubjectNId + "/" + operationKey)[..40];
        var sha = Convert.ToHexStringLower(SHA256.HashData(content.GetBuffer().AsSpan(0, (int)content.Length)));
        var existing = await database.Client.Queryable<StudioWorkbook>().Where(w => w.TenantNId == principal.TenantNId && w.NId == nId).FirstUtcAsync(database.Provider, token);
        if (existing is not null)
        {
            var version = await database.Client.Queryable<StudioWorkbookVersion>().Where(v => v.TenantNId == principal.TenantNId && v.WorkbookId == existing.Id && v.ContentVersion == 1).FirstUtcAsync(database.Provider, token) ?? throw Conflict();
            if (version.Sha256 != sha || existing.Name != name || existing.OwnerUserNId != principal.SubjectNId || existing.SourceArtifactNId != artifactNId) throw new StudioException("STUDIO_IDEMPOTENCY_CONFLICT", "导入键已用于不同内容。", 409);
            return Dto(existing);
        }
        var confirmed = await PersistAsync(principal, nId, operationKey, content, sha, token);
        var now = clock.GetUtcNow(); var workbook = new StudioWorkbook(now) { NId = nId, TenantNId = principal.TenantNId, Name = name, OwnerUserNId = principal.SubjectNId, VersionSequence = 1, HistorySequence = 1, SourceArtifactNId = artifactNId };
        var first = new StudioWorkbookVersion { NId = "wbv-" + Guid.NewGuid().ToString("N"), TenantNId = principal.TenantNId, WorkbookId = workbook.Id, ContentVersion = 1, HistoryVersion = 1, PinnedOn = now, FileNId = confirmed.FileNId!, FileReferenceNId = confirmed.ReferenceNId, ContentLength = content.Length, Sha256 = sha, SavedBy = principal.SubjectNId, SaveOperationKey = operationKey, CreatedOn = now };
        await resources.TransactionAsync(async () => { await database.Client.Insertable(workbook).ExecuteCommandAsync(token); await database.Client.Insertable(first).ExecuteCommandAsync(token); workbook.CurrentVersionId = first.Id; if (await database.Client.Updateable(workbook).ExecuteCommandAsync(token) != 1) throw Conflict(); await resources.AppendAuditAsync(principal, "studio.workbook.import", nId, new() { ["contentVersion"] = "1", ["sourceFileNId"] = sourceFileNId, ["sourceArtifactNId"] = artifactNId }, token); return true; }, token);
        return Dto(workbook);
    }
    public async Task<IReadOnlyList<WorkbookVersionDto>> VersionsAsync(StudioPrincipal p, string nId, CancellationToken token)
    {
        var (_, workbook) = await RequireAsync(p, nId, "view", token);
        var rows = await database.Client.Queryable<StudioWorkbookVersion>().Where(v => v.TenantNId == workbook.TenantNId && v.WorkbookId == workbook.Id && v.HistoryVersion != null).OrderByDescending(v => v.HistoryVersion).ToUtcListAsync(database.Provider, token);
        return rows.Select(v => new WorkbookVersionDto(v.NId, v.HistoryVersion!.Value.ToString(CultureInfo.InvariantCulture), v.Sha256, v.ContentLength.ToString(CultureInfo.InvariantCulture), v.PinnedOn ?? v.CreatedOn, v.VersionNote)).ToArray();
    }
    public async Task<Stream> ReadVersionAsync(StudioPrincipal p, string nId, string versionNId, CancellationToken token)
    {
        var (principal, workbook) = await RequireAsync(p, nId, "export", token);
        var version = await database.Client.Queryable<StudioWorkbookVersion>().Where(v => v.TenantNId == workbook.TenantNId && v.WorkbookId == workbook.Id && v.NId == versionNId && v.HistoryVersion != null).FirstUtcAsync(database.Provider, token) ?? throw Missing();
        return await ReadOwnedAsync(principal, version, token);
    }
    public async Task<WorkbookDto> ArchiveAsync(StudioPrincipal p, string nId, VersionCommand command, CancellationToken token)
    {
        var (principal, workbook) = await RequireAsync(p, nId, "edit", token); StudioPublishing.MatchVersion(workbook, command);
        var version = workbook.OptimisticVersion; var concurrency = workbook.ConcurrencyVersion; workbook.IsArchived = true; workbook.MarkChanged(clock.GetUtcNow());
        await resources.TransactionAsync(async () =>
        {
            if (await database.Client.Updateable(workbook).Where(w => w.OptimisticVersion == version && w.ConcurrencyVersion == concurrency && !w.IsArchived).ExecuteCommandAsync(token) != 1) throw Conflict();
            await resources.AppendAuditAsync(principal, "studio.workbook.archive", nId, new() { ["contentVersion"] = workbook.VersionSequence.ToString(CultureInfo.InvariantCulture) }, token); return true;
        }, token);
        return Dto(workbook);
    }
    // editorAction and publicWopiBase must come from an operator-registered, admitted Office provider, never request input.
    public async Task<WorkbookSessionDto> CreateSessionAsync(StudioPrincipal p, string nId, string mode, string baseContentVersion, Uri editorAction, Uri publicWopiBase, CancellationToken token)
    {
        var (principal, workbook) = await RequireAsync(p, nId, mode == "Edit" ? "edit" : "view", token);
        if (mode is not ("Edit" or "View") || editorAction.Scheme != "https" || publicWopiBase.Scheme != "https") throw new StudioException("OFFICE_UNAVAILABLE", "Office登记地址无效。", 503);
        if (mode == "Edit" && workbook.IsArchived) throw new StudioException("WORKBOOK_ARCHIVED", "归档工作簿只读。", 409);
        var version = await VersionAsync(workbook.CurrentVersionId!.Value, workbook.TenantNId, token);
        if (version.ContentVersion != StudioJson.Version(baseContentVersion)) throw Conflict();
        _ = await owned.GetReadTicketAsync(Context(principal), version.FileReferenceNId, token);
        var secret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)); var now = clock.GetUtcNow();
        var session = new StudioWorkbookSession { NId = "wbs-" + Guid.NewGuid().ToString("N"), TenantNId = principal.TenantNId, WorkbookId = workbook.Id, BaseVersionId = version.Id, BaseContentVersion = version.ContentVersion, UserNId = principal.SubjectNId, Mode = mode, ProviderKey = "Collabora", CreatedOn = now, LastSeenOn = now, ExpiresOn = now.AddMinutes(15), AccessTokenHash = StudioJson.Hash(secret), IdentityEvidenceJson = await CaptureSessionIdentityAsync(principal, now.AddMinutes(15), token) };
        await database.Client.Insertable(session).ExecuteCommandAsync(token);
        var wopi = new Uri(publicWopiBase, "office/wopi/files/" + session.NId).AbsoluteUri;
        var editorUrl = editorAction.AbsoluteUri + (editorAction.Query.Length == 0 ? "?" : "&") + "WOPISrc=" + Uri.EscapeDataString(wopi);
        return new(session.NId, editorUrl, secret, session.ExpiresOn, version.ContentVersion.ToString(CultureInfo.InvariantCulture), mode, profile.Office.SupportsTokenRefresh, HistoryVersion: workbook.HistorySequence.ToString(CultureInfo.InvariantCulture), HasWorkingCopy: workbook.HasWorkingCopy);
    }
    public async Task CloseAsync(StudioPrincipal p, string nId, string sessionNId, CancellationToken token)
    {
        var (_, workbook) = await RequireAsync(p, nId, "view", token);
        await database.Client.Updateable<StudioWorkbookSession>().SetColumns(s => new StudioWorkbookSession { Status = "Closed", LockHash = null, LockValueCiphertext = null, LockExpiresOn = null }).Where(s => s.TenantNId == workbook.TenantNId && s.WorkbookId == workbook.Id && s.NId == sessionNId && s.UserNId == workbook.OwnerUserNId).ExecuteCommandAsync(token);
    }
    public async Task<WorkbookTokenDto> RenewAsync(StudioPrincipal p, string nId, string sessionNId, CancellationToken token)
    {
        var (principal, workbook) = await RequireAsync(p, nId, "view", token); var now = clock.GetUtcNow();
        var session = await database.Client.Queryable<StudioWorkbookSession>().Where(s => s.TenantNId == principal.TenantNId && s.WorkbookId == workbook.Id && s.NId == sessionNId && s.UserNId == principal.SubjectNId && s.Status == "Active").FirstUtcAsync(database.Provider, token) ?? throw Missing();
        if (session.ExpiresOn <= now) throw new StudioException("WOPI_TOKEN_EXPIRED", "已到期的编辑会话不能续期，请保留恢复副本后重新打开。", 401);
        using var evidence = JsonDocument.Parse(session.IdentityEvidenceJson);
        var live = await identity.RefreshExecutionAsync(evidence.RootElement.GetProperty("identityEvidence").GetString()!, token);
        var original = evidence.RootElement.GetProperty("principal").Deserialize<StudioPrincipal>(Json)!;
        if (live.TenantNId != principal.TenantNId || live.SubjectNId != principal.SubjectNId || original.FactoryNIds.Except(live.FactoryNIds, StringComparer.Ordinal).Any()) throw Missing();
        authorization.RequirePermission(live, "studio.workbook." + (session.Mode == "Edit" ? "edit" : "view"));
        if (session.Mode == "Edit" && workbook.IsArchived) throw new StudioException("WORKBOOK_ARCHIVED", "归档后不能延长写入会话。", 409);
        // Keep the provider's token stable when rotation is not supported. The authenticated host
        // renews the lease while the editor stays open; abandoned/closed/revoked sessions still fail.
        var secret = profile.Office.SupportsTokenRefresh ? Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) : null;
        var expires = now.AddMinutes(15);
        var currentEvidence = await CaptureSessionIdentityAsync(principal, expires, token);
        var oldHash = session.AccessTokenHash;
        var nextHash = secret is null ? oldHash : StudioJson.Hash(secret);
        if (await database.Client.Updateable<StudioWorkbookSession>().SetColumns(s => new StudioWorkbookSession { AccessTokenHash = nextHash, ExpiresOn = expires, LastSeenOn = now, IdentityEvidenceJson = currentEvidence }).Where(s => s.Id == session.Id && s.AccessTokenHash == oldHash && s.Status == "Active" && s.ExpiresOn > now).ExecuteCommandAsync(token) != 1) throw Conflict();
        return new(secret, expires);
    }
    private async Task<string> CaptureSessionIdentityAsync(StudioPrincipal principal, DateTimeOffset expires, CancellationToken token)
    {
        // An authenticated host delegates a bounded Office lease, independent of its JWT's remaining
        // lifetime. The captured source session, user/security version and live permissions are still
        // checked on every WOPI call; renewing cannot resurrect an expired or revoked Office session.
        var leasePrincipal = principal with { ExpiresOn = expires };
        return JsonSerializer.Serialize(new { principal = leasePrincipal, identityEvidence = await identity.CaptureExecutionEvidenceAsync(leasePrincipal, token) }, Json);
    }
    public async Task<object> CheckFileInfoAsync(string sessionNId, string accessToken, CancellationToken token)
    {
        var (principal, workbook, session) = await SessionAsync(sessionNId, accessToken, false, token); var version = await VersionAsync(session.BaseVersionId, workbook.TenantNId, token);
        return new { BaseFileName = workbook.Name.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase) ? workbook.Name : workbook.Name + ".xlsx", OwnerId = workbook.OwnerUserNId, UserId = session.UserNId, Size = version.ContentLength, Version = version.ContentVersion.ToString(CultureInfo.InvariantCulture), UserCanWrite = CanWrite(principal, workbook, session), SupportsLocks = true, SupportsGetLock = true, SupportsUpdate = true, UserCanNotWriteRelative = true, DisablePrint = true, DisableExport = true, HideUserList = true, PostMessageOrigin = Uri.TryCreate(profile.PublicOrigin, UriKind.Absolute, out var host) ? host.GetLeftPart(UriPartial.Authority) : null };
    }
    public async Task<Stream> GetFileAsync(string sessionNId, string accessToken, CancellationToken token)
    {
        var (principal, workbook, session) = await SessionAsync(sessionNId, accessToken, false, token);
        return await ReadOwnedAsync(principal, await VersionAsync(session.BaseVersionId, workbook.TenantNId, token), token);
    }
    public async Task<string> LockAsync(string sessionNId, string accessToken, string operation, string? lockValue, CancellationToken token, string? oldLockValue = null)
    {
        var (_, workbook, session) = await SessionAsync(sessionNId, accessToken, operation != "GET_LOCK", token);
        if (operation is not ("LOCK" or "REFRESH_LOCK" or "UNLOCK" or "GET_LOCK")) throw new StudioException("WOPI_NOT_IMPLEMENTED", "不支持该WOPI操作。", 501);
        if (operation != "GET_LOCK" && (string.IsNullOrEmpty(lockValue) || Encoding.UTF8.GetByteCount(lockValue) > 1024)) throw new StudioException("STUDIO_VALIDATION", "WOPI锁长度无效。");
        try { return await resources.TransactionAsync(async () =>
        {
            var now = clock.GetUtcNow();
            // A database unique index arbitrates writers across processes; expired locks are cleared in this same transaction.
            await database.Client.Updateable<StudioWorkbookSession>().SetColumns(s => new StudioWorkbookSession { LockHash = null, LockValueCiphertext = null, LockExpiresOn = null }).Where(s => s.TenantNId == workbook.TenantNId && s.WorkbookId == workbook.Id && s.LockHash != null && (s.LockExpiresOn <= now || s.ExpiresOn <= now || s.Status == "Closed")).ExecuteCommandAsync(token);
            var holder = await database.Client.Queryable<StudioWorkbookSession>().Where(s => s.TenantNId == workbook.TenantNId && s.WorkbookId == workbook.Id && s.LockHash != null).FirstUtcAsync(database.Provider, token);
            var current = holder?.LockValueCiphertext is { } encrypted ? _locks.Unprotect(encrypted) : "";
            if (operation == "GET_LOCK") return current;
            if (oldLockValue is not null && (operation != "LOCK" || oldLockValue.Length == 0 || Encoding.UTF8.GetByteCount(oldLockValue) > 1024)) throw new StudioException("STUDIO_VALIDATION", "WOPI旧锁无效。");
            if (holder is not null && (holder.NId != session.NId || holder.LockHash != StudioJson.Hash(oldLockValue ?? lockValue!)) || holder is null && (operation is "REFRESH_LOCK" or "UNLOCK" || oldLockValue is not null)) throw new WopiLockConflictException(current);
            var hash = operation == "UNLOCK" ? null : StudioJson.Hash(lockValue!); var ciphertext = operation == "UNLOCK" ? null : _locks.Protect(lockValue!); DateTimeOffset? expires = operation == "UNLOCK" ? null : now.AddMinutes(30);
            if (await database.Client.Updateable<StudioWorkbookSession>().SetColumns(s => new StudioWorkbookSession { LockHash = hash, LockValueCiphertext = ciphertext, LockExpiresOn = expires, LastSeenOn = now }).Where(s => s.Id == session.Id && s.Status == "Active" && s.ExpiresOn > now).ExecuteCommandAsync(token) != 1) throw Conflict();
            return operation == "UNLOCK" ? "" : lockValue!;
        }, token); }
        catch (Exception error) when (StudioStorageErrors.IsUnique(error))
        {
            var holder = await database.Client.Queryable<StudioWorkbookSession>().Where(s => s.TenantNId == workbook.TenantNId && s.WorkbookId == workbook.Id && s.LockHash != null).FirstUtcAsync(database.Provider, token);
            throw new WopiLockConflictException(holder?.LockValueCiphertext is { } encrypted ? _locks.Unprotect(encrypted) : "");
        }
    }
    public async Task<WorkbookCommitResult> PutFileAsync(string sessionNId, string accessToken, string lockValue, Stream input, CancellationToken token, bool waitForConfirmation = false)
    {
        var (principal, workbook, session) = await SessionAsync(sessionNId, accessToken, true, token);
        await RequireLockAsync(workbook, session, lockValue, token);
        using var content = await ReadBoundedAsync(input, token); ValidateXlsx(content);
        var sha = Convert.ToHexStringLower(SHA256.HashData(content.GetBuffer().AsSpan(0, (int)content.Length)));
        var previous = await VersionAsync(session.BaseVersionId, workbook.TenantNId, token);
        if (previous.Sha256 == sha && workbook.CurrentVersionId == previous.Id) return new(true, previous.ContentVersion.ToString(CultureInfo.InvariantCulture), previous.FileNId, sha);
        var operationKey = "save-" + StudioJson.Hash(session.NId + "/" + session.BaseVersionId + "/" + session.BaseContentVersion.ToString(CultureInfo.InvariantCulture) + "/" + sha);
        var confirmed = await PersistAsync(principal, workbook.NId, operationKey, content, sha, token, waitForConfirmation);
        // Upload and scanning can outlive the original permission/read-only check; keep the original base for CAS.
        principal = (await SessionAsync(sessionNId, accessToken, true, token)).Principal;
        // If CAS loses, the confirmed owned reference remains a recovery copy; the old valid current version is retained.
        try { return await resources.TransactionAsync(async () =>
        {
            await LockWorkbookRowAsync(workbook.Id, token);
            var current = await database.Client.Queryable<StudioWorkbook>().Where(w => w.Id == workbook.Id).FirstUtcAsync(database.Provider, token) ?? throw Missing();
            var live = await database.Client.Queryable<StudioWorkbookSession>().Where(s => s.Id == session.Id).FirstUtcAsync(database.Provider, token) ?? throw Missing();
            if (!CanWrite(principal, current, live)) throw new StudioException("WORKBOOK_READ_ONLY", "当前会话只读。", 403);
            await RequireLockAsync(current, live, lockValue, token);
            var replay = await database.Client.Queryable<StudioWorkbookVersion>().Where(v => v.WorkbookId == workbook.Id && v.SaveOperationKey == operationKey).FirstUtcAsync(database.Provider, token);
            if (replay is not null) return new WorkbookCommitResult(true, replay.ContentVersion.ToString(CultureInfo.InvariantCulture), replay.FileNId, replay.Sha256);
            if (current.CurrentVersionId != session.BaseVersionId || live.BaseVersionId != session.BaseVersionId || current.VersionSequence != session.BaseContentVersion || live.BaseContentVersion != session.BaseContentVersion)
                return await RecoveryAsync(current, confirmed.FileNId!, token);
            var baseline = await VersionAsync(current.CurrentVersionId!.Value, current.TenantNId, token);
            var createWorking = baseline.HistoryVersion is not null;
            var version = createWorking ? new StudioWorkbookVersion { NId = "wbv-" + Guid.NewGuid().ToString("N"), TenantNId = workbook.TenantNId, WorkbookId = workbook.Id, BaseVersionId = baseline.Id, CreatedOn = clock.GetUtcNow() } : baseline;
            var oldReference = baseline.FileReferenceNId; var oldFile = baseline.FileNId;
            version.ContentVersion = checked(current.VersionSequence + 1); version.FileNId = confirmed.FileNId!; version.FileReferenceNId = confirmed.ReferenceNId;
            version.Sha256 = sha; version.ContentLength = content.Length; version.SavedBy = principal.SubjectNId; version.SaveOperationKey = operationKey;
            try
            {
                if (createWorking) await database.Client.Insertable(version).ExecuteCommandAsync(token);
                else
                {
                    if (await database.Client.Updateable(version).Where(v => v.Id == session.BaseVersionId && v.ContentVersion == session.BaseContentVersion && v.HistoryVersion == null).ExecuteCommandAsync(token) != 1) throw new WorkbookContentConflictException();
                    await database.Client.Insertable(new StudioWorkbookRetiredFile { NId = "wbr-" + StudioJson.Hash(oldReference)[..40], TenantNId = current.TenantNId, WorkbookId = current.Id, OwnerUserNId = current.OwnerUserNId, FileNId = oldFile, FileReferenceNId = oldReference, CreatedOn = clock.GetUtcNow() }).ExecuteCommandAsync(token);
                }
            }
            catch (Exception error) when (StudioStorageErrors.IsUnique(error)) { throw new WorkbookContentConflictException(error); }
            var expected = current.OptimisticVersion; var concurrency = current.ConcurrencyVersion; current.CurrentVersionId = version.Id; current.VersionSequence = version.ContentVersion; current.HasWorkingCopy = true; current.MarkChanged(clock.GetUtcNow());
            if (await database.Client.Updateable(current).Where(w => w.OptimisticVersion == expected && w.ConcurrencyVersion == concurrency && w.CurrentVersionId == session.BaseVersionId && !w.IsArchived && !w.IsFrozen && !w.IsLocked).ExecuteCommandAsync(token) != 1) throw new WorkbookContentConflictException();
            var savedOn = clock.GetUtcNow();
            if (await database.Client.Updateable<StudioWorkbookSession>().SetColumns(s => new StudioWorkbookSession { BaseVersionId = version.Id, BaseContentVersion = version.ContentVersion, LastSaveOperationKey = operationKey, LastSeenOn = savedOn }).Where(s => s.Id == live.Id && s.BaseVersionId == session.BaseVersionId && s.BaseContentVersion == session.BaseContentVersion && s.LockHash == live.LockHash && s.Status == "Active").ExecuteCommandAsync(token) != 1) throw Conflict();
            await resources.AppendAuditAsync(principal, "studio.workbook.save", workbook.NId, new() { ["contentVersion"] = version.ContentVersion.ToString(CultureInfo.InvariantCulture), ["sha256"] = sha }, token);
            return new WorkbookCommitResult(true, version.ContentVersion.ToString(CultureInfo.InvariantCulture), version.FileNId, sha);
        }, token); }
        catch (WorkbookContentConflictException conflict)
        {
            // The failed transaction has rolled back. Re-authorize before exposing a replay or recovery copy.
            var (_, current, live) = await SessionAsync(sessionNId, accessToken, true, token);
            await RequireLockAsync(current, live, lockValue, token);
            var replay = await database.Client.Queryable<StudioWorkbookVersion>().Where(v => v.TenantNId == current.TenantNId && v.WorkbookId == current.Id && v.SaveOperationKey == operationKey).FirstUtcAsync(database.Provider, token);
            // An unrelated unique-key failure is not evidence that another content commit won.
            if (conflict.InnerException is not null && replay is null && current.CurrentVersionId == session.BaseVersionId) throw;
            return replay is not null
                ? new(true, replay.ContentVersion.ToString(CultureInfo.InvariantCulture), replay.FileNId, replay.Sha256)
                : await RecoveryAsync(current, confirmed.FileNId!, token);
        }
    }
    private sealed class WorkbookContentConflictException(Exception? inner = null) : Exception("Workbook content changed during commit.", inner) { }
    private async Task<WorkbookCommitResult> RecoveryAsync(StudioWorkbook workbook, string recoveryFileNId, CancellationToken token)
    {
        var current = await VersionAsync(workbook.CurrentVersionId!.Value, workbook.TenantNId, token);
        return new(false, current.ContentVersion.ToString(CultureInfo.InvariantCulture), current.FileNId, current.Sha256, recoveryFileNId);
    }
    private async Task<OwnedFileStatusV1> PersistAsync(StudioPrincipal principal, string ownerNId, string operationKey, MemoryStream content, string sha, CancellationToken token, bool waitForConfirmation = false)
    {
        var context = Context(principal);
        var upload = await OwnedFileUploads.CommitAsync(owned, files, context, new(operationKey, "platform-studio", "WorkbookVersion", ownerNId, ownerNId + ".xlsx", XlsxType, content.Length, sha), content, token);
        var committed = waitForConfirmation
            ? await OwnedFileUploads.WaitForConfirmationAsync(owned, context, upload, TimeSpan.FromSeconds(30), token)
            : upload.Reference;
        if (committed.Status != "Active") throw new StudioException("WORKBOOK_SCAN_PENDING", "工作簿尚未获得Clean扫描及引用确认，未更新内容版本。", 409);
        return committed;
    }
    private async Task<(StudioPrincipal Principal, StudioWorkbook Workbook)> RequireAsync(StudioPrincipal p, string nId, string action, CancellationToken token)
    {
        p = await identity.RefreshAsync(p, token); authorization.RequirePermission(p, "studio.workbook." + action);
        var workbook = await database.Client.Queryable<StudioWorkbook>().Where(w => w.TenantNId == p.TenantNId && w.NId == nId && w.OwnerUserNId == p.SubjectNId && !w.IsDeleted).FirstUtcAsync(database.Provider, token) ?? throw Missing();
        return (p, workbook);
    }
    private async Task<(StudioPrincipal Principal, StudioWorkbook Workbook, StudioWorkbookSession Session)> SessionAsync(string nId, string secret, bool write, CancellationToken token)
    {
        if (string.IsNullOrEmpty(secret) || secret.Length > 256) throw new StudioException("WOPI_TOKEN_INVALID", "Office票据无效。", 401);
        var session = await database.Client.Queryable<StudioWorkbookSession>().Where(s => s.NId == nId).FirstUtcAsync(database.Provider, token) ?? throw Missing();
        if (session.Status != "Active" || session.ExpiresOn <= clock.GetUtcNow() || !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(session.AccessTokenHash), Encoding.ASCII.GetBytes(StudioJson.Hash(secret)))) throw new StudioException("WOPI_TOKEN_EXPIRED", "Office票据已失效。", 401);
        using var evidence = JsonDocument.Parse(session.IdentityEvidenceJson); var original = evidence.RootElement.GetProperty("principal").Deserialize<StudioPrincipal>(Json)!;
        var principal = await identity.RefreshExecutionAsync(evidence.RootElement.GetProperty("identityEvidence").GetString()!, token);
        if (principal.TenantNId != session.TenantNId || principal.SubjectNId != session.UserNId || original.FactoryNIds.Except(principal.FactoryNIds, StringComparer.Ordinal).Any()) throw Missing();
        authorization.RequirePermission(principal, "studio.workbook." + (write ? "edit" : "view"));
        var workbook = await database.Client.Queryable<StudioWorkbook>().Where(w => w.Id == session.WorkbookId && w.TenantNId == principal.TenantNId && w.OwnerUserNId == principal.SubjectNId && !w.IsDeleted).FirstUtcAsync(database.Provider, token) ?? throw Missing();
        if (write && !CanWrite(principal, workbook, session)) throw new StudioException("WORKBOOK_READ_ONLY", "当前会话只读。", 403);
        return (principal, workbook, session);
    }
    private static bool CanWrite(StudioPrincipal principal, StudioWorkbook workbook, StudioWorkbookSession session)
        => session.Mode == "Edit" && !workbook.IsArchived && !workbook.IsFrozen && !workbook.IsLocked && principal.HasPermission("studio.workbook.edit");
    private async Task RequireLockAsync(StudioWorkbook workbook, StudioWorkbookSession session, string value, CancellationToken token)
    {
        var now = clock.GetUtcNow();
        var holder = await database.Client.Queryable<StudioWorkbookSession>().Where(s => s.TenantNId == workbook.TenantNId && s.WorkbookId == workbook.Id && s.LockHash != null && s.LockExpiresOn > now && s.ExpiresOn > now && s.Status == "Active").FirstUtcAsync(database.Provider, token);
        if (holder is null || holder.NId != session.NId || holder.LockHash != StudioJson.Hash(value)) throw new WopiLockConflictException(holder?.LockValueCiphertext is { } encrypted ? _locks.Unprotect(encrypted) : "");
    }
    private async Task<StudioWorkbookVersion> VersionAsync(Guid id, string tenant, CancellationToken token) => await database.Client.Queryable<StudioWorkbookVersion>().Where(v => v.Id == id && v.TenantNId == tenant).FirstUtcAsync(database.Provider, token) ?? throw Missing();
    private async Task<Stream> ReadOwnedAsync(StudioPrincipal p, StudioWorkbookVersion version, CancellationToken token)
    {
        var context = Context(p); var ticket = await owned.GetReadTicketAsync(context, version.FileReferenceNId, token);
        return await owned.ReadAsync(context, new(version.FileReferenceNId, p.SubjectNId, ticket.AuthorizationTicket), token);
    }
    public static async Task<MemoryStream> ReadBoundedAsync(Stream input, CancellationToken token)
    {
        var output = new MemoryStream(); var buffer = new byte[81920];
        try { int read; while ((read = await input.ReadAsync(buffer, token)) != 0) { if (output.Length + read > MaxBytes) throw new StudioException("STUDIO_LIMIT", "工作簿超过50MiB。", 429); await output.WriteAsync(buffer.AsMemory(0, read), token); } output.Position = 0; return output; }
        catch { output.Dispose(); throw; }
    }
    public static void ValidateXlsx(MemoryStream input)
    {
        input.Position = 0;
        try
        {
            using var zip = new ZipArchive(input, ZipArchiveMode.Read, leaveOpen: true);
            if (zip.Entries.Count is 0 or > 10000 || zip.Entries.Sum(e => e.Length) > 100 * 1024 * 1024 || zip.Entries.Select(e => e.FullName).Distinct(StringComparer.OrdinalIgnoreCase).Count() != zip.Entries.Count || zip.Entries.Any(e => e.FullName.StartsWith('/') || e.FullName.Contains('\\') || e.FullName.Split('/').Contains("..") || e.FullName.Contains("vbaProject", StringComparison.OrdinalIgnoreCase) || e.FullName.StartsWith("xl/externalLinks/", StringComparison.OrdinalIgnoreCase))) throw new StudioException("WORKBOOK_FORMAT_UNSUPPORTED", "工作簿包路径、宏、外部链接或解压预算不受支持。", 422);
            foreach (var name in new[] { "[Content_Types].xml", "xl/workbook.xml" })
            {
                var entry = zip.GetEntry(name) ?? throw new StudioException("WORKBOOK_FORMAT_UNSUPPORTED", "文件不是有效xlsx工作簿。", 422);
                using var stream = entry.Open(); using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 10 * 1024 * 1024 });
                while (reader.Read()) { if (reader.NodeType == XmlNodeType.Element && reader.GetAttribute("ContentType") is { } type && type.Contains("macroEnabled", StringComparison.OrdinalIgnoreCase)) throw new StudioException("WORKBOOK_FORMAT_UNSUPPORTED", "不支持宏工作簿。", 422); }
            }
        }
        catch (Exception e) when (e is InvalidDataException or XmlException) { throw new StudioException("WORKBOOK_FORMAT_UNSUPPORTED", "工作簿ZIP或XML无效。", 422); }
        finally { input.Position = 0; }
    }
    private static WorkbookDto Dto(StudioWorkbook w) => new(w.NId, w.Name, w.VersionSequence.ToString(CultureInfo.InvariantCulture), w.IsArchived, w.OptimisticVersion.ToString(CultureInfo.InvariantCulture), w.ConcurrencyVersion, w.HistorySequence.ToString(CultureInfo.InvariantCulture), w.HasWorkingCopy);
    private static StudioException Missing() => new("STUDIO_NOT_FOUND", "工作簿不存在。", 404);
    private static StudioException Conflict() => new("WORKBOOK_CONFLICT", "工作簿内容版本发生变化，保留旧版并重新读取。", 409);
}
