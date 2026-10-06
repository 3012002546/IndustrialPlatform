-- SQL Server 2019/2022；新增 Provider 的独立脚本，应用后只能追加迁移。
-- system_audit_ingress_failure
IF OBJECT_ID(N'dbo.system_audit_ingress_failure',N'U') IS NULL
BEGIN
CREATE TABLE dbo.system_audit_ingress_failure (
    id uniqueidentifier PRIMARY KEY NOT NULL,
    tenant_n_id nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    failure_n_id nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    producer_service_key nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    audit_event_n_id nvarchar(max) COLLATE Latin1_General_100_BIN2 NULL,
    error_code nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    error_summary nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    payload_hash nvarchar(max) COLLATE Latin1_General_100_BIN2 NULL,
    occurred_on datetimeoffset(7) NOT NULL
);
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.system_audit_ingress_failure') AND name=N'ux_system_audit_ingress_failure_nid')
CREATE UNIQUE INDEX ux_system_audit_ingress_failure_nid ON dbo.system_audit_ingress_failure (tenant_n_id, failure_n_id);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.system_audit_ingress_failure') AND name=N'ix_system_audit_ingress_failure_time')
CREATE INDEX ix_system_audit_ingress_failure_time ON dbo.system_audit_ingress_failure (tenant_n_id, occurred_on);
