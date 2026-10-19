-- SQL Server 2019/2022；新增 Provider 的独立脚本，应用后只能追加迁移。
-- reference-data-2.7-004
IF OBJECT_ID(N'dbo.reference_data_dynamic_property_definition',N'U') IS NULL
BEGIN
CREATE TABLE dbo.reference_data_dynamic_property_definition (
    id uniqueidentifier PRIMARY KEY,
    n_id nvarchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL CHECK(n_id=upper(n_id)),
    name nvarchar(200) COLLATE Latin1_General_100_BIN2 NOT NULL,
    description nvarchar(max) COLLATE Latin1_General_100_BIN2 NULL,
    scope_type nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    tenant_nid nvarchar(128) COLLATE Latin1_General_100_BIN2 NULL,
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
    optimistic_version bigint NOT NULL CHECK(optimistic_version>=0),
    concurrency_version uniqueidentifier NOT NULL,
    CHECK((scope_type=N'Platform' AND tenant_nid IS NULL) OR (scope_type=N'Tenant' AND tenant_nid IS NOT NULL))
);
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.reference_data_dynamic_property_definition') AND name=N'dynamic_platform_revision_uq')
CREATE UNIQUE INDEX dynamic_platform_revision_uq ON dbo.reference_data_dynamic_property_definition (n_id,revision) WHERE tenant_nid IS NULL;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.reference_data_dynamic_property_definition') AND name=N'dynamic_tenant_revision_uq')
CREATE UNIQUE INDEX dynamic_tenant_revision_uq ON dbo.reference_data_dynamic_property_definition (tenant_nid,n_id,revision) WHERE tenant_nid IS NOT NULL;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.reference_data_dynamic_property_definition') AND name=N'dynamic_platform_draft_uq')
CREATE UNIQUE INDEX dynamic_platform_draft_uq ON dbo.reference_data_dynamic_property_definition (n_id) WHERE tenant_nid IS NULL AND status=N'Draft';

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.reference_data_dynamic_property_definition') AND name=N'dynamic_tenant_draft_uq')
CREATE UNIQUE INDEX dynamic_tenant_draft_uq ON dbo.reference_data_dynamic_property_definition (tenant_nid,n_id) WHERE tenant_nid IS NOT NULL AND status=N'Draft';

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.reference_data_dynamic_property_definition') AND name=N'dynamic_platform_published_uq')
CREATE UNIQUE INDEX dynamic_platform_published_uq ON dbo.reference_data_dynamic_property_definition (n_id) WHERE tenant_nid IS NULL AND status=N'Published';

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.reference_data_dynamic_property_definition') AND name=N'dynamic_tenant_published_uq')
CREATE UNIQUE INDEX dynamic_tenant_published_uq ON dbo.reference_data_dynamic_property_definition (tenant_nid,n_id) WHERE tenant_nid IS NOT NULL AND status=N'Published';

IF OBJECT_ID(N'dbo.reference_data_dynamic_property_field',N'U') IS NULL
BEGIN
CREATE TABLE dbo.reference_data_dynamic_property_field (
    id uniqueidentifier PRIMARY KEY,
    definition_id uniqueidentifier NOT NULL REFERENCES dbo.reference_data_dynamic_property_definition(id),
    n_id nvarchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL CHECK(n_id=upper(n_id)),
    name nvarchar(200) COLLATE Latin1_General_100_BIN2 NOT NULL,
    data_type nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL CHECK(data_type IN (N'String',N'Integer',N'Decimal',N'Boolean',N'Date',N'DateTime',N'Enum',N'Json',N'Reference')),
    required bit NOT NULL,
    enabled bit NOT NULL,
    sort int NOT NULL CHECK(sort>=0),
    default_value_json nvarchar(max) NULL,
    CHECK(default_value_json IS NULL OR ISJSON(default_value_json)=1),
    min_length int NULL,
    max_length int NULL,
    min_value decimal(28,10) NULL,
    max_value decimal(28,10) NULL,
    scale int NULL CHECK(scale BETWEEN 0 AND 10),
    pattern nvarchar(256) COLLATE Latin1_General_100_BIN2 NULL,
    dictionary_nid nvarchar(64) COLLATE Latin1_General_100_BIN2 NULL,
    reference_target nvarchar(128) COLLATE Latin1_General_100_BIN2 NULL,
    description nvarchar(max) COLLATE Latin1_General_100_BIN2 NULL,
    has_had_value bit NOT NULL,
    was_published bit NOT NULL,
    is_frozen bit NOT NULL,
    is_locked bit NOT NULL,
    is_deleted bit NOT NULL,
    entity_type nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    created_on datetimeoffset(7) NOT NULL,
    last_updated_on datetimeoffset(7) NOT NULL,
    optimistic_version bigint NOT NULL CHECK(optimistic_version>=0),
    concurrency_version uniqueidentifier NOT NULL,
    UNIQUE(definition_id,n_id),
    UNIQUE(id,definition_id)
);
END;

IF OBJECT_ID(N'dbo.reference_data_dynamic_property_record',N'U') IS NULL
BEGIN
CREATE TABLE dbo.reference_data_dynamic_property_record (
    id uniqueidentifier PRIMARY KEY,
    definition_id uniqueidentifier NOT NULL REFERENCES dbo.reference_data_dynamic_property_definition(id),
    n_id nvarchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL CHECK(n_id=upper(n_id)),
    name nvarchar(200) COLLATE Latin1_General_100_BIN2 NULL,
    category nvarchar(100) COLLATE Latin1_General_100_BIN2 NULL,
    sort int NOT NULL CHECK(sort>=0),
    enabled bit NOT NULL,
    is_frozen bit NOT NULL,
    is_locked bit NOT NULL,
    is_deleted bit NOT NULL,
    entity_type nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    created_on datetimeoffset(7) NOT NULL,
    last_updated_on datetimeoffset(7) NOT NULL,
    optimistic_version bigint NOT NULL CHECK(optimistic_version>=0),
    concurrency_version uniqueidentifier NOT NULL,
    UNIQUE(definition_id,n_id),
    UNIQUE(id,definition_id)
);
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.reference_data_dynamic_property_record') AND name=N'dynamic_record_page_ix')
CREATE INDEX dynamic_record_page_ix ON dbo.reference_data_dynamic_property_record (definition_id,sort,n_id);

IF OBJECT_ID(N'dbo.reference_data_dynamic_property_value',N'U') IS NULL
BEGIN
CREATE TABLE dbo.reference_data_dynamic_property_value (
    id uniqueidentifier PRIMARY KEY,
    definition_id uniqueidentifier NOT NULL REFERENCES dbo.reference_data_dynamic_property_definition(id),
    record_id uniqueidentifier NOT NULL REFERENCES dbo.reference_data_dynamic_property_record(id),
    field_id uniqueidentifier NOT NULL REFERENCES dbo.reference_data_dynamic_property_field(id),
    value_type nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    string_value nvarchar(max) COLLATE Latin1_General_100_BIN2 NULL,
    integer_value bigint NULL,
    decimal_value decimal(28,10) NULL,
    boolean_value bit NULL,
    date_value date NULL,
    date_time_value datetimeoffset(7) NULL,
    json_value nvarchar(max) NULL,
    CHECK(json_value IS NULL OR ISJSON(json_value)=1),
    reference_value nvarchar(128) COLLATE Latin1_General_100_BIN2 NULL,
    is_frozen bit NOT NULL,
    is_locked bit NOT NULL,
    is_deleted bit NOT NULL,
    entity_type nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    created_on datetimeoffset(7) NOT NULL,
    last_updated_on datetimeoffset(7) NOT NULL,
    optimistic_version bigint NOT NULL CHECK(optimistic_version>=0),
    concurrency_version uniqueidentifier NOT NULL,
    UNIQUE(record_id,field_id),
    CHECK((value_type IN (N'String',N'Enum') AND string_value IS NOT NULL AND integer_value IS NULL AND decimal_value IS NULL AND boolean_value IS NULL AND date_value IS NULL AND date_time_value IS NULL AND json_value IS NULL AND reference_value IS NULL) OR (value_type=N'Integer' AND string_value IS NULL AND integer_value IS NOT NULL AND decimal_value IS NULL AND boolean_value IS NULL AND date_value IS NULL AND date_time_value IS NULL AND json_value IS NULL AND reference_value IS NULL) OR (value_type=N'Decimal' AND string_value IS NULL AND integer_value IS NULL AND decimal_value IS NOT NULL AND boolean_value IS NULL AND date_value IS NULL AND date_time_value IS NULL AND json_value IS NULL AND reference_value IS NULL) OR (value_type=N'Boolean' AND string_value IS NULL AND integer_value IS NULL AND decimal_value IS NULL AND boolean_value IS NOT NULL AND date_value IS NULL AND date_time_value IS NULL AND json_value IS NULL AND reference_value IS NULL) OR (value_type=N'Date' AND string_value IS NULL AND integer_value IS NULL AND decimal_value IS NULL AND boolean_value IS NULL AND date_value IS NOT NULL AND date_time_value IS NULL AND json_value IS NULL AND reference_value IS NULL) OR (value_type=N'DateTime' AND string_value IS NULL AND integer_value IS NULL AND decimal_value IS NULL AND boolean_value IS NULL AND date_value IS NULL AND date_time_value IS NOT NULL AND json_value IS NULL AND reference_value IS NULL) OR (value_type=N'Json' AND string_value IS NULL AND integer_value IS NULL AND decimal_value IS NULL AND boolean_value IS NULL AND date_value IS NULL AND date_time_value IS NULL AND json_value IS NOT NULL AND reference_value IS NULL) OR (value_type=N'Reference' AND string_value IS NULL AND integer_value IS NULL AND decimal_value IS NULL AND boolean_value IS NULL AND date_value IS NULL AND date_time_value IS NULL AND json_value IS NULL AND reference_value IS NOT NULL)),
    FOREIGN KEY(record_id,definition_id) REFERENCES dbo.reference_data_dynamic_property_record(id,definition_id),
    FOREIGN KEY(field_id,definition_id) REFERENCES dbo.reference_data_dynamic_property_field(id,definition_id)
);
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.reference_data_dynamic_property_value') AND name=N'dynamic_value_definition_ix')
CREATE INDEX dynamic_value_definition_ix ON dbo.reference_data_dynamic_property_value (definition_id);
