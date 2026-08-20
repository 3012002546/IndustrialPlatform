using IndustrialPlatform.Identity.Application.Management;
using IndustrialPlatform.Identity.Infrastructure.Management;
using IndustrialPlatform.Identity.Infrastructure.Persistence.Migrations;
using IndustrialPlatform.Infrastructure.Database;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SqlSugar;
using SQLitePCL;

namespace IndustrialPlatform.Identity.Infrastructure.Tests;

/// <summary>
/// 写请求幂等存储测试(TASK-ID-020,§29A.5):新键预留、同键同哈希重放、
/// 同键不同哈希冲突、完成标记与失败释放。
/// </summary>
[Collection(BootstrapEnvironmentTestGroup.Name)]
public sealed class IdempotencyStoreTests : IDisposable
{
    private const string Tenant = "development";
    private const string Actor = "actor.admin";

    static IdempotencyStoreTests()
    {
        SQLitePCL.Batteries_V2.Init();
    }

    private readonly string _dbPath;
    private readonly SqlSugarDbContext _dbContext;
    private readonly IdempotencyStore _store;

    public IdempotencyStoreTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"industrial-platform-identity-idem-{Guid.NewGuid():N}.db");
        _dbContext = new SqlSugarDbContext(Options.Create(new SqlSugarOptions
        {
            ConnectionString = $"Data Source={_dbPath};Foreign Keys=True",
            DbType = DbType.Sqlite,
        }));
        new SchemaMigrationRunner(_dbContext, IdentitySchemaMigrations.All, NullLogger<SchemaMigrationRunner>.Instance)
            .ApplyPendingAsync()
            .GetAwaiter()
            .GetResult();
        _store = new IdempotencyStore(_dbContext);
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
            // 忽略清理失败
        }
    }

    [Fact]
    public async Task TryReserve_NewKey_ReturnsProceed_ThenReplayAfterComplete()
    {
        var first = await _store.TryReserveAsync(Tenant, Actor, "key-1", "hash-a");
        Assert.Equal(IdempotencyDecision.Proceed, first);

        // 未完成时同键同哈希重试 → 继续执行(写失败后的重试)
        var retry = await _store.TryReserveAsync(Tenant, Actor, "key-1", "hash-a");
        Assert.Equal(IdempotencyDecision.Proceed, retry);

        await _store.MarkCompletedAsync(Tenant, Actor, "key-1");

        // 完成后同键同哈希 → 重放(不再执行)
        var replay = await _store.TryReserveAsync(Tenant, Actor, "key-1", "hash-a");
        Assert.Equal(IdempotencyDecision.Replay, replay);
    }

    [Fact]
    public async Task TryReserve_SameKeyDifferentHash_ThrowsIdempotencyConflict()
    {
        await _store.TryReserveAsync(Tenant, Actor, "key-2", "hash-a");

        var exception = await Assert.ThrowsAsync<IdempotencyConflictException>(() =>
            _store.TryReserveAsync(Tenant, Actor, "key-2", "hash-b"));
        Assert.Equal("ID_IDEMPOTENCY_CONFLICT", exception.Code);
    }

    [Fact]
    public async Task TryReserve_KeyIsScopedByTenantAndActor()
    {
        await _store.TryReserveAsync(Tenant, Actor, "key-3", "hash-a");

        // 同键不同执行者 → 独立幂等域
        var other = await _store.TryReserveAsync(Tenant, "actor.other", "key-3", "hash-b");
        Assert.Equal(IdempotencyDecision.Proceed, other);

        // 同键不同租户 → 独立幂等域
        var otherTenant = await _store.TryReserveAsync("other", Actor, "key-3", "hash-c");
        Assert.Equal(IdempotencyDecision.Proceed, otherTenant);
    }

    [Fact]
    public async Task Release_AllowsSameKeySameHashToProceedAgain()
    {
        await _store.TryReserveAsync(Tenant, Actor, "key-4", "hash-a");

        // 写失败 → 释放预留 → 同键同哈希重试可再次执行
        await _store.ReleaseAsync(Tenant, Actor, "key-4");

        var retry = await _store.TryReserveAsync(Tenant, Actor, "key-4", "hash-a");
        Assert.Equal(IdempotencyDecision.Proceed, retry);
    }

    [Fact]
    public async Task Release_DoesNotReleaseCompletedKey()
    {
        await _store.TryReserveAsync(Tenant, Actor, "key-5", "hash-a");
        await _store.MarkCompletedAsync(Tenant, Actor, "key-5");

        await _store.ReleaseAsync(Tenant, Actor, "key-5");

        var again = await _store.TryReserveAsync(Tenant, Actor, "key-5", "hash-a");
        Assert.Equal(IdempotencyDecision.Replay, again);
    }
}
