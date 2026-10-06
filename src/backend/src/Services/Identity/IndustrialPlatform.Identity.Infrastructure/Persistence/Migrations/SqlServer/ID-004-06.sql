-- SQL Server 2019/2022；新增 Provider 的独立脚本，应用后只能追加迁移。
-- create identity_refresh_session
IF OBJECT_ID(N'dbo.identity_refresh_session',N'U') IS NULL
BEGIN
CREATE TABLE dbo.identity_refresh_session (
    id uniqueidentifier PRIMARY KEY NOT NULL,
    is_frozen bit NOT NULL,
    is_locked bit NOT NULL,
    is_deleted bit NOT NULL,
    entity_type nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    created_on datetimeoffset(7) NOT NULL,
    last_updated_on datetimeoffset(7) NOT NULL,
    optimistic_version bigint NOT NULL,
    concurrency_version uniqueidentifier NOT NULL,
    tenant_n_id nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    n_id nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    family_n_id nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    user_id uniqueidentifier NOT NULL,
    user_is_deleted bit NOT NULL,
    token_hash nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    expires_on datetimeoffset(7) NOT NULL,
    used_on datetimeoffset(7) NULL,
    revoked_on datetimeoffset(7) NULL,
    revoke_reason nvarchar(max) COLLATE Latin1_General_100_BIN2 NULL,
    replaced_by_session_n_id nvarchar(max) COLLATE Latin1_General_100_BIN2 NULL,
    replaced_by_session_id uniqueidentifier NULL,
    replaced_by_session_is_deleted bit NULL,
    user_agent_hash nvarchar(max) COLLATE Latin1_General_100_BIN2 NULL,
    ip_address_hash nvarchar(max) COLLATE Latin1_General_100_BIN2 NULL,
    CONSTRAINT uq_refresh_nid UNIQUE (n_id),
    CONSTRAINT uq_refresh_token UNIQUE (token_hash),
    CONSTRAINT uq_refresh_id_isdel UNIQUE (id, is_deleted),
    FOREIGN KEY (user_id, user_is_deleted) REFERENCES identity_user (id, is_deleted) ON UPDATE CASCADE,
    FOREIGN KEY (replaced_by_session_id) REFERENCES dbo.identity_refresh_session(id)
);
END;
EXEC(N'CREATE OR ALTER TRIGGER dbo.tr_identity_refresh_session_parent_deleted ON dbo.identity_refresh_session
AFTER INSERT, UPDATE AS
BEGIN
    SET NOCOUNT ON;
    UPDATE child SET replaced_by_session_is_deleted=parent.is_deleted
      FROM dbo.identity_refresh_session child
      JOIN inserted parent ON child.replaced_by_session_id=parent.id
      JOIN deleted old ON old.id=parent.id
     WHERE parent.is_deleted<>old.is_deleted;
    IF EXISTS (SELECT 1 FROM dbo.identity_refresh_session child
               JOIN inserted changed ON changed.id=child.id
               JOIN dbo.identity_refresh_session parent ON child.replaced_by_session_id=parent.id
               WHERE child.replaced_by_session_is_deleted<>parent.is_deleted OR child.replaced_by_session_is_deleted IS NULL)
        THROW 51001, N''Parent deletion snapshot mismatch.'', 1;
END');

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.identity_refresh_session') AND name=N'ix_refresh_family')
CREATE INDEX ix_refresh_family ON dbo.identity_refresh_session (family_n_id);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.identity_refresh_session') AND name=N'ix_refresh_user')
CREATE INDEX ix_refresh_user ON dbo.identity_refresh_session (user_id);
