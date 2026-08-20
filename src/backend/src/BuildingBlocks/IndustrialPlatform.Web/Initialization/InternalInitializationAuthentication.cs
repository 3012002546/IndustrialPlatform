using System.Security.Cryptography;
using System.Text;
using IndustrialPlatform.Application.Abstractions.Initialization;
using IndustrialPlatform.SharedKernel.Topology;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace IndustrialPlatform.Web.Initialization;

/// <summary>内部初始化端点的专用 Header 鉴权，不记录、不回显密钥。</summary>
public static class InternalInitializationAuthentication
{
    public const string HeaderName = "X-Industrial-Initialization-Key";
    public const string ConfigurationKey = "InternalInitialization:Key";
    public const string EnvironmentVariableName = "INDUSTRIAL_PLATFORM_INITIALIZATION_KEY";

    public static string? GetConfiguredKey(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return configuration[ConfigurationKey]
            ?? Environment.GetEnvironmentVariable(EnvironmentVariableName);
    }

    public static bool IsAuthorized(HttpRequest request, string? configuredKey)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(configuredKey)
            || !request.Headers.TryGetValue(HeaderName, out var supplied)
            || string.IsNullOrWhiteSpace(supplied))
        {
            return false;
        }

        var expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(configuredKey));
        var suppliedHash = SHA256.HashData(Encoding.UTF8.GetBytes(supplied.ToString()));
        return CryptographicOperations.FixedTimeEquals(expectedHash, suppliedHash);
    }

    public static ServiceInitializationContext BuildContext(
        InternalInitializationRequest request,
        string serviceKey,
        string moduleKey,
        string environmentName)
    {
        ArgumentNullException.ThrowIfNull(request);
        return new ServiceInitializationContext(
            environmentName,
            request.TenantNId,
            request.OperationNId,
            serviceKey,
            moduleKey,
            new ResolvedDatabaseTarget(
                environmentName,
                DatabaseTopologyMode.PerService,
                serviceKey,
                DatabaseProvider.Sqlite,
                $"{serviceKey}_db",
                "internal",
                false),
            request.DesiredVersion,
            request.Policy,
            request.TraceId);
    }
}

/// <summary>内部初始化请求只传递非敏感控制字段，数据库目标由服务本地解析。</summary>
public sealed record InternalInitializationRequest(
    string TenantNId,
    string OperationNId,
    string DesiredVersion,
    ServiceInitializationPolicy Policy,
    string TraceId,
    ServiceInitializationState? Inspection = null,
    ServiceInitializationPlan? Plan = null);
