-- SQL Server 2019/2022；新增 Provider 的独立脚本，应用后只能追加迁移。
-- create identity_role_permission
IF OBJECT_ID(N'dbo.identity_role_permission',N'U') IS NULL
BEGIN
CREATE TABLE dbo.identity_role_permission (
    id uniqueidentifier PRIMARY KEY NOT NULL,
    is_frozen bit NOT NULL,
    is_locked bit NOT NULL,
    is_deleted bit NOT NULL,
    entity_type nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    created_on datetimeoffset(7) NOT NULL,
    last_updated_on datetimeoffset(7) NOT NULL,
    optimistic_version bigint NOT NULL,
    concurrency_version uniqueidentifier NOT NULL,
    role_id uniqueidentifier NOT NULL,
    role_is_deleted bit NOT NULL,
    permission_id uniqueidentifier NOT NULL,
    permission_is_deleted bit NOT NULL,
    FOREIGN KEY (role_id, role_is_deleted) REFERENCES identity_role (id, is_deleted) ON UPDATE CASCADE,
    FOREIGN KEY (permission_id, permission_is_deleted) REFERENCES identity_permission (id, is_deleted) ON UPDATE CASCADE
);
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.identity_role_permission') AND name=N'ux_role_permission_active')
CREATE UNIQUE INDEX ux_role_permission_active ON dbo.identity_role_permission (role_id, permission_id) WHERE is_deleted = 0;
