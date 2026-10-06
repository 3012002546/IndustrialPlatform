-- SQL Server 2019/2022；新增 Provider 的独立脚本，应用后只能追加迁移。
-- create system_data_operation_audit
IF OBJECT_ID(N'dbo.system_data_operation_audit',N'U') IS NULL
BEGIN
CREATE TABLE dbo.system_data_operation_audit (
    id uniqueidentifier PRIMARY KEY NOT NULL,
    is_frozen bit NOT NULL,
    is_locked bit NOT NULL,
    is_deleted bit NOT NULL,
    entity_type nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    created_on datetimeoffset(7) NOT NULL,
    last_updated_on datetimeoffset(7) NOT NULL,
    optimistic_version bigint NOT NULL,
    concurrency_version uniqueidentifier NOT NULL,
    tenant_n_id nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    actor_user_n_id nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    action nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    object_type nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    object_n_id nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    reason nvarchar(max) COLLATE Latin1_General_100_BIN2 NULL,
    before_summary nvarchar(max) COLLATE Latin1_General_100_BIN2 NULL,
    after_summary nvarchar(max) COLLATE Latin1_General_100_BIN2 NULL,
    trace_id nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL
);
END;
