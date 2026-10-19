-- SQL Server 2019/2022；新增 Provider 的独立脚本，应用后只能追加迁移。
-- create system_data_database_plan_step
IF OBJECT_ID(N'dbo.system_data_database_plan_step',N'U') IS NULL
BEGIN
CREATE TABLE dbo.system_data_database_plan_step (
    id uniqueidentifier PRIMARY KEY NOT NULL,
    is_frozen bit NOT NULL,
    is_locked bit NOT NULL,
    is_deleted bit NOT NULL,
    entity_type nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    created_on datetimeoffset(7) NOT NULL,
    last_updated_on datetimeoffset(7) NOT NULL,
    optimistic_version bigint NOT NULL,
    concurrency_version uniqueidentifier NOT NULL,
    plan_id uniqueidentifier NOT NULL,
    plan_is_deleted bit NOT NULL,
    sequence int NOT NULL,
    step_kind nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    input_summary nvarchar(max) COLLATE Latin1_General_100_BIN2 NULL,
    precondition_summary nvarchar(max) COLLATE Latin1_General_100_BIN2 NULL,
    postcondition_summary nvarchar(max) COLLATE Latin1_General_100_BIN2 NULL,
    risk_level int NOT NULL,
    FOREIGN KEY (plan_id, plan_is_deleted) REFERENCES system_data_database_plan (id, is_deleted) ON UPDATE CASCADE
);
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.system_data_database_plan_step') AND name=N'ix_plan_step_parent')
CREATE INDEX ix_plan_step_parent ON dbo.system_data_database_plan_step (plan_id, sequence);
