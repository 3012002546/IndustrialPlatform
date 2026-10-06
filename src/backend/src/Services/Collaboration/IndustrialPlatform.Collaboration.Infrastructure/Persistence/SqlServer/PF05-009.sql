-- SQL Server 2019/2022；新增 Provider 的独立脚本，应用后只能追加迁移。
-- create collaboration event inbox
IF OBJECT_ID(N'dbo.collaboration_event_inbox',N'U') IS NULL
BEGIN
CREATE TABLE dbo.collaboration_event_inbox (
    event_id uniqueidentifier PRIMARY KEY NOT NULL,
    tenant_n_id nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    event_type nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    status nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    received_on datetimeoffset(7) NOT NULL,
    processed_on datetimeoffset(7) NULL,
    retry_count int NOT NULL,
    last_error nvarchar(max) COLLATE Latin1_General_100_BIN2 NULL,
    lease_n_id nvarchar(max) COLLATE Latin1_General_100_BIN2 NULL,
    lease_until datetimeoffset(7) NULL
);
END;
