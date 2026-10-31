using System.Data.Common;
using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using IndustrialPlatform.Infrastructure.Database;
using IndustrialPlatform.PlatformStudio.Domain;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Npgsql;
using SqlSugar;
using Microsoft.Extensions.Options;

namespace IndustrialPlatform.PlatformStudio.Infrastructure.Persistence;

/// <summary>Studio唯一已解析持久目标；初始化、查询和Verify共用同一Scope及schema。</summary>
public sealed class StudioDatabase : IDisposable
{
    private readonly SqlSugarScope _scope;
    private readonly SemaphoreSlim _targetGate = new(1, 1);
    private bool _targetChecked;
    private readonly bool _native;
    private readonly bool _schemaExplicit;
    private static readonly Dictionary<Type, string> Tables = new Dictionary<Type, string>
    {
        [typeof(StudioReportRun)] = "studio_report_run",
        [typeof(StudioReportArtifact)] = "studio_report_artifact",
        [typeof(StudioWorkbook)] = "studio_workbook", [typeof(StudioWorkbookVersion)] = "studio_workbook_version", [typeof(StudioWorkbookSession)] = "studio_workbook_session",
        [typeof(StudioWorkbookRetiredFile)] = "studio_workbook_retired_file",
        [typeof(StudioScreenLink)] = "studio_dashboard_screen",
        [typeof(StudioSource)] = "studio_source", [typeof(StudioResource)] = "studio_resource", [typeof(StudioRevision)] = "studio_revision",
        [typeof(StudioGrant)] = "studio_grant", [typeof(StudioLedgerRecord)] = "studio_ledger_record", [typeof(StudioLedgerValue)] = "studio_ledger_value",
        [typeof(StudioOperationReceipt)] = "studio_operation_receipt", [typeof(StudioAuditOutbox)] = "studio_audit_outbox",
        [typeof(StudioAuthRequest)] = "studio_auth_request", [typeof(StudioAuthSession)] = "studio_auth_session", [typeof(StudioLocalAdmin)] = "studio_local_admin",
    };
    public DbType Provider { get; }
    public string Schema { get; private set; }
    public string TargetIdentity => StudioJson.Hash(Provider + "|" + PhysicalIdentity(ConnectionString, Provider) + "|" + Schema);
    public string ConnectionString { get; }
    public ISqlSugarClient Client => _scope;

    public StudioDatabase(StudioDeploymentProfile profile, IConfiguration configuration, IOptions<SqlSugarOptions>? nativeOptions = null)
    {
        _native = profile.DeploymentMode == "PlatformNative";
        if (_native)
        {
            var options = nativeOptions?.Value ?? throw new StudioException("STUDIO_NOT_READY", "平台数据库尚未注册。", 503);
            Provider = options.DbType;
            ConnectionString = options.ConnectionString ?? throw new StudioException("STUDIO_NOT_READY", "平台数据库未配置。", 503);
            Schema = Provider == DbType.PostgreSQL ? configuration["DatabaseTopology:SharedDatabaseSchema"] ?? "public" : Provider == DbType.SqlServer ? "dbo" : "main";
            _schemaExplicit = !string.IsNullOrWhiteSpace(configuration["DatabaseTopology:SharedDatabaseSchema"]);
        }
        else
        {
            var options = profile.Database ?? throw new StudioException("STUDIO_VALIDATION", "独立Studio必须指定唯一数据库输入。");
            options.Validate();
            Provider = options.Provider switch { "PostgreSQL" => DbType.PostgreSQL, "SqlServer" => DbType.SqlServer, "Sqlite" => DbType.Sqlite, _ => throw new StudioException("STUDIO_VALIDATION", "数据库Provider未准入。") };
            Schema = Provider == DbType.PostgreSQL ? options.Schema ?? "platform_studio" : Provider == DbType.SqlServer ? "dbo" : "main";
            _schemaExplicit = true;
            if (Provider == DbType.Sqlite)
            {
                var file = Path.GetFullPath(options.SqliteFile!);
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                ConnectionString = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder { DataSource = file, ForeignKeys = true }.ConnectionString;
            }
            else
            {
                ConnectionString = configuration[options.ConnectionStringRef!] ?? throw new StudioException("STUDIO_NOT_READY", "数据库连接秘密引用无法解析。", 503);
                if (Provider == DbType.PostgreSQL)
                {
                    var builder = new NpgsqlConnectionStringBuilder(ConnectionString) { SearchPath = Schema };
                    if (string.IsNullOrWhiteSpace(builder.Database)) throw new StudioException("STUDIO_VALIDATION", "PG连接必须包含实际Database。");
                    ConnectionString = builder.ConnectionString;
                }
                else if (string.IsNullOrWhiteSpace(new SqlConnectionStringBuilder(ConnectionString).InitialCatalog)) throw new StudioException("STUDIO_VALIDATION", "SQL Server连接必须包含Initial Catalog。");
            }
            var conflicting = configuration["SqlSugar:ConnectionString"];
            if (!string.IsNullOrWhiteSpace(conflicting) && !SamePhysicalTarget(conflicting, ConnectionString, Provider)) throw new StudioException("STUDIO_VALIDATION", "独立数据库入口与宿主原始连接配置冲突。", path: "database");
        }
        if (Provider is not (DbType.PostgreSQL or DbType.SqlServer or DbType.Sqlite)) throw new StudioException("STUDIO_VALIDATION", "未支持的Studio持久Provider。");
        _ = StudioSchema.Quote(Schema, Provider);
        var nullability = new NullabilityInfoContext();
        _scope = new SqlSugarScope(new ConnectionConfig
        {
            DbType = Provider, ConnectionString = ConnectionString, IsAutoCloseConnection = true,
            ConfigureExternalServices = new ConfigureExternalServices
            {
                EntityNameService = (type, entity) => { if (Tables.TryGetValue(type, out var name)) entity.DbTableName = TableName(name); },
                EntityService = (property, column) =>
                {
                    if (!Tables.ContainsKey(property.ReflectedType ?? property.DeclaringType!)) return;
                    column.DbColumnName = Snake(property.Name);
                    column.IsPrimarykey = property.Name == "Id" || property.DeclaringType == typeof(StudioLedgerValue) && property.Name is "RecordId" or "FieldId";
                    column.IsNullable = Nullable.GetUnderlyingType(property.PropertyType) is not null || !property.PropertyType.IsValueType && nullability.Create(property).ReadState == NullabilityState.Nullable;
                },
            },
        }, client => client.Ado.CommandTimeOut = 30);
    }

    public string TableName(string name) => Schema + "." + name;
    public string Table(string name) => StudioSchema.Quote(Schema, Provider) + "." + StudioSchema.Quote(name, Provider);

    public async Task EnsureTargetAsync(CancellationToken cancellationToken)
    {
        if (_targetChecked) return;
        await _targetGate.WaitAsync(cancellationToken);
        try
        {
            if (_targetChecked) return;
            if (Provider == DbType.PostgreSQL)
            {
                if (_native)
                {
                    var actual = await Client.Ado.GetStringAsync("SELECT current_schema()");
                    if (string.IsNullOrWhiteSpace(actual) || _schemaExplicit && !string.Equals(actual, Schema, StringComparison.Ordinal)) throw new StudioException("STUDIO_NOT_READY", "实际PG业务schema与拓扑不一致，拒绝初始化。", 503);
                    Schema = actual;
                    _ = StudioSchema.Quote(Schema, Provider);
                }
                // 仅解析目标；创建schema由受控Apply执行，Inspect保持只读。
            }
            else if (Provider == DbType.Sqlite) await Client.Ado.ExecuteCommandAsync("PRAGMA foreign_keys=ON");
            _targetChecked = true;
        }
        finally { _targetGate.Release(); }
    }

    public async Task<bool> TableExistsAsync(string name, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var query = Provider switch
        {
            DbType.Sqlite => "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=@name",
            DbType.PostgreSQL => "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema=@schema AND table_name=@name",
            _ => "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema=@schema AND table_name=@name",
        };
        return await Client.Ado.GetIntAsync(query, new { name, schema = Schema }, cancellationToken) > 0;
    }

    public static string Snake(string name) => Regex.Replace(Regex.Replace(name, "([A-Z]+)([A-Z][a-z])", "$1_$2", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)), "([a-z0-9])([A-Z])", "$1_$2", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)).ToLowerInvariant();
    private static bool SamePhysicalTarget(string left, string right, DbType provider) => string.Equals(PhysicalIdentity(left, provider), PhysicalIdentity(right, provider), StringComparison.Ordinal);
    private static string PhysicalIdentity(string connection, DbType provider)
    {
        if (provider == DbType.PostgreSQL) { var builder = new NpgsqlConnectionStringBuilder(connection); return builder.Host + "|" + builder.Port.ToString(CultureInfo.InvariantCulture) + "|" + builder.Database; }
        if (provider == DbType.SqlServer) { var builder = new SqlConnectionStringBuilder(connection); return builder.DataSource + "|" + builder.InitialCatalog; }
        var sqlite = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder(connection);
        return Path.GetFullPath(sqlite.DataSource);
    }

    public bool MatchesTarget(IndustrialPlatform.SharedKernel.Topology.ResolvedDatabaseTarget target)
    {
        var provider = target.Provider switch { IndustrialPlatform.SharedKernel.Topology.DatabaseProvider.PostgreSQL => DbType.PostgreSQL, IndustrialPlatform.SharedKernel.Topology.DatabaseProvider.SqlServer => DbType.SqlServer, IndustrialPlatform.SharedKernel.Topology.DatabaseProvider.Sqlite => DbType.Sqlite, _ => (DbType)(-1) };
        if (provider != Provider || target.IsSharedPhysicalDatabase != (target.Mode == IndustrialPlatform.SharedKernel.Topology.DatabaseTopologyMode.Shared) || !_native && !target.IsStandalone) return false;
        var actual = Provider == DbType.PostgreSQL ? new NpgsqlConnectionStringBuilder(ConnectionString).Database : Provider == DbType.SqlServer ? new SqlConnectionStringBuilder(ConnectionString).InitialCatalog : new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder(ConnectionString).DataSource;
        if (Provider == DbType.Sqlite) return string.Equals(Path.GetFullPath(actual!), Path.GetFullPath(target.PhysicalDatabaseName), StringComparison.OrdinalIgnoreCase) && target.Schema is null or "main";
        return string.Equals(actual, target.PhysicalDatabaseName, StringComparison.Ordinal) && (target.Schema is null || string.Equals(target.Schema, Schema, StringComparison.Ordinal));
    }

    public void Dispose() { _scope.Dispose(); _targetGate.Dispose(); }
}
