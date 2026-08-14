using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using IndustrialPlatform.SystemData.Application.DatabaseOrchestration.Options;
using IndustrialPlatform.SystemData.Application.DatabaseOrchestration.Runner;
using IndustrialPlatform.SystemData.Domain.Topology;
using Microsoft.Extensions.Options;

namespace IndustrialPlatform.SystemData.Infrastructure.DatabaseOrchestration.Runner;

/// <summary>
/// 目标 Secret Sink 文件实现(05 方案 §7.1.5 本地替身):新生成的 migrator/runtime 凭据写入
/// <see cref="DatabaseOperationRunnerOptions.SecretSinkPath"/> 目录,文件按目标身份 SHA-256 命名,
/// 权限限定当前用户;云 Secret Provider 接入留后续,控制面 API 只回读引用不读值。
/// </summary>
public sealed class FileCredentialSink : IDatabaseCredentialSink
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    private readonly IOptions<DatabaseOperationRunnerOptions> _options;

    /// <summary>初始化文件凭据落盘器。</summary>
    public FileCredentialSink(IOptions<DatabaseOperationRunnerOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    /// <inheritdoc />
    public async Task WriteAsync(
        ResolvedDatabaseTarget target,
        ProvisionedRoles roles,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(roles);

        var root = _options.Value.SecretSinkPath;
        if (string.IsNullOrWhiteSpace(root))
        {
            // 未配置 Secret Sink(本地 SQLite 开发基线),跳过落盘;生产必须配置。
            return;
        }

        var payload = new CredentialSinkPayload(
            target.Provider.ToString(),
            roles.Migrator.Host,
            roles.Migrator.Port,
            roles.Migrator.Database,
            roles.Migrator.Schema,
            roles.Migrator.Username,
            roles.Migrator.Password,
            roles.Runtime.Username,
            roles.Runtime.Password);

        var directory = Directory.CreateDirectory(root);
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // 尽力限制目录权限为当前用户;Windows 默认继承,交由部署侧 ACL。
            File.SetUnixFileMode(directory.FullName, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        var fileName = $"credentials-{DatabaseTopologyFingerprint.Sha256Hex(ToCanonical(target))}.json";
        var path = Path.Combine(directory.FullName, fileName);
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        await File.WriteAllTextAsync(path, json, Encoding.UTF8, cancellationToken);
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    private static string ToCanonical(ResolvedDatabaseTarget target) =>
        $"{target.EnvironmentName}|{target.ServiceKey}|{target.Provider}|{target.PhysicalDatabaseName}";

    /// <summary>凭据落盘载荷(仅写文件,不进入任何日志/事件)。</summary>
    private sealed record CredentialSinkPayload(
        string Provider,
        string MigratorHost,
        int MigratorPort,
        string MigratorDatabase,
        string? MigratorSchema,
        string? MigratorUsername,
        string? MigratorPassword,
        string? RuntimeUsername,
        string? RuntimePassword);
}
