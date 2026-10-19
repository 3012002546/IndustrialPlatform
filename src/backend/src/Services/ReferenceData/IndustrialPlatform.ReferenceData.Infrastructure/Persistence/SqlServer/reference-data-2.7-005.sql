-- SQL Server 2019/2022；新增 Provider 的独立脚本，应用后只能追加迁移。
-- reference-data-2.7-005
IF OBJECT_ID(N'dbo.reference_data_unit_of_measure_dimension',N'U') IS NULL
BEGIN
CREATE TABLE dbo.reference_data_unit_of_measure_dimension (
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
    is_system_defined bit NOT NULL,
    conversion_kind nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL CHECK(conversion_kind IN (N'Ratio',N'AbsoluteTemperature')),
    base_unit_nid nvarchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL CHECK(base_unit_nid=upper(base_unit_nid)),
    is_frozen bit NOT NULL,
    is_locked bit NOT NULL,
    is_deleted bit NOT NULL,
    entity_type nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    created_on datetimeoffset(7) NOT NULL,
    last_updated_on datetimeoffset(7) NOT NULL,
    optimistic_version bigint NOT NULL,
    concurrency_version uniqueidentifier NOT NULL,
    CHECK((scope_type=N'Platform' AND tenant_nid IS NULL) OR (scope_type=N'Tenant' AND tenant_nid IS NOT NULL)),
    CHECK((status=N'Draft' AND published_on IS NULL) OR status=N'Disabled' OR
          (status IN (N'Published',N'Superseded') AND published_on IS NOT NULL)),
    CHECK(is_system_defined=0 OR (scope_type=N'Platform' AND status IN (N'Published',N'Superseded',N'Disabled')))
);
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.reference_data_unit_of_measure_dimension') AND name=N'uom_platform_revision_uq')
CREATE UNIQUE INDEX uom_platform_revision_uq ON dbo.reference_data_unit_of_measure_dimension (n_id,revision)
    WHERE tenant_nid IS NULL AND is_deleted=0;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.reference_data_unit_of_measure_dimension') AND name=N'uom_tenant_revision_uq')
CREATE UNIQUE INDEX uom_tenant_revision_uq ON dbo.reference_data_unit_of_measure_dimension (tenant_nid,n_id,revision)
    WHERE tenant_nid IS NOT NULL AND is_deleted=0;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.reference_data_unit_of_measure_dimension') AND name=N'uom_platform_draft_uq')
CREATE UNIQUE INDEX uom_platform_draft_uq ON dbo.reference_data_unit_of_measure_dimension (n_id)
    WHERE tenant_nid IS NULL AND status=N'Draft' AND is_deleted=0;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.reference_data_unit_of_measure_dimension') AND name=N'uom_tenant_draft_uq')
CREATE UNIQUE INDEX uom_tenant_draft_uq ON dbo.reference_data_unit_of_measure_dimension (tenant_nid,n_id)
    WHERE tenant_nid IS NOT NULL AND status=N'Draft' AND is_deleted=0;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.reference_data_unit_of_measure_dimension') AND name=N'uom_platform_current_uq')
CREATE UNIQUE INDEX uom_platform_current_uq ON dbo.reference_data_unit_of_measure_dimension (n_id)
    WHERE tenant_nid IS NULL AND status=N'Published' AND is_deleted=0;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.reference_data_unit_of_measure_dimension') AND name=N'uom_tenant_current_uq')
CREATE UNIQUE INDEX uom_tenant_current_uq ON dbo.reference_data_unit_of_measure_dimension (tenant_nid,n_id)
    WHERE tenant_nid IS NOT NULL AND status=N'Published' AND is_deleted=0;

IF OBJECT_ID(N'dbo.reference_data_unit_of_measure_unit',N'U') IS NULL
BEGIN
CREATE TABLE dbo.reference_data_unit_of_measure_unit (
    id uniqueidentifier PRIMARY KEY,
    unit_dimension_id uniqueidentifier NOT NULL REFERENCES dbo.reference_data_unit_of_measure_dimension(id),
    n_id nvarchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL CHECK(n_id=upper(n_id)),
    name nvarchar(200) COLLATE Latin1_General_100_BIN2 NOT NULL,
    symbol nvarchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
    factor_to_base decimal(28,12) NOT NULL CHECK(factor_to_base>0),
    offset_to_base decimal(28,12) NOT NULL,
    decimal_places int NOT NULL CHECK(decimal_places BETWEEN 0 AND 12),
    rounding_mode nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL CHECK(rounding_mode IN (N'ToEven',N'AwayFromZero')),
    enabled bit NOT NULL,
    sort int NOT NULL CHECK(sort>=0),
    is_frozen bit NOT NULL,
    is_locked bit NOT NULL,
    is_deleted bit NOT NULL,
    entity_type nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    created_on datetimeoffset(7) NOT NULL,
    last_updated_on datetimeoffset(7) NOT NULL,
    optimistic_version bigint NOT NULL,
    concurrency_version uniqueidentifier NOT NULL,
    UNIQUE(unit_dimension_id,n_id)
);
END;

IF NOT EXISTS (SELECT 1 FROM dbo.reference_data_unit_of_measure_dimension WITH (UPDLOCK,HOLDLOCK) WHERE id=N'10000000-0000-0000-0000-000000000001')
INSERT INTO dbo.reference_data_unit_of_measure_dimension (id,tenant_nid,scope_type,n_id,name,description,revision,status,source_revision,published_on,published_by,
     is_system_defined,conversion_kind,base_unit_nid,is_frozen,is_locked,is_deleted,entity_type,created_on,last_updated_on,
     optimistic_version,concurrency_version) VALUES (N'10000000-0000-0000-0000-000000000001',NULL,N'Platform',N'MASS',N'Mass',NULL,1,N'Published',NULL,N'2026-09-05T00:00:00+00:00',N'SYSTEM',
        1,N'Ratio',N'KG',0,0,0,N'IndustrialPlatform.ReferenceData.Domain.UnitOfMeasure.UnitDimension',N'2026-09-05T00:00:00+00:00',N'2026-09-05T00:00:00+00:00',1,
        N'10000000-0000-0000-0000-00000000000f');

IF NOT EXISTS (SELECT 1 FROM dbo.reference_data_unit_of_measure_unit WITH (UPDLOCK,HOLDLOCK) WHERE id=N'11000000-0000-0000-0000-000000000001')
INSERT INTO dbo.reference_data_unit_of_measure_unit (id,unit_dimension_id,n_id,name,symbol,factor_to_base,offset_to_base,decimal_places,rounding_mode,enabled,sort,
     is_frozen,is_locked,is_deleted,entity_type,created_on,last_updated_on,optimistic_version,concurrency_version) VALUES (N'11000000-0000-0000-0000-000000000001',N'10000000-0000-0000-0000-000000000001',N'KG',N'Kilogram',N'kg',N'1',N'0',3,N'ToEven',
        1,0,0,0,0,N'IndustrialPlatform.ReferenceData.Domain.UnitOfMeasure.UnitDefinition',N'2026-09-05T00:00:00+00:00',N'2026-09-05T00:00:00+00:00',0,
        N'11000000-0000-0000-0000-00000000000f');

IF NOT EXISTS (SELECT 1 FROM dbo.reference_data_unit_of_measure_unit WITH (UPDLOCK,HOLDLOCK) WHERE id=N'11000000-0000-0000-0000-000000000002')
INSERT INTO dbo.reference_data_unit_of_measure_unit (id,unit_dimension_id,n_id,name,symbol,factor_to_base,offset_to_base,decimal_places,rounding_mode,enabled,sort,
     is_frozen,is_locked,is_deleted,entity_type,created_on,last_updated_on,optimistic_version,concurrency_version) VALUES (N'11000000-0000-0000-0000-000000000002',N'10000000-0000-0000-0000-000000000001',N'G',N'Gram',N'g',N'0.001',N'0',3,N'ToEven',
        1,1,0,0,0,N'IndustrialPlatform.ReferenceData.Domain.UnitOfMeasure.UnitDefinition',N'2026-09-05T00:00:00+00:00',N'2026-09-05T00:00:00+00:00',0,
        N'11000000-0000-0000-0000-00000000000f');

IF NOT EXISTS (SELECT 1 FROM dbo.reference_data_unit_of_measure_dimension WITH (UPDLOCK,HOLDLOCK) WHERE id=N'10000000-0000-0000-0000-000000000002')
INSERT INTO dbo.reference_data_unit_of_measure_dimension (id,tenant_nid,scope_type,n_id,name,description,revision,status,source_revision,published_on,published_by,
     is_system_defined,conversion_kind,base_unit_nid,is_frozen,is_locked,is_deleted,entity_type,created_on,last_updated_on,
     optimistic_version,concurrency_version) VALUES (N'10000000-0000-0000-0000-000000000002',NULL,N'Platform',N'LENGTH',N'Length',NULL,1,N'Published',NULL,N'2026-09-05T00:00:00+00:00',N'SYSTEM',
        1,N'Ratio',N'M',0,0,0,N'IndustrialPlatform.ReferenceData.Domain.UnitOfMeasure.UnitDimension',N'2026-09-05T00:00:00+00:00',N'2026-09-05T00:00:00+00:00',1,
        N'10000000-0000-0000-0000-00000000000f');

IF NOT EXISTS (SELECT 1 FROM dbo.reference_data_unit_of_measure_unit WITH (UPDLOCK,HOLDLOCK) WHERE id=N'12000000-0000-0000-0000-000000000001')
INSERT INTO dbo.reference_data_unit_of_measure_unit (id,unit_dimension_id,n_id,name,symbol,factor_to_base,offset_to_base,decimal_places,rounding_mode,enabled,sort,
     is_frozen,is_locked,is_deleted,entity_type,created_on,last_updated_on,optimistic_version,concurrency_version) VALUES (N'12000000-0000-0000-0000-000000000001',N'10000000-0000-0000-0000-000000000002',N'M',N'Metre',N'm',N'1',N'0',3,N'ToEven',
        1,0,0,0,0,N'IndustrialPlatform.ReferenceData.Domain.UnitOfMeasure.UnitDefinition',N'2026-09-05T00:00:00+00:00',N'2026-09-05T00:00:00+00:00',0,
        N'12000000-0000-0000-0000-00000000000f');

IF NOT EXISTS (SELECT 1 FROM dbo.reference_data_unit_of_measure_unit WITH (UPDLOCK,HOLDLOCK) WHERE id=N'12000000-0000-0000-0000-000000000002')
INSERT INTO dbo.reference_data_unit_of_measure_unit (id,unit_dimension_id,n_id,name,symbol,factor_to_base,offset_to_base,decimal_places,rounding_mode,enabled,sort,
     is_frozen,is_locked,is_deleted,entity_type,created_on,last_updated_on,optimistic_version,concurrency_version) VALUES (N'12000000-0000-0000-0000-000000000002',N'10000000-0000-0000-0000-000000000002',N'MM',N'Millimetre',N'mm',N'0.001',N'0',3,N'ToEven',
        1,1,0,0,0,N'IndustrialPlatform.ReferenceData.Domain.UnitOfMeasure.UnitDefinition',N'2026-09-05T00:00:00+00:00',N'2026-09-05T00:00:00+00:00',0,
        N'12000000-0000-0000-0000-00000000000f');

IF NOT EXISTS (SELECT 1 FROM dbo.reference_data_unit_of_measure_dimension WITH (UPDLOCK,HOLDLOCK) WHERE id=N'10000000-0000-0000-0000-000000000003')
INSERT INTO dbo.reference_data_unit_of_measure_dimension (id,tenant_nid,scope_type,n_id,name,description,revision,status,source_revision,published_on,published_by,
     is_system_defined,conversion_kind,base_unit_nid,is_frozen,is_locked,is_deleted,entity_type,created_on,last_updated_on,
     optimistic_version,concurrency_version) VALUES (N'10000000-0000-0000-0000-000000000003',NULL,N'Platform',N'TIME',N'Time',NULL,1,N'Published',NULL,N'2026-09-05T00:00:00+00:00',N'SYSTEM',
        1,N'Ratio',N'S',0,0,0,N'IndustrialPlatform.ReferenceData.Domain.UnitOfMeasure.UnitDimension',N'2026-09-05T00:00:00+00:00',N'2026-09-05T00:00:00+00:00',1,
        N'10000000-0000-0000-0000-00000000000f');

IF NOT EXISTS (SELECT 1 FROM dbo.reference_data_unit_of_measure_unit WITH (UPDLOCK,HOLDLOCK) WHERE id=N'13000000-0000-0000-0000-000000000001')
INSERT INTO dbo.reference_data_unit_of_measure_unit (id,unit_dimension_id,n_id,name,symbol,factor_to_base,offset_to_base,decimal_places,rounding_mode,enabled,sort,
     is_frozen,is_locked,is_deleted,entity_type,created_on,last_updated_on,optimistic_version,concurrency_version) VALUES (N'13000000-0000-0000-0000-000000000001',N'10000000-0000-0000-0000-000000000003',N'S',N'Second',N's',N'1',N'0',3,N'ToEven',
        1,0,0,0,0,N'IndustrialPlatform.ReferenceData.Domain.UnitOfMeasure.UnitDefinition',N'2026-09-05T00:00:00+00:00',N'2026-09-05T00:00:00+00:00',0,
        N'13000000-0000-0000-0000-00000000000f');

IF NOT EXISTS (SELECT 1 FROM dbo.reference_data_unit_of_measure_unit WITH (UPDLOCK,HOLDLOCK) WHERE id=N'13000000-0000-0000-0000-000000000002')
INSERT INTO dbo.reference_data_unit_of_measure_unit (id,unit_dimension_id,n_id,name,symbol,factor_to_base,offset_to_base,decimal_places,rounding_mode,enabled,sort,
     is_frozen,is_locked,is_deleted,entity_type,created_on,last_updated_on,optimistic_version,concurrency_version) VALUES (N'13000000-0000-0000-0000-000000000002',N'10000000-0000-0000-0000-000000000003',N'MIN',N'Minute',N'min',N'60',N'0',3,N'ToEven',
        1,1,0,0,0,N'IndustrialPlatform.ReferenceData.Domain.UnitOfMeasure.UnitDefinition',N'2026-09-05T00:00:00+00:00',N'2026-09-05T00:00:00+00:00',0,
        N'13000000-0000-0000-0000-00000000000f');

IF NOT EXISTS (SELECT 1 FROM dbo.reference_data_unit_of_measure_dimension WITH (UPDLOCK,HOLDLOCK) WHERE id=N'10000000-0000-0000-0000-000000000004')
INSERT INTO dbo.reference_data_unit_of_measure_dimension (id,tenant_nid,scope_type,n_id,name,description,revision,status,source_revision,published_on,published_by,
     is_system_defined,conversion_kind,base_unit_nid,is_frozen,is_locked,is_deleted,entity_type,created_on,last_updated_on,
     optimistic_version,concurrency_version) VALUES (N'10000000-0000-0000-0000-000000000004',NULL,N'Platform',N'VOLUME',N'Volume',NULL,1,N'Published',NULL,N'2026-09-05T00:00:00+00:00',N'SYSTEM',
        1,N'Ratio',N'L',0,0,0,N'IndustrialPlatform.ReferenceData.Domain.UnitOfMeasure.UnitDimension',N'2026-09-05T00:00:00+00:00',N'2026-09-05T00:00:00+00:00',1,
        N'10000000-0000-0000-0000-00000000000f');

IF NOT EXISTS (SELECT 1 FROM dbo.reference_data_unit_of_measure_unit WITH (UPDLOCK,HOLDLOCK) WHERE id=N'14000000-0000-0000-0000-000000000001')
INSERT INTO dbo.reference_data_unit_of_measure_unit (id,unit_dimension_id,n_id,name,symbol,factor_to_base,offset_to_base,decimal_places,rounding_mode,enabled,sort,
     is_frozen,is_locked,is_deleted,entity_type,created_on,last_updated_on,optimistic_version,concurrency_version) VALUES (N'14000000-0000-0000-0000-000000000001',N'10000000-0000-0000-0000-000000000004',N'L',N'Litre',N'L',N'1',N'0',3,N'ToEven',
        1,0,0,0,0,N'IndustrialPlatform.ReferenceData.Domain.UnitOfMeasure.UnitDefinition',N'2026-09-05T00:00:00+00:00',N'2026-09-05T00:00:00+00:00',0,
        N'14000000-0000-0000-0000-00000000000f');

IF NOT EXISTS (SELECT 1 FROM dbo.reference_data_unit_of_measure_unit WITH (UPDLOCK,HOLDLOCK) WHERE id=N'14000000-0000-0000-0000-000000000002')
INSERT INTO dbo.reference_data_unit_of_measure_unit (id,unit_dimension_id,n_id,name,symbol,factor_to_base,offset_to_base,decimal_places,rounding_mode,enabled,sort,
     is_frozen,is_locked,is_deleted,entity_type,created_on,last_updated_on,optimistic_version,concurrency_version) VALUES (N'14000000-0000-0000-0000-000000000002',N'10000000-0000-0000-0000-000000000004',N'ML',N'Millilitre',N'mL',N'0.001',N'0',3,N'ToEven',
        1,1,0,0,0,N'IndustrialPlatform.ReferenceData.Domain.UnitOfMeasure.UnitDefinition',N'2026-09-05T00:00:00+00:00',N'2026-09-05T00:00:00+00:00',0,
        N'14000000-0000-0000-0000-00000000000f');

IF NOT EXISTS (SELECT 1 FROM dbo.reference_data_unit_of_measure_dimension WITH (UPDLOCK,HOLDLOCK) WHERE id=N'10000000-0000-0000-0000-000000000005')
INSERT INTO dbo.reference_data_unit_of_measure_dimension (id,tenant_nid,scope_type,n_id,name,description,revision,status,source_revision,published_on,published_by,
     is_system_defined,conversion_kind,base_unit_nid,is_frozen,is_locked,is_deleted,entity_type,created_on,last_updated_on,
     optimistic_version,concurrency_version) VALUES (N'10000000-0000-0000-0000-000000000005',NULL,N'Platform',N'COUNT',N'Count',NULL,1,N'Published',NULL,N'2026-09-05T00:00:00+00:00',N'SYSTEM',
        1,N'Ratio',N'PIECE',0,0,0,N'IndustrialPlatform.ReferenceData.Domain.UnitOfMeasure.UnitDimension',N'2026-09-05T00:00:00+00:00',N'2026-09-05T00:00:00+00:00',1,
        N'10000000-0000-0000-0000-00000000000f');

IF NOT EXISTS (SELECT 1 FROM dbo.reference_data_unit_of_measure_unit WITH (UPDLOCK,HOLDLOCK) WHERE id=N'15000000-0000-0000-0000-000000000001')
INSERT INTO dbo.reference_data_unit_of_measure_unit (id,unit_dimension_id,n_id,name,symbol,factor_to_base,offset_to_base,decimal_places,rounding_mode,enabled,sort,
     is_frozen,is_locked,is_deleted,entity_type,created_on,last_updated_on,optimistic_version,concurrency_version) VALUES (N'15000000-0000-0000-0000-000000000001',N'10000000-0000-0000-0000-000000000005',N'PIECE',N'Piece',N'piece',N'1',N'0',0,N'ToEven',
        1,0,0,0,0,N'IndustrialPlatform.ReferenceData.Domain.UnitOfMeasure.UnitDefinition',N'2026-09-05T00:00:00+00:00',N'2026-09-05T00:00:00+00:00',0,
        N'15000000-0000-0000-0000-00000000000f');

IF NOT EXISTS (SELECT 1 FROM dbo.reference_data_unit_of_measure_dimension WITH (UPDLOCK,HOLDLOCK) WHERE id=N'10000000-0000-0000-0000-000000000006')
INSERT INTO dbo.reference_data_unit_of_measure_dimension (id,tenant_nid,scope_type,n_id,name,description,revision,status,source_revision,published_on,published_by,
     is_system_defined,conversion_kind,base_unit_nid,is_frozen,is_locked,is_deleted,entity_type,created_on,last_updated_on,
     optimistic_version,concurrency_version) VALUES (N'10000000-0000-0000-0000-000000000006',NULL,N'Platform',N'ABSOLUTE_TEMPERATURE',N'Absolute temperature',NULL,1,N'Published',NULL,N'2026-09-05T00:00:00+00:00',N'SYSTEM',
        1,N'AbsoluteTemperature',N'K',0,0,0,N'IndustrialPlatform.ReferenceData.Domain.UnitOfMeasure.UnitDimension',N'2026-09-05T00:00:00+00:00',N'2026-09-05T00:00:00+00:00',1,
        N'10000000-0000-0000-0000-00000000000f');

IF NOT EXISTS (SELECT 1 FROM dbo.reference_data_unit_of_measure_unit WITH (UPDLOCK,HOLDLOCK) WHERE id=N'16000000-0000-0000-0000-000000000001')
INSERT INTO dbo.reference_data_unit_of_measure_unit (id,unit_dimension_id,n_id,name,symbol,factor_to_base,offset_to_base,decimal_places,rounding_mode,enabled,sort,
     is_frozen,is_locked,is_deleted,entity_type,created_on,last_updated_on,optimistic_version,concurrency_version) VALUES (N'16000000-0000-0000-0000-000000000001',N'10000000-0000-0000-0000-000000000006',N'K',N'Kelvin',N'K',N'1',N'0',2,N'ToEven',
        1,0,0,0,0,N'IndustrialPlatform.ReferenceData.Domain.UnitOfMeasure.UnitDefinition',N'2026-09-05T00:00:00+00:00',N'2026-09-05T00:00:00+00:00',0,
        N'16000000-0000-0000-0000-00000000000f');

IF NOT EXISTS (SELECT 1 FROM dbo.reference_data_unit_of_measure_unit WITH (UPDLOCK,HOLDLOCK) WHERE id=N'16000000-0000-0000-0000-000000000002')
INSERT INTO dbo.reference_data_unit_of_measure_unit (id,unit_dimension_id,n_id,name,symbol,factor_to_base,offset_to_base,decimal_places,rounding_mode,enabled,sort,
     is_frozen,is_locked,is_deleted,entity_type,created_on,last_updated_on,optimistic_version,concurrency_version) VALUES (N'16000000-0000-0000-0000-000000000002',N'10000000-0000-0000-0000-000000000006',N'DEGC',N'Degree Celsius',N'°C',N'1',N'273.15',2,N'ToEven',
        1,1,0,0,0,N'IndustrialPlatform.ReferenceData.Domain.UnitOfMeasure.UnitDefinition',N'2026-09-05T00:00:00+00:00',N'2026-09-05T00:00:00+00:00',0,
        N'16000000-0000-0000-0000-00000000000f');

IF NOT EXISTS (SELECT 1 FROM dbo.reference_data_unit_of_measure_unit WITH (UPDLOCK,HOLDLOCK) WHERE id=N'16000000-0000-0000-0000-000000000003')
INSERT INTO dbo.reference_data_unit_of_measure_unit (id,unit_dimension_id,n_id,name,symbol,factor_to_base,offset_to_base,decimal_places,rounding_mode,enabled,sort,
     is_frozen,is_locked,is_deleted,entity_type,created_on,last_updated_on,optimistic_version,concurrency_version) VALUES (N'16000000-0000-0000-0000-000000000003',N'10000000-0000-0000-0000-000000000006',N'DEGF',N'Degree Fahrenheit',N'°F',N'0.555555555556',N'255.372222222222',2,N'ToEven',
        1,2,0,0,0,N'IndustrialPlatform.ReferenceData.Domain.UnitOfMeasure.UnitDefinition',N'2026-09-05T00:00:00+00:00',N'2026-09-05T00:00:00+00:00',0,
        N'16000000-0000-0000-0000-00000000000f');
