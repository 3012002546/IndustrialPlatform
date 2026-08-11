using Microsoft.Extensions.Logging;

namespace IndustrialPlatform.Identity.Application.Authorization;

/// <summary>
/// 权限拒绝审计实现:结构化日志(Serilog),尽力而为,不影响授权裁决。
/// </summary>
public sealed partial class AuthorizationDenialSink : IAuthorizationDenialSink
{
    private readonly ILogger<AuthorizationDenialSink> _logger;

    public AuthorizationDenialSink(ILogger<AuthorizationDenialSink> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc/>
    public Task RecordDenialAsync(AuthorizationDenial denial, CancellationToken cancellationToken)
    {
        LogDenial(
            _logger,
            denial.TenantNId,
            denial.UserNId,
            denial.SessionNId,
            denial.RequiredPermissionNId,
            denial.Reason.ToString(),
            denial.TraceId);
        return Task.CompletedTask;
    }

    [LoggerMessage(
        EventId = 2001,
        Level = LogLevel.Warning,
        Message = "权限拒绝: 租户={TenantNId} 用户={UserNId} 会话={SessionNId} 所需权限={RequiredPermissionNId} 原因={Reason} traceId={TraceId}")]
    private static partial void LogDenial(
        ILogger logger,
        string tenantNId,
        string userNId,
        string? sessionNId,
        string requiredPermissionNId,
        string reason,
        string? traceId);
}
