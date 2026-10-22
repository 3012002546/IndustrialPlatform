namespace IndustrialPlatform.SystemData.Application.Auditing;

/// <summary>
/// 本地审计命令端口(TASK-SD-006,05 方案 §11.3)。
/// 真实 <c>system_data_operation_audit</c> 表随 SDM-012(PF-04 前追加记录,
/// 与业务写、Outbox 同事务)由 SD-010 落库;本任务仅冻结端口与命令形状,
/// 默认注册 <see cref="NoopLocalAuditCommand"/> 保证主机可运行,测试可用替身断言。
/// 禁止记录 Token、权限全集、完整用户目录响应或任职查询条件中的敏感文本。
/// </summary>
public interface ILocalAuditCommand
{
    /// <summary>追加一条操作审计记录(实现按 Action 幂等/去重)。</summary>
    Task RecordAsync(LocalAuditEntry entry, CancellationToken cancellationToken);
}

/// <summary>本地审计条目(§11.3 字段)。</summary>
/// <param name="TenantNId">租户业务标识。</param>
/// <param name="ActorUserNId">执行者用户业务标识。</param>
/// <param name="Action">动作名(如 organization.create / assignment.primary-switch)。</param>
/// <param name="ObjectType">对象类型(Organization/Position/UserAssignment)。</param>
/// <param name="ObjectNId">对象业务标识。</param>
/// <param name="Reason">原因(可选,来自请求)。</param>
/// <param name="BeforeSummary">变更前摘要(可选)。</param>
/// <param name="AfterSummary">变更后摘要(可选)。</param>
/// <param name="TraceId">请求跟踪标识。</param>
public sealed record LocalAuditEntry(
    string TenantNId,
    string ActorUserNId,
    string Action,
    string ObjectType,
    string ObjectNId,
    string? Reason,
    string? BeforeSummary,
    string? AfterSummary,
    string TraceId);

/// <summary>本地审计默认实现(no-op):真实落库待 SD-010(SDM-012)接入。</summary>
public sealed class NoopLocalAuditCommand : ILocalAuditCommand
{
    /// <inheritdoc />
    public Task RecordAsync(LocalAuditEntry entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return Task.CompletedTask;
    }
}
