-- SQL Server 2019/2022；新增 Provider 的独立脚本，应用后只能追加迁移。
-- system_notification_inbox_delivery
IF OBJECT_ID(N'dbo.system_notification_inbox_delivery',N'U') IS NULL
BEGIN
CREATE TABLE dbo.system_notification_inbox_delivery (
    id uniqueidentifier PRIMARY KEY NOT NULL,
    tenant_n_id nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    notification_n_id nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    recipient_user_n_id nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    delivered_on datetimeoffset(7) NOT NULL,
    read_on datetimeoffset(7) NULL,
    expires_on datetimeoffset(7) NULL,
    revoked_on datetimeoffset(7) NULL
);
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.system_notification_inbox_delivery') AND name=N'ux_system_notification_delivery_recipient')
CREATE UNIQUE INDEX ux_system_notification_delivery_recipient ON dbo.system_notification_inbox_delivery (tenant_n_id, notification_n_id, recipient_user_n_id);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.system_notification_inbox_delivery') AND name=N'ix_system_notification_delivery_inbox')
CREATE INDEX ix_system_notification_delivery_inbox ON dbo.system_notification_inbox_delivery (tenant_n_id, recipient_user_n_id, read_on, delivered_on);
