-- SQL Server 2019/2022；新增 Provider 的独立脚本，应用后只能追加迁移。
-- alter registration module scope
IF COL_LENGTH(N'dbo.system_data_database_registration',N'module_key') IS NULL
ALTER TABLE dbo.system_data_database_registration ADD module_key nvarchar(128) COLLATE Latin1_General_100_BIN2 NULL;
GO

UPDATE system_data_database_registration SET module_key = service_key WHERE module_key IS NULL;

IF COL_LENGTH(N'dbo.system_data_database_registration',N'seed_sets') IS NULL
ALTER TABLE dbo.system_data_database_registration ADD seed_sets nvarchar(max) COLLATE Latin1_General_100_BIN2 NULL;
GO

DROP INDEX IF EXISTS ux_registration_active ON dbo.system_data_database_registration;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.system_data_database_registration') AND name=N'ux_registration_active')
CREATE UNIQUE INDEX ux_registration_active ON dbo.system_data_database_registration (tenant_n_id, environment_n_id, service_key, module_key) WHERE is_deleted = 0;

IF COL_LENGTH(N'dbo.system_data_database_plan',N'module_key') IS NULL
ALTER TABLE dbo.system_data_database_plan ADD module_key nvarchar(max) COLLATE Latin1_General_100_BIN2 NULL;
GO

UPDATE system_data_database_plan SET module_key = service_key WHERE module_key IS NULL;

IF COL_LENGTH(N'dbo.system_data_database_operation',N'module_key') IS NULL
ALTER TABLE dbo.system_data_database_operation ADD module_key nvarchar(max) COLLATE Latin1_General_100_BIN2 NULL;
GO

UPDATE system_data_database_operation SET module_key = service_key WHERE module_key IS NULL;
