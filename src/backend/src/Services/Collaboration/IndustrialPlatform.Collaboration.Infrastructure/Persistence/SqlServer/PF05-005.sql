-- SQL Server 2019/2022；新增 Provider 的独立脚本，应用后只能追加迁移。
-- persist canonical compliance command payload
IF COL_LENGTH(N'dbo.collaboration_compliance_command',N'command_json') IS NULL
ALTER TABLE dbo.collaboration_compliance_command ADD command_json nvarchar(max) COLLATE Latin1_General_100_BIN2 NULL;
