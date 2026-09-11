using System.Security.Cryptography;
using System.Text;
using IndustrialPlatform.Infrastructure.Database;
using SqlSugar;

namespace IndustrialPlatform.Collaboration.Infrastructure.Persistence;

public sealed record CollaborationMigrationStep(
    string Id,
    string Description,
    Func<ISqlSugarClient, CancellationToken, Task> Apply,
    Func<ISqlSugarClient, CancellationToken, Task>? Validate = null);

public static class CollaborationSchemaMigrations
{
    public static IReadOnlyList<CollaborationMigrationStep> All { get; } =
    [
        new("PF05-001", "create collaboration messaging tables", (sugar, _) => sugar.Ado.ExecuteCommandAsync(MessagingDdl(sugar.CurrentConnectionConfig.DbType)), ValidateMessagingSchemaAsync),
        new("PF05-002", "create collaboration compliance tables", (sugar, _) => sugar.Ado.ExecuteCommandAsync(ComplianceDdl(sugar.CurrentConnectionConfig.DbType)), ValidateComplianceSchemaAsync),
        new("PF05-003", "create collaboration indexes and retention defaults", (sugar, _) => sugar.Ado.ExecuteCommandAsync(IndexDdl(sugar.CurrentConnectionConfig.DbType))),
        new("PF05-004", "add export worker lease columns", AddExportWorkerLeaseColumnsAsync),
        new("PF05-005", "persist canonical compliance command payload", AddComplianceCommandPayloadColumnAsync),
        new("PF05-006", "add outbox lease and dead-letter columns", AddOutboxLeaseColumnsAsync),
        new("PF05-007", "create retention checkpoints", CreateRetentionCheckpointTableAsync),
        new("PF05-008", "persist attachment file reference identity", AddAttachmentReferenceColumnAsync),
        new("PF05-009", "create collaboration event inbox", CreateEventInboxTableAsync),
        new("PF05-010", "add collaboration event inbox leases", AddEventInboxLeaseColumnsAsync),
        new("PF05-011", "create personal message visibility", CreatePersonalMessageVisibilityTableAsync),
    ];

    private static async Task AddExportWorkerLeaseColumnsAsync(ISqlSugarClient sugar, CancellationToken cancellationToken)
    {
        var dbType = sugar.CurrentConnectionConfig.DbType;
        var timeType = dbType == DbType.Sqlite ? "TEXT" : "timestamptz";
        await AddColumnIfMissingAsync(sugar, "collaboration_compliance_export", "worker_lease_n_id", "TEXT NULL", dbType, cancellationToken);
        await AddColumnIfMissingAsync(sugar, "collaboration_compliance_export", "worker_lease_until", $"{timeType} NULL", dbType, cancellationToken);
    }

    private static Task ValidateMessagingSchemaAsync(ISqlSugarClient sugar, CancellationToken _)
    {
        SchemaPhysicalDriftGuard.Validate(
            sugar,
            "collaboration_conversation",
            ["id", "tenant_n_id", "n_id", "participant_low_user_n_id", "participant_high_user_n_id", "status", "last_message_sequence"],
            []);
        return Task.CompletedTask;
    }

    private static Task ValidateComplianceSchemaAsync(ISqlSugarClient sugar, CancellationToken _)
    {
        SchemaPhysicalDriftGuard.Validate(
            sugar,
            "collaboration_compliance_command",
            ["tenant_n_id", "actor_user_n_id", "request_n_id", "action", "request_hash", "scope_checksum", "command_json", "status"],
            []);
        return Task.CompletedTask;
    }

    private static async Task AddComplianceCommandPayloadColumnAsync(ISqlSugarClient sugar, CancellationToken cancellationToken)
    {
        var dbType = sugar.CurrentConnectionConfig.DbType;
        await AddColumnIfMissingAsync(sugar, "collaboration_compliance_command", "command_json", "TEXT NULL", dbType, cancellationToken);
    }

    private static async Task AddOutboxLeaseColumnsAsync(ISqlSugarClient sugar, CancellationToken cancellationToken)
    {
        var dbType = sugar.CurrentConnectionConfig.DbType;
        var timeType = dbType == DbType.Sqlite ? "TEXT" : "timestamptz";
        await AddColumnIfMissingAsync(sugar, "collaboration_outbox_message", "lease_n_id", "TEXT NULL", dbType, cancellationToken);
        await AddColumnIfMissingAsync(sugar, "collaboration_outbox_message", "lease_until", $"{timeType} NULL", dbType, cancellationToken);
        await AddColumnIfMissingAsync(sugar, "collaboration_outbox_message", "dead_lettered_on", $"{timeType} NULL", dbType, cancellationToken);
    }

    private static async Task CreateRetentionCheckpointTableAsync(ISqlSugarClient sugar, CancellationToken cancellationToken)
    {
        var (g, t, _, _, _) = Types(sugar.CurrentConnectionConfig.DbType);
        await sugar.Ado.ExecuteCommandAsync($"""
            CREATE TABLE IF NOT EXISTS collaboration_retention_checkpoint (
                id {g} PRIMARY KEY NOT NULL,
                tenant_n_id TEXT NOT NULL,
                operation_n_id TEXT NOT NULL,
                cutoff_on {t} NOT NULL,
                last_accepted_on {t} NULL,
                last_message_id {g} NULL,
                stage TEXT NOT NULL,
                lease_owner TEXT NULL,
                lease_expires_on {t} NULL,
                updated_on {t} NOT NULL,
                error_code TEXT NULL,
                CONSTRAINT uq_collaboration_retention_checkpoint UNIQUE (tenant_n_id, operation_n_id)
            )
            """, parameters: null, cancellationToken: cancellationToken);
    }

    private static async Task AddAttachmentReferenceColumnAsync(ISqlSugarClient sugar, CancellationToken cancellationToken)
    {
        await AddColumnIfMissingAsync(
            sugar,
            "collaboration_chat_attachment",
            "reference_n_id",
            "TEXT NULL",
            sugar.CurrentConnectionConfig.DbType,
            cancellationToken);
    }

    private static async Task CreateEventInboxTableAsync(ISqlSugarClient sugar, CancellationToken cancellationToken)
    {
        var (g, t, _, _, _) = Types(sugar.CurrentConnectionConfig.DbType);
        await sugar.Ado.ExecuteCommandAsync($"""
            CREATE TABLE IF NOT EXISTS collaboration_event_inbox (
                event_id {g} PRIMARY KEY NOT NULL,
                tenant_n_id TEXT NOT NULL,
                event_type TEXT NOT NULL,
                status TEXT NOT NULL,
                received_on {t} NOT NULL,
                processed_on {t} NULL,
                retry_count INTEGER NOT NULL,
                last_error TEXT NULL,
                lease_n_id TEXT NULL,
                lease_until {t} NULL
            )
            """, parameters: null, cancellationToken: cancellationToken);
    }

    private static async Task CreatePersonalMessageVisibilityTableAsync(ISqlSugarClient sugar, CancellationToken cancellationToken)
    {
        var (_, t, _, _, _) = Types(sugar.CurrentConnectionConfig.DbType);
        await sugar.Ado.ExecuteCommandAsync($"""
            CREATE TABLE IF NOT EXISTS collaboration_message_personal_visibility (
                tenant_n_id TEXT NOT NULL,
                user_n_id TEXT NOT NULL,
                conversation_n_id TEXT NOT NULL,
                message_n_id TEXT NOT NULL,
                hidden_on {t} NOT NULL,
                PRIMARY KEY (tenant_n_id, user_n_id, message_n_id)
            );
            CREATE INDEX IF NOT EXISTS ix_collaboration_message_personal_visibility_lookup
                ON collaboration_message_personal_visibility (tenant_n_id, user_n_id, conversation_n_id, message_n_id)
            """, parameters: null, cancellationToken: cancellationToken);
    }

    private static async Task AddColumnIfMissingAsync(
        ISqlSugarClient sugar,
        string tableName,
        string columnName,
        string definition,
        DbType dbType,
        CancellationToken cancellationToken)
    {
        if (dbType == DbType.PostgreSQL)
        {
            await sugar.Ado.ExecuteCommandAsync(
                $"ALTER TABLE {tableName} ADD COLUMN IF NOT EXISTS {columnName} {definition}",
                parameters: null,
                cancellationToken: cancellationToken);
            return;
        }

        var exists = sugar.Ado.GetDataTable($"SELECT name FROM pragma_table_info('{tableName}') WHERE lower(name) = lower('{columnName}')").Rows.Count > 0;
        if (!exists)
            await sugar.Ado.ExecuteCommandAsync($"ALTER TABLE {tableName} ADD COLUMN {columnName} {definition}", parameters: null, cancellationToken: cancellationToken);
    }

    private static (string Guid, string Time, string Bool, string Big, string False) Types(DbType dbType) => dbType switch
    {
        DbType.Sqlite => ("TEXT", "TEXT", "INTEGER", "INTEGER", "0"),
        DbType.PostgreSQL => ("uuid", "timestamptz", "BOOLEAN", "BIGINT", "false"),
        _ => throw new NotSupportedException($"不支持的目标数据库类型:{dbType}。"),
    };

    private static string Common(string g, string t, string b, string big) => $"""
        id {g} PRIMARY KEY NOT NULL,
        is_frozen {b} NOT NULL,
        is_locked {b} NOT NULL,
        is_deleted {b} NOT NULL,
        entity_type TEXT NOT NULL,
        created_on {t} NOT NULL,
        last_updated_on {t} NOT NULL,
        optimistic_version {big} NOT NULL,
        concurrency_version {g} NOT NULL
        """;

    private static string MessagingDdl(DbType dbType)
    {
        var (g, t, b, big, f) = Types(dbType);
        return $"""
        CREATE TABLE IF NOT EXISTS collaboration_conversation (
            {Common(g, t, b, big)},
            tenant_n_id TEXT NOT NULL,
            n_id TEXT NOT NULL,
            participant_low_user_n_id TEXT NOT NULL,
            participant_high_user_n_id TEXT NOT NULL,
            status TEXT NOT NULL,
            last_message_sequence {big} NOT NULL,
            last_message_n_id TEXT NULL,
            last_message_on {t} NULL,
            retention_floor_sequence {big} NOT NULL,
            CONSTRAINT uq_collaboration_conversation_nid UNIQUE (tenant_n_id, n_id),
            CONSTRAINT uq_collaboration_conversation_pair UNIQUE (tenant_n_id, participant_low_user_n_id, participant_high_user_n_id),
            CONSTRAINT ck_collaboration_conversation_pair CHECK (participant_low_user_n_id < participant_high_user_n_id)
        );
        CREATE TABLE IF NOT EXISTS collaboration_conversation_member (
            {Common(g, t, b, big)},
            tenant_n_id TEXT NOT NULL,
            conversation_n_id TEXT NOT NULL,
            user_n_id TEXT NOT NULL,
            display_name_snapshot TEXT NOT NULL,
            joined_on {t} NOT NULL,
            visibility_state TEXT NOT NULL,
            hidden_on {t} NULL,
            hidden_through_sequence {big} NOT NULL,
            last_read_sequence {big} NOT NULL,
            last_read_on {t} NULL,
            unread_count {big} NOT NULL,
            projection_version {big} NOT NULL,
            CONSTRAINT uq_collaboration_member UNIQUE (tenant_n_id, conversation_n_id, user_n_id)
        );
        CREATE TABLE IF NOT EXISTS collaboration_message (
            {Common(g, t, b, big)},
            tenant_n_id TEXT NOT NULL,
            conversation_n_id TEXT NOT NULL,
            message_n_id TEXT NOT NULL,
            sequence {big} NOT NULL,
            sender_user_n_id TEXT NOT NULL,
            client_message_n_id TEXT NOT NULL,
            request_hash TEXT NOT NULL,
            message_type TEXT NOT NULL,
            text_content TEXT NULL,
            reply_to_message_n_id TEXT NULL,
            attachment_n_id TEXT NULL,
            accepted_on {t} NOT NULL,
            retracted_on {t} NULL,
            retracted_by_user_n_id TEXT NULL,
            retraction_reason TEXT NULL,
            message_state_version INTEGER NOT NULL,
            CONSTRAINT uq_collaboration_message_sequence UNIQUE (tenant_n_id, conversation_n_id, sequence),
            CONSTRAINT uq_collaboration_message_client UNIQUE (tenant_n_id, sender_user_n_id, client_message_n_id)
        );
        CREATE TABLE IF NOT EXISTS collaboration_chat_attachment (
            {Common(g, t, b, big)},
            tenant_n_id TEXT NOT NULL,
            conversation_n_id TEXT NOT NULL,
            attachment_n_id TEXT NOT NULL,
            uploader_user_n_id TEXT NOT NULL,
            file_n_id TEXT NULL,
            file_name_snapshot TEXT NOT NULL,
            content_type_snapshot TEXT NOT NULL,
            size_snapshot {big} NOT NULL,
            purpose TEXT NOT NULL,
            file_state_projection TEXT NOT NULL,
            file_state_version INTEGER NULL,
            file_observed_on {t} NULL,
            reference_state TEXT NOT NULL,
            retention_state TEXT NOT NULL,
            bound_message_n_id TEXT NULL,
            intent_request_n_id TEXT NOT NULL,
            intent_request_hash TEXT NOT NULL,
            reference_n_id TEXT NULL,
            CONSTRAINT uq_collaboration_attachment_nid UNIQUE (tenant_n_id, conversation_n_id, attachment_n_id),
            CONSTRAINT uq_collaboration_attachment_intent UNIQUE (tenant_n_id, uploader_user_n_id, intent_request_n_id)
        );
        """;
    }

    private static string ComplianceDdl(DbType dbType)
    {
        var (g, t, b, big, _) = Types(dbType);
        return $"""
        CREATE TABLE IF NOT EXISTS collaboration_compliance_disposition (
            {Common(g, t, b, big)},
            tenant_n_id TEXT NOT NULL,
            disposition_n_id TEXT NOT NULL,
            subject_type TEXT NOT NULL,
            subject_n_id TEXT NOT NULL,
            state TEXT NOT NULL,
            reason TEXT NOT NULL,
            created_by_user_n_id TEXT NOT NULL,
            expires_on {t} NULL,
            request_n_id TEXT NULL,
            request_hash TEXT NULL,
            CONSTRAINT uq_collaboration_disposition UNIQUE (tenant_n_id, disposition_n_id),
            CONSTRAINT uq_collaboration_disposition_request UNIQUE (tenant_n_id, created_by_user_n_id, request_n_id)
        );
        CREATE TABLE IF NOT EXISTS collaboration_legal_hold_case (
            {Common(g, t, b, big)},
            tenant_n_id TEXT NOT NULL,
            hold_case_n_id TEXT NOT NULL,
            state TEXT NOT NULL,
            scope_json TEXT NOT NULL,
            scope_checksum TEXT NOT NULL,
            reason TEXT NOT NULL,
            created_by_user_n_id TEXT NOT NULL,
            released_on {t} NULL,
            request_n_id TEXT NULL,
            request_hash TEXT NULL,
            operation_id TEXT NULL,
            release_requested_by_user_n_id TEXT NULL,
            release_request_n_id TEXT NULL,
            release_approval_expires_on {t} NULL,
            release_approved_by_user_n_id TEXT NULL,
            reviewed_by_user_n_id TEXT NULL,
            reviewed_on {t} NULL,
            external_case_reference TEXT NULL,
            file_sync_state TEXT NOT NULL DEFAULT 'Pending',
            CONSTRAINT uq_collaboration_hold UNIQUE (tenant_n_id, hold_case_n_id),
            CONSTRAINT uq_collaboration_hold_request UNIQUE (tenant_n_id, created_by_user_n_id, request_n_id)
        );
        CREATE TABLE IF NOT EXISTS collaboration_compliance_export (
            {Common(g, t, b, big)},
            tenant_n_id TEXT NOT NULL,
            export_n_id TEXT NOT NULL,
            state TEXT NOT NULL,
            scope_json TEXT NOT NULL,
            scope_checksum TEXT NOT NULL,
            reason TEXT NOT NULL,
            created_by_user_n_id TEXT NOT NULL,
            expires_on {t} NULL,
            artifact_reference TEXT NULL,
            request_n_id TEXT NULL,
            request_hash TEXT NULL,
            case_reference TEXT NULL,
            approved_by_user_n_id TEXT NULL,
            approved_on {t} NULL,
            approval_expires_on {t} NULL,
            approval_consumed_on {t} NULL,
            run_deadline_on {t} NULL,
            completed_on {t} NULL,
            error_code TEXT NULL,
            fields_json TEXT NULL,
            operation_id TEXT NULL,
            export_retention_hours INTEGER NOT NULL DEFAULT 24,
            CONSTRAINT uq_collaboration_export UNIQUE (tenant_n_id, export_n_id),
            CONSTRAINT uq_collaboration_export_request UNIQUE (tenant_n_id, created_by_user_n_id, request_n_id),
            CONSTRAINT uq_collaboration_export_operation UNIQUE (tenant_n_id, operation_id)
        );
        CREATE TABLE IF NOT EXISTS collaboration_retention_policy (
            {Common(g, t, b, big)},
            tenant_n_id TEXT NOT NULL,
            policy_n_id TEXT NOT NULL,
            message_retention_days INTEGER NOT NULL,
            attachment_retention_days INTEGER NOT NULL,
            audit_retention_days INTEGER NOT NULL,
            enabled {b} NOT NULL,
            export_retention_hours INTEGER NOT NULL DEFAULT 24,
            review_due_hours INTEGER NOT NULL DEFAULT 24,
            status TEXT NOT NULL DEFAULT 'Active',
            CONSTRAINT uq_collaboration_retention UNIQUE (tenant_n_id, policy_n_id)
        );
        CREATE TABLE IF NOT EXISTS collaboration_compliance_command (
            tenant_n_id TEXT NOT NULL,
            actor_user_n_id TEXT NOT NULL,
            request_n_id TEXT NOT NULL,
            action TEXT NOT NULL,
            target_n_id TEXT NULL,
            request_hash TEXT NOT NULL,
            scope_checksum TEXT NOT NULL,
            command_json TEXT NOT NULL,
            status TEXT NOT NULL,
            result_json TEXT NULL,
            completed_on {t} NULL,
            CONSTRAINT pk_collaboration_compliance_command PRIMARY KEY (tenant_n_id, actor_user_n_id, request_n_id)
        );
        CREATE TABLE IF NOT EXISTS collaboration_compliance_preparation (
            tenant_n_id TEXT NOT NULL,
            actor_user_n_id TEXT NOT NULL,
            request_n_id TEXT NOT NULL,
            actor_session_n_id TEXT NOT NULL,
            action TEXT NOT NULL,
            target_n_id TEXT NULL,
            request_hash TEXT NOT NULL,
            scope_checksum TEXT NOT NULL,
            command_json TEXT NOT NULL,
            snapshot_json TEXT NOT NULL,
            prepared_on {t} NOT NULL,
            expires_on {t} NOT NULL,
            CONSTRAINT pk_collaboration_compliance_preparation PRIMARY KEY (tenant_n_id, actor_user_n_id, request_n_id)
        );
        CREATE TABLE IF NOT EXISTS collaboration_compliance_view_budget (
            tenant_n_id TEXT NOT NULL,
            actor_user_n_id TEXT NOT NULL,
            window_start_on {t} NOT NULL,
            result_count INTEGER NOT NULL,
            CONSTRAINT pk_collaboration_compliance_view_budget PRIMARY KEY (tenant_n_id, actor_user_n_id, window_start_on)
        );
        CREATE TABLE IF NOT EXISTS collaboration_messaging_deduplication (
            tenant_n_id TEXT NOT NULL,
            sender_user_n_id TEXT NOT NULL,
            client_message_n_id TEXT NOT NULL,
            request_hash TEXT NOT NULL,
            message_n_id TEXT NOT NULL,
            conversation_n_id TEXT NOT NULL,
            sequence {big} NOT NULL,
            accepted_on {t} NOT NULL,
            content_purged_on {t} NULL,
            CONSTRAINT pk_collaboration_message_deduplication PRIMARY KEY (tenant_n_id, sender_user_n_id, client_message_n_id)
        );
        CREATE TABLE IF NOT EXISTS collaboration_outbox_message (
            event_id {g} PRIMARY KEY NOT NULL,
            tenant_n_id TEXT NOT NULL,
            event_type TEXT NOT NULL,
            payload TEXT NOT NULL,
            created_on {t} NOT NULL,
            published_on {t} NULL,
            retry_count INTEGER NOT NULL,
            last_error TEXT NULL,
            lease_n_id TEXT NULL,
            lease_until {t} NULL,
            dead_lettered_on {t} NULL
        );
        CREATE TABLE IF NOT EXISTS collaboration_retention_checkpoint (
            id {g} PRIMARY KEY NOT NULL,
            tenant_n_id TEXT NOT NULL,
            operation_n_id TEXT NOT NULL,
            cutoff_on {t} NOT NULL,
            last_accepted_on {t} NULL,
            last_message_id {g} NULL,
            stage TEXT NOT NULL,
            lease_owner TEXT NULL,
            lease_expires_on {t} NULL,
            updated_on {t} NOT NULL,
            error_code TEXT NULL,
            CONSTRAINT uq_collaboration_retention_checkpoint UNIQUE (tenant_n_id, operation_n_id)
        );
        CREATE TABLE IF NOT EXISTS collaboration_event_inbox (
            event_id {g} PRIMARY KEY NOT NULL,
            tenant_n_id TEXT NOT NULL,
            event_type TEXT NOT NULL,
            status TEXT NOT NULL,
            received_on {t} NOT NULL,
            processed_on {t} NULL,
            retry_count INTEGER NOT NULL,
            last_error TEXT NULL,
            lease_n_id TEXT NULL
            ,lease_until {t} NULL
        );
        """;
    }

    private static async Task AddEventInboxLeaseColumnsAsync(ISqlSugarClient sugar, CancellationToken cancellationToken)
    {
        var dbType = sugar.CurrentConnectionConfig.DbType;
        var timeType = dbType == DbType.Sqlite ? "TEXT" : "timestamptz";
        await AddColumnIfMissingAsync(sugar, "collaboration_event_inbox", "lease_n_id", "TEXT NULL", dbType, cancellationToken);
        await AddColumnIfMissingAsync(sugar, "collaboration_event_inbox", "lease_until", $"{timeType} NULL", dbType, cancellationToken);
    }

    private static string IndexDdl(DbType dbType)
    {
        var (_, _, _, _, _) = Types(dbType);
        return """
        CREATE INDEX IF NOT EXISTS ix_collaboration_conversation_member_user ON collaboration_conversation_member (tenant_n_id, user_n_id, visibility_state);
        CREATE INDEX IF NOT EXISTS ix_collaboration_message_conversation_sequence ON collaboration_message (tenant_n_id, conversation_n_id, sequence);
        CREATE INDEX IF NOT EXISTS ix_collaboration_message_sender ON collaboration_message (tenant_n_id, sender_user_n_id, accepted_on);
        CREATE INDEX IF NOT EXISTS ix_collaboration_attachment_file ON collaboration_chat_attachment (tenant_n_id, file_n_id);
        CREATE INDEX IF NOT EXISTS ix_collaboration_disposition_subject ON collaboration_compliance_disposition (tenant_n_id, subject_type, subject_n_id, state);
        CREATE INDEX IF NOT EXISTS ix_collaboration_hold_state ON collaboration_legal_hold_case (tenant_n_id, state);
        CREATE INDEX IF NOT EXISTS ix_collaboration_export_state ON collaboration_compliance_export (tenant_n_id, state, created_on);
        CREATE INDEX IF NOT EXISTS ix_collaboration_outbox_pending ON collaboration_outbox_message (published_on, dead_lettered_on, lease_until, created_on);
        CREATE INDEX IF NOT EXISTS ix_collaboration_retention_checkpoint_active ON collaboration_retention_checkpoint (tenant_n_id, stage, updated_on);
        """;
    }
}

[SugarTable("collaboration_schema_migrations")]
public sealed class CollaborationSchemaMigrationRecord
{
    [SugarColumn(ColumnName = "migration_id", IsPrimaryKey = true)] public string MigrationId { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "description")] public string Description { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "checksum", IsNullable = true)] public string? Checksum { get; set; }
    [SugarColumn(ColumnName = "applied_on")] public DateTimeOffset AppliedOn { get; set; }
}
