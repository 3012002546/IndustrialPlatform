namespace IndustrialPlatform.SystemData.Application.DatabaseOrchestration.Runner;

/// <summary>
/// Runner 稳定错误码(05 方案 §9.9 扩充)。复用既有控制面错误码,新增 runner 特有码;
/// 所有错误摘要经脱敏,不泄漏数据库地址、密码、Token、SecretRef、SQL 或原始迁移输出。
/// </summary>
public static class DatabaseOrchestrationRunnerErrors
{
    // ===== 复用既有 §9.9 错误码 =====

    /// <summary>迁移产物不在允许列表或 checksum/签名无效。</summary>
    public const string ArtifactInvalid = "SD_DB_ARTIFACT_INVALID";

    /// <summary>目标状态或输入在计划后漂移。</summary>
    public const string PlanDrift = "SD_DB_PLAN_DRIFT";

    /// <summary>计划已过期。</summary>
    public const string PlanExpired = "SD_DB_PLAN_EXPIRED";

    /// <summary>缺少匹配的生产审批。</summary>
    public const string ApprovalRequired = "SD_DB_APPROVAL_REQUIRED";

    /// <summary>缺少匹配且已验证的备份证据。</summary>
    public const string BackupRequired = "SD_DB_BACKUP_REQUIRED";

    /// <summary>数据库身份或当前版本与注册不符。</summary>
    public const string TargetMismatch = "SD_DB_TARGET_MISMATCH";

    /// <summary>同目标操作冲突或锁超时。</summary>
    public const string OperationConflict = "SD_DB_OPERATION_CONFLICT";

    /// <summary>必需 Secret 无法安全解析。</summary>
    public const string SecretUnavailable = "SD_DB_SECRET_UNAVAILABLE";

    /// <summary>迁移失败且未证明可安全恢复。</summary>
    public const string MigrationFailed = "SD_DB_MIGRATION_FAILED";

    /// <summary>注册清单不存在。</summary>
    public const string RegistrationNotFound = "SD_DB_REGISTRATION_NOT_FOUND";

    /// <summary>提供程序不受支持。</summary>
    public const string ProviderUnsupported = "SD_DB_PROVIDER_UNSUPPORTED";

    // ===== Runner 特有错误码(新增,脱敏) =====

    /// <summary>目标数据库缺失但清单未允许自动 provision。</summary>
    public const string ProvisionRequired = "SD_DB_PROVISION_REQUIRED";

    /// <summary>数据库/角色 provision 失败。</summary>
    public const string ProvisionFailed = "SD_DB_PROVISION_FAILED";

    /// <summary>目标检查(inspect)失败。</summary>
    public const string TargetInspectionFailed = "SD_DB_TARGET_INSPECTION_FAILED";

    /// <summary>迁移前阶段瞬时失败,达到最大重试次数。</summary>
    public const string RetryExhausted = "SD_DB_RETRY_EXHAUSTED";

    /// <summary>内部未知失败(不泄漏异常细节)。</summary>
    public const string InternalFailure = "SD_DB_INTERNAL_FAILURE";
}
