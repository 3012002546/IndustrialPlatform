-- SQL Server 2019/2022；新增 Provider 的独立脚本，应用后只能追加迁移。
-- create identity_sso_external_account
IF OBJECT_ID(N'dbo.identity_sso_external_account',N'U') IS NULL
BEGIN
CREATE TABLE dbo.identity_sso_external_account (
    id uniqueidentifier PRIMARY KEY NOT NULL,
    is_frozen bit NOT NULL,
    is_locked bit NOT NULL,
    is_deleted bit NOT NULL,
    entity_type nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    created_on datetimeoffset(7) NOT NULL,
    last_updated_on datetimeoffset(7) NOT NULL,
    optimistic_version bigint NOT NULL,
    concurrency_version uniqueidentifier NOT NULL,
    n_id nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    normalized_n_id nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    sso_provider_id uniqueidentifier NOT NULL,
    sso_provider_is_deleted bit NOT NULL,
    external_subject nvarchar(256) COLLATE Latin1_General_100_BIN2 NOT NULL,
    user_id uniqueidentifier NOT NULL,
    user_is_deleted bit NOT NULL,
    external_name nvarchar(max) COLLATE Latin1_General_100_BIN2 NULL,
    external_email nvarchar(max) COLLATE Latin1_General_100_BIN2 NULL,
    last_login_on datetimeoffset(7) NULL,
    CONSTRAINT uq_sso_external_account_nid UNIQUE (n_id),
    CONSTRAINT uq_sso_external_account_id_isdel UNIQUE (id, is_deleted),
    FOREIGN KEY (sso_provider_id, sso_provider_is_deleted) REFERENCES identity_sso_provider (id, is_deleted) ON UPDATE CASCADE,
    FOREIGN KEY (user_id, user_is_deleted) REFERENCES identity_user (id, is_deleted) ON UPDATE CASCADE
);
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.identity_sso_external_account') AND name=N'ux_sso_external_account_active')
CREATE UNIQUE INDEX ux_sso_external_account_active ON dbo.identity_sso_external_account (sso_provider_id, external_subject) WHERE is_deleted = 0;
