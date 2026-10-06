-- SQL Server 2019/2022；新增 Provider 的独立脚本，应用后只能追加迁移。
-- create personal message visibility
IF OBJECT_ID(N'dbo.collaboration_message_personal_visibility',N'U') IS NULL
BEGIN
CREATE TABLE dbo.collaboration_message_personal_visibility (
    tenant_n_id nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    user_n_id nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    conversation_n_id nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    message_n_id nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    hidden_on datetimeoffset(7) NOT NULL,
    PRIMARY KEY (tenant_n_id, user_n_id, message_n_id)
);
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.collaboration_message_personal_visibility') AND name=N'ix_collaboration_message_personal_visibility_lookup')
CREATE INDEX ix_collaboration_message_personal_visibility_lookup ON dbo.collaboration_message_personal_visibility (tenant_n_id, user_n_id, conversation_n_id, message_n_id);
