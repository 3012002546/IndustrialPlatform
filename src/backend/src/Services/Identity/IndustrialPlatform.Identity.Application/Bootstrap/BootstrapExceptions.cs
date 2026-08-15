namespace IndustrialPlatform.Identity.Application.Bootstrap;

/// <summary>
/// bootstrap 用例业务异常基类:携带标准 HTTP 状态码与 §29A.5 错误码。
/// message 不得包含明文密码、Token、引用或内部哈希。
/// </summary>
public abstract class BootstrapException : Exception
{
    /// <summary>标准 HTTP 状态码。</summary>
    public int StatusCode { get; }

    /// <summary>§29A.5 错误码。</summary>
    public string Code { get; }

    /// <summary>外部可见消息。</summary>
    protected BootstrapException(int statusCode, string code, string message)
        : base(message)
    {
        StatusCode = statusCode;
        Code = code;
    }
}

/// <summary>bootstrap 尚未完成(§29A.5 ID_BOOTSTRAP_PENDING)。</summary>
public sealed class BootstrapPendingException : BootstrapException
{
    public BootstrapPendingException()
        : base(409, "ID_BOOTSTRAP_PENDING", "初始化尚未完成,请先完成 admin 引导。")
    {
    }
}

/// <summary>凭据已领取过,不得再次读取(§29A.5 ID_BOOTSTRAP_CREDENTIAL_ALREADY_RETRIEVED)。</summary>
public sealed class BootstrapCredentialAlreadyRetrievedException : BootstrapException
{
    public BootstrapCredentialAlreadyRetrievedException()
        : base(409, "ID_BOOTSTRAP_CREDENTIAL_ALREADY_RETRIEVED", "临时密码已领取,不能再次读取;遗失请走紧急恢复。")
    {
    }
}

/// <summary>admin 异常或凭据遗失,必须走审计化紧急恢复(§29A.5 ID_BOOTSTRAP_RECOVERY_REQUIRED)。</summary>
public sealed class BootstrapRecoveryRequiredException : BootstrapException
{
    public BootstrapRecoveryRequiredException()
        : base(409, "ID_BOOTSTRAP_RECOVERY_REQUIRED", "admin 处于异常状态,必须走紧急恢复流程。")
    {
    }
}

/// <summary>恢复引用无效、过期或审批关联缺失(§29A.5,可诊断但不泄漏细节)。</summary>
public sealed class BootstrapRecoveryRejectedException : BootstrapException
{
    public BootstrapRecoveryRejectedException()
        : base(400, "ID_BOOTSTRAP_RECOVERY_REJECTED", "恢复引用或审批关联无效。")
    {
    }
}

/// <summary>种子同版本不同 checksum,判定为 drift 拒绝(§7.1.6:同版本不同 checksum 为 drift 并拒绝)。</summary>
public sealed class SeedDriftException : BootstrapException
{
    public SeedDriftException(string seedKey, string seedVersion)
        : base(409, "ID_SEED_DRIFT", $"种子 {seedKey} 版本 {seedVersion} 内容校验和变化,拒绝应用;请显式升级种子版本。")
    {
    }
}
