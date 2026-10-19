-- SQL Server 2019/2022；新增 Provider 的独立脚本，应用后只能追加迁移。
-- create system_data_seed_observation
IF OBJECT_ID(N'dbo.system_data_seed_observation',N'U') IS NULL
BEGIN
CREATE TABLE dbo.system_data_seed_observation (
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
    service_key nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    module_key nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    seed_key nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    seed_version nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    checksum nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    scope int NOT NULL,
    status int NOT NULL,
    applied_on datetimeoffset(7) NULL,
    operation_n_id nvarchar(max) COLLATE Latin1_General_100_BIN2 NULL,
    verification_status int NOT NULL,
    CONSTRAINT uq_seed_observation_id_isdel UNIQUE (id, is_deleted)
);
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.system_data_seed_observation') AND name=N'ix_seed_observation_target')
CREATE INDEX ix_seed_observation_target ON dbo.system_data_seed_observation (tenant_n_id, environment_n_id, service_key, module_key, seed_key, created_on);
