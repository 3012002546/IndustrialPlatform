-- SQL Server 2019/2022；新增 Provider 的独立脚本，应用后只能追加迁移。
-- create retention checkpoints
IF OBJECT_ID(N'dbo.collaboration_retention_checkpoint',N'U') IS NULL
BEGIN
CREATE TABLE dbo.collaboration_retention_checkpoint (
    id uniqueidentifier PRIMARY KEY NOT NULL,
    tenant_n_id nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    operation_n_id nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    cutoff_on datetimeoffset(7) NOT NULL,
    last_accepted_on datetimeoffset(7) NULL,
    last_message_id uniqueidentifier NULL,
    stage nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    lease_owner nvarchar(max) COLLATE Latin1_General_100_BIN2 NULL,
    lease_expires_on datetimeoffset(7) NULL,
    updated_on datetimeoffset(7) NOT NULL,
    error_code nvarchar(max) COLLATE Latin1_General_100_BIN2 NULL,
    CONSTRAINT uq_collaboration_retention_checkpoint UNIQUE (tenant_n_id, operation_n_id)
);
END;
