using System.Security.Cryptography;
using System.Text;
using IndustrialPlatform.Infrastructure.Database;
using IndustrialPlatform.ReferenceData.Application.Caching;
using SqlSugar;

namespace IndustrialPlatform.ReferenceData.Infrastructure.Caching;

/// <summary>Persists cache invalidation generations inside the same transaction as business writes.</summary>
public static class ReferenceDataCacheGenerationStore
{
    public static async Task AdvanceAsync(ISqlSugarClient database,
        IEnumerable<ReferenceDataCachePattern> patterns, CancellationToken cancellationToken)
    {
        var generationKeys = patterns.Select(pattern => StorageKey(pattern.GenerationKey))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (generationKeys.Length == 0) return;
        var postgres = database.CurrentConnectionConfig.DbType == DbType.PostgreSQL;
        var table = postgres ? "reference_data.cache_generation" : "reference_data_cache_generation";
        var now = postgres ? DateTimeOffset.UtcNow : DateTimeOffset.Now;
        foreach (var generationKey in generationKeys)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var generationToken = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
            var command = postgres
                ? $"""
                    INSERT INTO {table} AS current_generation
                        (generation_key,generation_token,last_updated_on)
                    VALUES (@generationKey,@generationToken,@now)
                    ON CONFLICT(generation_key) DO UPDATE
                    SET generation_token=excluded.generation_token,
                        last_updated_on=excluded.last_updated_on
                    """
                : $"""
                    INSERT INTO {table} (generation_key,generation_token,last_updated_on)
                    VALUES (@generationKey,@generationToken,@now)
                    ON CONFLICT(generation_key) DO UPDATE
                    SET generation_token=excluded.generation_token,
                        last_updated_on=excluded.last_updated_on
                    """;
            await database.Ado.ExecuteCommandAsync(command,
                new SugarParameter("@generationKey", generationKey),
                new SugarParameter("@generationToken", generationToken),
                new SugarParameter("@now", now));
        }
    }

    public static async Task<string> ReadAsync(SqlSugarDbContext context, string generationKey,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        generationKey = StorageKey(generationKey);
        var database = context.SqlSugar;
        var table = database.CurrentConnectionConfig.DbType == DbType.PostgreSQL
            ? "reference_data.cache_generation"
            : "reference_data_cache_generation";
        var rows = await database.Queryable<ReferenceDataCacheGenerationRow>().AS(table)
            .Where(row => row.GenerationKey == generationKey)
            .Select(row => row.GenerationToken)
            .Take(1)
            .ToListAsync(cancellationToken);
        return rows.Count == 0 ? "0" : rows[0];
    }

    internal static string StorageKey(string generationKey) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(generationKey)));

    private sealed class ReferenceDataCacheGenerationRow
    {
        [SugarColumn(ColumnName = "generation_key", IsPrimaryKey = true)]
        public string GenerationKey { get; set; } = string.Empty;

        [SugarColumn(ColumnName = "generation_token")]
        public string GenerationToken { get; set; } = string.Empty;
    }
}
