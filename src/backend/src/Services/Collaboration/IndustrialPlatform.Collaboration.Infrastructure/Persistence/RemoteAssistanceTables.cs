using SqlSugar;

namespace IndustrialPlatform.Collaboration.Infrastructure.Persistence;

[SugarTable("collaboration_remote_assistance_session")]
internal sealed class RemoteAssistanceScreenTable : CommonCollaborationTable
{
    [SugarColumn(ColumnName = "tenant_n_id")] public string TenantNId { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "n_id")] public string NId { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "conversation_n_id")] public string ConversationNId { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "initiator_user_n_id")] public string InitiatorUserNId { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "invitee_user_n_id")] public string InviteeUserNId { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "direction")] public string Direction { get; set; } = "ShareMine";
    [SugarColumn(ColumnName = "sharer_user_n_id")] public string SharerUserNId { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "viewer_user_n_id")] public string ViewerUserNId { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "state")] public string State { get; set; } = "Pending";
    [SugarColumn(ColumnName = "answer", IsNullable = true)] public string? Answer { get; set; }
    [SugarColumn(ColumnName = "request_n_id")] public string RequestNId { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "request_hash")] public string RequestHash { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "deadline_on")] public DateTimeOffset DeadlineOn { get; set; }
    [SugarColumn(ColumnName = "accepted_on", IsNullable = true)] public DateTimeOffset? AcceptedOn { get; set; }
    [SugarColumn(ColumnName = "started_on", IsNullable = true)] public DateTimeOffset? StartedOn { get; set; }
    [SugarColumn(ColumnName = "ended_on", IsNullable = true)] public DateTimeOffset? EndedOn { get; set; }
    [SugarColumn(ColumnName = "end_reason", IsNullable = true)] public string? EndReason { get; set; }
    [SugarColumn(ColumnName = "initiator_connection_id")] public string InitiatorConnectionId { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "invitee_connection_id", IsNullable = true)] public string? InviteeConnectionId { get; set; }
    [SugarColumn(ColumnName = "initiator_alive_until", IsNullable = true)] public DateTimeOffset? InitiatorAliveUntil { get; set; }
    [SugarColumn(ColumnName = "invitee_alive_until", IsNullable = true)] public DateTimeOffset? InviteeAliveUntil { get; set; }
    [SugarColumn(ColumnName = "initiator_stopped_reported_on", IsNullable = true)] public DateTimeOffset? InitiatorStoppedReportedOn { get; set; }
    [SugarColumn(ColumnName = "invitee_stopped_reported_on", IsNullable = true)] public DateTimeOffset? InviteeStoppedReportedOn { get; set; }
}

[SugarTable("collaboration_remote_assistance_voice_call")]
internal sealed class RemoteAssistanceVoiceTable : CommonCollaborationTable
{
    [SugarColumn(ColumnName = "tenant_n_id")] public string TenantNId { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "n_id")] public string NId { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "conversation_n_id")] public string ConversationNId { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "caller_user_n_id")] public string CallerUserNId { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "callee_user_n_id")] public string CalleeUserNId { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "state")] public string State { get; set; } = "Ringing";
    [SugarColumn(ColumnName = "answer", IsNullable = true)] public string? Answer { get; set; }
    [SugarColumn(ColumnName = "request_n_id")] public string RequestNId { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "request_hash")] public string RequestHash { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "deadline_on")] public DateTimeOffset DeadlineOn { get; set; }
    [SugarColumn(ColumnName = "accepted_on", IsNullable = true)] public DateTimeOffset? AcceptedOn { get; set; }
    [SugarColumn(ColumnName = "started_on", IsNullable = true)] public DateTimeOffset? StartedOn { get; set; }
    [SugarColumn(ColumnName = "ended_on", IsNullable = true)] public DateTimeOffset? EndedOn { get; set; }
    [SugarColumn(ColumnName = "end_reason", IsNullable = true)] public string? EndReason { get; set; }
    [SugarColumn(ColumnName = "caller_connection_id")] public string CallerConnectionId { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "callee_connection_id", IsNullable = true)] public string? CalleeConnectionId { get; set; }
    [SugarColumn(ColumnName = "caller_alive_until", IsNullable = true)] public DateTimeOffset? CallerAliveUntil { get; set; }
    [SugarColumn(ColumnName = "callee_alive_until", IsNullable = true)] public DateTimeOffset? CalleeAliveUntil { get; set; }
    [SugarColumn(ColumnName = "caller_ready_on", IsNullable = true)] public DateTimeOffset? CallerReadyOn { get; set; }
    [SugarColumn(ColumnName = "callee_ready_on", IsNullable = true)] public DateTimeOffset? CalleeReadyOn { get; set; }
    [SugarColumn(ColumnName = "caller_stopped_reported_on", IsNullable = true)] public DateTimeOffset? CallerStoppedReportedOn { get; set; }
    [SugarColumn(ColumnName = "callee_stopped_reported_on", IsNullable = true)] public DateTimeOffset? CalleeStoppedReportedOn { get; set; }
}

[SugarTable("collaboration_remote_assistance_voice_slot")]
internal sealed class RemoteAssistanceVoiceSlotTable
{
    [SugarColumn(ColumnName = "tenant_n_id", IsPrimaryKey = true)] public string TenantNId { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "user_n_id", IsPrimaryKey = true)] public string UserNId { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "voice_call_n_id")] public string VoiceCallNId { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "created_on")] public DateTimeOffset CreatedOn { get; set; }
    [SugarColumn(ColumnName = "expires_on")] public DateTimeOffset ExpiresOn { get; set; }
}
