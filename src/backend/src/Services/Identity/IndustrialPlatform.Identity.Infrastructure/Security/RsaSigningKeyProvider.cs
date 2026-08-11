using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IndustrialPlatform.Identity.Infrastructure.Security;

/// <summary>
/// RSA 签名密钥提供者(§12)。单例持有私钥与独立公钥实例。
/// <c>Identity:Jwt:SigningKey</c> 配置 PEM 私钥;为空时启动生成临时密钥并告警(开发环境),
/// 配置了但 PEM 非法 → 抛异常使启动失败(fail-closed)。
/// </summary>
public sealed class RsaSigningKeyProvider : IDisposable
{
    private static readonly Action<ILogger, Exception?> EphemeralKeyGenerated =
        LoggerMessage.Define(
            LogLevel.Warning,
            new EventId(1, nameof(EphemeralKeyGenerated)),
            "Identity:Jwt:SigningKey 未配置,已生成临时 RSA 密钥用于签名;重启后既有令牌将失效,仅限开发环境。");

    private readonly RSA _privateKey;
    private readonly RSA _publicKey;
    private readonly string _keyId;
    private bool _disposed;

    /// <summary>初始化签名密钥提供者。</summary>
    /// <param name="options">JWT 配置。</param>
    /// <param name="logger">日志。</param>
    /// <exception cref="InvalidOperationException">配置了但 PEM 非法时抛出(fail-closed)。</exception>
    public RsaSigningKeyProvider(IOptions<JwtOptions> options, ILogger<RsaSigningKeyProvider> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        var jwt = options.Value;
        _keyId = string.IsNullOrWhiteSpace(jwt.KeyId) ? "identity-default" : jwt.KeyId;

        if (string.IsNullOrWhiteSpace(jwt.SigningKey))
        {
            _privateKey = RSA.Create(2048);
            EphemeralKeyGenerated(logger, null);
        }
        else
        {
            _privateKey = RSA.Create();
            try
            {
                _privateKey.ImportFromPem(jwt.SigningKey);
            }
            catch (Exception ex)
            {
                _privateKey.Dispose();
                throw new InvalidOperationException("Identity:Jwt:SigningKey 配置的 PEM 无效,拒绝启动。", ex);
            }
        }

        // 仅导入公钥参数,避免下游拿到私钥
        var publicParameters = _privateKey.ExportParameters(false);
        _publicKey = RSA.Create();
        _publicKey.ImportParameters(publicParameters);
    }

    /// <summary>用于签名的私钥。</summary>
    public RSA PrivateKey => _privateKey;

    /// <summary>用于验签/JWKS 的独立公钥(只含公钥参数)。</summary>
    public RSA PublicKey => _publicKey;

    /// <summary>JWT Header kid。</summary>
    public string KeyId => _keyId;

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _privateKey.Dispose();
        _publicKey.Dispose();
        _disposed = true;
    }
}
