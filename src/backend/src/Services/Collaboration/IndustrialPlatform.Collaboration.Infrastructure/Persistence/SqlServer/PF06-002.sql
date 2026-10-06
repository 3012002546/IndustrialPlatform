-- SQL Server 2019/2022；新增 Provider 的独立脚本，应用后只能追加迁移。
-- create remote assistance voice call and slots
IF OBJECT_ID(N'dbo.collaboration_remote_assistance_voice_call',N'U') IS NULL
BEGIN
CREATE TABLE dbo.collaboration_remote_assistance_voice_call (
    id uniqueidentifier PRIMARY KEY NOT NULL,
    is_frozen bit NOT NULL,
    is_locked bit NOT NULL,
    is_deleted bit NOT NULL,
    entity_type nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    created_on datetimeoffset(7) NOT NULL,
    last_updated_on datetimeoffset(7) NOT NULL,
    optimistic_version bigint NOT NULL,
    concurrency_version uniqueidentifier NOT NULL,
    tenant_n_id nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    n_id nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    conversation_n_id nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    caller_user_n_id nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    callee_user_n_id nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    state nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    answer nvarchar(max) COLLATE Latin1_General_100_BIN2 NULL,
    request_n_id nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    request_hash nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    deadline_on datetimeoffset(7) NOT NULL,
    accepted_on datetimeoffset(7) NULL,
    started_on datetimeoffset(7) NULL,
    ended_on datetimeoffset(7) NULL,
    end_reason nvarchar(max) COLLATE Latin1_General_100_BIN2 NULL,
    caller_connection_id nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    callee_connection_id nvarchar(max) COLLATE Latin1_General_100_BIN2 NULL,
    caller_alive_until datetimeoffset(7) NULL,
    callee_alive_until datetimeoffset(7) NULL,
    caller_ready_on datetimeoffset(7) NULL,
    callee_ready_on datetimeoffset(7) NULL,
    caller_stopped_reported_on datetimeoffset(7) NULL,
    callee_stopped_reported_on datetimeoffset(7) NULL,
    CONSTRAINT uq_collaboration_remote_assistance_voice_call_nid UNIQUE (tenant_n_id, n_id),
    CONSTRAINT uq_collaboration_remote_assistance_voice_call_request UNIQUE (tenant_n_id, caller_user_n_id, request_n_id),
    CONSTRAINT ck_collaboration_remote_assistance_voice_state CHECK (state IN (N'Ringing', N'Accepted', N'Connecting', N'Active', N'Declined', N'Cancelled', N'Missed', N'Failed', N'Ended')),
    CONSTRAINT ck_collaboration_remote_assistance_voice_answer CHECK (answer IS NULL OR answer IN (N'Accept', N'Decline'))
);
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.collaboration_remote_assistance_voice_call') AND name=N'uq_collaboration_remote_assistance_voice_call_active_conversation')
CREATE UNIQUE INDEX uq_collaboration_remote_assistance_voice_call_active_conversation ON dbo.collaboration_remote_assistance_voice_call (tenant_n_id, conversation_n_id)
    WHERE is_deleted = 0 AND state IN (N'Ringing', N'Accepted', N'Connecting', N'Active');

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.collaboration_remote_assistance_voice_call') AND name=N'ix_collaboration_remote_assistance_voice_call_state_deadline')
CREATE INDEX ix_collaboration_remote_assistance_voice_call_state_deadline ON dbo.collaboration_remote_assistance_voice_call (tenant_n_id, state, deadline_on);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.collaboration_remote_assistance_voice_call') AND name=N'ix_collaboration_remote_assistance_voice_call_conversation_created')
CREATE INDEX ix_collaboration_remote_assistance_voice_call_conversation_created ON dbo.collaboration_remote_assistance_voice_call (tenant_n_id, conversation_n_id, created_on, n_id);

IF OBJECT_ID(N'dbo.collaboration_remote_assistance_voice_slot',N'U') IS NULL
BEGIN
CREATE TABLE dbo.collaboration_remote_assistance_voice_slot (
    tenant_n_id nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    user_n_id nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    voice_call_n_id nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    created_on datetimeoffset(7) NOT NULL,
    expires_on datetimeoffset(7) NOT NULL,
    CONSTRAINT pk_collaboration_remote_assistance_voice_slot PRIMARY KEY (tenant_n_id, user_n_id)
);
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.collaboration_remote_assistance_voice_slot') AND name=N'ix_collaboration_remote_assistance_voice_slot_call')
CREATE INDEX ix_collaboration_remote_assistance_voice_slot_call ON dbo.collaboration_remote_assistance_voice_slot (tenant_n_id, voice_call_n_id);
