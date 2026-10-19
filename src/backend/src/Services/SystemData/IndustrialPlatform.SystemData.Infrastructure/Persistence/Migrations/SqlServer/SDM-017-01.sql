-- SQL Server 2019/2022；新增 Provider 的独立脚本，应用后只能追加迁移。
-- SystemData navigation action resource associations
IF COL_LENGTH(N'dbo.system_data_navigation',N'action_resource_n_ids_json') IS NULL
ALTER TABLE dbo.system_data_navigation ADD action_resource_n_ids_json nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL DEFAULT N'[]';
GO
