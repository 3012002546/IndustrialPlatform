-- SQL Server 2019/2022；新增 Provider 的独立脚本，应用后只能追加迁移。
-- create identity_login_audit
IF OBJECT_ID(N'dbo.identity_login_audit',N'U') IS NULL
BEGIN
CREATE TABLE dbo.identity_login_audit (
    id uniqueidentifier PRIMARY KEY NOT NULL,
    is_frozen bit NOT NULL,
    is_locked bit NOT NULL,
    is_deleted bit NOT NULL,
    entity_type nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    created_on datetimeoffset(7) NOT NULL,
    last_updated_on datetimeoffset(7) NOT NULL,
    optimistic_version bigint NOT NULL,
    concurrency_version uniqueidentifier NOT NULL,
    tenant_n_id nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    user_n_id nvarchar(128) COLLATE Latin1_General_100_BIN2 NULL,
    login_name_snapshot nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    result int NOT NULL,
    failure_reason nvarchar(max) COLLATE Latin1_General_100_BIN2 NULL,
    ip_address_hash nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    user_agent_hash nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    trace_id nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL
);
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.identity_login_audit') AND name=N'ix_login_audit_tenant')
CREATE INDEX ix_login_audit_tenant ON dbo.identity_login_audit (tenant_n_id, created_on);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.identity_login_audit') AND name=N'ix_login_audit_user')
CREATE INDEX ix_login_audit_user ON dbo.identity_login_audit (user_n_id, created_on);
