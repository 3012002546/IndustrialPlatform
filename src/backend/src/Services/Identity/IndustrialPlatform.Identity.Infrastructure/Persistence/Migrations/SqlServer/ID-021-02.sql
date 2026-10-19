-- SQL Server 2019/2022；新增 Provider 的独立脚本，应用后只能追加迁移。
-- create pf05_step_up_grant
IF OBJECT_ID(N'dbo.pf05_step_up_grant',N'U') IS NULL
BEGIN
CREATE TABLE dbo.pf05_step_up_grant (
    id uniqueidentifier PRIMARY KEY NOT NULL,
    tenant_n_id nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    actor_user_n_id nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    actor_session_n_id nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    actor_security_version nvarchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
    action nvarchar(96) COLLATE Latin1_General_100_BIN2 NOT NULL,
    request_n_id nvarchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
    scope_checksum nvarchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
    request_hash nvarchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
    binding_hash nvarchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
    proof_hash nvarchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
    issued_on datetimeoffset(7) NOT NULL,
    expires_on datetimeoffset(7) NOT NULL,
    consumed_on datetimeoffset(7) NULL,
    consumed_by_service nvarchar(64) COLLATE Latin1_General_100_BIN2 NULL,
    consumed_receipt_n_id nvarchar(128) COLLATE Latin1_General_100_BIN2 NULL,
    CONSTRAINT uq_pf05_step_up_grant_proof UNIQUE (proof_hash)
);
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.pf05_step_up_grant') AND name=N'ix_pf05_step_up_grant_expiry')
CREATE INDEX ix_pf05_step_up_grant_expiry ON dbo.pf05_step_up_grant (expires_on);
