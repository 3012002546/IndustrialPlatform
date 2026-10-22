using System.Globalization;
using IndustrialPlatform.PlatformStudio.Application.Publishing;
using IndustrialPlatform.PlatformStudio.Contracts;
using IndustrialPlatform.PlatformStudio.Domain;
using IndustrialPlatform.SharedKernel.Entities;
using IndustrialPlatform.SharedKernel.Exceptions;

namespace IndustrialPlatform.PlatformStudio.Tests;

public sealed class StudioEntityReuseTests
{
    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Theory]
    [InlineData("Resource")]
    [InlineData("Source")]
    [InlineData("LedgerRecord")]
    public void Editable_entities_share_lifecycle_guards_versions_and_controlled_time(string kind)
    {
        var now = new DateTimeOffset(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);
        StudioEntity entity = kind switch
        {
            "Resource" => new StudioResource(now),
            "Source" => new StudioSource(now),
            _ => new StudioLedgerRecord(now),
        };
        Assert.IsAssignableFrom<Entity>(entity);
        Assert.Equal(entity.GetType().FullName, entity.EntityType);
        Assert.Equal(now, entity.CreatedOn);
        Assert.Equal(now, entity.LastUpdatedOn);
        var token = entity.ConcurrencyVersion;
        entity.MarkChanged(now.AddMinutes(1));
        Assert.Equal(1, entity.OptimisticVersion);
        Assert.NotEqual(token, entity.ConcurrencyVersion);
        Assert.Equal(now.AddMinutes(1), entity.LastUpdatedOn);

        entity.Freeze();
        var frozenVersion = entity.OptimisticVersion;
        Assert.Throws<BusinessException>(() => entity.MarkChanged(now));
        var error = Assert.Throws<StudioException>(() => StudioPublishing.GuardWritable(entity));
        Assert.IsAssignableFrom<DomainException>(error);
        Assert.Equal("STUDIO_VERSION_CONFLICT", error.Code);
        Assert.Equal(frozenVersion, entity.OptimisticVersion);
        entity.Unfreeze();
        entity.Lock();
        Assert.Throws<BusinessException>(() => entity.MarkDeleted(now));
        entity.Unlock();

        var version = entity.OptimisticVersion;
        entity.MarkDeleted(now.AddMinutes(2));
        token = entity.ConcurrencyVersion;
        entity.MarkDeleted(now.AddMinutes(3));
        Assert.True(entity.IsDeleted);
        Assert.Equal(version + 1, entity.OptimisticVersion);
        Assert.Equal(token, entity.ConcurrencyVersion);
        Assert.Equal(now.AddMinutes(2), entity.LastUpdatedOn);
        Assert.Throws<BusinessException>(() => entity.MarkChanged(now));
        entity.Restore(now.AddMinutes(4));
        token = entity.ConcurrencyVersion;
        entity.Restore(now.AddMinutes(5));
        Assert.False(entity.IsDeleted);
        Assert.Equal(version + 2, entity.OptimisticVersion);
        Assert.Equal(token, entity.ConcurrencyVersion);
        Assert.Equal(now.AddMinutes(4), entity.LastUpdatedOn);
        Assert.Equal(now, entity.CreatedOn);
    }

    [Fact]
    public async Task Inherited_metadata_round_trips_through_real_store_preserves_CAS_and_advances_once_per_state_change()
    {
        await using var fixture = await StudioTestContext.CreateAsync();
        var clock = new Clock(new DateTimeOffset(2026, 10, 22, 12, 0, 0, TimeSpan.Zero));
        var publishing = new StudioPublishing(fixture.Store, fixture.Authorization, fixture.Commands, fixture.Profile, clock, [], fixture.SourceExecutor);
        var created = await publishing.CreateAsync(fixture.Principal,
            new("reuse", "LedgerModel", "实体复用", null, null, StudioPublishingTests.Model()), "reuse-create", CancellationToken.None);
        var row = (await fixture.Store.FindResourceAsync(fixture.Principal.TenantNId, created.NId, CancellationToken.None))!;
        var stale = (await fixture.Store.FindResourceAsync(fixture.Principal.TenantNId, created.NId, CancellationToken.None))!;
        Assert.Equal(clock.Now, row.CreatedOn);
        Assert.Equal(typeof(StudioResource).FullName, row.EntityType);
        Assert.Equal(created.ConcurrencyVersion, row.ConcurrencyVersion);

        clock.Now = clock.Now.AddMinutes(1);
        row.Name = "保留首次修改";
        row.MarkChanged(clock.Now);
        Assert.True(await fixture.Store.UpdateResourceAsync(row, 0, created.ConcurrencyVersion, CancellationToken.None));
        stale.Name = "拒绝迟到修改";
        stale.MarkChanged(clock.Now);
        Assert.False(await fixture.Store.UpdateResourceAsync(stale, 0, created.ConcurrencyVersion, CancellationToken.None));
        row = (await fixture.Store.FindResourceAsync(fixture.Principal.TenantNId, created.NId, CancellationToken.None))!;
        Assert.Equal("保留首次修改", row.Name);
        Assert.Equal(1, row.OptimisticVersion);
        Assert.Equal(clock.Now, row.LastUpdatedOn);

        // 公共生命周期不允许删除冻结实体;ORM必须恢复继承的非公开setter。
        var oldVersion = row.OptimisticVersion;
        var oldToken = row.ConcurrencyVersion;
        row.Freeze();
        Assert.True(await fixture.Store.UpdateResourceAsync(row, oldVersion, oldToken, CancellationToken.None));
        row = (await fixture.Store.FindResourceAsync(fixture.Principal.TenantNId, created.NId, CancellationToken.None))!;
        Assert.True(row.IsFrozen);
        Assert.Equal(409, (await Assert.ThrowsAsync<StudioException>(() => publishing.SetStateAsync(fixture.Principal, row.NId,
            "delete", new(row.OptimisticVersion.ToString(CultureInfo.InvariantCulture), row.ConcurrencyVersion), CancellationToken.None))).StatusCode);
        oldVersion = row.OptimisticVersion;
        oldToken = row.ConcurrencyVersion;
        row.Unfreeze();
        Assert.True(await fixture.Store.UpdateResourceAsync(row, oldVersion, oldToken, CancellationToken.None));

        clock.Now = clock.Now.AddMinutes(1);
        var deleted = await publishing.SetStateAsync(fixture.Principal, row.NId, "delete",
            new(row.OptimisticVersion.ToString(CultureInfo.InvariantCulture), row.ConcurrencyVersion), CancellationToken.None);
        Assert.Equal((row.OptimisticVersion + 1).ToString(CultureInfo.InvariantCulture), deleted.OptimisticVersion);
        var persisted = (await fixture.Store.FindResourceAsync(fixture.Principal.TenantNId, row.NId, CancellationToken.None))!;
        Assert.True(persisted.IsDeleted);
        Assert.Equal(clock.Now, persisted.LastUpdatedOn);
        clock.Now = clock.Now.AddMinutes(1);
        var restored = await publishing.SetStateAsync(fixture.Principal, row.NId, "restore",
            new(deleted.OptimisticVersion, deleted.ConcurrencyVersion), CancellationToken.None);
        Assert.Equal((persisted.OptimisticVersion + 1).ToString(CultureInfo.InvariantCulture), restored.OptimisticVersion);
        Assert.Equal(clock.Now, restored.LastUpdatedOn);
        Assert.Equal(created.CreatedOn, restored.CreatedOn);

        var unchanged = await publishing.SetStateAsync(fixture.Principal, row.NId, "restore",
            new(restored.OptimisticVersion, restored.ConcurrencyVersion), CancellationToken.None);
        Assert.Equal(restored.OptimisticVersion, unchanged.OptimisticVersion);
        Assert.Equal(restored.LastUpdatedOn, unchanged.LastUpdatedOn);
        var plan = await publishing.ValidateAsync(fixture.Principal, row.NId, StudioPublishingTests.Model(), CancellationToken.None);
        await publishing.PublishAsync(fixture.Principal, row.NId,
            new(restored.OptimisticVersion, restored.ConcurrencyVersion, "复核恢复", true, plan.DependencyPlanHash), "reuse-publish", CancellationToken.None);
        var active = (await fixture.Store.FindResourceAsync(fixture.Principal.TenantNId, row.NId, CancellationToken.None))!;
        Assert.True(active.IsEnabled);
        var disabled = await publishing.SetStateAsync(fixture.Principal, row.NId, "restore",
            new(active.OptimisticVersion.ToString(CultureInfo.InvariantCulture), active.ConcurrencyVersion), CancellationToken.None);
        Assert.False(disabled.IsEnabled);
        Assert.Equal((active.OptimisticVersion + 1).ToString(CultureInfo.InvariantCulture), disabled.OptimisticVersion);
    }
}
