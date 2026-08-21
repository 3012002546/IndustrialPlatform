using System.Runtime.CompilerServices;

internal static class TestDevelopmentInfrastructureMode
{
    private const string ModeKey = "IndustrialPlatform__DevelopmentInfrastructureMode";
    private static readonly string[] FormalIntegrationGateKeys =
    [
        "IDENTITY_E2E_DB",
        "SYSTEMDATA_PG_E2E",
        "SYSTEMDATA_CONTROL_PLANE_E2E",
    ];

#pragma warning disable CA2255 // Test assemblies intentionally initialize process-wide infrastructure isolation.
    [ModuleInitializer]
    internal static void Initialize()
    {
        if (ShouldUseSqliteIsolation())
        {
            Environment.SetEnvironmentVariable(ModeKey, "Sqlite");
        }
    }
#pragma warning restore CA2255

    internal static bool ShouldUseSqliteIsolation()
    {
        // 任何显式模式都由调用方负责：Unified 允许真实云联调，Sqlite 保持本地隔离，非法值交给宿主启动校验。
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ModeKey)))
        {
            return false;
        }

        // 集成门禁本身也是正式联调的显式选择；不能先被 ModuleInitializer 改成 Sqlite，再把早退误报为真实通过。
        return !FormalIntegrationGateKeys.Any(key =>
            string.Equals(Environment.GetEnvironmentVariable(key), "1", StringComparison.Ordinal));
    }
}
