-- SQL Server 2019/2022；新增 Provider 的独立脚本，应用后只能追加迁移。
-- create system_data_module_manifest
IF OBJECT_ID(N'dbo.system_data_module_manifest',N'U') IS NULL
BEGIN
CREATE TABLE dbo.system_data_module_manifest (
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
    module_n_id nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    manifest_version nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    checksum nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    permission_n_ids_json nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    permission_receipt_version nvarchar(max) COLLATE Latin1_General_100_BIN2 NULL,
    permission_receipt_checksum nvarchar(max) COLLATE Latin1_General_100_BIN2 NULL,
    permission_verified_on datetimeoffset(7) NULL
);
END;
