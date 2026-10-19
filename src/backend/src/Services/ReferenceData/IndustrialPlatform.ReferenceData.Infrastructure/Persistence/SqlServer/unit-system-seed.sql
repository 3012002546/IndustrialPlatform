-- SQL Server：单位系统种子，重复初始化保留已存在行。
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
