-- SQL Server 2019/2022；新增 Provider 的独立脚本，应用后只能追加迁移。
-- create system_data_organization
IF OBJECT_ID(N'dbo.system_data_organization',N'U') IS NULL
BEGIN
CREATE TABLE dbo.system_data_organization (
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
    name nvarchar(256) COLLATE Latin1_General_100_BIN2 NOT NULL,
    normalized_name nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    type int NOT NULL,
    parent_organization_n_id nvarchar(128) COLLATE Latin1_General_100_BIN2 NULL,
    parent_organization_id uniqueidentifier NULL,
    parent_organization_is_deleted bit NOT NULL,
    display_order int NOT NULL,
    status int NOT NULL,
    organization_revision bigint NOT NULL,
    CONSTRAINT uq_organization_id_isdel UNIQUE (id, is_deleted),
    FOREIGN KEY (parent_organization_id) REFERENCES dbo.system_data_organization(id)
);
END;
EXEC(N'CREATE OR ALTER TRIGGER dbo.tr_system_data_organization_parent_deleted ON dbo.system_data_organization
AFTER INSERT, UPDATE AS
BEGIN
    SET NOCOUNT ON;
    UPDATE child SET parent_organization_is_deleted=parent.is_deleted
      FROM dbo.system_data_organization child
      JOIN inserted parent ON child.parent_organization_id=parent.id
      JOIN deleted old ON old.id=parent.id
     WHERE parent.is_deleted<>old.is_deleted;
    IF EXISTS (SELECT 1 FROM dbo.system_data_organization child
               JOIN inserted changed ON changed.id=child.id
               JOIN dbo.system_data_organization parent ON child.parent_organization_id=parent.id
               WHERE child.parent_organization_is_deleted<>parent.is_deleted OR child.parent_organization_is_deleted IS NULL)
        THROW 51001, N''Parent deletion snapshot mismatch.'', 1;
END');

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.system_data_organization') AND name=N'ux_organization_active_nid')
CREATE UNIQUE INDEX ux_organization_active_nid ON dbo.system_data_organization (tenant_n_id, normalized_n_id) WHERE is_deleted = 0;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.system_data_organization') AND name=N'ux_organization_active_sibling_name')
CREATE UNIQUE INDEX ux_organization_active_sibling_name ON dbo.system_data_organization (tenant_n_id, parent_organization_n_id, normalized_name) WHERE is_deleted = 0;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.system_data_organization') AND name=N'ix_organization_parent')
CREATE INDEX ix_organization_parent ON dbo.system_data_organization (tenant_n_id, parent_organization_n_id, status, display_order);
