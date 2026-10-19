-- SQL Server 2019/2022；新增 Provider 的独立脚本，应用后只能追加迁移。
-- mark service-owned initialization registrations
IF COL_LENGTH(N'dbo.system_data_database_registration',N'uses_service_initializer') IS NULL
ALTER TABLE dbo.system_data_database_registration ADD uses_service_initializer bit NOT NULL DEFAULT 0;
GO
