-- SQL Server 2019/2022；新增 Provider 的独立脚本，应用后只能追加迁移。
-- create remote assistance screen session
IF OBJECT_ID(N'dbo.collaboration_remote_assistance_session',N'U') IS NULL
BEGIN
CREATE TABLE dbo.collaboration_remote_assistance_session (
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
    initiator_user_n_id nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    invitee_user_n_id nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    direction nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    sharer_user_n_id nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    viewer_user_n_id nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    state nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    answer nvarchar(max) COLLATE Latin1_General_100_BIN2 NULL,
    request_n_id nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    request_hash nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    deadline_on datetimeoffset(7) NOT NULL,
    accepted_on datetimeoffset(7) NULL,
    started_on datetimeoffset(7) NULL,
    ended_on datetimeoffset(7) NULL,
    end_reason nvarchar(max) COLLATE Latin1_General_100_BIN2 NULL,
    initiator_connection_id nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    invitee_connection_id nvarchar(max) COLLATE Latin1_General_100_BIN2 NULL,
    initiator_alive_until datetimeoffset(7) NULL,
    invitee_alive_until datetimeoffset(7) NULL,
    initiator_stopped_reported_on datetimeoffset(7) NULL,
    invitee_stopped_reported_on datetimeoffset(7) NULL,
    CONSTRAINT uq_collaboration_remote_assistance_session_nid UNIQUE (tenant_n_id, n_id),
    CONSTRAINT uq_collaboration_remote_assistance_session_request UNIQUE (tenant_n_id, initiator_user_n_id, request_n_id),
    CONSTRAINT ck_collaboration_remote_assistance_session_direction CHECK (direction IN (N'ShareMine', N'RequestPeer')),
    CONSTRAINT ck_collaboration_remote_assistance_session_state CHECK (state IN (N'Pending', N'Accepted', N'Connecting', N'Sharing', N'Declined', N'Cancelled', N'Expired', N'Failed', N'Ended')),
    CONSTRAINT ck_collaboration_remote_assistance_session_answer CHECK (answer IS NULL OR answer IN (N'Accept', N'Decline'))
);
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.collaboration_remote_assistance_session') AND name=N'uq_collaboration_remote_assistance_session_active_conversation')
CREATE UNIQUE INDEX uq_collaboration_remote_assistance_session_active_conversation ON dbo.collaboration_remote_assistance_session (tenant_n_id, conversation_n_id)
    WHERE is_deleted = 0 AND state IN (N'Pending', N'Accepted', N'Connecting', N'Sharing');

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.collaboration_remote_assistance_session') AND name=N'uq_collaboration_remote_assistance_session_active_sharer')
CREATE UNIQUE INDEX uq_collaboration_remote_assistance_session_active_sharer ON dbo.collaboration_remote_assistance_session (tenant_n_id, sharer_user_n_id)
    WHERE is_deleted = 0 AND state IN (N'Accepted', N'Connecting', N'Sharing');

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.collaboration_remote_assistance_session') AND name=N'ix_collaboration_remote_assistance_session_state_deadline')
CREATE INDEX ix_collaboration_remote_assistance_session_state_deadline ON dbo.collaboration_remote_assistance_session (tenant_n_id, state, deadline_on);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.collaboration_remote_assistance_session') AND name=N'ix_collaboration_remote_assistance_session_conversation_created')
CREATE INDEX ix_collaboration_remote_assistance_session_conversation_created ON dbo.collaboration_remote_assistance_session (tenant_n_id, conversation_n_id, created_on, n_id);
