using System.Globalization;
using IndustrialPlatform.PlatformStudio.Application.Publishing;
using IndustrialPlatform.PlatformStudio.Contracts;
using IndustrialPlatform.PlatformStudio.Domain;
using IndustrialPlatform.PlatformStudio.Infrastructure.Persistence;
using IndustrialPlatform.RuntimeSupport.Files;
using IndustrialPlatform.SystemData.Application.Operations;

namespace IndustrialPlatform.PlatformStudio.Infrastructure.Report;

public sealed partial class StudioWorkbooks
{
    public async Task<WorkbookDto> StateAsync(StudioPrincipal p, string nId, string sessionNId, CancellationToken token)
    {
        var (principal, workbook) = await RequireAsync(p, nId, "view", token);
        if (!await database.Client.Queryable<StudioWorkbookSession>().Where(s => s.TenantNId == principal.TenantNId && s.WorkbookId == workbook.Id && s.NId == sessionNId && s.UserNId == principal.SubjectNId && s.Status == "Active").AnyAsync(token)) throw Missing();
        return Dto(workbook);
    }

    public async Task<WorkbookDto> EditAsync(StudioPrincipal p, string nId, string name, VersionCommand command, CancellationToken token)
    {
        var (principal, workbook) = await RequireAsync(p, nId, "edit", token);
        RequireEditable(workbook); StudioPublishing.MatchVersion(workbook, command);
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 128) throw new StudioException("STUDIO_VALIDATION", "工作簿名称无效。");
        name = name.Trim(); if (name == workbook.Name) return Dto(workbook);
        var expected = workbook.OptimisticVersion; var concurrency = workbook.ConcurrencyVersion;
        workbook.Name = name; workbook.MarkChanged(clock.GetUtcNow());
        await resources.TransactionAsync(async () =>
        {
            if (await database.Client.Updateable(workbook).Where(w => w.OptimisticVersion == expected && w.ConcurrencyVersion == concurrency && !w.IsArchived && !w.IsFrozen && !w.IsLocked).ExecuteCommandAsync(token) != 1) throw Conflict();
            await resources.AppendAuditAsync(principal, "studio.workbook.edit", nId, new() { ["name"] = name }, token);
            return true;
        }, token);
        return Dto(workbook);
    }

    public async Task<WorkbookDto> SaveVersionAsync(StudioPrincipal p, string nId, string baseContentVersion, string? note, CancellationToken token)
    {
        var (principal, workbook) = await RequireAsync(p, nId, "edit", token);
        if (note?.Length > 1000) throw new StudioException("STUDIO_VALIDATION", "版本说明不能超过1000字符。");
        return await resources.TransactionAsync(async () =>
        {
            await LockWorkbookRowAsync(workbook.Id, token);
            var current = await database.Client.Queryable<StudioWorkbook>().Where(w => w.Id == workbook.Id).FirstUtcAsync(database.Provider, token) ?? throw Missing();
            RequireEditable(current);
            if (current.VersionSequence != StudioJson.Version(baseContentVersion)) throw Conflict();
            var version = await VersionAsync(current.CurrentVersionId!.Value, current.TenantNId, token);
            if (version.HistoryVersion is not null) return Dto(current); // Same content checkpoint is idempotent.
            var expected = current.OptimisticVersion; var concurrency = current.ConcurrencyVersion;
            version.HistoryVersion = checked(current.HistorySequence + 1); version.PinnedOn = clock.GetUtcNow(); version.VersionNote = note?.Trim();
            if (await database.Client.Updateable(version).Where(v => v.Id == version.Id && v.HistoryVersion == null && v.ContentVersion == current.VersionSequence).ExecuteCommandAsync(token) != 1) throw Conflict();
            current.HistorySequence = version.HistoryVersion.Value; current.HasWorkingCopy = false; current.MarkChanged(clock.GetUtcNow());
            if (await database.Client.Updateable(current).Where(w => w.OptimisticVersion == expected && w.ConcurrencyVersion == concurrency).ExecuteCommandAsync(token) != 1) throw Conflict();
            await resources.AppendAuditAsync(principal, "studio.workbook.version", nId, new() { ["historyVersion"] = current.HistorySequence.ToString(CultureInfo.InvariantCulture), ["sha256"] = version.Sha256, ["note"] = version.VersionNote }, token);
            return Dto(current);
        }, token);
    }

    // A database row lock serializes checkpoints and content commits across host processes.
    private async Task LockWorkbookRowAsync(Guid id, CancellationToken token)
    {
        if (await database.Client.Ado.ExecuteCommandAsync($"UPDATE {database.Table("studio_workbook")} SET optimistic_version=optimistic_version WHERE id=@id", new { id }, token) != 1) throw Missing();
    }
    private static void RequireEditable(StudioWorkbook workbook)
    {
        if (workbook.IsArchived || workbook.IsFrozen || workbook.IsLocked) throw new StudioException("WORKBOOK_READ_ONLY", "当前工作簿只读。", 409);
    }

    /// <summary>仅回收同事务登记的被替代工作文件；File统一流程处理引用、保全、删除和跨平台文件锁。</summary>
    public async Task<int> RecycleWorkingFilesAsync(CancellationToken token)
    {
        var cutoff = clock.GetUtcNow().AddMinutes(-1); // Existing File read tickets expire in 30 seconds.
        var pending = await database.Client.Queryable<StudioWorkbookRetiredFile>().Where(r => r.CreatedOn <= cutoff).OrderBy(r => r.CreatedOn).Take(100).ToUtcListAsync(database.Provider, token);
        var completed = 0;
        foreach (var retired in pending)
        {
            token.ThrowIfCancellationRequested();
            if (await database.Client.Queryable<StudioWorkbookVersion>().Where(v => v.TenantNId == retired.TenantNId && v.FileReferenceNId == retired.FileReferenceNId).AnyAsync(token)) continue;
            try
            {
                var context = new TrustedServiceContext(retired.TenantNId, "platform-studio", retired.OwnerUserNId);
                var reference = await owned.GetStatusAsync(context, retired.FileReferenceNId, token);
                if (reference.Status != "Released") await owned.ReleaseAsync(context, new(retired.FileReferenceNId, reference.Version, "已被后续工作副本替代"), token);
                var file = await files.GetFileAsync(retired.TenantNId, retired.FileNId, token);
                if (file is not null && file.DeletionStatus == "Active") await files.RequestDeletionAsync(retired.TenantNId, retired.OwnerUserNId, retired.FileNId, token);
                await database.Client.Deleteable<StudioWorkbookRetiredFile>().Where(r => r.Id == retired.Id).ExecuteCommandAsync(token);
                completed++;
            }
            catch (SystemDataOperationException error) when (error.StatusCode == 409) { /* Retention, legal holds or another reference keep the file; retry later. */ }
        }
        return completed;
    }
}
