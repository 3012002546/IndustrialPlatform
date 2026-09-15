using IndustrialPlatform.ReferenceData.Infrastructure.Persistence;
using IndustrialPlatform.ReferenceData.Infrastructure.UnitOfMeasure;
using IndustrialPlatform.SharedKernel.Topology;

namespace IndustrialPlatform.ReferenceData.Tests;

public sealed class StandaloneSchemaMetadataTests
{
    private static readonly string[] ModuleKeys = ["identity", "systemdata", "referencedata", "collaboration"];

    [Fact]
    public void All_shared_standalone_module_targets_keep_the_configured_schema()
    {
        var topology = new DatabaseTopology(
            "Production",
            DatabaseTopologyMode.Shared,
            "mes_collaboration",
            null,
            new Dictionary<string, string>(),
            "mes_embedded",
            IsStandalone: true);

        var targets = ModuleKeys
            .Select(service => DatabaseTopologyResolver.Resolve(
                topology,
                service,
                DatabaseProvider.PostgreSQL,
                $"{service}_db"))
            .ToArray();

        Assert.All(targets, target => Assert.Equal("mes_embedded", target.Schema));
        Assert.DoesNotContain("reference_data.", ReferenceDataSharedMigration.Sql(true, "mes_embedded"), StringComparison.Ordinal);
        Assert.Contains("mes_embedded.outbox_message", ReferenceDataSharedMigration.Sql(true, "mes_embedded"), StringComparison.Ordinal);
        Assert.Contains("mes_embedded.unit_of_measure_dimension", UnitOfMeasureSystemSeed.Sql(true, "mes_embedded"), StringComparison.Ordinal);
    }

    [Fact]
    public void Default_reference_data_sql_remains_byte_compatible_with_explicit_default_schema()
    {
        var implicitScripts = ReferenceDataMigrations.Scripts(false);
        var explicitScripts = ReferenceDataMigrations.Scripts(false, "reference_data");

        Assert.Equal(implicitScripts.Count, explicitScripts.Count);
        for (var index = 0; index < implicitScripts.Count; index++)
        {
            Assert.Equal(implicitScripts[index].Version, explicitScripts[index].Version);
            Assert.Equal(implicitScripts[index].Sql, explicitScripts[index].Sql);
        }

        Assert.Equal(UnitOfMeasureSystemSeed.Sql(false), UnitOfMeasureSystemSeed.Sql(false, "reference_data"));
    }
}
