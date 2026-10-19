-- SQL Server 2019/2022；新增 Provider 的独立脚本，应用后只能追加迁移。
-- system_notification_message
IF OBJECT_ID(N'dbo.system_notification_message',N'U') IS NULL
BEGIN
CREATE TABLE dbo.system_notification_message (
    id uniqueidentifier PRIMARY KEY NOT NULL,
    tenant_n_id nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    notification_n_id nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    announcement_n_id nvarchar(max) COLLATE Latin1_General_100_BIN2 NULL,
    kind nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    title nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    body nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    sender_user_n_id nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    created_on datetimeoffset(7) NOT NULL,
    expires_on datetimeoffset(7) NULL,
    revoked_on datetimeoffset(7) NULL,
    resource_n_id nvarchar(max) COLLATE Latin1_General_100_BIN2 NULL,
    target_route nvarchar(max) COLLATE Latin1_General_100_BIN2 NULL,
    idempotency_key nvarchar(max) COLLATE Latin1_General_100_BIN2 NULL
);
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.system_notification_message') AND name=N'ux_system_notification_message_nid')
CREATE UNIQUE INDEX ux_system_notification_message_nid ON dbo.system_notification_message (tenant_n_id, notification_n_id);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.system_notification_message') AND name=N'ix_system_notification_message_created')
CREATE INDEX ix_system_notification_message_created ON dbo.system_notification_message (tenant_n_id, created_on);
