-- SQL Server 2019/2022；新增 Provider 的独立脚本，应用后只能追加迁移。
-- system_audit_outbox
IF OBJECT_ID(N'dbo.system_audit_outbox',N'U') IS NULL
BEGIN
CREATE TABLE dbo.system_audit_outbox (
    id uniqueidentifier PRIMARY KEY NOT NULL,
    event_id uniqueidentifier NOT NULL,
    tenant_n_id nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    payload nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    created_on datetimeoffset(7) NOT NULL,
    published_on datetimeoffset(7) NULL,
    retry_count int NOT NULL,
    last_error nvarchar(max) COLLATE Latin1_General_100_BIN2 NULL,
    next_attempt_on datetimeoffset(7) NULL,
    dead_on datetimeoffset(7) NULL
);
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.system_audit_outbox') AND name=N'ux_system_audit_outbox_event')
CREATE UNIQUE INDEX ux_system_audit_outbox_event ON dbo.system_audit_outbox (event_id);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.system_audit_outbox') AND name=N'ix_system_audit_outbox_pending')
CREATE INDEX ix_system_audit_outbox_pending ON dbo.system_audit_outbox (published_on, created_on);
