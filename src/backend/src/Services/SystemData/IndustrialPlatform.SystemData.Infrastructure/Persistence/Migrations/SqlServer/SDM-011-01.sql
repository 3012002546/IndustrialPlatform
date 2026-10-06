-- SQL Server 2019/2022；新增 Provider 的独立脚本，应用后只能追加迁移。
-- create system_data_theme_policy
IF OBJECT_ID(N'dbo.system_data_theme_policy',N'U') IS NULL
BEGIN
CREATE TABLE dbo.system_data_theme_policy (
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
    policy_n_id nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    allowed_palettes_json nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    allowed_modes_json nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    allowed_pc_densities_json nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    default_palette nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    default_mode nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    default_pc_density nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    policy_revision bigint NOT NULL
);
END;
