-- SQL Server 2019/2022；新增 Provider 的独立脚本，应用后只能追加迁移。
-- create identity_user_group
IF OBJECT_ID(N'dbo.identity_user_group',N'U') IS NULL
BEGIN
CREATE TABLE dbo.identity_user_group (
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
    description nvarchar(max) COLLATE Latin1_General_100_BIN2 NULL,
    status int NOT NULL,
    CONSTRAINT uq_user_group_nid UNIQUE (tenant_n_id, normalized_n_id),
    CONSTRAINT uq_user_group_id_isdel UNIQUE (id, is_deleted)
);
END;
