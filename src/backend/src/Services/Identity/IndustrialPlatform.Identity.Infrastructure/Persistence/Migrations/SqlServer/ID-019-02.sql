-- SQL Server 2019/2022；新增 Provider 的独立脚本，应用后只能追加迁移。
-- create identity_bootstrap_credential
IF OBJECT_ID(N'dbo.identity_bootstrap_credential',N'U') IS NULL
BEGIN
CREATE TABLE dbo.identity_bootstrap_credential (
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
    user_n_id nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    state int NOT NULL,
    delivery_reference_hash nvarchar(max) COLLATE Latin1_General_100_BIN2 NULL,
    recovery_reference_hash nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    delivered_on datetimeoffset(7) NULL,
    recovered_on datetimeoffset(7) NULL
);
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.identity_bootstrap_credential') AND name=N'ix_bootstrap_credential_tenant')
CREATE INDEX ix_bootstrap_credential_tenant ON dbo.identity_bootstrap_credential (tenant_n_id, user_n_id, created_on);
