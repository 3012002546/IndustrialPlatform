using System.Text;
using System.Text.Json;
using IndustrialPlatform.SystemData.Application.DatabaseOrchestration;
using IndustrialPlatform.SystemData.Application.DatabaseOrchestration.Options;
using IndustrialPlatform.SystemData.Application.DatabaseOrchestration.Runner;
using Microsoft.Extensions.Options;

namespace IndustrialPlatform.SystemData.Infrastructure.DatabaseOrchestration.Runner;

/// <summary>
/// 迁移产物文件系统实现(05 方案 §7.1.5 本地替身):从 <see cref="DatabaseOperationRunnerOptions.ArtifactRootPath"/>
/// 目录按不可变标识读取 <c>&lt;artifactId&gt;.json</c>。产物含完整 SQL,只经签名产物校验后执行;
/// 云 Artifact Registry 接入留后续。文件不存在/不可读/反序列化失败映射 SD_DB_ARTIFACT_INVALID。
/// </summary>
public sealed class FileSystemArtifactStore : IMigrationArtifactStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly IOptions<DatabaseOperationRunnerOptions> _options;

    /// <summary>初始化产物文件存储。</summary>
    public FileSystemArtifactStore(IOptions<DatabaseOperationRunnerOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    /// <inheritdoc />
    public async Task<DatabaseMigrationArtifact> ResolveAsync(string artifactId, CancellationToken cancellationToken)
    {
        var root = _options.Value.ArtifactRootPath;
        if (string.IsNullOrWhiteSpace(root))
        {
            throw new DatabaseOrchestrationRunnerException(500, DatabaseOrchestrationRunnerErrors.ArtifactInvalid, "未配置迁移产物根目录。");
        }

        var path = Path.Combine(root, $"{SanitizeFileName(artifactId)}.json");
        if (!File.Exists(path))
        {
            throw new DatabaseOrchestrationRunnerException(404, DatabaseOrchestrationRunnerErrors.ArtifactInvalid, "迁移产物不存在。");
        }

        try
        {
            var json = await File.ReadAllTextAsync(path, Encoding.UTF8, cancellationToken);
            var artifact = JsonSerializer.Deserialize<DatabaseMigrationArtifact>(json, JsonOptions);
            if (artifact is null || string.IsNullOrWhiteSpace(artifact.ArtifactId))
            {
                throw new DatabaseOrchestrationRunnerException(500, DatabaseOrchestrationRunnerErrors.ArtifactInvalid, "迁移产物内容无效。");
            }

            return artifact;
        }
        catch (JsonException)
        {
            throw new DatabaseOrchestrationRunnerException(500, DatabaseOrchestrationRunnerErrors.ArtifactInvalid, "迁移产物格式无效。");
        }
    }

    /// <summary>清理文件名中平台非法字符,仅保留字母数字与常见安全字符。</summary>
    private static string SanitizeFileName(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            builder.Append(char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.' ? ch : '_');
        }

        return builder.ToString();
    }
}
