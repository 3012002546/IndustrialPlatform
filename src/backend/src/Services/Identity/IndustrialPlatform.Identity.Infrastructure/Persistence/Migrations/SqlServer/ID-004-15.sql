-- SQL Server 2019/2022；新增 Provider 的独立脚本，应用后只能追加迁移。
-- create identity_sso_client_endpoint
IF OBJECT_ID(N'dbo.identity_sso_client_endpoint',N'U') IS NULL
BEGIN
CREATE TABLE dbo.identity_sso_client_endpoint (
    id uniqueidentifier PRIMARY KEY NOT NULL,
    is_frozen bit NOT NULL,
    is_locked bit NOT NULL,
    is_deleted bit NOT NULL,
    entity_type nvarchar(max) COLLATE Latin1_General_100_BIN2 NOT NULL,
    created_on datetimeoffset(7) NOT NULL,
    last_updated_on datetimeoffset(7) NOT NULL,
    optimistic_version bigint NOT NULL,
    concurrency_version uniqueidentifier NOT NULL,
    sso_client_id uniqueidentifier NOT NULL,
    sso_client_is_deleted bit NOT NULL,
    n_id nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
    endpoint_type int NOT NULL,
    uri nvarchar(2048) COLLATE Latin1_General_100_BIN2 NOT NULL,
    normalized_uri nvarchar(2048) COLLATE Latin1_General_100_BIN2 NOT NULL,
    enabled bit NOT NULL,
    FOREIGN KEY (sso_client_id, sso_client_is_deleted) REFERENCES identity_sso_client (id, is_deleted) ON UPDATE CASCADE,
    normalized_uri_hash AS CONVERT(binary(32),HASHBYTES(N'SHA2_256',normalized_uri)) PERSISTED
);
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.identity_sso_client_endpoint') AND name=N'ux_sso_client_endpoint_active_nid')
CREATE UNIQUE INDEX ux_sso_client_endpoint_active_nid ON dbo.identity_sso_client_endpoint (sso_client_id, n_id) WHERE is_deleted = 0;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.identity_sso_client_endpoint') AND name=N'ux_sso_client_endpoint_active_uri')
CREATE UNIQUE INDEX ux_sso_client_endpoint_active_uri ON dbo.identity_sso_client_endpoint (sso_client_id, endpoint_type, normalized_uri_hash) WHERE is_deleted = 0;
