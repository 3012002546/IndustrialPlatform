-- SQL Server 2019/2022；新增 Provider 的独立脚本，应用后只能追加迁移。
-- persist service initialization plan semantics
IF COL_LENGTH(N'dbo.system_data_database_plan',N'service_requires_apply') IS NULL
ALTER TABLE dbo.system_data_database_plan ADD service_requires_apply bit NULL;
GO
