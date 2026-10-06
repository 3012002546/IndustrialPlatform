-- SQL Server 2019/2022；新增 Provider 的独立脚本，应用后只能追加迁移。
-- create identity_outbox
IF OBJECT_ID(N'dbo.identity_outbox',N'U') IS NULL
BEGIN
CREATE TABLE dbo.identity_outbox (
    event_id uniqueidentifier PRIMARY KEY NOT NULL,
    event_type nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    event_version int NOT NULL,
    payload nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    event_created_time datetimeoffset(7) NOT NULL,
    published_on datetimeoffset(7) NULL,
    retry_count int NOT NULL,
    last_error nvarchar(max) COLLATE Latin1_General_100_BIN2 NULL
);
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.identity_outbox') AND name=N'ix_outbox_publish')
CREATE INDEX ix_outbox_publish ON dbo.identity_outbox (published_on, event_created_time);
