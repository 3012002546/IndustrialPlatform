namespace IndustrialPlatform.PlatformStudio.Domain;

/// <summary>部署输入只来自受控运维配置；产品装配与身份模式分别校验。</summary>
public sealed class StudioDeploymentProfile
{
    public bool Enabled { get; set; }
    public string DeploymentMode { get; set; } = "PlatformNative";
    public string[] InstalledModules { get; set; } = [];
    public string[]? EnabledModules { get; set; }
    public string ProfileVersion { get; set; } = "1";
    public StudioIdentityOptions Identity { get; set; } = new();
    public StudioFilesOptions Files { get; set; } = new();
    public StudioAdapterOptions Audit { get; set; } = new() { Provider = "Platform" };
    public StudioAdapterOptions Jobs { get; set; } = new() { Provider = "Disabled" };
    public StudioDatabaseOptions? Database { get; set; }
    public string PublicOrigin { get; set; } = "";
    public string PathBase { get; set; } = "";
    public string[] AllowedFrameOrigins { get; set; } = [];
    public int DefaultQueryTimeoutSeconds { get; set; } = 30;
    public int MaxQueryConcurrencyPerTenant { get; set; } = 8;
    public Dictionary<string, StudioSourceEndpoint> SourceRegistry { get; set; } = new(StringComparer.Ordinal);
    public string[] ActiveModules => EnabledModules ?? InstalledModules;

    public void Validate()
    {
        if (!Enabled) return;
        _ = StudioJson.Version(ProfileVersion, "profileVersion");
        _ = StudioJson.Version(Identity.PolicyVersion, "identity.policyVersion");
        if (string.IsNullOrWhiteSpace(Identity.IssuerKey) || Identity.IssuerKey.Length > 64) Fail("identity.issuerKey");
        if (Identity.TenantMappings.GroupBy(m=>m.ExternalTenantId,StringComparer.Ordinal).Any(g=>g.Count()>1) || Identity.RoleMappings.GroupBy(m=>(m.ExternalTenantId,m.ExternalRoleId)).Any(g=>g.Count()>1) || Identity.FactoryMappings.GroupBy(m=>(m.ExternalTenantId,m.ExternalFactoryId)).Any(g=>g.Count()>1)) Fail("identity.mappings");
        foreach (var mapping in Identity.RoleMappings)
            if (mapping.RoleNId != StudioPrincipal.ExternalNId(Identity.IssuerKey,mapping.ExternalTenantId,mapping.ExternalRoleId,"mes-role-") || mapping.Permissions.Any(p=>string.IsNullOrWhiteSpace(p)||p.Length>128||p.Contains('*'))) Fail("identity.roleMappings");
        if (DeploymentMode is not ("PlatformNative" or "ExternalMes")) Fail("deploymentMode");
        var allowed = new[] { "LowCode", "Dashboard", "Report", "Traceability" };
        if (InstalledModules.Length == 0 || InstalledModules.Distinct(StringComparer.Ordinal).Count() != InstalledModules.Length || InstalledModules.Any(value => !allowed.Contains(value, StringComparer.Ordinal))) Fail("installedModules");
        if (ActiveModules.Distinct(StringComparer.Ordinal).Count() != ActiveModules.Length || ActiveModules.Except(InstalledModules, StringComparer.Ordinal).Any()) Fail("enabledModules");
        if (DefaultQueryTimeoutSeconds is < 1 or > 120 || MaxQueryConcurrencyPerTenant is < 1 or > 64) Fail("queryBudget");
        if (DeploymentMode == "PlatformNative")
        {
            if (Identity.Provider != "Platform" || Audit.Provider != "Platform" || Files.Provider is not ("Platform" or "Disabled") || Database is not null) Fail("platformAdapters");
        }
        else
        {
            if (Identity.Provider is not ("Oidc" or "MesCode" or "LocalAdmin") || Audit.Provider != "Embedded" || Files.Provider is not ("Embedded" or "Disabled")) Fail("externalAdapters");
            if (Database is null) Fail("database");
            Database!.Validate();
            if (Identity.Provider == "LocalAdmin" && !Identity.LocalAdminEnabled || Identity.Provider != "LocalAdmin" && Identity.LocalAdminEnabled) Fail("identity.localAdminEnabled");
            if (string.IsNullOrWhiteSpace(PublicOrigin) || !Uri.TryCreate(PublicOrigin, UriKind.Absolute, out var origin) || origin.Scheme != "https") Fail("publicOrigin");
            if (Identity.Provider is "Oidc" or "MesCode" && (string.IsNullOrWhiteSpace(Identity.AuthorityKey) || string.IsNullOrWhiteSpace(Identity.ContextEndpointKey))) Fail("identity.authorityKey");
        }
        if (PathBase.Length > 128 || PathBase.Length > 0 && (!PathBase.StartsWith('/') || PathBase.EndsWith('/') || PathBase.Contains("..", StringComparison.Ordinal) || PathBase.Any(c=>!(char.IsAsciiLetterOrDigit(c)||c is '/' or '-' or '_')))) Fail("pathBase");
        var report = ActiveModules.Contains("Report", StringComparer.Ordinal);
        if (report && Jobs.Provider != (DeploymentMode == "PlatformNative" ? "Platform" : "Embedded") || !report && Jobs.Provider != "Disabled") Fail("jobs.provider");
        if (InstalledModules.Contains("Report", StringComparer.Ordinal) && Files.Provider == "Disabled") Fail("files.provider");
        if (Files.Provider == "Embedded" && (string.IsNullOrWhiteSpace(Files.Root) || !Path.IsPathFullyQualified(Files.Root))) Fail("files.root");
        if (AllowedFrameOrigins.Any(value => !Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.AbsolutePath != "/" || !string.IsNullOrEmpty(uri.Query))) Fail("allowedFrameOrigins");
    }

    public bool IsKindEnabled(string kind) => StudioKinds.Modules.TryGetValue(kind, out var module) && (module == "Core" || ActiveModules.Contains(module, StringComparer.Ordinal));
    private static void Fail(string path) => throw new StudioException("STUDIO_VALIDATION", "部署模式、模块及必需适配器配置不一致。", path: path);
}

public sealed class StudioIdentityOptions
{
    public string Provider { get; set; } = "Platform";
    public string IssuerKey { get; set; } = "platform";
    public string PolicyVersion { get; set; } = "1";
    public string? AuthorityKey { get; set; }
    public string? ClientCredentialRef { get; set; }
    public string? ContextEndpointKey { get; set; }
    public bool LocalAdminEnabled { get; set; }
    public StudioNativeFactoryScope[] NativeFactoryScopes { get; set; } = [];
    public string[] LocalAdminFactoryNIds { get; set; } = [];
    public string? DataProtectionKeyPath { get; set; }
    public Dictionary<string, StudioAuthority> Authorities { get; set; } = new(StringComparer.Ordinal);
    public StudioTenantMapping[] TenantMappings { get; set; } = [];
    public StudioRoleMapping[] RoleMappings { get; set; } = [];
    public StudioFactoryMapping[] FactoryMappings { get; set; } = [];
}
public sealed record StudioTenantMapping(string ExternalTenantId, string TenantNId);
public sealed record StudioRoleMapping(string ExternalTenantId, string ExternalRoleId, string RoleNId, string[] Permissions);
public sealed record StudioFactoryMapping(string ExternalTenantId, string ExternalFactoryId, string FactoryNId);
public class StudioAdapterOptions { public string Provider { get; set; } = "Platform"; }
public sealed class StudioFilesOptions : StudioAdapterOptions
{
    public string? Root { get; set; }
    public string? ScannerEndpointKey { get; set; }
}
public sealed class StudioDatabaseOptions
{
    public string Provider { get; set; } = "Sqlite";
    public string? ConnectionStringRef { get; set; }
    public string? SqliteFile { get; set; }
    public string? Schema { get; set; }
    public void Validate()
    {
        if (Provider == "Sqlite")
        {
            if (string.IsNullOrWhiteSpace(SqliteFile) || !Path.IsPathFullyQualified(SqliteFile) || Schema is not (null or "main")) throw new StudioException("STUDIO_VALIDATION", "SQLite必须指定绝对主文件，schema仅允许main。", path: "database");
        }
        else if (Provider is "PostgreSQL" or "SqlServer")
        {
            if (string.IsNullOrWhiteSpace(ConnectionStringRef)) throw new StudioException("STUDIO_VALIDATION", "数据库必须使用服务器连接秘密引用。", path: "database.connectionStringRef");
            if (Provider == "SqlServer" && Schema is not (null or "dbo")) throw new StudioException("STUDIO_VALIDATION", "SQL Server业务schema仅支持dbo。", path: "database.schema");
            if (Provider == "PostgreSQL" && Schema is { } schema && (!System.Text.RegularExpressions.Regex.IsMatch(schema, "^[a-z_][a-z0-9_]{0,62}$", System.Text.RegularExpressions.RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)) || schema.StartsWith("pg_", StringComparison.Ordinal))) throw new StudioException("STUDIO_VALIDATION", "PG schema必须为单一受控标识符。", path: "database.schema");
        }
        else throw new StudioException("STUDIO_VALIDATION", "不支持的数据库Provider。", path: "database.provider");
    }
}

/// <summary>受控来源登记；秘密只通过服务端配置引用解析，绝不进入SourceDto。</summary>
public sealed class StudioSourceEndpoint
{
    public string Provider { get; set; } = "RegisteredApi";
    public string? BaseAddress { get; set; }
    public string? CatalogKey { get; set; }
    public string? ProbeOperationKey { get; set; }
    public string? ConnectionStringRef { get; set; }
    public string? CredentialRef { get; set; }
    public bool ReadOnlyVerified { get; set; }
    public bool PublicQueriesAllowed { get; set; }
    public string[] RowsPath { get; set; } = [];
    public string[] FactoryFieldPath { get; set; } = [];
    public string? ClientCertificatePathRef { get; set; }
    public string? ClientCertificatePasswordRef { get; set; }
    public string[] AllowedObjects { get; set; } = [];
    public StudioCatalogDeclaration[] Catalog { get; set; } = [];
    public Dictionary<string, StudioApiOperation> Operations { get; set; } = new(StringComparer.Ordinal);
    public string[] AllowedProcedures { get; set; } = [];
    public Dictionary<string, string> ProcedureDefinitionHashes { get; set; } = new(StringComparer.Ordinal);
}
public sealed class StudioApiOperation
{
    public string Path { get; set; } = "";
    public string Method { get; set; } = "GET";
    public bool ReadOnly { get; set; } = true;
    public string[] RequiredParameters { get; set; } = [];
}

/// <summary>平台尚无业务工厂事实提供者时，由受控部署输入明确授予的范围，不接受通配主体或工厂。</summary>
public sealed record StudioNativeFactoryScope(string TenantNId,string? UserNId,string? RoleNId,string[] FactoryNIds);

/// <summary>安装端明确登记的公开结构，等同代码登记；只读元数据还会与实际物理列求交集。</summary>
public sealed record StudioCatalogDeclaration(string ObjectName,string Kind,string PermissionKey,string ResultSchemaJson,string? ParametersJson=null);
