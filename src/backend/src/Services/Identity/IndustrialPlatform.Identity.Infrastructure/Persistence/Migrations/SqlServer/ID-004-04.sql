-- SQL Server 2019/2022；新增 Provider 的独立脚本，应用后只能追加迁移。
-- create identity_user_role
IF OBJECT_ID(N'dbo.identity_user_role',N'U') IS NULL
BEGIN
CREATE TABLE dbo.identity_user_role (
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
    user_id uniqueidentifier NOT NULL,
    user_is_deleted bit NOT NULL,
    role_id uniqueidentifier NOT NULL,
    role_is_deleted bit NOT NULL,
    FOREIGN KEY (user_id, user_is_deleted) REFERENCES identity_user (id, is_deleted) ON UPDATE CASCADE,
    FOREIGN KEY (role_id, role_is_deleted) REFERENCES identity_role (id, is_deleted) ON UPDATE CASCADE
);
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.identity_user_role') AND name=N'ux_user_role_active')
CREATE UNIQUE INDEX ux_user_role_active ON dbo.identity_user_role (tenant_n_id, user_id, role_id) WHERE is_deleted = 0;
