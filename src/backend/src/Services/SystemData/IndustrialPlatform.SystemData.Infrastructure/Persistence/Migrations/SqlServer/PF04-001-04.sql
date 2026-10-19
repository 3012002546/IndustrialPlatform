-- SQL Server 2019/2022；新增 Provider 的独立脚本，应用后只能追加迁移。
-- system_file_reference_grant
IF OBJECT_ID(N'dbo.system_file_reference_grant',N'U') IS NULL
BEGIN
CREATE TABLE dbo.system_file_reference_grant (
    id uniqueidentifier PRIMARY KEY NOT NULL,
    tenant_n_id nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    reference_n_id nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    file_n_id nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    owner_user_n_id nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    purpose nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    created_on datetimeoffset(7) NOT NULL,
    deleted_on datetimeoffset(7) NULL
);
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.system_file_reference_grant') AND name=N'ux_system_file_reference_grant_nid')
CREATE UNIQUE INDEX ux_system_file_reference_grant_nid ON dbo.system_file_reference_grant (tenant_n_id, reference_n_id);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.system_file_reference_grant') AND name=N'ux_system_file_reference_grant_active')
CREATE UNIQUE INDEX ux_system_file_reference_grant_active ON dbo.system_file_reference_grant (tenant_n_id, file_n_id, owner_user_n_id, purpose);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.system_file_reference_grant') AND name=N'ix_system_file_reference_grant_file')
CREATE INDEX ix_system_file_reference_grant_file ON dbo.system_file_reference_grant (tenant_n_id, file_n_id, deleted_on);
