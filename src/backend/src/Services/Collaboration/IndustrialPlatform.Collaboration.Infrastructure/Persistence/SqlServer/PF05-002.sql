-- SQL Server 2019/2022；新增 Provider 的独立脚本，应用后只能追加迁移。
-- create collaboration compliance tables
IF OBJECT_ID(N'dbo.collaboration_compliance_disposition',N'U') IS NULL
BEGIN
CREATE TABLE dbo.collaboration_compliance_disposition (
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
    disposition_n_id nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    subject_type nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    subject_n_id nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    state nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    reason nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    created_by_user_n_id nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    expires_on datetimeoffset(7) NULL,
    request_n_id nvarchar(128) COLLATE Latin1_General_100_BIN2 NULL,
    request_hash nvarchar(max) COLLATE Latin1_General_100_BIN2 NULL,
    CONSTRAINT uq_collaboration_disposition UNIQUE (tenant_n_id, disposition_n_id),
    CONSTRAINT uq_collaboration_disposition_request UNIQUE (tenant_n_id, created_by_user_n_id, request_n_id)
);
END;

IF OBJECT_ID(N'dbo.collaboration_legal_hold_case',N'U') IS NULL
BEGIN
CREATE TABLE dbo.collaboration_legal_hold_case (
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
    hold_case_n_id nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    state nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    scope_json nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    scope_checksum nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    reason nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    created_by_user_n_id nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    released_on datetimeoffset(7) NULL,
    request_n_id nvarchar(128) COLLATE Latin1_General_100_BIN2 NULL,
    request_hash nvarchar(max) COLLATE Latin1_General_100_BIN2 NULL,
    operation_id nvarchar(max) COLLATE Latin1_General_100_BIN2 NULL,
    release_requested_by_user_n_id nvarchar(max) COLLATE Latin1_General_100_BIN2 NULL,
    release_request_n_id nvarchar(max) COLLATE Latin1_General_100_BIN2 NULL,
    release_approval_expires_on datetimeoffset(7) NULL,
    release_approved_by_user_n_id nvarchar(max) COLLATE Latin1_General_100_BIN2 NULL,
    reviewed_by_user_n_id nvarchar(max) COLLATE Latin1_General_100_BIN2 NULL,
    reviewed_on datetimeoffset(7) NULL,
    external_case_reference nvarchar(max) COLLATE Latin1_General_100_BIN2 NULL,
    file_sync_state nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL DEFAULT N'Pending',
    CONSTRAINT uq_collaboration_hold UNIQUE (tenant_n_id, hold_case_n_id),
    CONSTRAINT uq_collaboration_hold_request UNIQUE (tenant_n_id, created_by_user_n_id, request_n_id)
);
END;

IF OBJECT_ID(N'dbo.collaboration_compliance_export',N'U') IS NULL
BEGIN
CREATE TABLE dbo.collaboration_compliance_export (
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
    export_n_id nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    state nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    scope_json nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    scope_checksum nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    reason nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    created_by_user_n_id nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    expires_on datetimeoffset(7) NULL,
    artifact_reference nvarchar(max) COLLATE Latin1_General_100_BIN2 NULL,
    request_n_id nvarchar(128) COLLATE Latin1_General_100_BIN2 NULL,
    request_hash nvarchar(max) COLLATE Latin1_General_100_BIN2 NULL,
    case_reference nvarchar(max) COLLATE Latin1_General_100_BIN2 NULL,
    approved_by_user_n_id nvarchar(max) COLLATE Latin1_General_100_BIN2 NULL,
    approved_on datetimeoffset(7) NULL,
    approval_expires_on datetimeoffset(7) NULL,
    approval_consumed_on datetimeoffset(7) NULL,
    run_deadline_on datetimeoffset(7) NULL,
    completed_on datetimeoffset(7) NULL,
    error_code nvarchar(max) COLLATE Latin1_General_100_BIN2 NULL,
    fields_json nvarchar(max) COLLATE Latin1_General_100_BIN2 NULL,
    operation_id nvarchar(128) COLLATE Latin1_General_100_BIN2 NULL,
    export_retention_hours int NOT NULL DEFAULT 24,
    CONSTRAINT uq_collaboration_export UNIQUE (tenant_n_id, export_n_id),
    CONSTRAINT uq_collaboration_export_request UNIQUE (tenant_n_id, created_by_user_n_id, request_n_id),
    CONSTRAINT uq_collaboration_export_operation UNIQUE (tenant_n_id, operation_id)
);
END;

IF OBJECT_ID(N'dbo.collaboration_retention_policy',N'U') IS NULL
BEGIN
CREATE TABLE dbo.collaboration_retention_policy (
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
    policy_n_id nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    message_retention_days int NOT NULL,
    attachment_retention_days int NOT NULL,
    audit_retention_days int NOT NULL,
    enabled bit NOT NULL,
    export_retention_hours int NOT NULL DEFAULT 24,
    review_due_hours int NOT NULL DEFAULT 24,
    status nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL DEFAULT N'Active',
    CONSTRAINT uq_collaboration_retention UNIQUE (tenant_n_id, policy_n_id)
);
END;

IF OBJECT_ID(N'dbo.collaboration_compliance_command',N'U') IS NULL
BEGIN
CREATE TABLE dbo.collaboration_compliance_command (
    tenant_n_id nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    actor_user_n_id nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    request_n_id nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    action nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    target_n_id nvarchar(max) COLLATE Latin1_General_100_BIN2 NULL,
    request_hash nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    scope_checksum nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    command_json nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    status nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    result_json nvarchar(max) COLLATE Latin1_General_100_BIN2 NULL,
    completed_on datetimeoffset(7) NULL,
    CONSTRAINT pk_collaboration_compliance_command PRIMARY KEY (tenant_n_id, actor_user_n_id, request_n_id)
);
END;

IF OBJECT_ID(N'dbo.collaboration_compliance_preparation',N'U') IS NULL
BEGIN
CREATE TABLE dbo.collaboration_compliance_preparation (
    tenant_n_id nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    actor_user_n_id nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    request_n_id nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    actor_session_n_id nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    action nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    target_n_id nvarchar(max) COLLATE Latin1_General_100_BIN2 NULL,
    request_hash nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    scope_checksum nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    command_json nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    snapshot_json nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    prepared_on datetimeoffset(7) NOT NULL,
    expires_on datetimeoffset(7) NOT NULL,
    CONSTRAINT pk_collaboration_compliance_preparation PRIMARY KEY (tenant_n_id, actor_user_n_id, request_n_id)
);
END;

IF OBJECT_ID(N'dbo.collaboration_compliance_view_budget',N'U') IS NULL
BEGIN
CREATE TABLE dbo.collaboration_compliance_view_budget (
    tenant_n_id nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    actor_user_n_id nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    window_start_on datetimeoffset(7) NOT NULL,
    result_count int NOT NULL,
    CONSTRAINT pk_collaboration_compliance_view_budget PRIMARY KEY (tenant_n_id, actor_user_n_id, window_start_on)
);
END;

IF OBJECT_ID(N'dbo.collaboration_messaging_deduplication',N'U') IS NULL
BEGIN
CREATE TABLE dbo.collaboration_messaging_deduplication (
    tenant_n_id nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    sender_user_n_id nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    client_message_n_id nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    request_hash nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    message_n_id nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    conversation_n_id nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    sequence bigint NOT NULL,
    accepted_on datetimeoffset(7) NOT NULL,
    content_purged_on datetimeoffset(7) NULL,
    CONSTRAINT pk_collaboration_message_deduplication PRIMARY KEY (tenant_n_id, sender_user_n_id, client_message_n_id)
);
END;

IF OBJECT_ID(N'dbo.collaboration_outbox_message',N'U') IS NULL
BEGIN
CREATE TABLE dbo.collaboration_outbox_message (
    event_id uniqueidentifier PRIMARY KEY NOT NULL,
    tenant_n_id nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    event_type nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    payload nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    created_on datetimeoffset(7) NOT NULL,
    published_on datetimeoffset(7) NULL,
    retry_count int NOT NULL,
    last_error nvarchar(max) COLLATE Latin1_General_100_BIN2 NULL,
    lease_n_id nvarchar(max) COLLATE Latin1_General_100_BIN2 NULL,
    lease_until datetimeoffset(7) NULL,
    dead_lettered_on datetimeoffset(7) NULL
);
END;

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

IF OBJECT_ID(N'dbo.collaboration_event_inbox',N'U') IS NULL
BEGIN
CREATE TABLE dbo.collaboration_event_inbox (
    event_id uniqueidentifier PRIMARY KEY NOT NULL,
    tenant_n_id nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    event_type nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    status nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    received_on datetimeoffset(7) NOT NULL,
    processed_on datetimeoffset(7) NULL,
    retry_count int NOT NULL,
    last_error nvarchar(max) COLLATE Latin1_General_100_BIN2 NULL,
    lease_n_id nvarchar(max) COLLATE Latin1_General_100_BIN2 NULL,
    lease_until datetimeoffset(7) NULL
);
END;
