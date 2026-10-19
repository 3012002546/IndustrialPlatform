-- SQL Server 2019/2022；新增 Provider 的独立脚本，应用后只能追加迁移。
-- system_collaboration_file_reference
IF OBJECT_ID(N'dbo.system_collaboration_file_reference',N'U') IS NULL
BEGIN
CREATE TABLE dbo.system_collaboration_file_reference (
    tenant_n_id nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    reference_n_id nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    file_n_id nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    conversation_n_id nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    message_n_id nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    attachment_n_id nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    uploader_user_n_id nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    purpose nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    owner_service nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    status nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    version bigint NOT NULL,
    created_on datetimeoffset(7) NOT NULL,
    released_on datetimeoffset(7) NULL,
    PRIMARY KEY (tenant_n_id, reference_n_id)
);
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.system_collaboration_file_reference') AND name=N'ux_system_collaboration_file_reference_attachment_purpose')
CREATE UNIQUE INDEX ux_system_collaboration_file_reference_attachment_purpose ON dbo.system_collaboration_file_reference (tenant_n_id, attachment_n_id, purpose);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.system_collaboration_file_reference') AND name=N'ix_system_collaboration_file_reference_file_status')
CREATE INDEX ix_system_collaboration_file_reference_file_status ON dbo.system_collaboration_file_reference (tenant_n_id, file_n_id, status);
