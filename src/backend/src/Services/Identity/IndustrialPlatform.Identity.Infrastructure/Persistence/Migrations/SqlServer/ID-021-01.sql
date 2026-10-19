-- SQL Server 2019/2022；新增 Provider 的独立脚本，应用后只能追加迁移。
-- create pf05_service_call_nonce
IF OBJECT_ID(N'dbo.pf05_service_call_nonce',N'U') IS NULL
BEGIN
CREATE TABLE dbo.pf05_service_call_nonce (
    issuer nvarchar(256) COLLATE Latin1_General_100_BIN2 NOT NULL,
    nonce nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    expires_on datetimeoffset(7) NOT NULL,
    created_on datetimeoffset(7) NOT NULL,
    CONSTRAINT pk_pf05_service_call_nonce PRIMARY KEY (issuer, nonce)
);
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.pf05_service_call_nonce') AND name=N'ix_pf05_service_call_nonce_expires')
CREATE INDEX ix_pf05_service_call_nonce_expires ON dbo.pf05_service_call_nonce (expires_on);
