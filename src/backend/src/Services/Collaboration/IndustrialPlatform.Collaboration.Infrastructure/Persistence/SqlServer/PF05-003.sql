-- SQL Server 2019/2022；新增 Provider 的独立脚本，应用后只能追加迁移。
-- create collaboration indexes and retention defaults
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.collaboration_conversation_member') AND name=N'ix_collaboration_conversation_member_user')
CREATE INDEX ix_collaboration_conversation_member_user ON dbo.collaboration_conversation_member (tenant_n_id, user_n_id, visibility_state);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.collaboration_message') AND name=N'ix_collaboration_message_conversation_sequence')
CREATE INDEX ix_collaboration_message_conversation_sequence ON dbo.collaboration_message (tenant_n_id, conversation_n_id, sequence);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.collaboration_message') AND name=N'ix_collaboration_message_sender')
CREATE INDEX ix_collaboration_message_sender ON dbo.collaboration_message (tenant_n_id, sender_user_n_id, accepted_on);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.collaboration_chat_attachment') AND name=N'ix_collaboration_attachment_file')
CREATE INDEX ix_collaboration_attachment_file ON dbo.collaboration_chat_attachment (tenant_n_id, file_n_id);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.collaboration_compliance_disposition') AND name=N'ix_collaboration_disposition_subject')
CREATE INDEX ix_collaboration_disposition_subject ON dbo.collaboration_compliance_disposition (tenant_n_id, subject_type, subject_n_id, state);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.collaboration_legal_hold_case') AND name=N'ix_collaboration_hold_state')
CREATE INDEX ix_collaboration_hold_state ON dbo.collaboration_legal_hold_case (tenant_n_id, state);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.collaboration_compliance_export') AND name=N'ix_collaboration_export_state')
CREATE INDEX ix_collaboration_export_state ON dbo.collaboration_compliance_export (tenant_n_id, state, created_on);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.collaboration_outbox_message') AND name=N'ix_collaboration_outbox_pending')
CREATE INDEX ix_collaboration_outbox_pending ON dbo.collaboration_outbox_message (published_on, dead_lettered_on, lease_until, created_on);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.collaboration_retention_checkpoint') AND name=N'ix_collaboration_retention_checkpoint_active')
CREATE INDEX ix_collaboration_retention_checkpoint_active ON dbo.collaboration_retention_checkpoint (tenant_n_id, stage, updated_on);
