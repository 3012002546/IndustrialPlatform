-- SQL Server 2019/2022；新增 Provider 的独立脚本，应用后只能追加迁移。
-- system_audit_lifecycle legal hold
IF COL_LENGTH(N'dbo.system_audit_lifecycle',N'legal_hold') IS NULL
ALTER TABLE dbo.system_audit_lifecycle ADD legal_hold bit NOT NULL DEFAULT 0;

IF COL_LENGTH(N'dbo.system_audit_lifecycle',N'legal_hold_reason') IS NULL
ALTER TABLE dbo.system_audit_lifecycle ADD legal_hold_reason nvarchar(max) COLLATE Latin1_General_100_BIN2 NULL;
