namespace IndustrialPlatform.SystemData.Application.DatabaseOrchestration.Runner;

/// <summary>
/// 数据库编排 Runner 选项(05 方案 §7.1.4/§7.1.5)。配置节 <c>DatabaseOrchestration:Runner</c>。
/// 启用开关、实例标识、迁移产物与 Secret Sink 路径、provision admin 凭据环境变量前缀。
/// 时序参数(lease/heartbeat/poll/超时/重试)沿用 <see cref="Options.DatabaseOrchestrationOptions"/>。
/// </summary>
public sealed class DatabaseOperationRunnerOptions
{
    /// <summary>配置节名。</summary>
    public const string SectionName = "DatabaseOrchestration:Runner";

    /// <summary>Runner 启用开关(默认关闭,避免未配置产物/凭据时自动消费队列)。</summary>
    public bool Enabled { get; set; }

    /// <summary>Runner 实例标识(租约所有者);缺省按主机/进程/随机生成。</summary>
    public string? InstanceId { get; set; }

    /// <summary>迁移产物根目录(FileSystemArtifactStore 读取 <c>manifest.json</c> 与 SQL 文件)。</summary>
    public string? ArtifactRootPath { get; set; }

    /// <summary>Secret Sink 目录(新生成的 migrator/runtime 凭据直接写入,目录应 gitignore)。</summary>
    public string? SecretSinkPath { get; set; }

    /// <summary>provision admin 凭据环境变量前缀(如 <c>DB_PROVISION_ADMIN_HOST</c>)。</summary>
    public string ProvisionAdminEnvPrefix { get; set; } = "DB_PROVISION_ADMIN";

    /// <summary>目标 advisory lock 等待超时(秒)。</summary>
    public int AdvisoryLockTimeoutSeconds { get; set; } = 30;
}
