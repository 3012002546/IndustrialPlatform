-- SQL Server 2019/2022；新增 Provider 的独立脚本，应用后只能追加迁移。
-- system_file_status_outbox
IF OBJECT_ID(N'dbo.system_file_status_outbox',N'U') IS NULL
BEGIN
CREATE TABLE dbo.system_file_status_outbox (
    event_id uniqueidentifier PRIMARY KEY NOT NULL,
    tenant_n_id nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    file_n_id nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    scan_status nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    restricted bit NOT NULL,
    deletion_status nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    observed_on datetimeoffset(7) NOT NULL,
    published_on datetimeoffset(7) NULL,
    next_attempt_on datetimeoffset(7) NULL,
    retry_count int NOT NULL,
    last_error nvarchar(max) COLLATE Latin1_General_100_BIN2 NULL,
    dead_lettered_on datetimeoffset(7) NULL
);
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.system_file_status_outbox') AND name=N'ix_system_file_status_outbox_pending')
CREATE INDEX ix_system_file_status_outbox_pending ON dbo.system_file_status_outbox (published_on, dead_lettered_on, next_attempt_on, observed_on);
