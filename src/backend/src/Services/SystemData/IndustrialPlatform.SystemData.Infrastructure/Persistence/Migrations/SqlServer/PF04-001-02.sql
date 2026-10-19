-- SQL Server 2019/2022；新增 Provider 的独立脚本，应用后只能追加迁移。
-- system_file_object
IF OBJECT_ID(N'dbo.system_file_object',N'U') IS NULL
BEGIN
CREATE TABLE dbo.system_file_object (
    id uniqueidentifier PRIMARY KEY NOT NULL,
    tenant_n_id nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    file_n_id nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    upload_session_n_id nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    owner_user_n_id nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    file_name nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    content_type nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    content_length bigint NOT NULL,
    sha256 nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    storage_key nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    scan_status nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    restricted bit NOT NULL,
    deletion_status nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    created_on datetimeoffset(7) NOT NULL,
    last_updated_on datetimeoffset(7) NOT NULL,
    deleted_on datetimeoffset(7) NULL,
    retention_until datetimeoffset(7) NULL
);
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.system_file_object') AND name=N'ux_system_file_object_nid')
CREATE UNIQUE INDEX ux_system_file_object_nid ON dbo.system_file_object (tenant_n_id, file_n_id);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.system_file_object') AND name=N'ux_system_file_object_session')
CREATE UNIQUE INDEX ux_system_file_object_session ON dbo.system_file_object (tenant_n_id, upload_session_n_id);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.system_file_object') AND name=N'ix_system_file_object_sha256')
CREATE INDEX ix_system_file_object_sha256 ON dbo.system_file_object (tenant_n_id, sha256, content_length);
