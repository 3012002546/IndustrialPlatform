using System.Globalization;
using IndustrialPlatform.Infrastructure.Database;
using IndustrialPlatform.Infrastructure.Repository;
using IndustrialPlatform.SharedKernel.Entities;
using IndustrialPlatform.SharedKernel.Exceptions;
using Microsoft.Extensions.Options;
using SqlSugar;
using SQLitePCL;
using Xunit;

namespace IndustrialPlatform.BuildingBlocks.Tests;

/// <summary>
/// 通用仓储集成测试,基于 SQLite 文件库验证双版本原子更新、软删除与恢复。
/// </summary>
public sealed class BaseRepositoryTests : IDisposable
{
    static BaseRepositoryTests()
    {
        SQLitePCL.Batteries_V2.Init();
    }

    private readonly string _dbPath;
    private readonly SqlSugarDbContext _dbContext;
    private readonly BaseRepository<TestEntity> _repository;

    public BaseRepositoryTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"industrial-platform-test-{Guid.NewGuid():N}.db");
        _dbContext = new SqlSugarDbContext(Options.Create(new SqlSugarOptions
        {
            ConnectionString = $"Data Source={_dbPath}",
            DbType = DbType.Sqlite,
        }));
        _dbContext.SqlSugar.CodeFirst.InitTables<TestEntity>();
        _repository = new BaseRepository<TestEntity>(_dbContext);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        try
        {
            if (File.Exists(_dbPath))
            {
                File.Delete(_dbPath);
            }
        }
        catch (IOException)
        {
            // SqlSugarScope 连接池可能短暂占用文件句柄,忽略清理失败。
        }
    }

    [Fact]
    public async Task Add_ThenGetById_ReturnsEntityPreservingAllFields()
    {
        var entity = new TestEntity { Name = "material-a" };

        await _repository.AddAsync(entity);

        var loaded = await _repository.GetByIdAsync(entity.Id);

        Assert.NotNull(loaded);
        Assert.Equal(entity.Id, loaded.Id);
        Assert.Equal("material-a", loaded.Name);
        Assert.Equal(entity.EntityType, loaded.EntityType);
        // SqlSugar 的 SQLite provider 存储 DateTimeOffset 时丢弃 UTC 偏移(读回为本地偏移),
        // 墙钟值保持不变;偏移保留与 PostgreSQL timestamptz 的映射由 TASK-BASE-003 验收。
        const string wallClockFormat = "yyyy-MM-dd'T'HH:mm:ss.fffffff";
        Assert.Equal(
            entity.CreatedOn.ToString(wallClockFormat, CultureInfo.InvariantCulture),
            loaded.CreatedOn.ToString(wallClockFormat, CultureInfo.InvariantCulture));
        Assert.Equal(
            entity.LastUpdatedOn.ToString(wallClockFormat, CultureInfo.InvariantCulture),
            loaded.LastUpdatedOn.ToString(wallClockFormat, CultureInfo.InvariantCulture));
        Assert.Equal(0, loaded.OptimisticVersion);
        Assert.Equal(entity.ConcurrencyVersion, loaded.ConcurrencyVersion);
        Assert.False(loaded.IsFrozen);
        Assert.False(loaded.IsLocked);
        Assert.False(loaded.IsDeleted);
    }

    [Fact]
    public async Task GetById_ExcludesSoftDeletedEntity()
    {
        var entity = new TestEntity { Name = "material-a" };
        await _repository.AddAsync(entity);
        await _repository.DeleteAsync(entity, entity.OptimisticVersion, entity.ConcurrencyVersion);

        var loaded = await _repository.GetByIdAsync(entity.Id);

        Assert.Null(loaded);
    }

    [Fact]
    public async Task Delete_IsSoftDelete_EntityRemainsStoredAsDeleted()
    {
        var entity = new TestEntity { Name = "material-a" };
        await _repository.AddAsync(entity);

        await _repository.DeleteAsync(entity, entity.OptimisticVersion, entity.ConcurrencyVersion);

        var stored = await _dbContext.SqlSugar
            .Queryable<TestEntity>()
            .Where(it => it.Id == entity.Id)
            .FirstAsync();

        Assert.NotNull(stored);
        Assert.True(stored.IsDeleted);
    }

    [Fact]
    public async Task Delete_WithCorrectVersions_Succeeds()
    {
        var entity = new TestEntity { Name = "material-a" };
        await _repository.AddAsync(entity);

        await _repository.DeleteAsync(entity, entity.OptimisticVersion, entity.ConcurrencyVersion);

        Assert.Null(await _repository.GetByIdAsync(entity.Id));
    }

    [Fact]
    public async Task Delete_WithWrongOptimisticVersion_ThrowsConcurrencyException()
    {
        var entity = new TestEntity { Name = "material-a" };
        await _repository.AddAsync(entity);

        await Assert.ThrowsAsync<ConcurrencyException>(() =>
            _repository.DeleteAsync(entity, 999, entity.ConcurrencyVersion));
    }

    [Fact]
    public async Task Delete_WithWrongConcurrencyVersion_ThrowsConcurrencyException()
    {
        var entity = new TestEntity { Name = "material-a" };
        await _repository.AddAsync(entity);

        await Assert.ThrowsAsync<ConcurrencyException>(() =>
            _repository.DeleteAsync(entity, entity.OptimisticVersion, Guid.NewGuid()));
    }

    [Fact]
    public async Task Delete_AlreadyDeletedEntity_ThrowsConcurrencyException()
    {
        var entity = new TestEntity { Name = "material-a" };
        await _repository.AddAsync(entity);
        await _repository.DeleteAsync(entity, entity.OptimisticVersion, entity.ConcurrencyVersion);

        await Assert.ThrowsAsync<ConcurrencyException>(() =>
            _repository.DeleteAsync(entity, entity.OptimisticVersion, entity.ConcurrencyVersion));
    }

    [Fact]
    public async Task Update_WithCorrectVersions_Succeeds()
    {
        var entity = new TestEntity { Name = "original" };
        await _repository.AddAsync(entity);

        var loaded = await _repository.GetByIdAsync(entity.Id);
        Assert.NotNull(loaded);
        var expectedOptimistic = loaded.OptimisticVersion;
        var expectedConcurrency = loaded.ConcurrencyVersion;
        loaded.Rename("updated");
        await _repository.UpdateAsync(loaded, expectedOptimistic, expectedConcurrency);

        var current = await _repository.GetByIdAsync(entity.Id);
        Assert.NotNull(current);
        Assert.Equal("updated", current.Name);
        Assert.Equal(1, current.OptimisticVersion);
        Assert.NotEqual(expectedConcurrency, current.ConcurrencyVersion);
    }

    [Fact]
    public async Task Update_WithWrongVersion_ThrowsConcurrencyException()
    {
        var entity = new TestEntity { Name = "original" };
        await _repository.AddAsync(entity);
        var loaded = await _repository.GetByIdAsync(entity.Id);
        Assert.NotNull(loaded);

        var expectedOptimistic = loaded.OptimisticVersion;
        var expectedConcurrency = loaded.ConcurrencyVersion;
        loaded.Rename("updated");
        await _repository.UpdateAsync(loaded, expectedOptimistic, expectedConcurrency);

        await Assert.ThrowsAsync<ConcurrencyException>(() =>
            _repository.UpdateAsync(loaded, 0, loaded.ConcurrencyVersion));
    }

    [Fact]
    public async Task Update_Conflict_DoesNotOverwriteNewerRecord()
    {
        var entity = new TestEntity { Name = "original" };
        await _repository.AddAsync(entity);

        var stale = await _repository.GetByIdAsync(entity.Id);
        var fresh = await _repository.GetByIdAsync(entity.Id);
        Assert.NotNull(stale);
        Assert.NotNull(fresh);

        var staleOptimistic = stale.OptimisticVersion;
        var staleConcurrency = stale.ConcurrencyVersion;
        var freshOptimistic = fresh.OptimisticVersion;
        var freshConcurrency = fresh.ConcurrencyVersion;
        fresh.Rename("newer");
        await _repository.UpdateAsync(fresh, freshOptimistic, freshConcurrency);

        await Assert.ThrowsAsync<ConcurrencyException>(() =>
            _repository.UpdateAsync(stale, staleOptimistic, staleConcurrency));

        var current = await _repository.GetByIdAsync(entity.Id);
        Assert.NotNull(current);
        Assert.Equal(1, current.OptimisticVersion);
        Assert.Equal("newer", current.Name);
    }

    [Fact]
    public async Task Restore_WithCorrectVersions_RestoresEntity()
    {
        var entity = new TestEntity { Name = "material-a" };
        await _repository.AddAsync(entity);
        await _repository.DeleteAsync(entity, entity.OptimisticVersion, entity.ConcurrencyVersion);

        await _repository.RestoreAsync(entity, entity.OptimisticVersion, entity.ConcurrencyVersion);

        var restored = await _repository.GetByIdAsync(entity.Id);
        Assert.NotNull(restored);
        Assert.False(restored.IsDeleted);
    }

    [Fact]
    public async Task Restore_WithWrongVersion_ThrowsConcurrencyException()
    {
        var entity = new TestEntity { Name = "material-a" };
        await _repository.AddAsync(entity);
        await _repository.DeleteAsync(entity, entity.OptimisticVersion, entity.ConcurrencyVersion);

        await Assert.ThrowsAsync<ConcurrencyException>(() =>
            _repository.RestoreAsync(entity, 999, entity.ConcurrencyVersion));
    }

    [Fact]
    public async Task Restore_ActiveEntity_ThrowsConcurrencyException()
    {
        var entity = new TestEntity { Name = "material-a" };
        await _repository.AddAsync(entity);

        await Assert.ThrowsAsync<ConcurrencyException>(() =>
            _repository.RestoreAsync(entity, entity.OptimisticVersion, entity.ConcurrencyVersion));
    }
}
