-- SQL Server 2019/2022；新增 Provider 的独立脚本，应用后只能追加迁移。
-- alter migration observation module scope
IF COL_LENGTH(N'dbo.system_data_database_migration_observation',N'module_key') IS NULL
ALTER TABLE dbo.system_data_database_migration_observation ADD module_key nvarchar(128) COLLATE Latin1_General_100_BIN2 NULL;
GO

UPDATE system_data_database_migration_observation SET module_key = service_key WHERE module_key IS NULL;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.system_data_database_migration_observation') AND name=N'ix_migration_observation_module_target')
CREATE INDEX ix_migration_observation_module_target ON dbo.system_data_database_migration_observation (tenant_n_id, environment_n_id, service_key, module_key, observed_on);
