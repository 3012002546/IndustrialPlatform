using SqlSugar;

namespace IndustrialPlatform.RuntimeSupport.Migrations;

/// <summary>平台与Embedded共用的文件/审计DDL；原有步骤调用同一定义，保持历史输出不变。</summary>
public static class RuntimeFileAuditSchema
{
    public static string FileStatusOutboxDdl(DbType dbType)
    {
        var (g, t, b, _, _) = TypeWords(dbType);
        return $"""
            CREATE TABLE IF NOT EXISTS system_file_status_outbox (
                event_id {g} PRIMARY KEY NOT NULL,
                tenant_n_id TEXT NOT NULL,
                file_n_id TEXT NOT NULL,
                scan_status TEXT NOT NULL,
                restricted {b} NOT NULL,
                deletion_status TEXT NOT NULL,
                observed_on {t} NOT NULL,
                published_on {t} NULL,
                next_attempt_on {t} NULL,
                retry_count INTEGER NOT NULL,
                last_error TEXT NULL,
                dead_lettered_on {t} NULL
            );
            CREATE INDEX IF NOT EXISTS ix_system_file_status_outbox_pending ON system_file_status_outbox (published_on, dead_lettered_on, next_attempt_on, observed_on);
            """;
    }

    public static string FileUploadSessionDdl(DbType dbType)
    {
        var (g, t, _, big, _) = TypeWords(dbType);
        return $"""
            CREATE TABLE IF NOT EXISTS system_file_upload_session (
                id {g} PRIMARY KEY NOT NULL,
                tenant_n_id TEXT NOT NULL,
                session_n_id TEXT NOT NULL,
                transport_id TEXT NOT NULL,
                uploader_user_n_id TEXT NOT NULL,
                purpose TEXT NOT NULL,
                file_name TEXT NOT NULL,
                content_type TEXT NOT NULL,
                expected_length {big} NOT NULL,
                expected_sha256 TEXT NULL,
                sample_fingerprint TEXT NULL,
                current_offset {big} NOT NULL,
                writer_epoch INTEGER NOT NULL,
                status TEXT NOT NULL,
                expires_on {t} NOT NULL,
                completed_on {t} NULL,
                file_n_id TEXT NULL,
                error_code TEXT NULL,
                created_on {t} NOT NULL,
                last_updated_on {t} NOT NULL
            );
            CREATE UNIQUE INDEX IF NOT EXISTS ux_system_file_upload_session_nid ON system_file_upload_session (tenant_n_id, session_n_id);
            CREATE UNIQUE INDEX IF NOT EXISTS ux_system_file_upload_session_transport ON system_file_upload_session (tenant_n_id, transport_id);
            CREATE INDEX IF NOT EXISTS ix_system_file_upload_session_candidate ON system_file_upload_session (tenant_n_id, uploader_user_n_id, purpose, sample_fingerprint, status);
            """;
    }

    public static string FileObjectDdl(DbType dbType)
    {
        var (g, t, b, big, _) = TypeWords(dbType);
        return $"""
            CREATE TABLE IF NOT EXISTS system_file_object (
                id {g} PRIMARY KEY NOT NULL,
                tenant_n_id TEXT NOT NULL,
                file_n_id TEXT NOT NULL,
                upload_session_n_id TEXT NOT NULL,
                owner_user_n_id TEXT NOT NULL,
                file_name TEXT NOT NULL,
                content_type TEXT NOT NULL,
                content_length {big} NOT NULL,
                sha256 TEXT NOT NULL,
                storage_key TEXT NOT NULL,
                scan_status TEXT NOT NULL,
                restricted {b} NOT NULL,
                deletion_status TEXT NOT NULL,
                created_on {t} NOT NULL,
                last_updated_on {t} NOT NULL,
                deleted_on {t} NULL,
                retention_until {t} NULL
            );
            CREATE UNIQUE INDEX IF NOT EXISTS ux_system_file_object_nid ON system_file_object (tenant_n_id, file_n_id);
            CREATE UNIQUE INDEX IF NOT EXISTS ux_system_file_object_session ON system_file_object (tenant_n_id, upload_session_n_id);
            CREATE INDEX IF NOT EXISTS ix_system_file_object_sha256 ON system_file_object (tenant_n_id, sha256, content_length);
            """;
    }

    public static string FileScanAttemptDdl(DbType dbType)
    {
        var (g, t, _, _, _) = TypeWords(dbType);
        return $"""
            CREATE TABLE IF NOT EXISTS system_file_scan_attempt (
                id {g} PRIMARY KEY NOT NULL,
                tenant_n_id TEXT NOT NULL,
                scan_n_id TEXT NOT NULL,
                file_n_id TEXT NOT NULL,
                engine TEXT NOT NULL,
                status TEXT NOT NULL,
                started_on {t} NOT NULL,
                completed_on {t} NULL,
                detail TEXT NULL
            );
            CREATE UNIQUE INDEX IF NOT EXISTS ux_system_file_scan_attempt_nid ON system_file_scan_attempt (tenant_n_id, scan_n_id);
            CREATE INDEX IF NOT EXISTS ix_system_file_scan_attempt_file ON system_file_scan_attempt (tenant_n_id, file_n_id, started_on);
            """;
    }

    public static string FileReferenceGrantDdl(DbType dbType)
    {
        var (g, t, _, _, _) = TypeWords(dbType);
        return $"""
            CREATE TABLE IF NOT EXISTS system_file_reference_grant (
                id {g} PRIMARY KEY NOT NULL,
                tenant_n_id TEXT NOT NULL,
                reference_n_id TEXT NOT NULL,
                file_n_id TEXT NOT NULL,
                owner_user_n_id TEXT NOT NULL,
                purpose TEXT NOT NULL,
                created_on {t} NOT NULL,
                deleted_on {t} NULL
            );
            CREATE UNIQUE INDEX IF NOT EXISTS ux_system_file_reference_grant_nid ON system_file_reference_grant (tenant_n_id, reference_n_id);
            CREATE UNIQUE INDEX IF NOT EXISTS ux_system_file_reference_grant_active ON system_file_reference_grant (tenant_n_id, file_n_id, owner_user_n_id, purpose);
            CREATE INDEX IF NOT EXISTS ix_system_file_reference_grant_file ON system_file_reference_grant (tenant_n_id, file_n_id, deleted_on);
            """;
    }

    public static string AuditFactDdl(DbType dbType)
    {
        var (g, t, _, _, _) = TypeWords(dbType);
        return $"""
            CREATE TABLE IF NOT EXISTS system_audit_fact (
                id {g} PRIMARY KEY NOT NULL,
                tenant_n_id TEXT NOT NULL,
                producer_service_key TEXT NOT NULL,
                audit_event_n_id TEXT NOT NULL,
                occurred_on {t} NOT NULL,
                received_on {t} NOT NULL,
                actor_user_n_id TEXT NULL,
                action TEXT NOT NULL,
                object_type TEXT NOT NULL,
                object_n_id TEXT NULL,
                payload_json TEXT NOT NULL,
                payload_hash TEXT NOT NULL,
                trace_id TEXT NULL,
                severity TEXT NOT NULL,
                source_ip TEXT NULL,
                user_agent TEXT NULL
            );
            CREATE UNIQUE INDEX IF NOT EXISTS ux_system_audit_fact_event ON system_audit_fact (tenant_n_id, producer_service_key, audit_event_n_id);
            CREATE INDEX IF NOT EXISTS ix_system_audit_fact_query ON system_audit_fact (tenant_n_id, occurred_on, producer_service_key, action);
            """;
    }

    public static string AuditOutboxDdl(DbType dbType)
    {
        var (g, t, _, _, _) = TypeWords(dbType);
        return $"""
            CREATE TABLE IF NOT EXISTS system_audit_outbox (
                id {g} PRIMARY KEY NOT NULL,
                event_id {g} NOT NULL,
                tenant_n_id TEXT NOT NULL,
                payload TEXT NOT NULL,
                created_on {t} NOT NULL,
                published_on {t} NULL,
                retry_count INTEGER NOT NULL,
                last_error TEXT NULL,
                next_attempt_on {t} NULL,
                dead_on {t} NULL
            );
            CREATE UNIQUE INDEX IF NOT EXISTS ux_system_audit_outbox_event ON system_audit_outbox (event_id);
            CREATE INDEX IF NOT EXISTS ix_system_audit_outbox_pending ON system_audit_outbox (published_on, created_on);
            """;
    }

    public static string AuditLifecycleDdl(DbType dbType)
    {
        var (g, t, _, _, _) = TypeWords(dbType);
        return $"""
            CREATE TABLE IF NOT EXISTS system_audit_lifecycle (
                id {g} PRIMARY KEY NOT NULL,
                tenant_n_id TEXT NOT NULL,
                producer_service_key TEXT NOT NULL,
                audit_event_n_id TEXT NOT NULL,
                state TEXT NOT NULL,
                retention_until {t} NULL,
                archived_on {t} NULL,
                deleted_on {t} NULL,
                changed_on {t} NOT NULL
            );
            CREATE UNIQUE INDEX IF NOT EXISTS ux_system_audit_lifecycle_event ON system_audit_lifecycle (tenant_n_id, producer_service_key, audit_event_n_id);
            """;
    }

    public static string AuditIngressFailureDdl(DbType dbType)
    {
        var (g, t, _, _, _) = TypeWords(dbType);
        return $"""
            CREATE TABLE IF NOT EXISTS system_audit_ingress_failure (
                id {g} PRIMARY KEY NOT NULL,
                tenant_n_id TEXT NOT NULL,
                failure_n_id TEXT NOT NULL,
                producer_service_key TEXT NOT NULL,
                audit_event_n_id TEXT NULL,
                error_code TEXT NOT NULL,
                error_summary TEXT NOT NULL,
                payload_hash TEXT NULL,
                occurred_on {t} NOT NULL
            );
            CREATE UNIQUE INDEX IF NOT EXISTS ux_system_audit_ingress_failure_nid ON system_audit_ingress_failure (tenant_n_id, failure_n_id);
            CREATE INDEX IF NOT EXISTS ix_system_audit_ingress_failure_time ON system_audit_ingress_failure (tenant_n_id, occurred_on);
            """;
    }

    public static string AuditLifecycleLegalHoldAlterDdl(DbType dbType)
    {
        var (_, _, b, _, f) = TypeWords(dbType);
        var ifNotExists = dbType == DbType.PostgreSQL ? " IF NOT EXISTS" : string.Empty;
        return $"""
            ALTER TABLE system_audit_lifecycle ADD COLUMN{ifNotExists} legal_hold {b} NOT NULL DEFAULT {f};
            ALTER TABLE system_audit_lifecycle ADD COLUMN{ifNotExists} legal_hold_reason TEXT NULL;
            """;
    }

    private static (string G, string T, string B, string Big, string F) TypeWords(DbType dbType) => dbType switch
    {
        DbType.Sqlite => ("TEXT", "TEXT", "INTEGER", "INTEGER", "0"),
        DbType.PostgreSQL => ("uuid", "timestamptz", "BOOLEAN", "BIGINT", "false"),
        _ => throw new NotSupportedException("未支持的共享DDL Provider。"),
    };
}
