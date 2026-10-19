-- SQL Server 2019/2022；新增 Provider 的独立脚本，应用后只能追加迁移。
-- create system_data_database_operation_step
IF OBJECT_ID(N'dbo.system_data_database_operation_step',N'U') IS NULL
BEGIN
CREATE TABLE dbo.system_data_database_operation_step (
    id uniqueidentifier PRIMARY KEY NOT NULL,
    is_frozen bit NOT NULL,
    is_locked bit NOT NULL,
    is_deleted bit NOT NULL,
    entity_type nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    created_on datetimeoffset(7) NOT NULL,
    last_updated_on datetimeoffset(7) NOT NULL,
    optimistic_version bigint NOT NULL,
    concurrency_version uniqueidentifier NOT NULL,
    operation_id uniqueidentifier NOT NULL,
    operation_is_deleted bit NOT NULL,
    sequence int NOT NULL,
    phase int NOT NULL,
    attempt int NOT NULL,
    status int NOT NULL,
    started_on datetimeoffset(7) NULL,
    completed_on datetimeoffset(7) NULL,
    sanitized_error_code nvarchar(max) COLLATE Latin1_General_100_BIN2 NULL,
    sanitized_error_summary nvarchar(max) COLLATE Latin1_General_100_BIN2 NULL,
    FOREIGN KEY (operation_id, operation_is_deleted) REFERENCES system_data_database_operation (id, is_deleted) ON UPDATE CASCADE
);
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.system_data_database_operation_step') AND name=N'ix_operation_step_parent')
CREATE INDEX ix_operation_step_parent ON dbo.system_data_database_operation_step (operation_id, sequence);
