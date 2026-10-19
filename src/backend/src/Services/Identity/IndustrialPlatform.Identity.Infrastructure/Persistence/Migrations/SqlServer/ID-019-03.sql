-- SQL Server 2019/2022；新增 Provider 的独立脚本，应用后只能追加迁移。
-- add identity_user.must_change_password column
IF COL_LENGTH(N'dbo.identity_user',N'must_change_password') IS NULL
ALTER TABLE dbo.identity_user ADD must_change_password bit NOT NULL DEFAULT 0;
