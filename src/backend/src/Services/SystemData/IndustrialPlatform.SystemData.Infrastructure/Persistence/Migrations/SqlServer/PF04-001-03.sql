-- SQL Server 2019/2022；新增 Provider 的独立脚本，应用后只能追加迁移。
-- system_file_scan_attempt
IF OBJECT_ID(N'dbo.system_file_scan_attempt',N'U') IS NULL
BEGIN
CREATE TABLE dbo.system_file_scan_attempt (
    id uniqueidentifier PRIMARY KEY NOT NULL,
    tenant_n_id nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    scan_n_id nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    file_n_id nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    engine nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    status nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    started_on datetimeoffset(7) NOT NULL,
    completed_on datetimeoffset(7) NULL,
    detail nvarchar(max) COLLATE Latin1_General_100_BIN2 NULL
);
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.system_file_scan_attempt') AND name=N'ux_system_file_scan_attempt_nid')
CREATE UNIQUE INDEX ux_system_file_scan_attempt_nid ON dbo.system_file_scan_attempt (tenant_n_id, scan_n_id);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.system_file_scan_attempt') AND name=N'ix_system_file_scan_attempt_file')
CREATE INDEX ix_system_file_scan_attempt_file ON dbo.system_file_scan_attempt (tenant_n_id, file_n_id, started_on);
