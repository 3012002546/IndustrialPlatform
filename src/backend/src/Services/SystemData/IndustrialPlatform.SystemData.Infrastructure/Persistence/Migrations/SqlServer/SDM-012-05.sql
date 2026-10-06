-- SQL Server 2019/2022；新增 Provider 的独立脚本，应用后只能追加迁移。
-- system_data_projection_revision tenant area unique index
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.system_data_projection_revision') AND name=N'ux_system_data_projection_revision_tenant_area')
CREATE UNIQUE INDEX ux_system_data_projection_revision_tenant_area ON dbo.system_data_projection_revision (tenant_n_id, area);
