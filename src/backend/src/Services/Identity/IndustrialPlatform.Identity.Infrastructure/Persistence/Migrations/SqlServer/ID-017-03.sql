-- SQL Server 2019/2022；新增 Provider 的独立脚本，应用后只能追加迁移。
-- create identity_user_group_role
IF OBJECT_ID(N'dbo.identity_user_group_role',N'U') IS NULL
BEGIN
CREATE TABLE dbo.identity_user_group_role (
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
    user_group_id uniqueidentifier NOT NULL,
    user_group_is_deleted bit NOT NULL,
    role_id uniqueidentifier NOT NULL,
    role_is_deleted bit NOT NULL,
    FOREIGN KEY (user_group_id, user_group_is_deleted) REFERENCES identity_user_group (id, is_deleted) ON UPDATE CASCADE,
    FOREIGN KEY (role_id, role_is_deleted) REFERENCES identity_role (id, is_deleted) ON UPDATE CASCADE
);
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.identity_user_group_role') AND name=N'ux_user_group_role_active')
CREATE UNIQUE INDEX ux_user_group_role_active ON dbo.identity_user_group_role (tenant_n_id, user_group_id, role_id) WHERE is_deleted = 0;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.identity_user_group_role') AND name=N'ix_user_group_role_group')
CREATE INDEX ix_user_group_role_group ON dbo.identity_user_group_role (user_group_id);
