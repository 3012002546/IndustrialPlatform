-- SQL Server 2019/2022；新增 Provider 的独立脚本，应用后只能追加迁移。
-- create system_data_database_backup_evidence
IF OBJECT_ID(N'dbo.system_data_database_backup_evidence',N'U') IS NULL
BEGIN
CREATE TABLE dbo.system_data_database_backup_evidence (
    id uniqueidentifier PRIMARY KEY NOT NULL,
    is_frozen bit NOT NULL,
    is_locked bit NOT NULL,
    is_deleted bit NOT NULL,
    entity_type nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    created_on datetimeoffset(7) NOT NULL,
    last_updated_on datetimeoffset(7) NOT NULL,
    optimistic_version bigint NOT NULL,
    concurrency_version uniqueidentifier NOT NULL,
    tenant_n_id nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    evidence_n_id nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    plan_n_id nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    plan_checksum nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    target_state_fingerprint nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    backup_provider nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    backup_reference nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    captured_on datetimeoffset(7) NOT NULL,
    verified_on datetimeoffset(7) NULL,
    retention_until datetimeoffset(7) NOT NULL,
    verified_by_user_n_id nvarchar(max) COLLATE Latin1_General_100_BIN2 NULL,
    status int NOT NULL,
    CONSTRAINT uq_backup_evidence_id_isdel UNIQUE (id, is_deleted)
);
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.system_data_database_backup_evidence') AND name=N'ux_backup_evidence_active_nid')
CREATE UNIQUE INDEX ux_backup_evidence_active_nid ON dbo.system_data_database_backup_evidence (tenant_n_id, evidence_n_id) WHERE is_deleted = 0;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.system_data_database_backup_evidence') AND name=N'ix_backup_evidence_plan')
CREATE INDEX ix_backup_evidence_plan ON dbo.system_data_database_backup_evidence (plan_n_id, captured_on);
