-- SQL Server 2019/2022；新增 Provider 的独立脚本，应用后只能追加迁移。
-- system_audit_fact
IF OBJECT_ID(N'dbo.system_audit_fact',N'U') IS NULL
BEGIN
CREATE TABLE dbo.system_audit_fact (
    id uniqueidentifier PRIMARY KEY NOT NULL,
    tenant_n_id nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    producer_service_key nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    audit_event_n_id nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    occurred_on datetimeoffset(7) NOT NULL,
    received_on datetimeoffset(7) NOT NULL,
    actor_user_n_id nvarchar(max) COLLATE Latin1_General_100_BIN2 NULL,
    action nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    object_type nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    object_n_id nvarchar(max) COLLATE Latin1_General_100_BIN2 NULL,
    payload_json nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    payload_hash nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    trace_id nvarchar(max) COLLATE Latin1_General_100_BIN2 NULL,
    severity nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    source_ip nvarchar(max) COLLATE Latin1_General_100_BIN2 NULL,
    user_agent nvarchar(max) COLLATE Latin1_General_100_BIN2 NULL
);
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.system_audit_fact') AND name=N'ux_system_audit_fact_event')
CREATE UNIQUE INDEX ux_system_audit_fact_event ON dbo.system_audit_fact (tenant_n_id, producer_service_key, audit_event_n_id);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.system_audit_fact') AND name=N'ix_system_audit_fact_query')
CREATE INDEX ix_system_audit_fact_query ON dbo.system_audit_fact (tenant_n_id, occurred_on, producer_service_key, action);
