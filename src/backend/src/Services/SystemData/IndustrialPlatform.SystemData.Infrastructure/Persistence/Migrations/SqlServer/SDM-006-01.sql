-- SQL Server 2019/2022；新增 Provider 的独立脚本，应用后只能追加迁移。
-- create system_data_user_assignment
IF OBJECT_ID(N'dbo.system_data_user_assignment',N'U') IS NULL
BEGIN
CREATE TABLE dbo.system_data_user_assignment (
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
    n_id nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    normalized_n_id nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    user_n_id nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    user_display_name_snapshot nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    organization_n_id nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    position_n_id nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    position_id uniqueidentifier NOT NULL,
    position_is_deleted bit NOT NULL,
    is_primary bit NOT NULL,
    effective_from datetimeoffset(7) NOT NULL,
    effective_to datetimeoffset(7) NULL,
    state int NOT NULL,
    cancelled_on datetimeoffset(7) NULL,
    cancel_reason nvarchar(max) COLLATE Latin1_General_100_BIN2 NULL,
    CONSTRAINT uq_user_assignment_id_isdel UNIQUE (id, is_deleted),
    FOREIGN KEY (position_id, position_is_deleted)
    REFERENCES system_data_position (id, is_deleted) ON UPDATE CASCADE
);
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.system_data_user_assignment') AND name=N'ux_user_assignment_active_nid')
CREATE UNIQUE INDEX ux_user_assignment_active_nid ON dbo.system_data_user_assignment (tenant_n_id, normalized_n_id) WHERE is_deleted = 0;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.system_data_user_assignment') AND name=N'ix_user_assignment_user_time')
CREATE INDEX ix_user_assignment_user_time ON dbo.system_data_user_assignment (tenant_n_id, user_n_id, effective_from);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.system_data_user_assignment') AND name=N'ix_user_assignment_position_time')
CREATE INDEX ix_user_assignment_position_time ON dbo.system_data_user_assignment (position_n_id, effective_from);
