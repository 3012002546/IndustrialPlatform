-- SQL Server 2019/2022；新增 Provider 的独立脚本，应用后只能追加迁移。
-- reference-data-2.7-003
IF OBJECT_ID(N'dbo.reference_data_parameter_app_domain',N'U') IS NULL
BEGIN
CREATE TABLE dbo.reference_data_parameter_app_domain (
    id uniqueidentifier PRIMARY KEY,
    tenant_nid nvarchar(128) COLLATE Latin1_General_100_BIN2 NULL,
    scope_type nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    n_id nvarchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL CHECK(n_id=upper(n_id)),
    name nvarchar(200) COLLATE Latin1_General_100_BIN2 NOT NULL,
    description nvarchar(max) COLLATE Latin1_General_100_BIN2 NULL,
    revision bigint NOT NULL CHECK(revision>0),
    status nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL CHECK(status IN (N'Active',N'Disabled')),
    is_frozen bit NOT NULL,
    is_locked bit NOT NULL,
    is_deleted bit NOT NULL,
    entity_type nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    created_on datetimeoffset(7) NOT NULL,
    last_updated_on datetimeoffset(7) NOT NULL,
    optimistic_version bigint NOT NULL,
    concurrency_version uniqueidentifier NOT NULL,
    CHECK((scope_type=N'Platform' AND tenant_nid IS NULL) OR (scope_type=N'Tenant' AND tenant_nid IS NOT NULL))
);
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.reference_data_parameter_app_domain') AND name=N'parameter_platform_nid_uq')
CREATE UNIQUE INDEX parameter_platform_nid_uq ON dbo.reference_data_parameter_app_domain (n_id) WHERE tenant_nid IS NULL;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.reference_data_parameter_app_domain') AND name=N'parameter_tenant_nid_uq')
CREATE UNIQUE INDEX parameter_tenant_nid_uq ON dbo.reference_data_parameter_app_domain (tenant_nid,n_id) WHERE tenant_nid IS NOT NULL;

IF OBJECT_ID(N'dbo.reference_data_parameter_key',N'U') IS NULL
BEGIN
CREATE TABLE dbo.reference_data_parameter_key (
    id uniqueidentifier PRIMARY KEY,
    configuration_app_domain_id uniqueidentifier NOT NULL REFERENCES dbo.reference_data_parameter_app_domain(id),
    n_id nvarchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL CHECK(n_id=upper(n_id)),
    name nvarchar(200) COLLATE Latin1_General_100_BIN2 NOT NULL,
    description nvarchar(max) COLLATE Latin1_General_100_BIN2 NULL,
    data_type nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL CHECK(data_type IN (N'String',N'Integer',N'Decimal',N'Boolean',N'Date',N'DateTime',N'Enum',N'Json',N'Reference')),
    value_mode nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL CHECK(value_mode IN (N'Single',N'Multi')),
    value_json nvarchar(max) NULL,
    CHECK(value_json IS NULL OR ISJSON(value_json)=1),
    default_value_json nvarchar(max) NULL,
    CHECK(default_value_json IS NULL OR ISJSON(default_value_json)=1),
    is_mandatory bit NOT NULL,
    is_read_only bit NOT NULL,
    has_had_value bit NOT NULL,
    dictionary_nid nvarchar(64) COLLATE Latin1_General_100_BIN2 NULL,
    reference_target nvarchar(128) COLLATE Latin1_General_100_BIN2 NULL,
    status nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL CHECK(status IN (N'Active',N'Disabled')),
    sort int NOT NULL CHECK(sort>=0),
    is_frozen bit NOT NULL,
    is_locked bit NOT NULL,
    is_deleted bit NOT NULL,
    entity_type nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    created_on datetimeoffset(7) NOT NULL,
    last_updated_on datetimeoffset(7) NOT NULL,
    optimistic_version bigint NOT NULL,
    concurrency_version uniqueidentifier NOT NULL,
    CHECK(value_mode=N'Single' OR (value_json IS NULL AND default_value_json IS NULL)),
    UNIQUE(configuration_app_domain_id,n_id)
);
END;

IF OBJECT_ID(N'dbo.reference_data_parameter_multi_value',N'U') IS NULL
BEGIN
CREATE TABLE dbo.reference_data_parameter_multi_value (
    id uniqueidentifier PRIMARY KEY,
    configuration_key_id uniqueidentifier NOT NULL REFERENCES dbo.reference_data_parameter_key(id),
    n_id nvarchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL CHECK(n_id=upper(n_id)),
    name nvarchar(200) COLLATE Latin1_General_100_BIN2 NULL,
    value_json nvarchar(max) NOT NULL,
    CHECK(value_json IS NULL OR ISJSON(value_json)=1),
    canonical_value_hash nvarchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
    sort int NOT NULL CHECK(sort>=0),
    is_default bit NOT NULL,
    enabled bit NOT NULL,
    is_frozen bit NOT NULL,
    is_locked bit NOT NULL,
    is_deleted bit NOT NULL,
    entity_type nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    created_on datetimeoffset(7) NOT NULL,
    last_updated_on datetimeoffset(7) NOT NULL,
    optimistic_version bigint NOT NULL,
    concurrency_version uniqueidentifier NOT NULL,
    CHECK(is_default=0 OR enabled=1),
    UNIQUE(configuration_key_id,n_id)
);
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.reference_data_parameter_multi_value') AND name=N'parameter_enabled_value_uq')
CREATE UNIQUE INDEX parameter_enabled_value_uq ON dbo.reference_data_parameter_multi_value (configuration_key_id,canonical_value_hash) WHERE enabled=1;

IF OBJECT_ID(N'dbo.reference_data_parameter_history',N'U') IS NULL
BEGIN
CREATE TABLE dbo.reference_data_parameter_history (
    id uniqueidentifier PRIMARY KEY,
    app_domain_id uniqueidentifier NOT NULL REFERENCES dbo.reference_data_parameter_app_domain(id),
    key_id uniqueidentifier NULL,
    object_type nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    object_id uniqueidentifier NOT NULL,
    full_nid nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    change_type nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    change_reason nvarchar(500) COLLATE Latin1_General_100_BIN2 NOT NULL,
    before_summary nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    after_summary nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    revision bigint NOT NULL,
    user_nid nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    trace_id nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    created_on datetimeoffset(7) NOT NULL
);
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.reference_data_parameter_history') AND name=N'parameter_history_path_ix')
CREATE INDEX parameter_history_path_ix ON dbo.reference_data_parameter_history (app_domain_id,key_id,revision);
