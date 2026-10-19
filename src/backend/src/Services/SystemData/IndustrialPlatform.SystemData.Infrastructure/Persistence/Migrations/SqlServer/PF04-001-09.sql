-- SQL Server 2019/2022；新增 Provider 的独立脚本，应用后只能追加迁移。
-- system_audit_lifecycle
IF OBJECT_ID(N'dbo.system_audit_lifecycle',N'U') IS NULL
BEGIN
CREATE TABLE dbo.system_audit_lifecycle (
    id uniqueidentifier PRIMARY KEY NOT NULL,
    tenant_n_id nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    producer_service_key nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    audit_event_n_id nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    state nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    retention_until datetimeoffset(7) NULL,
    archived_on datetimeoffset(7) NULL,
    deleted_on datetimeoffset(7) NULL,
    changed_on datetimeoffset(7) NOT NULL
);
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.system_audit_lifecycle') AND name=N'ux_system_audit_lifecycle_event')
CREATE UNIQUE INDEX ux_system_audit_lifecycle_event ON dbo.system_audit_lifecycle (tenant_n_id, producer_service_key, audit_event_n_id);
