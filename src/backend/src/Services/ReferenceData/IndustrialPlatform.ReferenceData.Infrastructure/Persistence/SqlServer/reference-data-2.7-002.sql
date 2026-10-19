-- SQL Server 2019/2022；新增 Provider 的独立脚本，应用后只能追加迁移。
-- reference-data-2.7-002
IF OBJECT_ID(N'dbo.reference_data_dictionary_definition',N'U') IS NULL
BEGIN
CREATE TABLE dbo.reference_data_dictionary_definition (
    id uniqueidentifier PRIMARY KEY,
    tenant_nid nvarchar(128) COLLATE Latin1_General_100_BIN2 NULL,
    scope_type nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    n_id nvarchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL CHECK(n_id=upper(n_id)),
    name nvarchar(200) COLLATE Latin1_General_100_BIN2 NOT NULL,
    description nvarchar(max) COLLATE Latin1_General_100_BIN2 NULL,
    revision int NOT NULL CHECK(revision>0),
    status nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL CHECK(status IN (N'Draft',N'Published',N'Superseded',N'Disabled')),
    published_on datetimeoffset(7) NULL,
    published_by nvarchar(max) COLLATE Latin1_General_100_BIN2 NULL,
    is_frozen bit NOT NULL,
    is_locked bit NOT NULL,
    is_deleted bit NOT NULL,
    entity_type nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    created_on datetimeoffset(7) NOT NULL,
    last_updated_on datetimeoffset(7) NOT NULL,
    optimistic_version bigint NOT NULL,
    concurrency_version uniqueidentifier NOT NULL,
    CHECK((scope_type=N'Platform' AND tenant_nid IS NULL) OR (scope_type=N'Tenant' AND tenant_nid IS NOT NULL)),
    tenant_scope AS ISNULL(tenant_nid,N'') PERSISTED
);
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.reference_data_dictionary_definition') AND name=N'dictionary_scope_revision_uq')
CREATE UNIQUE INDEX dictionary_scope_revision_uq ON dbo.reference_data_dictionary_definition (tenant_scope,n_id,revision);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.reference_data_dictionary_definition') AND name=N'dictionary_current_uq')
CREATE UNIQUE INDEX dictionary_current_uq ON dbo.reference_data_dictionary_definition (tenant_scope,n_id) WHERE status=N'Published' AND is_deleted=0;

IF OBJECT_ID(N'dbo.reference_data_dictionary_item',N'U') IS NULL
BEGIN
CREATE TABLE dbo.reference_data_dictionary_item (
    id uniqueidentifier PRIMARY KEY,
    dictionary_definition_id uniqueidentifier NOT NULL REFERENCES dbo.reference_data_dictionary_definition(id),
    n_id nvarchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL CHECK(n_id=upper(n_id)),
    name nvarchar(200) COLLATE Latin1_General_100_BIN2 NOT NULL,
    description nvarchar(max) COLLATE Latin1_General_100_BIN2 NULL,
    sort int NOT NULL CHECK(sort>=0),
    enabled bit NOT NULL,
    is_frozen bit NOT NULL,
    is_locked bit NOT NULL,
    is_deleted bit NOT NULL,
    entity_type nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    created_on datetimeoffset(7) NOT NULL,
    last_updated_on datetimeoffset(7) NOT NULL,
    optimistic_version bigint NOT NULL,
    concurrency_version uniqueidentifier NOT NULL,
    UNIQUE(dictionary_definition_id,n_id)
);
END;
