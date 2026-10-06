-- SQL Server 2019/2022；新增 Provider 的独立脚本，应用后只能追加迁移。
-- create identity_permission
IF OBJECT_ID(N'dbo.identity_permission',N'U') IS NULL
BEGIN
CREATE TABLE dbo.identity_permission (
    id uniqueidentifier PRIMARY KEY NOT NULL,
    is_frozen bit NOT NULL,
    is_locked bit NOT NULL,
    is_deleted bit NOT NULL,
    entity_type nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    created_on datetimeoffset(7) NOT NULL,
    last_updated_on datetimeoffset(7) NOT NULL,
    optimistic_version bigint NOT NULL,
    concurrency_version uniqueidentifier NOT NULL,
    n_id nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    normalized_n_id nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    name nvarchar(256) COLLATE Latin1_General_100_BIN2 NOT NULL,
    type int NOT NULL,
    parent_permission_n_id nvarchar(max) COLLATE Latin1_General_100_BIN2 NULL,
    parent_permission_id uniqueidentifier NULL,
    parent_permission_is_deleted bit NULL,
    description nvarchar(max) COLLATE Latin1_General_100_BIN2 NULL,
    status int NOT NULL,
    CONSTRAINT uq_permission_nid UNIQUE (normalized_n_id),
    CONSTRAINT uq_permission_id_isdel UNIQUE (id, is_deleted),
    CONSTRAINT ck_permission_parent CHECK ((parent_permission_n_id IS NULL AND parent_permission_id IS NULL AND parent_permission_is_deleted IS NULL) OR (parent_permission_n_id IS NOT NULL AND parent_permission_id IS NOT NULL AND parent_permission_is_deleted IS NOT NULL)),
    FOREIGN KEY (parent_permission_id) REFERENCES dbo.identity_permission(id)
);
END;
EXEC(N'CREATE OR ALTER TRIGGER dbo.tr_identity_permission_parent_deleted ON dbo.identity_permission
AFTER INSERT, UPDATE AS
BEGIN
    SET NOCOUNT ON;
    UPDATE child SET parent_permission_is_deleted=parent.is_deleted
      FROM dbo.identity_permission child
      JOIN inserted parent ON child.parent_permission_id=parent.id
      JOIN deleted old ON old.id=parent.id
     WHERE parent.is_deleted<>old.is_deleted;
    IF EXISTS (SELECT 1 FROM dbo.identity_permission child
               JOIN inserted changed ON changed.id=child.id
               JOIN dbo.identity_permission parent ON child.parent_permission_id=parent.id
               WHERE child.parent_permission_is_deleted<>parent.is_deleted OR child.parent_permission_is_deleted IS NULL)
        THROW 51001, N''Parent deletion snapshot mismatch.'', 1;
END');
