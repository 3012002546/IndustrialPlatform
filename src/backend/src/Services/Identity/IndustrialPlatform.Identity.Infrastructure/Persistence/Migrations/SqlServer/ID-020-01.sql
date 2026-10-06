-- SQL Server 2019/2022；新增 Provider 的独立脚本，应用后只能追加迁移。
-- create identity_write_idempotency
IF OBJECT_ID(N'dbo.identity_write_idempotency',N'U') IS NULL
BEGIN
CREATE TABLE dbo.identity_write_idempotency (
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
    actor_user_n_id nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    idempotency_key nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    request_hash nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    completed bit NOT NULL,
    completed_on datetimeoffset(7) NULL
);
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.identity_write_idempotency') AND name=N'ux_write_idempotency_active')
CREATE UNIQUE INDEX ux_write_idempotency_active ON dbo.identity_write_idempotency (tenant_n_id, actor_user_n_id, idempotency_key) WHERE is_deleted = 0;
