-- SQL Server 2019/2022；新增 Provider 的独立脚本，应用后只能追加迁移。
-- system_collaboration_file_hold
IF OBJECT_ID(N'dbo.system_collaboration_file_hold',N'U') IS NULL
BEGIN
CREATE TABLE dbo.system_collaboration_file_hold (
    tenant_n_id nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    case_n_id nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    file_n_id nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    scope_checksum nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    case_revision bigint NOT NULL,
    owner_service nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    status nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    created_on datetimeoffset(7) NOT NULL,
    updated_on datetimeoffset(7) NOT NULL,
    released_on datetimeoffset(7) NULL,
    PRIMARY KEY (tenant_n_id, case_n_id, file_n_id)
);
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.system_collaboration_file_hold') AND name=N'ix_system_collaboration_file_hold_file_status')
CREATE INDEX ix_system_collaboration_file_hold_file_status ON dbo.system_collaboration_file_hold (tenant_n_id, file_n_id, status);
