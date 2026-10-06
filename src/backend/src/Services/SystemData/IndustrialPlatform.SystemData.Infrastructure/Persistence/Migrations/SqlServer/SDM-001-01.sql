-- SQL Server 2019/2022；新增 Provider 的独立脚本，应用后只能追加迁移。
-- create system_data_database_environment_policy
IF OBJECT_ID(N'dbo.system_data_database_environment_policy',N'U') IS NULL
BEGIN
CREATE TABLE dbo.system_data_database_environment_policy (
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
    environment_n_id nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    environment_kind int NOT NULL,
    approval_required bit NOT NULL,
    backup_required bit NOT NULL,
    plan_ttl_seconds int NOT NULL,
    plan_timeout_seconds int NOT NULL,
    apply_timeout_seconds int NOT NULL,
    max_pre_migration_retries int NOT NULL,
    policy_revision int NOT NULL,
    CONSTRAINT uq_env_policy_id_isdel UNIQUE (id, is_deleted)
);
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.system_data_database_environment_policy') AND name=N'ux_env_policy_active')
CREATE UNIQUE INDEX ux_env_policy_active ON dbo.system_data_database_environment_policy (tenant_n_id, environment_n_id) WHERE is_deleted = 0;
