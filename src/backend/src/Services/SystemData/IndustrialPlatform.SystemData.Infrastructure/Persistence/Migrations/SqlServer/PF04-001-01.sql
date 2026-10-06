-- SQL Server 2019/2022；新增 Provider 的独立脚本，应用后只能追加迁移。
-- system_file_upload_session
IF OBJECT_ID(N'dbo.system_file_upload_session',N'U') IS NULL
BEGIN
CREATE TABLE dbo.system_file_upload_session (
    id uniqueidentifier PRIMARY KEY NOT NULL,
    tenant_n_id nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    session_n_id nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    transport_id nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    uploader_user_n_id nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    purpose nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    file_name nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    content_type nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    expected_length bigint NOT NULL,
    expected_sha256 nvarchar(max) COLLATE Latin1_General_100_BIN2 NULL,
    sample_fingerprint nvarchar(128) COLLATE Latin1_General_100_BIN2 NULL,
    current_offset bigint NOT NULL,
    writer_epoch int NOT NULL,
    status nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    expires_on datetimeoffset(7) NOT NULL,
    completed_on datetimeoffset(7) NULL,
    file_n_id nvarchar(max) COLLATE Latin1_General_100_BIN2 NULL,
    error_code nvarchar(max) COLLATE Latin1_General_100_BIN2 NULL,
    created_on datetimeoffset(7) NOT NULL,
    last_updated_on datetimeoffset(7) NOT NULL
);
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.system_file_upload_session') AND name=N'ux_system_file_upload_session_nid')
CREATE UNIQUE INDEX ux_system_file_upload_session_nid ON dbo.system_file_upload_session (tenant_n_id, session_n_id);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.system_file_upload_session') AND name=N'ux_system_file_upload_session_transport')
CREATE UNIQUE INDEX ux_system_file_upload_session_transport ON dbo.system_file_upload_session (tenant_n_id, transport_id);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.system_file_upload_session') AND name=N'ix_system_file_upload_session_candidate')
CREATE INDEX ix_system_file_upload_session_candidate ON dbo.system_file_upload_session (tenant_n_id, uploader_user_n_id, purpose, sample_fingerprint, status);
