using IndustrialPlatform.SystemData.Application.DatabaseOrchestration;

namespace IndustrialPlatform.SystemData.Application.DatabaseOrchestration.Runner;

/// <summary>
/// Runner 端口可抛出的具体编排异常(TASK-SD-003):携带任意 Runner 稳定错误码
/// (见 <see cref="DatabaseOrchestrationRunnerErrors"/>),供适配器/产物存储等 Runner 端口
/// 在失败时映射稳定码。基类为抽象,端口需要可实例化异常;摘要必须脱敏(不泄漏
/// 数据库地址/密码/Token/SQL/原始迁移输出)。
/// </summary>
public sealed class DatabaseOrchestrationRunnerException : DatabaseOrchestrationException
{
    /// <summary>按稳定错误码与脱敏摘要构造。</summary>
    /// <param name="statusCode">标准 HTTP 状态码。</param>
    /// <param name="code">Runner 稳定错误码(<see cref="DatabaseOrchestrationRunnerErrors"/>)。</param>
    /// <param name="message">脱敏摘要,不包含 Secret/连接细节。</param>
    public DatabaseOrchestrationRunnerException(int statusCode, string code, string message)
        : base(statusCode, code, message)
    {
    }
}
