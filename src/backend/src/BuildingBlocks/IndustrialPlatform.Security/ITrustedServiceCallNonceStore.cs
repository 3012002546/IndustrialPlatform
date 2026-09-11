namespace IndustrialPlatform.Security;

/// <summary>接收服务自有的 PF05 assertion 重放存储端口。</summary>
public interface ITrustedServiceCallNonceStore
{
    /// <summary>原子登记 nonce；已存在返回 false，存储不可用必须抛出。</summary>
    Task<bool> TryRegisterAsync(string issuer, string nonce, DateTimeOffset expiresOn, CancellationToken cancellationToken);
}
