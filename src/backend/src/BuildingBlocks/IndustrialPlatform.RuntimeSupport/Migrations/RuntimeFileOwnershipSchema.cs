using SqlSugar;

namespace IndustrialPlatform.RuntimeSupport.Migrations;

/// <summary>新增文件归属迁移。原平台已应用DDL不变，Embedded首次清单明确包含上下文及owned表。</summary>
public static class RuntimeFileOwnershipSchema
{
    public static string UploadContextColumn(DbType provider) => provider switch
    {
        DbType.SqlServer => "IF COL_LENGTH(N'dbo.system_file_upload_session',N'owner_context_json') IS NULL ALTER TABLE dbo.system_file_upload_session ADD owner_context_json nvarchar(max) NULL;",
        DbType.PostgreSQL => "ALTER TABLE system_file_upload_session ADD COLUMN IF NOT EXISTS owner_context_json text NULL;",
        DbType.Sqlite => "ALTER TABLE system_file_upload_session ADD COLUMN owner_context_json TEXT NULL;",
        _ => throw new NotSupportedException(),
    };

    public static string OwnedTable(DbType provider)
    {
        var key128 = provider == DbType.SqlServer ? "nvarchar(128) COLLATE Latin1_General_100_BIN2" : "TEXT";
        var key64 = provider == DbType.SqlServer ? "nvarchar(64) COLLATE Latin1_General_100_BIN2" : "TEXT";
        var key32 = provider == DbType.SqlServer ? "nvarchar(32) COLLATE Latin1_General_100_BIN2" : "TEXT";
        var key16 = provider == DbType.SqlServer ? "nvarchar(16) COLLATE Latin1_General_100_BIN2" : "TEXT";
        var text512 = provider == DbType.SqlServer ? "nvarchar(512)" : "TEXT";
        var time = provider == DbType.PostgreSQL ? "timestamptz" : provider == DbType.SqlServer ? "datetimeoffset(7)" : "TEXT";
        var flag = provider == DbType.PostgreSQL ? "BOOLEAN" : provider == DbType.SqlServer ? "bit" : "INTEGER";
        var big = provider == DbType.Sqlite ? "INTEGER" : "BIGINT";
        var prefix = provider == DbType.SqlServer ? "IF OBJECT_ID(N'dbo.file_owned_reference',N'U') IS NULL BEGIN CREATE TABLE dbo.file_owned_reference" : "CREATE TABLE IF NOT EXISTS file_owned_reference";
        var ddl = $"""
            {prefix} (
              tenant_n_id {key128} NOT NULL,
              reference_n_id {key128} NOT NULL,
              file_n_id {key128} NULL,
              owner_service {key64} NOT NULL,
              owner_kind {key32} NOT NULL,
              owner_n_id {key128} NOT NULL,
              operation_key {key128} NOT NULL,
              status {key16} NOT NULL,
              version {big} NOT NULL,
              created_on {time} NOT NULL,
              expires_on {time} NULL,
              released_on {time} NULL,
              retention_until {time} NULL,
              legal_hold {flag} NOT NULL,
              hold_reason {text512} NULL,
              last_updated_on {time} NOT NULL,
              PRIMARY KEY (tenant_n_id,reference_n_id),
              UNIQUE (tenant_n_id,owner_service,operation_key),
              CHECK (status IN ('Pending','Active','Released') AND version>=0)
            );
            """;
        if (provider == DbType.SqlServer) ddl += " END; IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.file_owned_reference') AND name=N'ix_owned_file_status') CREATE INDEX ix_owned_file_status ON dbo.file_owned_reference (tenant_n_id,file_n_id,status); IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.file_owned_reference') AND name=N'ix_owned_owner') CREATE INDEX ix_owned_owner ON dbo.file_owned_reference (tenant_n_id,owner_service,owner_kind,owner_n_id,reference_n_id);";
        else ddl += " CREATE INDEX IF NOT EXISTS ix_owned_file_status ON file_owned_reference (tenant_n_id,file_n_id,status); CREATE INDEX IF NOT EXISTS ix_owned_owner ON file_owned_reference (tenant_n_id,owner_service,owner_kind,owner_n_id,reference_n_id);";
        return ddl;
    }
}
