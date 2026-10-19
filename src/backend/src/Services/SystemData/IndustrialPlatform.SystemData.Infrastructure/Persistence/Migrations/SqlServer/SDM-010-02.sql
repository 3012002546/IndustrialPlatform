-- SQL Server 2019/2022；新增 Provider 的独立脚本，应用后只能追加迁移。
-- system_data_service_catalog owner snapshots
IF COL_LENGTH(N'dbo.system_data_service_catalog',N'owner_organization_n_id') IS NULL
ALTER TABLE dbo.system_data_service_catalog ADD owner_organization_n_id nvarchar(max) COLLATE Latin1_General_100_BIN2 NULL;
GO

IF COL_LENGTH(N'dbo.system_data_service_catalog',N'owner_organization_name_snapshot') IS NULL
ALTER TABLE dbo.system_data_service_catalog ADD owner_organization_name_snapshot nvarchar(max) COLLATE Latin1_General_100_BIN2 NULL;
GO

IF COL_LENGTH(N'dbo.system_data_service_catalog',N'owner_display_snapshot') IS NULL
ALTER TABLE dbo.system_data_service_catalog ADD owner_display_snapshot nvarchar(max) COLLATE Latin1_General_100_BIN2 NULL;
GO

IF COL_LENGTH(N'dbo.system_data_service_catalog',N'technical_lead_user_n_id') IS NULL
ALTER TABLE dbo.system_data_service_catalog ADD technical_lead_user_n_id nvarchar(max) COLLATE Latin1_General_100_BIN2 NULL;
GO

IF COL_LENGTH(N'dbo.system_data_service_catalog',N'technical_owner_user_n_id') IS NULL
ALTER TABLE dbo.system_data_service_catalog ADD technical_owner_user_n_id nvarchar(max) COLLATE Latin1_General_100_BIN2 NULL;
GO

IF COL_LENGTH(N'dbo.system_data_service_catalog',N'technical_owner_auth_version') IS NULL
ALTER TABLE dbo.system_data_service_catalog ADD technical_owner_auth_version nvarchar(max) COLLATE Latin1_General_100_BIN2 NULL;
GO

IF COL_LENGTH(N'dbo.system_data_service_catalog',N'technical_lead_display_name_snapshot') IS NULL
ALTER TABLE dbo.system_data_service_catalog ADD technical_lead_display_name_snapshot nvarchar(max) COLLATE Latin1_General_100_BIN2 NULL;
GO
