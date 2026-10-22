using IndustrialPlatform.PlatformStudio.Domain;
using IndustrialPlatform.SystemData.Application.Files;

namespace IndustrialPlatform.PlatformStudio.Infrastructure.Integration.Embedded;

/// <summary>公开的文件扫描能力状态，不暴露扫描端点/路径/凭据；每次探测保留真实检查与收到时间。</summary>
public sealed record StudioFileCapabilityStatus(bool Enabled, bool Available, string Status, string? ReasonCode, DateTimeOffset CheckedOn, DateTimeOffset ReceivedOn);
public sealed class StudioFileCapabilityProbe(StudioDeploymentProfile profile, TimeProvider clock, IFileScanner? scanner = null)
{
    public async Task<StudioFileCapabilityStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        var checkedOn = clock.GetUtcNow();
        if (profile.Files.Provider == "Disabled") return new(false, false, "Disabled", "STUDIO_FILES_DISABLED", checkedOn, clock.GetUtcNow());
        if (scanner is not IFileScannerHealth health) return new(true, false, "Unknown", "STUDIO_SCANNER_UNAVAILABLE", checkedOn, clock.GetUtcNow());
        string status;
        try { status = await health.ReadHealthAsync(cancellationToken); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) when (exception is not OutOfMemoryException) { return new(true, false, "Unknown", "STUDIO_SCANNER_UNAVAILABLE", checkedOn, clock.GetUtcNow()); }
        return status == "Healthy"
            ? new(true, true, "Ready", null, checkedOn, clock.GetUtcNow())
            : new(true, false, status == "Unknown" ? "Unknown" : "Degraded", "STUDIO_SCANNER_UNAVAILABLE", checkedOn, clock.GetUtcNow());
    }
}
