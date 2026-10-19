-- SQL Server 2019/2022；新增 Provider 的独立脚本，应用后只能追加迁移。
-- create system_data_database_approval
IF OBJECT_ID(N'dbo.system_data_database_approval',N'U') IS NULL
BEGIN
CREATE TABLE dbo.system_data_database_approval (
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
    approval_n_id nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    plan_n_id nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    plan_checksum nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    target_state_fingerprint nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    approved_by_user_n_id nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    reason nvarchar(max) COLLATE Latin1_General_100_BIN2 NULL,
    approved_on datetimeoffset(7) NOT NULL,
    expires_on datetimeoffset(7) NOT NULL,
    status int NOT NULL,
    CONSTRAINT uq_approval_id_isdel UNIQUE (id, is_deleted)
);
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.system_data_database_approval') AND name=N'ux_approval_active_nid')
CREATE UNIQUE INDEX ux_approval_active_nid ON dbo.system_data_database_approval (tenant_n_id, approval_n_id) WHERE is_deleted = 0;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.system_data_database_approval') AND name=N'ix_approval_plan')
CREATE INDEX ix_approval_plan ON dbo.system_data_database_approval (plan_n_id, approved_on);
