using SqlSugar;

namespace IndustrialPlatform.Identity.Infrastructure.Persistence.Entities;

/// <summary>登录审计结果(§19.1,identity_login_audit.result 整数列)。</summary>
public enum LoginAuditResult
{
    /// <summary>登录成功。</summary>
    Success = 0,

    /// <summary>登录失败。</summary>
    Failure = 1,
}

/// <summary>
/// identity_login_audit 表的持久化模型(§6 公共列 + §19.1 业务列,snake_case)。
/// 只追加、不可变。IP 与 User-Agent 只存 SHA-256 哈希,绝不落库原始值;
/// 日志/事件/契约中不得出现密码、Token 或内部哈希。
/// </summary>
[SugarTable("identity_login_audit")]
public sealed class LoginAuditTable
{
    [SugarColumn(ColumnName = "id", IsPrimaryKey = true)]
    public Guid Id { get; set; }

    [SugarColumn(ColumnName = "is_frozen")]
    public bool IsFrozen { get; set; }

    [SugarColumn(ColumnName = "is_locked")]
    public bool IsLocked { get; set; }

    [SugarColumn(ColumnName = "is_deleted")]
    public bool IsDeleted { get; set; }

    [SugarColumn(ColumnName = "entity_type")]
    public string EntityType { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "created_on")]
    public DateTimeOffset CreatedOn { get; set; }

    [SugarColumn(ColumnName = "last_updated_on")]
    public DateTimeOffset LastUpdatedOn { get; set; }

    [SugarColumn(ColumnName = "optimistic_version")]
    public long OptimisticVersion { get; set; }

    [SugarColumn(ColumnName = "concurrency_version")]
    public Guid ConcurrencyVersion { get; set; }

    [SugarColumn(ColumnName = "tenant_n_id")]
    public string TenantNId { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "user_n_id")]
    public string? UserNId { get; set; }

    [SugarColumn(ColumnName = "login_name_snapshot")]
    public string LoginNameSnapshot { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "result")]
    public LoginAuditResult Result { get; set; }

    [SugarColumn(ColumnName = "failure_reason")]
    public string? FailureReason { get; set; }

    [SugarColumn(ColumnName = "ip_address_hash")]
    public string IpAddressHash { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "user_agent_hash")]
    public string UserAgentHash { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "trace_id")]
    public string TraceId { get; set; } = string.Empty;
}
