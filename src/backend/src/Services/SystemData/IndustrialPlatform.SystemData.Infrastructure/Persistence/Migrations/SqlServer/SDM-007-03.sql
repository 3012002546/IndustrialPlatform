-- SQL Server 2019/2022；新增 Provider 的独立脚本，应用后只能追加迁移。
-- system_data_module_manifest permission declarations
IF COL_LENGTH(N'dbo.system_data_module_manifest',N'permission_manifest_json') IS NULL
ALTER TABLE dbo.system_data_module_manifest ADD permission_manifest_json nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL DEFAULT N'[]';
GO
