-- SQL Server 2019/2022；新增 Provider 的独立脚本，应用后只能追加迁移。
-- create system_data_database_operation
IF OBJECT_ID(N'dbo.system_data_database_operation',N'U') IS NULL
BEGIN
CREATE TABLE dbo.system_data_database_operation (
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
    operation_n_id nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    kind int NOT NULL,
    environment_n_id nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    service_key nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    plan_n_id nvarchar(max) COLLATE Latin1_General_100_BIN2 NULL,
    requested_version nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    idempotency_key nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    request_hash nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    status int NOT NULL,
    phase int NOT NULL,
    attempt int NOT NULL,
    lease_owner nvarchar(max) COLLATE Latin1_General_100_BIN2 NULL,
    lease_expires_on datetimeoffset(7) NULL,
    heartbeat_on datetimeoffset(7) NULL,
    queued_on datetimeoffset(7) NOT NULL,
    started_on datetimeoffset(7) NULL,
    completed_on datetimeoffset(7) NULL,
    timeout_on datetimeoffset(7) NOT NULL,
    sanitized_error_code nvarchar(max) COLLATE Latin1_General_100_BIN2 NULL,
    sanitized_error_summary nvarchar(max) COLLATE Latin1_General_100_BIN2 NULL,
    trace_id nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    created_by_user_n_id nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    CONSTRAINT uq_operation_id_isdel UNIQUE (id, is_deleted)
);
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.system_data_database_operation') AND name=N'ux_operation_active_nid')
CREATE UNIQUE INDEX ux_operation_active_nid ON dbo.system_data_database_operation (tenant_n_id, operation_n_id) WHERE is_deleted = 0;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.system_data_database_operation') AND name=N'ux_operation_active_idempotency')
CREATE UNIQUE INDEX ux_operation_active_idempotency ON dbo.system_data_database_operation (tenant_n_id, idempotency_key) WHERE is_deleted = 0;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.system_data_database_operation') AND name=N'ix_operation_tenant_status')
CREATE INDEX ix_operation_tenant_status ON dbo.system_data_database_operation (tenant_n_id, status, queued_on);
