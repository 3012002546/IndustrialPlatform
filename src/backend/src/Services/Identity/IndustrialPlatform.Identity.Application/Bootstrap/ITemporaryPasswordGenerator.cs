namespace IndustrialPlatform.Identity.Application.Bootstrap;

/// <summary>
/// 安全随机临时密码生成端口(§29A.4):长度不少于指定值且满足密码策略,每次随机。
/// 实现由基础设施层基于 <see cref="System.Security.Cryptography.RandomNumberGenerator"/> 提供。
/// </summary>
public interface ITemporaryPasswordGenerator
{
    /// <summary>生成随机临时密码。</summary>
    /// <param name="minLength">最短长度(bootstrap 要求不少于 20)。</param>
    /// <param name="loginName">登录名,用于策略校验。</param>
    /// <param name="nId">业务标识,用于策略校验。</param>
    string Generate(int minLength, string? loginName = null, string? nId = null);
}
