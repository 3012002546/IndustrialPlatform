namespace IndustrialPlatform.SystemData.Application.DatabaseOrchestration;

/// <summary>
/// 数据库编排业务异常基类:携带标准 HTTP 状态码与 05 方案 §9.9 错误码。
/// 由 Api 控制器映射为统一 ApiResult 信封;message 不得包含数据库地址、密码、
/// Token、SecretRef、SQL 或原始迁移输出。
/// </summary>
public abstract class DatabaseOrchestrationException : Exception
{
    /// <summary>标准 HTTP 状态码。</summary>
    public int StatusCode { get; }

    /// <summary>§9.9 错误码。</summary>
    public string Code { get; }

    protected DatabaseOrchestrationException(int statusCode, string code, string message)
        : base(message)
    {
        StatusCode = statusCode;
        Code = code;
    }
}

/// <summary>请求校验失败(§9.9 SD_VALIDATION_FAILED)。</summary>
public sealed class ValidationFailedException : DatabaseOrchestrationException
{
    public ValidationFailedException(string message)
        : base(400, "SD_VALIDATION_FAILED", message)
    {
    }
}

/// <summary>资源不存在(§9.9 SD_NOT_FOUND)。返回稳定 404,不泄漏资源存在性。</summary>
public sealed class NotFoundException : DatabaseOrchestrationException
{
    public NotFoundException()
        : base(404, "SD_NOT_FOUND", "资源不存在。")
    {
    }
}

/// <summary>服务数据库注册清单不存在(§9.9 SD_DB_REGISTRATION_NOT_FOUND)。</summary>
public sealed class RegistrationNotFoundException : DatabaseOrchestrationException
{
    public RegistrationNotFoundException()
        : base(404, "SD_DB_REGISTRATION_NOT_FOUND", "服务数据库注册清单不存在。")
    {
    }
}

/// <summary>数据库提供程序不受支持(§9.9 SD_DB_PROVIDER_UNSUPPORTED)。</summary>
public sealed class ProviderUnsupportedException : DatabaseOrchestrationException
{
    public ProviderUnsupportedException(string provider)
        : base(400, "SD_DB_PROVIDER_UNSUPPORTED", $"不支持的数据库提供程序:{provider}。")
    {
    }
}

/// <summary>拓扑不受支持或与请求冲突(§9.9 SD_DB_TOPOLOGY_UNSUPPORTED)。</summary>
public sealed class TopologyUnsupportedException : DatabaseOrchestrationException
{
    public TopologyUnsupportedException(string message)
        : base(400, "SD_DB_TOPOLOGY_UNSUPPORTED", message)
    {
    }
}

/// <summary>Shared 拓扑缺少目标库名(§9.9 SD_DB_SHARED_TARGET_MISSING)。</summary>
public sealed class SharedTargetMissingException : DatabaseOrchestrationException
{
    public SharedTargetMissingException(string message)
        : base(400, "SD_DB_SHARED_TARGET_MISSING", message)
    {
    }
}

/// <summary>PerService 拓扑缺少服务物理映射(§9.9 SD_DB_SERVICE_MAPPING_MISSING)。</summary>
public sealed class ServiceMappingMissingException : DatabaseOrchestrationException
{
    public ServiceMappingMissingException(string message)
        : base(400, "SD_DB_SERVICE_MAPPING_MISSING", message)
    {
    }
}

/// <summary>Shared 拓扑被用于非允许环境(§9.9 SD_DB_SHARED_ENVIRONMENT_FORBIDDEN)。</summary>
public sealed class SharedEnvironmentForbiddenException : DatabaseOrchestrationException
{
    public SharedEnvironmentForbiddenException(string message)
        : base(409, "SD_DB_SHARED_ENVIRONMENT_FORBIDDEN", message)
    {
    }
}

/// <summary>同版本下产物校验和不一致(§9.9 SD_DB_ARTIFACT_INVALID)。</summary>
public sealed class ArtifactInvalidException : DatabaseOrchestrationException
{
    public ArtifactInvalidException()
        : base(409, "SD_DB_ARTIFACT_INVALID", "同迁移版本下产物校验和不一致,拒绝重注册。")
    {
    }
}

/// <summary>计划已过期(§9.9 SD_DB_PLAN_EXPIRED)。</summary>
public sealed class PlanExpiredException : DatabaseOrchestrationException
{
    public PlanExpiredException()
        : base(409, "SD_DB_PLAN_EXPIRED", "计划已过期,请重新生成。")
    {
    }
}

/// <summary>计划目标状态指纹漂移(§9.9 SD_DB_PLAN_DRIFT)。</summary>
public sealed class PlanDriftException : DatabaseOrchestrationException
{
    public PlanDriftException()
        : base(409, "SD_DB_PLAN_DRIFT", "目标状态已漂移,请重新生成计划。")
    {
    }
}

/// <summary>拓扑 revision 漂移(§9.9 SD_DB_TOPOLOGY_DRIFT)。</summary>
public sealed class TopologyDriftException : DatabaseOrchestrationException
{
    public TopologyDriftException()
        : base(409, "SD_DB_TOPOLOGY_DRIFT", "受信任拓扑已变化,请重新生成计划。")
    {
    }
}

/// <summary>生产门禁:缺少有效审批(§9.9 SD_DB_APPROVAL_REQUIRED)。</summary>
public sealed class ApprovalRequiredException : DatabaseOrchestrationException
{
    public ApprovalRequiredException()
        : base(409, "SD_DB_APPROVAL_REQUIRED", "该环境要求先完成计划审批。")
    {
    }
}

/// <summary>生产门禁:缺少有效备份证据(§9.9 SD_DB_BACKUP_REQUIRED)。</summary>
public sealed class BackupRequiredException : DatabaseOrchestrationException
{
    public BackupRequiredException()
        : base(409, "SD_DB_BACKUP_REQUIRED", "该环境要求先登记并验证备份证据。")
    {
    }
}

/// <summary>操作冲突(§9.9 SD_DB_OPERATION_CONFLICT):幂等键冲突或并发更新冲突。</summary>
public sealed class OperationConflictException : DatabaseOrchestrationException
{
    public OperationConflictException(string message)
        : base(409, "SD_DB_OPERATION_CONFLICT", message)
    {
    }
}

/// <summary>操作不可取消(§9.9 SD_DB_OPERATION_NOT_CANCELLABLE)。</summary>
public sealed class OperationNotCancellableException : DatabaseOrchestrationException
{
    public OperationNotCancellableException()
        : base(409, "SD_DB_OPERATION_NOT_CANCELLABLE", "当前状态不允许取消:仅 Queued 或 Running 且未越过 Inspect 阶段。")
    {
    }
}

/// <summary>目标不匹配(§9.9 SD_DB_TARGET_MISMATCH)。</summary>
public sealed class TargetMismatchException : DatabaseOrchestrationException
{
    public TargetMismatchException(string message)
        : base(409, "SD_DB_TARGET_MISMATCH", message)
    {
    }
}

/// <summary>种子校验和漂移:同 key/scope/version 不同 checksum,拒绝重注册/执行(§9.9 SD_INIT_SEED_CHECKSUM_DRIFT)。</summary>
public sealed class SeedChecksumDriftException : DatabaseOrchestrationException
{
    public SeedChecksumDriftException(string seedKey)
        : base(409, "SD_INIT_SEED_CHECKSUM_DRIFT", $"种子 {seedKey} 同版本校验和漂移,拒绝应用。")
    {
    }
}

/// <summary>种子前置条件不满足(依赖种子/迁移版本未达)(§9.9 SD_INIT_SEED_DEPENDENCY_UNSATISFIED)。</summary>
public sealed class SeedDependencyUnsatisfiedException : DatabaseOrchestrationException
{
    public SeedDependencyUnsatisfiedException(string message)
        : base(409, "SD_INIT_SEED_DEPENDENCY_UNSATISFIED", message)
    {
    }
}

/// <summary>EnvironmentSample 种子在 Staging/Production 被拒绝(§9.9 SD_INIT_SAMPLE_ENVIRONMENT_FORBIDDEN)。</summary>
public sealed class SampleEnvironmentForbiddenException : DatabaseOrchestrationException
{
    public SampleEnvironmentForbiddenException()
        : base(409, "SD_INIT_SAMPLE_ENVIRONMENT_FORBIDDEN", "EnvironmentSample 种子禁止在 Staging/Production 环境执行。")
    {
    }
}

/// <summary>注册/执行所需管理数据冲突(§9.9 SD_INIT_ADMIN_DATA_CONFLICT)。</summary>
public sealed class AdminDataConflictException : DatabaseOrchestrationException
{
    public AdminDataConflictException(string message)
        : base(409, "SD_INIT_ADMIN_DATA_CONFLICT", message)
    {
    }
}

/// <summary>SecretBootstrap 缺 Secret 且策略 fail-closed(§9.9 SD_INIT_BOOTSTRAP_SECRET_MISSING)。</summary>
public sealed class BootstrapSecretMissingException : DatabaseOrchestrationException
{
    public BootstrapSecretMissingException(string seedKey)
        : base(409, "SD_INIT_BOOTSTRAP_SECRET_MISSING", $"SecretBootstrap 种子 {seedKey} 缺少秘密,拒绝初始化。")
    {
    }
}

/// <summary>服务 initializer 执行失败(§9.9 SD_INIT_INITIALIZER_FAILED)。</summary>
public sealed class InitializerFailedException : DatabaseOrchestrationException
{
    public InitializerFailedException(string message)
        : base(500, "SD_INIT_INITIALIZER_FAILED", message)
    {
    }
}
