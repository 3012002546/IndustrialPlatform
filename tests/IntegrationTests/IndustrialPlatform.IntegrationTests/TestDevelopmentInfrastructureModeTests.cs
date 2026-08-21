namespace IndustrialPlatform.IntegrationTests;

public sealed class TestDevelopmentInfrastructureModeTests
{
    [Fact]
    public void Explicit_formal_mode_does_not_select_sqlite_isolation()
    {
        const string key = "IndustrialPlatform__DevelopmentInfrastructureMode";
        var original = Environment.GetEnvironmentVariable(key);
        try
        {
            Environment.SetEnvironmentVariable(key, "Unified");

            Assert.False(TestDevelopmentInfrastructureMode.ShouldUseSqliteIsolation());
        }
        finally
        {
            Environment.SetEnvironmentVariable(key, original);
        }
    }

    [Fact]
    public void Real_integration_gate_does_not_select_sqlite_isolation_when_mode_is_unset()
    {
        const string modeKey = "IndustrialPlatform__DevelopmentInfrastructureMode";
        const string gateKey = "SYSTEMDATA_CONTROL_PLANE_E2E";
        var originalMode = Environment.GetEnvironmentVariable(modeKey);
        var originalGate = Environment.GetEnvironmentVariable(gateKey);
        try
        {
            Environment.SetEnvironmentVariable(modeKey, null);
            Environment.SetEnvironmentVariable(gateKey, "1");

            Assert.False(TestDevelopmentInfrastructureMode.ShouldUseSqliteIsolation());
        }
        finally
        {
            Environment.SetEnvironmentVariable(modeKey, originalMode);
            Environment.SetEnvironmentVariable(gateKey, originalGate);
        }
    }
}
