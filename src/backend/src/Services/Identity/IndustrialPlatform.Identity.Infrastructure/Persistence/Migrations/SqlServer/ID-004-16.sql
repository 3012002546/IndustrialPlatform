-- SQL Server 2019/2022；新增 Provider 的独立脚本，应用后只能追加迁移。
-- create identity_sso_browser_session
IF OBJECT_ID(N'dbo.identity_sso_browser_session',N'U') IS NULL
BEGIN
CREATE TABLE dbo.identity_sso_browser_session (
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
    n_id nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    normalized_n_id nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    provider_n_id nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    user_id uniqueidentifier NOT NULL,
    user_is_deleted bit NOT NULL,
    session_handle_hash nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    auth_version int NOT NULL,
    last_activity_on datetimeoffset(7) NOT NULL,
    expires_on datetimeoffset(7) NOT NULL,
    revoked_on datetimeoffset(7) NULL,
    revoke_reason int NULL,
    CONSTRAINT uq_sso_browser_session_nid UNIQUE (n_id),
    CONSTRAINT uq_sso_browser_session_id_isdel UNIQUE (id, is_deleted),
    FOREIGN KEY (user_id, user_is_deleted) REFERENCES identity_user (id, is_deleted) ON UPDATE CASCADE
);
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.identity_sso_browser_session') AND name=N'ux_sso_browser_session_active_handle')
CREATE UNIQUE INDEX ux_sso_browser_session_active_handle ON dbo.identity_sso_browser_session (session_handle_hash) WHERE is_deleted = 0;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.identity_sso_browser_session') AND name=N'ix_sso_browser_session_tenant')
CREATE INDEX ix_sso_browser_session_tenant ON dbo.identity_sso_browser_session (tenant_n_id, user_id);
