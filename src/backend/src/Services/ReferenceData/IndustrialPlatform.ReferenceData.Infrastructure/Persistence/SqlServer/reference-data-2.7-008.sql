-- SQL Server 2019/2022；新增 Provider 的独立脚本，应用后只能追加迁移。
-- reference-data-2.7-008
IF OBJECT_ID(N'dbo.reference_data_state_machine_definition',N'U') IS NULL
BEGIN
CREATE TABLE dbo.reference_data_state_machine_definition (
    id uniqueidentifier PRIMARY KEY,
    tenant_nid nvarchar(128) COLLATE Latin1_General_100_BIN2 NULL,
    scope_type nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    n_id nvarchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL CHECK(n_id=upper(n_id)),
    name nvarchar(200) COLLATE Latin1_General_100_BIN2 NOT NULL,
    description nvarchar(max) COLLATE Latin1_General_100_BIN2 NULL,
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
    UNIQUE(id,revision),
    CHECK((scope_type=N'Platform' AND tenant_nid IS NULL)
        OR (scope_type=N'Tenant' AND tenant_nid IS NOT NULL)),
    CHECK(source_revision IS NULL OR source_revision<revision),
    CHECK((status=N'Draft' AND published_on IS NULL)
        OR (status IN (N'Published',N'Superseded') AND published_on IS NOT NULL)
        OR status=N'Disabled'),
    tenant_scope AS ISNULL(tenant_nid,N'') PERSISTED
);
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.reference_data_state_machine_definition') AND name=N'state_machine_scope_revision_uq')
CREATE UNIQUE INDEX state_machine_scope_revision_uq ON dbo.reference_data_state_machine_definition (tenant_scope,n_id,revision);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.reference_data_state_machine_definition') AND name=N'state_machine_draft_uq')
CREATE UNIQUE INDEX state_machine_draft_uq ON dbo.reference_data_state_machine_definition (tenant_scope,n_id)
    WHERE status=N'Draft' AND is_deleted=0;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.reference_data_state_machine_definition') AND name=N'state_machine_current_uq')
CREATE UNIQUE INDEX state_machine_current_uq ON dbo.reference_data_state_machine_definition (tenant_scope,n_id)
    WHERE status=N'Published' AND is_deleted=0;

IF OBJECT_ID(N'dbo.reference_data_state_machine_node',N'U') IS NULL
BEGIN
CREATE TABLE dbo.reference_data_state_machine_node (
    id uniqueidentifier PRIMARY KEY,
    state_machine_definition_id uniqueidentifier NOT NULL,
    definition_revision int NOT NULL CHECK(definition_revision>0),
    n_id nvarchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL CHECK(n_id=upper(n_id)),
    name nvarchar(200) COLLATE Latin1_General_100_BIN2 NOT NULL,
    description nvarchar(max) COLLATE Latin1_General_100_BIN2 NULL,
    is_initial bit NOT NULL,
    is_terminal bit NOT NULL,
    outcome nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL CHECK(outcome IN (N'None',N'Success',N'Failure',N'Skipped')),
    color nvarchar(7) COLLATE Latin1_General_100_BIN2 NULL CHECK(color IS NULL OR ((LEN(color+N'#')-1)=7 AND color LIKE N'#[0-9A-F][0-9A-F][0-9A-F][0-9A-F][0-9A-F][0-9A-F]')),
    sort int NOT NULL CHECK(sort>=0),
    FOREIGN KEY (state_machine_definition_id,definition_revision)
        REFERENCES dbo.reference_data_state_machine_definition(id,revision),
    UNIQUE(state_machine_definition_id,definition_revision,n_id)
);
END;

IF OBJECT_ID(N'dbo.reference_data_state_machine_transition',N'U') IS NULL
BEGIN
CREATE TABLE dbo.reference_data_state_machine_transition (
    id uniqueidentifier PRIMARY KEY,
    state_machine_definition_id uniqueidentifier NOT NULL,
    definition_revision int NOT NULL CHECK(definition_revision>0),
    from_status_nid nvarchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL CHECK(from_status_nid=upper(from_status_nid)),
    action_nid nvarchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL CHECK(action_nid=upper(action_nid)),
    action_name nvarchar(200) COLLATE Latin1_General_100_BIN2 NOT NULL,
    to_status_nid nvarchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL CHECK(to_status_nid=upper(to_status_nid)),
    description nvarchar(max) COLLATE Latin1_General_100_BIN2 NULL,
    FOREIGN KEY (state_machine_definition_id,definition_revision)
        REFERENCES dbo.reference_data_state_machine_definition(id,revision),
    FOREIGN KEY (state_machine_definition_id,definition_revision,from_status_nid)
        REFERENCES dbo.reference_data_state_machine_node(state_machine_definition_id,definition_revision,n_id),
    FOREIGN KEY (state_machine_definition_id,definition_revision,to_status_nid)
        REFERENCES dbo.reference_data_state_machine_node(state_machine_definition_id,definition_revision,n_id),
    UNIQUE(state_machine_definition_id,definition_revision,from_status_nid,action_nid)
);
END;
