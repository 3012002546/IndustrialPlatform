-- SQL Server 2019/2022；新增 Provider 的独立脚本，应用后只能追加迁移。
-- reference-data-2.7-007
IF OBJECT_ID(N'dbo.reference_data_coding_rule_definition',N'U') IS NULL
BEGIN
CREATE TABLE dbo.reference_data_coding_rule_definition (
    id uniqueidentifier PRIMARY KEY,
    tenant_nid nvarchar(128) COLLATE Latin1_General_100_BIN2 NULL,
    scope_type nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    n_id nvarchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL CHECK(n_id=upper(n_id)),
    name nvarchar(200) COLLATE Latin1_General_100_BIN2 NOT NULL,
    target_entity_nid nvarchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL CHECK(target_entity_nid=upper(target_entity_nid)),
    template nvarchar(1024) COLLATE Latin1_General_100_BIN2 NOT NULL CHECK((LEN(template+N'#N')-1) BETWEEN 1 AND 1024),
    reset_policy nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL CHECK(reset_policy IN ('Never',N'Yearly',N'Monthly',N'Daily')),
    revision int NOT NULL CHECK(revision>0),
    status nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL CHECK(status IN (N'Draft',N'Published',N'Superseded',N'Disabled')),
    source_revision int NULL CHECK(source_revision IS NULL OR source_revision>0),
    published_on datetimeoffset(7) NULL,
    published_by nvarchar(max) COLLATE Latin1_General_100_BIN2 NULL,
    is_frozen bit NOT NULL,
    is_locked bit NOT NULL,
    is_deleted bit NOT NULL,
    entity_type nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    created_on datetimeoffset(7) NOT NULL,
    last_updated_on datetimeoffset(7) NOT NULL,
    optimistic_version bigint NOT NULL,
    concurrency_version uniqueidentifier NOT NULL,
    CHECK((scope_type=N'Platform' AND tenant_nid IS NULL) OR (scope_type=N'Tenant' AND tenant_nid IS NOT NULL)),
    CHECK((status=N'Draft' AND published_on IS NULL) OR status=N'Disabled' OR
          (status IN (N'Published',N'Superseded') AND published_on IS NOT NULL)),
    CHECK((published_on IS NULL AND published_by IS NULL) OR
          (published_on IS NOT NULL AND published_by IS NOT NULL)),
    CHECK(source_revision IS NULL OR source_revision<revision),
    UNIQUE(id,revision)
);
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.reference_data_coding_rule_definition') AND name=N'coding_rule_platform_revision_uq')
CREATE UNIQUE INDEX coding_rule_platform_revision_uq ON dbo.reference_data_coding_rule_definition (n_id,revision)
    WHERE tenant_nid IS NULL AND is_deleted=0;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.reference_data_coding_rule_definition') AND name=N'coding_rule_tenant_revision_uq')
CREATE UNIQUE INDEX coding_rule_tenant_revision_uq ON dbo.reference_data_coding_rule_definition (tenant_nid,n_id,revision)
    WHERE tenant_nid IS NOT NULL AND is_deleted=0;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.reference_data_coding_rule_definition') AND name=N'coding_rule_platform_draft_uq')
CREATE UNIQUE INDEX coding_rule_platform_draft_uq ON dbo.reference_data_coding_rule_definition (n_id)
    WHERE tenant_nid IS NULL AND status=N'Draft' AND is_deleted=0;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.reference_data_coding_rule_definition') AND name=N'coding_rule_tenant_draft_uq')
CREATE UNIQUE INDEX coding_rule_tenant_draft_uq ON dbo.reference_data_coding_rule_definition (tenant_nid,n_id)
    WHERE tenant_nid IS NOT NULL AND status=N'Draft' AND is_deleted=0;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.reference_data_coding_rule_definition') AND name=N'coding_rule_platform_current_uq')
CREATE UNIQUE INDEX coding_rule_platform_current_uq ON dbo.reference_data_coding_rule_definition (n_id)
    WHERE tenant_nid IS NULL AND status=N'Published' AND is_deleted=0;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.reference_data_coding_rule_definition') AND name=N'coding_rule_tenant_current_uq')
CREATE UNIQUE INDEX coding_rule_tenant_current_uq ON dbo.reference_data_coding_rule_definition (tenant_nid,n_id)
    WHERE tenant_nid IS NOT NULL AND status=N'Published' AND is_deleted=0;

IF OBJECT_ID(N'dbo.reference_data_coding_rule_sequence',N'U') IS NULL
BEGIN
CREATE TABLE dbo.reference_data_coding_rule_sequence (
    coding_rule_id uniqueidentifier NOT NULL,
    rule_revision int NOT NULL CHECK(rule_revision>0),
    period_key nvarchar(8) COLLATE Latin1_General_100_BIN2 NOT NULL CHECK((LEN(period_key+N'#N')-1) BETWEEN 1 AND 8),
    context_hash nvarchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL CHECK((LEN(context_hash+N'#')-1)=64),
    last_value bigint NOT NULL CHECK(last_value>0),
    created_on datetimeoffset(7) NOT NULL,
    last_updated_on datetimeoffset(7) NOT NULL,
    PRIMARY KEY(coding_rule_id,rule_revision,period_key,context_hash),
    FOREIGN KEY(coding_rule_id,rule_revision)
        REFERENCES dbo.reference_data_coding_rule_definition(id,revision)
);
END;

IF OBJECT_ID(N'dbo.reference_data_coding_rule_idempotency_record',N'U') IS NULL
BEGIN
CREATE TABLE dbo.reference_data_coding_rule_idempotency_record (
    id uniqueidentifier PRIMARY KEY,
    tenant_nid nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    rule_nid nvarchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL CHECK(rule_nid=upper(rule_nid)),
    idempotency_key_hash nvarchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL CHECK((LEN(idempotency_key_hash+N'#N')-1)=64),
    request_hash nvarchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL CHECK((LEN(request_hash+N'#N')-1)=64),
    coding_rule_id uniqueidentifier NOT NULL,
    rule_revision int NOT NULL CHECK(rule_revision>0),
    source_scope nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL CHECK(source_scope IN ('Platform',N'Tenant')),
    source_tenant_nid nvarchar(max) COLLATE Latin1_General_100_BIN2 NULL,
    code nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL CHECK((LEN(code+N'#N')-1) BETWEEN 1 AND 128),
    sequence_value bigint NOT NULL CHECK(sequence_value>0),
    period_key nvarchar(8) COLLATE Latin1_General_100_BIN2 NOT NULL CHECK((LEN(period_key+N'#N')-1) BETWEEN 1 AND 8),
    generated_on datetimeoffset(7) NOT NULL,
    expires_on datetimeoffset(7) NOT NULL,
    CHECK((source_scope='Platform' AND source_tenant_nid IS NULL) OR
          (source_scope=N'Tenant' AND source_tenant_nid IS NOT NULL)),
    CHECK(expires_on>generated_on),
    UNIQUE(tenant_nid,rule_nid,idempotency_key_hash),
    FOREIGN KEY(coding_rule_id,rule_revision)
        REFERENCES dbo.reference_data_coding_rule_definition(id,revision)
);
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.reference_data_coding_rule_idempotency_record') AND name=N'coding_rule_idempotency_expiry_ix')
CREATE INDEX coding_rule_idempotency_expiry_ix ON dbo.reference_data_coding_rule_idempotency_record (expires_on);
