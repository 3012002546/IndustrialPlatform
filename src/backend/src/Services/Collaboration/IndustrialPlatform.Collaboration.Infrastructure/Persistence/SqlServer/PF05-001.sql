-- SQL Server 2019/2022；新增 Provider 的独立脚本，应用后只能追加迁移。
-- create collaboration messaging tables
IF OBJECT_ID(N'dbo.collaboration_conversation',N'U') IS NULL
BEGIN
CREATE TABLE dbo.collaboration_conversation (
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
    participant_low_user_n_id nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    participant_high_user_n_id nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    status nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    last_message_sequence bigint NOT NULL,
    last_message_n_id nvarchar(max) COLLATE Latin1_General_100_BIN2 NULL,
    last_message_on datetimeoffset(7) NULL,
    retention_floor_sequence bigint NOT NULL,
    CONSTRAINT uq_collaboration_conversation_nid UNIQUE (tenant_n_id, n_id),
    CONSTRAINT uq_collaboration_conversation_pair UNIQUE (tenant_n_id, participant_low_user_n_id, participant_high_user_n_id),
    CONSTRAINT ck_collaboration_conversation_pair CHECK (participant_low_user_n_id < participant_high_user_n_id)
);
END;

IF OBJECT_ID(N'dbo.collaboration_conversation_member',N'U') IS NULL
BEGIN
CREATE TABLE dbo.collaboration_conversation_member (
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
    conversation_n_id nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    user_n_id nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    display_name_snapshot nvarchar(256) COLLATE Latin1_General_100_BIN2 NOT NULL,
    joined_on datetimeoffset(7) NOT NULL,
    visibility_state nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    hidden_on datetimeoffset(7) NULL,
    hidden_through_sequence bigint NOT NULL,
    last_read_sequence bigint NOT NULL,
    last_read_on datetimeoffset(7) NULL,
    unread_count bigint NOT NULL,
    projection_version bigint NOT NULL,
    CONSTRAINT uq_collaboration_member UNIQUE (tenant_n_id, conversation_n_id, user_n_id)
);
END;

IF OBJECT_ID(N'dbo.collaboration_message',N'U') IS NULL
BEGIN
CREATE TABLE dbo.collaboration_message (
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
    conversation_n_id nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    message_n_id nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    sequence bigint NOT NULL,
    sender_user_n_id nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    client_message_n_id nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    request_hash nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    message_type nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    text_content nvarchar(max) COLLATE Latin1_General_100_BIN2 NULL,
    reply_to_message_n_id nvarchar(max) COLLATE Latin1_General_100_BIN2 NULL,
    attachment_n_id nvarchar(max) COLLATE Latin1_General_100_BIN2 NULL,
    accepted_on datetimeoffset(7) NOT NULL,
    retracted_on datetimeoffset(7) NULL,
    retracted_by_user_n_id nvarchar(max) COLLATE Latin1_General_100_BIN2 NULL,
    retraction_reason nvarchar(max) COLLATE Latin1_General_100_BIN2 NULL,
    message_state_version int NOT NULL,
    CONSTRAINT uq_collaboration_message_sequence UNIQUE (tenant_n_id, conversation_n_id, sequence),
    CONSTRAINT uq_collaboration_message_client UNIQUE (tenant_n_id, sender_user_n_id, client_message_n_id)
);
END;

IF OBJECT_ID(N'dbo.collaboration_chat_attachment',N'U') IS NULL
BEGIN
CREATE TABLE dbo.collaboration_chat_attachment (
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
    conversation_n_id nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    attachment_n_id nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    uploader_user_n_id nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    file_n_id nvarchar(128) COLLATE Latin1_General_100_BIN2 NULL,
    file_name_snapshot nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    content_type_snapshot nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    size_snapshot bigint NOT NULL,
    purpose nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    file_state_projection nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    file_state_version int NULL,
    file_observed_on datetimeoffset(7) NULL,
    reference_state nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    retention_state nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    bound_message_n_id nvarchar(max) COLLATE Latin1_General_100_BIN2 NULL,
    intent_request_n_id nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    intent_request_hash nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    reference_n_id nvarchar(max) COLLATE Latin1_General_100_BIN2 NULL,
    CONSTRAINT uq_collaboration_attachment_nid UNIQUE (tenant_n_id, conversation_n_id, attachment_n_id),
    CONSTRAINT uq_collaboration_attachment_intent UNIQUE (tenant_n_id, uploader_user_n_id, intent_request_n_id)
);
END;
