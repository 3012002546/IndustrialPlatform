-- SQL Server 2019/2022；新增 Provider 的独立脚本，应用后只能追加迁移。
-- create identity_user
IF OBJECT_ID(N'dbo.identity_user',N'U') IS NULL
BEGIN
CREATE TABLE dbo.identity_user (
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
    n_id nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    normalized_n_id nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    login_name nvarchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
    normalized_login_name nvarchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
    name nvarchar(256) COLLATE Latin1_General_100_BIN2 NOT NULL,
    password_hash nvarchar(1024) COLLATE Latin1_General_100_BIN2 NOT NULL,
    email nvarchar(256) COLLATE Latin1_General_100_BIN2 NULL,
    phone nvarchar(32) COLLATE Latin1_General_100_BIN2 NULL,
    status int NOT NULL,
    failed_login_count int NOT NULL,
    locked_until datetimeoffset(7) NULL,
    auth_version int NOT NULL,
    last_login_on datetimeoffset(7) NULL,
    must_change_password bit NOT NULL DEFAULT 0,
    CONSTRAINT uq_user_nid UNIQUE (tenant_n_id, normalized_n_id),
    CONSTRAINT uq_user_id_isdel UNIQUE (id, is_deleted)
);
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.identity_user') AND name=N'ix_user_status_updated')
CREATE INDEX ix_user_status_updated ON dbo.identity_user (status, last_updated_on, id);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.identity_user') AND name=N'ux_user_active_login_name')
CREATE UNIQUE INDEX ux_user_active_login_name ON dbo.identity_user (tenant_n_id, normalized_login_name) WHERE is_deleted = 0;
