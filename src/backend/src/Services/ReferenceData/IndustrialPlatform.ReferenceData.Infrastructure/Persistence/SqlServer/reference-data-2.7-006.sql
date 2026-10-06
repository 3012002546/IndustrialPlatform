-- SQL Server 2019/2022；新增 Provider 的独立脚本，应用后只能追加迁移。
-- reference-data-2.7-006
IF OBJECT_ID(N'dbo.reference_data_metadata_entity_schema',N'U') IS NULL
BEGIN
CREATE TABLE dbo.reference_data_metadata_entity_schema (
    id uniqueidentifier PRIMARY KEY,
    tenant_nid nvarchar(128) COLLATE Latin1_General_100_BIN2 NULL,
    scope_type nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    n_id nvarchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL CHECK(n_id=upper(n_id)),
    name nvarchar(200) COLLATE Latin1_General_100_BIN2 NOT NULL,
    description nvarchar(max) COLLATE Latin1_General_100_BIN2 NULL,
    revision int NOT NULL CHECK(revision>0),
    status nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL CHECK(status IN (N'Draft',N'Published',N'Superseded',N'Disabled')),
    source_revision int NULL CHECK(source_revision IS NULL OR source_revision>0),
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
    CHECK(source_revision IS NULL OR source_revision<revision),
    CHECK((status=N'Draft' AND published_on IS NULL)
        OR (status IN (N'Published',N'Superseded') AND published_on IS NOT NULL)
        OR status=N'Disabled'),
    tenant_scope AS ISNULL(tenant_nid,N'') PERSISTED
);
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.reference_data_metadata_entity_schema') AND name=N'metadata_scope_revision_uq')
CREATE UNIQUE INDEX metadata_scope_revision_uq ON dbo.reference_data_metadata_entity_schema (tenant_scope,n_id,revision);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.reference_data_metadata_entity_schema') AND name=N'metadata_draft_uq')
CREATE UNIQUE INDEX metadata_draft_uq ON dbo.reference_data_metadata_entity_schema (tenant_scope,n_id)
    WHERE status=N'Draft' AND is_deleted=0;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.reference_data_metadata_entity_schema') AND name=N'metadata_current_uq')
CREATE UNIQUE INDEX metadata_current_uq ON dbo.reference_data_metadata_entity_schema (tenant_scope,n_id)
    WHERE status=N'Published' AND is_deleted=0;

IF OBJECT_ID(N'dbo.reference_data_metadata_attribute_definition',N'U') IS NULL
BEGIN
CREATE TABLE dbo.reference_data_metadata_attribute_definition (
    id uniqueidentifier PRIMARY KEY,
    entity_schema_id uniqueidentifier NOT NULL REFERENCES dbo.reference_data_metadata_entity_schema(id),
    n_id nvarchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL CHECK(n_id=upper(n_id)),
    name nvarchar(200) COLLATE Latin1_General_100_BIN2 NOT NULL,
    data_type nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL CHECK(data_type IN (N'String',N'Integer',N'Decimal',N'Boolean',N'Date',N'DateTime',N'Enum',N'Reference')),
    required bit NOT NULL,
    is_array bit NOT NULL,
    enabled bit NOT NULL,
    sort int NOT NULL CHECK(sort>=0),
    default_value nvarchar(max) COLLATE Latin1_General_100_BIN2 NULL,
    min_length int NULL CHECK(min_length IS NULL OR min_length>=0),
    max_length int NULL CHECK(max_length IS NULL OR max_length>=0),
    min_value decimal(28,12) NULL,
    max_value decimal(28,12) NULL,
    pattern nvarchar(max) COLLATE Latin1_General_100_BIN2 NULL CHECK(pattern IS NULL OR (LEN(pattern+N'#N')-1)<=256),
    dictionary_nid nvarchar(64) COLLATE Latin1_General_100_BIN2 NULL,
    reference_target nvarchar(64) COLLATE Latin1_General_100_BIN2 NULL,
    precision_value int NULL CHECK(precision_value IS NULL OR precision_value BETWEEN 1 AND 28),
    scale_value int NULL CHECK(scale_value IS NULL OR scale_value BETWEEN 0 AND 12),
    unit_dimension_nid nvarchar(64) COLLATE Latin1_General_100_BIN2 NULL,
    default_unit_nid nvarchar(64) COLLATE Latin1_General_100_BIN2 NULL,
    unit_revision int NULL CHECK(unit_revision IS NULL OR unit_revision>0),
    unit_source_scope nvarchar(max) COLLATE Latin1_General_100_BIN2 NULL CHECK(unit_source_scope IS NULL OR unit_source_scope IN ('Platform',N'Tenant')),
    unit_source_tenant_nid nvarchar(max) COLLATE Latin1_General_100_BIN2 NULL,
    description nvarchar(max) COLLATE Latin1_General_100_BIN2 NULL,
    was_published bit NOT NULL,
    is_frozen bit NOT NULL,
    is_locked bit NOT NULL,
    is_deleted bit NOT NULL,
    entity_type nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    created_on datetimeoffset(7) NOT NULL,
    last_updated_on datetimeoffset(7) NOT NULL,
    optimistic_version bigint NOT NULL,
    concurrency_version uniqueidentifier NOT NULL,
    UNIQUE(entity_schema_id,n_id),
    CHECK(min_length IS NULL OR max_length IS NULL OR min_length<=max_length),
    CHECK(min_value IS NULL OR max_value IS NULL OR min_value<=max_value),
    CHECK(precision_value IS NULL OR scale_value IS NULL OR scale_value<=precision_value),
    CHECK(dictionary_nid IS NULL OR dictionary_nid=upper(dictionary_nid)),
    CHECK(reference_target IS NULL OR reference_target=upper(reference_target)),
    CHECK(unit_dimension_nid IS NULL OR unit_dimension_nid=upper(unit_dimension_nid)),
    CHECK(default_unit_nid IS NULL OR default_unit_nid=upper(default_unit_nid)),
    CHECK(data_type=N'String' OR (min_length IS NULL AND max_length IS NULL AND pattern IS NULL)),
    CHECK(data_type IN (N'Integer',N'Decimal') OR (min_value IS NULL AND max_value IS NULL)),
    CHECK(data_type=N'Decimal' OR (precision_value IS NULL AND scale_value IS NULL
        AND unit_dimension_nid IS NULL AND default_unit_nid IS NULL AND unit_revision IS NULL
        AND unit_source_scope IS NULL AND unit_source_tenant_nid IS NULL)),
    CHECK((data_type=N'Enum' AND dictionary_nid IS NOT NULL)
        OR (data_type<>N'Enum' AND dictionary_nid IS NULL)),
    CHECK((data_type=N'Reference' AND reference_target IS NOT NULL)
        OR (data_type<>N'Reference' AND reference_target IS NULL)),
    CHECK((unit_dimension_nid IS NULL AND default_unit_nid IS NULL AND unit_revision IS NULL
            AND unit_source_scope IS NULL AND unit_source_tenant_nid IS NULL)
        OR (data_type=N'Decimal' AND unit_dimension_nid IS NOT NULL AND unit_revision IS NOT NULL
            AND ((unit_source_scope=N'Platform' AND unit_source_tenant_nid IS NULL)
                OR (unit_source_scope=N'Tenant' AND unit_source_tenant_nid IS NOT NULL))))
);
END;
