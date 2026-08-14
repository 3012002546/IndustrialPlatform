namespace IndustrialPlatform.SystemData.Application.DatabaseOrchestration.Options;

/// <summary>
/// 数据库编排选项(05 方案 §7.1.4/§8.1)。配置节 <c>DatabaseOrchestration</c>。
/// 无策略记录时由 Application 按环境回退默认值(Development/Test 自动、Staging 无硬门禁、Production 审批+备份强制)。
/// </summary>
public sealed class DatabaseOrchestrationOptions
{
    /// <summary>配置节名。</summary>
    public const string SectionName = "DatabaseOrchestration";

    /// <summary>计划默认有效期(秒)。</summary>
    public int PlanTtlSeconds { get; set; } = 1800;

    /// <summary>plan 类操作超时(秒)。</summary>
    public int PlanTimeoutSeconds { get; set; } = 120;

    /// <summary>apply 类操作超时(秒)。</summary>
    public int ApplyTimeoutSeconds { get; set; } = 1800;

    /// <summary>Runner 租约时长(秒)。</summary>
    public int LeaseSeconds { get; set; } = 60;

    /// <summary>Runner 心跳间隔(秒)。</summary>
    public int HeartbeatSeconds { get; set; } = 15;

    /// <summary>Runner 轮询间隔(秒)。</summary>
    public int PollSeconds { get; set; } = 2;

    /// <summary>预迁移阶段最大重试次数。</summary>
    public int MaxPreMigrationRetries { get; set; } = 3;

    /// <summary>无鉴权上下文的缺省租户业务标识(开发基线)。</summary>
    public string DefaultTenantNId { get; set; } = "development";

    /// <summary>备份证据缺省保留天数。</summary>
    public int DefaultBackupRetentionDays { get; set; } = 7;
}
