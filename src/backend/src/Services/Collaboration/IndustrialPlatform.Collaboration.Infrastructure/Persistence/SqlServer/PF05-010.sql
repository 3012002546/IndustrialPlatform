-- SQL Server 2019/2022；新增 Provider 的独立脚本，应用后只能追加迁移。
-- add collaboration event inbox leases
IF COL_LENGTH(N'dbo.collaboration_event_inbox',N'lease_n_id') IS NULL
ALTER TABLE dbo.collaboration_event_inbox ADD lease_n_id nvarchar(max) COLLATE Latin1_General_100_BIN2 NULL;

IF COL_LENGTH(N'dbo.collaboration_event_inbox',N'lease_until') IS NULL
ALTER TABLE dbo.collaboration_event_inbox ADD lease_until datetimeoffset(7) NULL;
