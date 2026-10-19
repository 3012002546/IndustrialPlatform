-- SQL Server 2019/2022；新增 Provider 的独立脚本，应用后只能追加迁移。
-- create system_data_position
IF OBJECT_ID(N'dbo.system_data_position',N'U') IS NULL
BEGIN
CREATE TABLE dbo.system_data_position (
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
    n_id nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    normalized_n_id nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    organization_n_id nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    organization_id uniqueidentifier NOT NULL,
    organization_is_deleted bit NOT NULL,
    name nvarchar(256) COLLATE Latin1_General_100_BIN2 NOT NULL,
    normalized_name nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    description nvarchar(max) COLLATE Latin1_General_100_BIN2 NULL,
    display_order int NOT NULL,
    status int NOT NULL,
    CONSTRAINT uq_position_id_isdel UNIQUE (id, is_deleted),
    FOREIGN KEY (organization_id, organization_is_deleted)
    REFERENCES system_data_organization (id, is_deleted) ON UPDATE CASCADE
);
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.system_data_position') AND name=N'ux_position_active_nid')
CREATE UNIQUE INDEX ux_position_active_nid ON dbo.system_data_position (tenant_n_id, normalized_n_id) WHERE is_deleted = 0;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.system_data_position') AND name=N'ux_position_active_org_name')
CREATE UNIQUE INDEX ux_position_active_org_name ON dbo.system_data_position (tenant_n_id, organization_n_id, normalized_name) WHERE is_deleted = 0;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.system_data_position') AND name=N'ix_position_organization')
CREATE INDEX ix_position_organization ON dbo.system_data_position (tenant_n_id, organization_n_id);
