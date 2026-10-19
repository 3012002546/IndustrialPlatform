-- SQL Server 2019/2022；新增 Provider 的独立脚本，应用后只能追加迁移。
-- create identity_sso_provider
IF OBJECT_ID(N'dbo.identity_sso_provider',N'U') IS NULL
BEGIN
CREATE TABLE dbo.identity_sso_provider (
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
    protocol int NOT NULL,
    authority_or_metadata_url nvarchar(1024) COLLATE Latin1_General_100_BIN2 NOT NULL,
    client_id_or_entity_id nvarchar(256) COLLATE Latin1_General_100_BIN2 NOT NULL,
    secret_or_certificate_reference nvarchar(256) COLLATE Latin1_General_100_BIN2 NULL,
    callback_path nvarchar(256) COLLATE Latin1_General_100_BIN2 NOT NULL,
    enabled bit NOT NULL,
    auto_redirect bit NOT NULL,
    provisioning_mode int NOT NULL,
    logout_mode int NOT NULL,
    allowed_email_domains_json nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    jit_default_role_n_ids_json nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    CONSTRAINT uq_sso_provider_nid UNIQUE (tenant_n_id, normalized_n_id),
    CONSTRAINT uq_sso_provider_id_isdel UNIQUE (id, is_deleted)
);
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.identity_sso_provider') AND name=N'ux_sso_provider_active_client')
CREATE UNIQUE INDEX ux_sso_provider_active_client ON dbo.identity_sso_provider (tenant_n_id, client_id_or_entity_id) WHERE is_deleted = 0;
