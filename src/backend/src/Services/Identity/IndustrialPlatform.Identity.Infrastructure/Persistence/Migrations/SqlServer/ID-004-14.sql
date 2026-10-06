-- SQL Server 2019/2022；新增 Provider 的独立脚本，应用后只能追加迁移。
-- create identity_sso_client
IF OBJECT_ID(N'dbo.identity_sso_client',N'U') IS NULL
BEGIN
CREATE TABLE dbo.identity_sso_client (
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
    name nvarchar(256) COLLATE Latin1_General_100_BIN2 NOT NULL,
    oauth_client_id nvarchar(256) COLLATE Latin1_General_100_BIN2 NOT NULL,
    enabled bit NOT NULL,
    CONSTRAINT uq_sso_client_nid UNIQUE (tenant_n_id, normalized_n_id),
    CONSTRAINT uq_sso_client_id_isdel UNIQUE (id, is_deleted)
);
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.identity_sso_client') AND name=N'ux_sso_client_active_oauth')
CREATE UNIQUE INDEX ux_sso_client_active_oauth ON dbo.identity_sso_client (tenant_n_id, oauth_client_id) WHERE is_deleted = 0;
