using IndustrialPlatform.ReferenceData.Infrastructure.Dictionary;

namespace IndustrialPlatform.ReferenceData.Infrastructure.Persistence;

internal static class ReferenceDataMigrations
{
    // Append immutable SQL artifacts to this one service-level stream.
    public static IReadOnlyList<(string Version, string Sql)> Scripts(bool postgres) =>
    [
        (DictionaryMigration.Version, DictionaryMigration.Sql(postgres)),
        (Parameter.ParameterMigration.Version, Parameter.ParameterMigration.Sql(postgres)),
        (DynamicProperty.DynamicConfigurationMigration.Version, DynamicProperty.DynamicConfigurationMigration.Sql(postgres)),
        (UnitOfMeasure.UnitOfMeasureMigration.Version, UnitOfMeasure.UnitOfMeasureMigration.Sql(postgres)),
        (Metadata.MetadataMigration.Version, Metadata.MetadataMigration.Sql(postgres)),
        (CodingRule.CodingRuleMigration.Version, CodingRule.CodingRuleMigration.Sql(postgres)),
        (StateMachine.StateMachineMigration.Version, StateMachine.StateMachineMigration.Sql(postgres)),
        (ReferenceDataSharedMigration.Version, ReferenceDataSharedMigration.Sql(postgres)),
        (ReferenceDataIntegrityMigration.Version, ReferenceDataIntegrityMigration.Sql(postgres)),
        (ReferenceDataCacheGenerationMigration.Version, ReferenceDataCacheGenerationMigration.Sql(postgres)),
    ];
}
