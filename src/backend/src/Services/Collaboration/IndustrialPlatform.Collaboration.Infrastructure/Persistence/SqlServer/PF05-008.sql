-- SQL Server 2019/2022；新增 Provider 的独立脚本，应用后只能追加迁移。
-- persist attachment file reference identity
IF COL_LENGTH(N'dbo.collaboration_chat_attachment',N'reference_n_id') IS NULL
ALTER TABLE dbo.collaboration_chat_attachment ADD reference_n_id nvarchar(max) COLLATE Latin1_General_100_BIN2 NULL;
