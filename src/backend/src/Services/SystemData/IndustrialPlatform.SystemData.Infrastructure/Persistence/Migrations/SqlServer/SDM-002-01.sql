-- SQL Server 2019/2022；新增 Provider 的独立脚本，应用后只能追加迁移。
-- create system_data_database_plan
IF OBJECT_ID(N'dbo.system_data_database_plan',N'U') IS NULL
BEGIN
CREATE TABLE dbo.system_data_database_plan (
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
    plan_n_id nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    environment_n_id nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    service_key nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    requested_migration_version nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    current_migration_version nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    target_state_fingerprint nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    plan_checksum nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    risk_level int NOT NULL,
    destructive_change_detected bit NOT NULL,
    required_policies int NOT NULL,
    expires_on datetimeoffset(7) NOT NULL,
    created_by_user_n_id nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    CONSTRAINT uq_plan_id_isdel UNIQUE (id, is_deleted)
);
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.system_data_database_plan') AND name=N'ux_plan_active_nid')
CREATE UNIQUE INDEX ux_plan_active_nid ON dbo.system_data_database_plan (tenant_n_id, plan_n_id) WHERE is_deleted = 0;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.system_data_database_plan') AND name=N'ux_plan_active_checksum')
CREATE UNIQUE INDEX ux_plan_active_checksum ON dbo.system_data_database_plan (plan_checksum) WHERE is_deleted = 0;
