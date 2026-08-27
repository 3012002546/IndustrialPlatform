using IndustrialPlatform.Identity.Api.Modules;
using IndustrialPlatform.ReferenceData.Api.Modules;
using IndustrialPlatform.SystemData.Api.Modules;
using IndustrialPlatform.Web.Extensions;

namespace IndustrialPlatform.UnifiedHost;

/// <summary>
/// UnifiedHost 的显式模块目录。顺序同时表达服务注册和启动前置关系；禁止自动扫描实现。
/// </summary>
public static class UnifiedHostModuleCatalog
{
    public static IReadOnlyList<IUnifiedHostModule> Modules { get; } =
    [
        new IdentityUnifiedHostModule(),
        new SystemDataUnifiedHostModule(),
        new ReferenceDataUnifiedHostModule(),
    ];

    public static IReadOnlyList<string> GetServiceKeys(IEnumerable<IUnifiedHostModule> modules) =>
        modules.Select(module => module.ServiceKey).ToArray();

    public static IReadOnlyList<string> GetExternalPathPrefixes(IEnumerable<IUnifiedHostModule> modules) =>
        modules.Select(module => module.ExternalPathPrefix).ToArray();
}
