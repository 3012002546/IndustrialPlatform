using IndustrialPlatform.ReferenceData.Infrastructure.Dictionary;

namespace IndustrialPlatform.ReferenceData.Infrastructure.Persistence;

internal static class ReferenceDataMigrations
{
    // Append immutable SQL artifacts to this one service-level stream.
    public static IReadOnlyList<(string Version, string Sql)> Scripts(bool postgres, string schema = "reference_data") =>
    [
        (DictionaryMigration.Version, DictionaryMigration.Sql(postgres, schema)),
        (Parameter.ParameterMigration.Version, Parameter.ParameterMigration.Sql(postgres, schema)),
        (DynamicProperty.DynamicConfigurationMigration.Version, DynamicProperty.DynamicConfigurationMigration.Sql(postgres, schema)),
        (UnitOfMeasure.UnitOfMeasureMigration.Version, UnitOfMeasure.UnitOfMeasureMigration.Sql(postgres, schema)),
        (Metadata.MetadataMigration.Version, Metadata.MetadataMigration.Sql(postgres, schema)),
        (CodingRule.CodingRuleMigration.Version, CodingRule.CodingRuleMigration.Sql(postgres, schema)),
        (StateMachine.StateMachineMigration.Version, StateMachine.StateMachineMigration.Sql(postgres, schema)),
        (ReferenceDataSharedMigration.Version, ReferenceDataSharedMigration.Sql(postgres, schema)),
        (ReferenceDataIntegrityMigration.Version, ReferenceDataIntegrityMigration.Sql(postgres, schema)),
        (ReferenceDataCacheGenerationMigration.Version, ReferenceDataCacheGenerationMigration.Sql(postgres, schema)),
    ];
}
