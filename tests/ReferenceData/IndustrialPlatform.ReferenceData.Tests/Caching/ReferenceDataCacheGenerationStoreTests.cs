using IndustrialPlatform.Infrastructure.Database;
using IndustrialPlatform.ReferenceData.Application.Caching;
using IndustrialPlatform.ReferenceData.Infrastructure.Caching;
using IndustrialPlatform.ReferenceData.Infrastructure.Persistence;
using Microsoft.Extensions.Options;
using SqlSugar;

namespace IndustrialPlatform.ReferenceData.Tests.Caching;

public sealed class ReferenceDataCacheGenerationStoreTests
{
    [Fact]
    public async Task Generation_advances_commit_and_rollback_with_the_business_transaction()
    {
        var path = Path.Combine(Path.GetTempPath(), $"pf03-cache-generation-{Guid.NewGuid():N}.db");
        try
        {
            using var context = new SqlSugarDbContext(Options.Create(new SqlSugarOptions
            {
                DbType = DbType.Sqlite,
                ConnectionString = $"Data Source={path};Pooling=False",
            }));
            await context.SqlSugar.Ado.ExecuteCommandAsync(ReferenceDataCacheGenerationMigration.Sql(false));
            var pattern = ReferenceDataCacheKeys.Dictionaries("STATUS");

            await context.SqlSugar.Ado.BeginTranAsync();
            await ReferenceDataCacheGenerationStore.AdvanceAsync(
                context.SqlSugar, [pattern], CancellationToken.None);
            var rolledBackToken = await ReferenceDataCacheGenerationStore.ReadAsync(
                context, pattern.GenerationKey, CancellationToken.None);
            Assert.Equal(64, rolledBackToken.Length);
            await context.SqlSugar.Ado.RollbackTranAsync();
            Assert.Equal("0", await ReferenceDataCacheGenerationStore.ReadAsync(
                context, pattern.GenerationKey, CancellationToken.None));

            await context.SqlSugar.Ado.BeginTranAsync();
            await ReferenceDataCacheGenerationStore.AdvanceAsync(
                context.SqlSugar, [pattern, pattern], CancellationToken.None);
            await context.SqlSugar.Ado.CommitTranAsync();
            var firstCommittedToken = await ReferenceDataCacheGenerationStore.ReadAsync(
                context, pattern.GenerationKey, CancellationToken.None);
            Assert.Equal(64, firstCommittedToken.Length);
            Assert.NotEqual(rolledBackToken, firstCommittedToken);

            await context.SqlSugar.Ado.BeginTranAsync();
            await ReferenceDataCacheGenerationStore.AdvanceAsync(
                context.SqlSugar, [pattern], CancellationToken.None);
            await context.SqlSugar.Ado.CommitTranAsync();
            var secondCommittedToken = await ReferenceDataCacheGenerationStore.ReadAsync(
                context, pattern.GenerationKey, CancellationToken.None);
            Assert.Equal(64, secondCommittedToken.Length);
            Assert.NotEqual(firstCommittedToken, secondCommittedToken);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
