namespace IndustrialPlatform.ReferenceData.Infrastructure.Persistence;

/// <summary>Durable cache generations used to validate shared Redis entries across service replicas.</summary>
public static class ReferenceDataCacheGenerationMigration
{
    public const string Version = "reference-data-2.7-011";

    public static string Sql(bool postgres)
    {
        var table = postgres ? "reference_data.cache_generation" : "reference_data_cache_generation";
        var time = postgres ? "timestamptz" : "TEXT";
        return $"""
            CREATE TABLE {table} (
                generation_key varchar(64) PRIMARY KEY NOT NULL CHECK(length(generation_key)=64),
                generation_token varchar(64) NOT NULL CHECK(length(generation_token)=64),
                last_updated_on {time} NOT NULL
            );
            """;
    }
}
