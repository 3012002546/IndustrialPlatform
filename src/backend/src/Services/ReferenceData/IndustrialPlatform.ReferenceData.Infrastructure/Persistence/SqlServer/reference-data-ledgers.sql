-- SQL Server 2019/2022：ReferenceData 服务自有迁移账本与种子账本。
IF OBJECT_ID(N'dbo.reference_data_schema_migrations',N'U') IS NULL
CREATE TABLE dbo.reference_data_schema_migrations (
    migration_id nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL PRIMARY KEY,
    checksum varchar(64) NULL,
    target_identity varchar(64) NULL,
    applied_on datetimeoffset(7) NOT NULL
);
IF OBJECT_ID(N'dbo.reference_data_seed_ledger',N'U') IS NULL
CREATE TABLE dbo.reference_data_seed_ledger (
    seed_key nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    seed_version nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    checksum varchar(64) NOT NULL,
    scope nvarchar(32) COLLATE Latin1_General_100_BIN2 NULL,
    applied_on datetimeoffset(7) NOT NULL,
    operation_n_id nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    trace_id nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    PRIMARY KEY(seed_key,seed_version)
);
