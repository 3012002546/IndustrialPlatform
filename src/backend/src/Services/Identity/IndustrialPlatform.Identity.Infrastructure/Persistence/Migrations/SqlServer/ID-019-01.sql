-- SQL Server 2019/2022；新增 Provider 的独立脚本，应用后只能追加迁移。
-- create identity_seed_ledger
IF OBJECT_ID(N'dbo.identity_seed_ledger',N'U') IS NULL
BEGIN
CREATE TABLE dbo.identity_seed_ledger (
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
    seed_n_id nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    seed_version nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    seed_class nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    scope nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    checksum nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    status nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    applied_on datetimeoffset(7) NULL,
    system_data_operation_n_id nvarchar(max) COLLATE Latin1_General_100_BIN2 NULL,
    trace_id nvarchar(max) COLLATE Latin1_General_100_BIN2 NULL,
    CONSTRAINT uq_seed_ledger_version UNIQUE (tenant_n_id, seed_n_id, seed_version)
);
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.identity_seed_ledger') AND name=N'ix_seed_ledger_tenant')
CREATE INDEX ix_seed_ledger_tenant ON dbo.identity_seed_ledger (tenant_n_id, created_on);
