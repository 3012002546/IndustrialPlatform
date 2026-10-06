-- SQL Server 2019/2022；新增 Provider 的独立脚本，应用后只能追加迁移。
-- system_notification_announcement
IF OBJECT_ID(N'dbo.system_notification_announcement',N'U') IS NULL
BEGIN
CREATE TABLE dbo.system_notification_announcement (
    id uniqueidentifier PRIMARY KEY NOT NULL,
    tenant_n_id nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    announcement_n_id nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    title nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    body nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    priority int NOT NULL,
    status nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    audience_user_n_ids_json nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    published_on datetimeoffset(7) NULL,
    expires_on datetimeoffset(7) NULL,
    created_by_user_n_id nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    created_on datetimeoffset(7) NOT NULL,
    last_updated_on datetimeoffset(7) NOT NULL,
    revoked_on datetimeoffset(7) NULL,
    resource_n_id nvarchar(max) COLLATE Latin1_General_100_BIN2 NULL,
    target_route nvarchar(max) COLLATE Latin1_General_100_BIN2 NULL,
    idempotency_key nvarchar(max) COLLATE Latin1_General_100_BIN2 NULL
);
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.system_notification_announcement') AND name=N'ux_system_notification_announcement_nid')
CREATE UNIQUE INDEX ux_system_notification_announcement_nid ON dbo.system_notification_announcement (tenant_n_id, announcement_n_id);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.system_notification_announcement') AND name=N'ix_system_notification_announcement_status')
CREATE INDEX ix_system_notification_announcement_status ON dbo.system_notification_announcement (tenant_n_id, status, published_on);
