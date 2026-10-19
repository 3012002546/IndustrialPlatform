-- SQL Server 2019/2022；新增 Provider 的独立脚本，应用后只能追加迁移。
-- create system_data_database_registration
IF OBJECT_ID(N'dbo.system_data_database_registration',N'U') IS NULL
BEGIN
CREATE TABLE dbo.system_data_database_registration (
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
    provider nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    logical_database_name nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    physical_database_name nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    is_shared_physical_database bit NOT NULL,
    topology_mode nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    topology_revision nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    migration_artifact_id nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    migration_version nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    artifact_checksum nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    artifact_signature nvarchar(max) COLLATE Latin1_General_100_BIN2 NULL,
    owner_n_id nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    desired_state int NOT NULL,
    auto_provision bit NOT NULL,
    auto_migrate bit NOT NULL,
    manifest_version nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    manifest_checksum nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    status int NOT NULL,
    CONSTRAINT uq_registration_id_isdel UNIQUE (id, is_deleted)
);
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.system_data_database_registration') AND name=N'ux_registration_active')
CREATE UNIQUE INDEX ux_registration_active ON dbo.system_data_database_registration (tenant_n_id, environment_n_id, service_key) WHERE is_deleted = 0;
