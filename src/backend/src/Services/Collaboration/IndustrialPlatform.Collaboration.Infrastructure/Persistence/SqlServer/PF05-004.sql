-- SQL Server 2019/2022；新增 Provider 的独立脚本，应用后只能追加迁移。
-- add export worker lease columns
IF COL_LENGTH(N'dbo.collaboration_compliance_export',N'worker_lease_n_id') IS NULL
ALTER TABLE dbo.collaboration_compliance_export ADD worker_lease_n_id nvarchar(max) COLLATE Latin1_General_100_BIN2 NULL;

IF COL_LENGTH(N'dbo.collaboration_compliance_export',N'worker_lease_until') IS NULL
ALTER TABLE dbo.collaboration_compliance_export ADD worker_lease_until datetimeoffset(7) NULL;
