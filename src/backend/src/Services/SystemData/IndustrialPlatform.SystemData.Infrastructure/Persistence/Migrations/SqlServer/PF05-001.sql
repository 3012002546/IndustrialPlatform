-- SQL Server 2019/2022；新增 Provider 的独立脚本，应用后只能追加迁移。
-- system_trusted_service_call_nonce
IF OBJECT_ID(N'dbo.system_trusted_service_call_nonce',N'U') IS NULL
BEGIN
CREATE TABLE dbo.system_trusted_service_call_nonce (
    issuer nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    nonce nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    expires_on datetimeoffset(7) NOT NULL,
    created_on datetimeoffset(7) NOT NULL,
    PRIMARY KEY (issuer, nonce)
);
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.system_trusted_service_call_nonce') AND name=N'ix_system_trusted_service_call_nonce_expiry')
CREATE INDEX ix_system_trusted_service_call_nonce_expiry ON dbo.system_trusted_service_call_nonce (expires_on);
