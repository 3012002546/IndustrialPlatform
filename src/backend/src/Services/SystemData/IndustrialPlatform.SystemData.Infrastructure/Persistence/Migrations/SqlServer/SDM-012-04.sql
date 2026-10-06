-- SQL Server 2019/2022；新增 Provider 的独立脚本，应用后只能追加迁移。
-- system_data_outbox retry backoff
IF COL_LENGTH(N'dbo.system_data_outbox',N'next_attempt_on') IS NULL
ALTER TABLE dbo.system_data_outbox ADD next_attempt_on datetimeoffset(7) NULL;
GO
