using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IndustrialPlatform.Identity.Application.Authentication;
using IndustrialPlatform.Identity.Application.Authorization;
using IndustrialPlatform.Identity.Domain.Users;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace IndustrialPlatform.Identity.Application.Tests;

/// <summary>
/// 权限评估器测试(§14/§18):撤销 fail-closed、版本化缓存命中/降级、数据库权威、
/// 拒绝审计,以及禁用用户无特权绕过。
/// </summary>
public sealed class PermissionEvaluatorTests
{
    private const string Tenant = "development";
    private const string UserNId = "user.alice";
    private const string Session = "SES-abc";
    private const string Required = "identity.user.view";

    [Fact]
    public async Task EvaluateAsync_MissingClaims_DeniesSessionInvalidWithoutAudit()
    {
        var denialSink = new FakeDenialSink();
        var evaluator = CreateEvaluator(denialSink: denialSink);

        var result = await evaluator.EvaluateAsync("", "", null, 3, Required, CancellationToken.None);

        Assert.False(result.Allowed);
        Assert.Equal(AuthorizationDenialReason.SessionInvalid, result.Reason);
        Assert.Empty(denialSink.Denials);
    }

    [Fact]
    public async Task EvaluateAsync_WithPermission_AllowsAndPopulatesCache()
    {
        var dataStore = new FakeAuthorizationDataStore { Snapshot = Snapshot(Active(), [Required]) };
        var cache = new FakePermissionCache();
        var evaluator = CreateEvaluator(dataStore, cache);

        var result = await evaluator.EvaluateAsync(Tenant, UserNId, Session, 3, Required, CancellationToken.None);

        Assert.True(result.Allowed);
        Assert.Equal(AuthorizationDenialReason.None, result.Reason);

        // 数据库权威装载后回填缓存(版本化快照)
        Assert.Equal(1, cache.SetCount);
        Assert.Equal(UserNId, cache.SetSnapshots[0].UserNId);
    }

    [Fact]
    public async Task EvaluateAsync_WithoutPermission_DeniesMissingPermissionAndAudits()
    {
        var dataStore = new FakeAuthorizationDataStore { Snapshot = Snapshot(Active(), ["identity.user.update"]) };
        var denialSink = new FakeDenialSink();
        var evaluator = CreateEvaluator(dataStore, denialSink: denialSink);

        var result = await evaluator.EvaluateAsync(Tenant, UserNId, Session, 3, Required, CancellationToken.None);

        Assert.False(result.Allowed);
        Assert.Equal(AuthorizationDenialReason.MissingPermission, result.Reason);
        var denial = Assert.Single(denialSink.Denials);
        Assert.Equal(Tenant, denial.TenantNId);
        Assert.Equal(UserNId, denial.UserNId);
        Assert.Equal(Required, denial.RequiredPermissionNId);
        Assert.Equal(AuthorizationDenialReason.MissingPermission, denial.Reason);
    }

    [Fact]
    public async Task EvaluateAsync_RevokedSession_DeniesSessionInvalidWithoutStoreAccess()
    {
        // 撤销校验在数据装载之前(fail-closed),已撤销会话不得触达数据库/缓存写入
        var dataStore = new FakeAuthorizationDataStore { Snapshot = Snapshot(Active(), [Required]) };
        var cache = new FakePermissionCache();
        var revocation = new FakeRevocationStore();
        revocation.Revoked.Add(Session);
        var evaluator = CreateEvaluator(dataStore, cache, revocation);

        var result = await evaluator.EvaluateAsync(Tenant, UserNId, Session, 3, Required, CancellationToken.None);

        Assert.False(result.Allowed);
        Assert.Equal(AuthorizationDenialReason.SessionInvalid, result.Reason);
        Assert.Equal(0, dataStore.GetCount);
        Assert.Equal(0, cache.SetCount);
    }

    [Fact]
    public async Task EvaluateAsync_RevocationStoreUnavailable_ThrowsSecurityStoreUnavailable()
    {
        var revocation = new FakeRevocationStore { FailOnCheck = true };
        var evaluator = CreateEvaluator(revocation: revocation);

        await Assert.ThrowsAsync<SecurityStoreUnavailableException>(() =>
            evaluator.EvaluateAsync(Tenant, UserNId, Session, 3, Required, CancellationToken.None));
    }

    [Fact]
    public async Task EvaluateAsync_AuthVersionMismatch_DeniesSessionInvalidAndSkipsCache()
    {
        // 数据库 AuthVersion=3,令牌 ver=4 → 会话失效,且不写入与令牌不匹配的缓存
        var dataStore = new FakeAuthorizationDataStore { Snapshot = Snapshot(Active(), [Required], authVersion: 3) };
        var cache = new FakePermissionCache();
        var evaluator = CreateEvaluator(dataStore, cache);

        var result = await evaluator.EvaluateAsync(Tenant, UserNId, Session, 4, Required, CancellationToken.None);

        Assert.False(result.Allowed);
        Assert.Equal(AuthorizationDenialReason.SessionInvalid, result.Reason);
        Assert.Equal(0, cache.SetCount);
    }

    [Fact]
    public async Task EvaluateAsync_DisabledUser_DeniesAccountDisabled()
    {
        var dataStore = new FakeAuthorizationDataStore { Snapshot = Snapshot(UserStatus.Disabled, [Required]) };
        var evaluator = CreateEvaluator(dataStore);

        var result = await evaluator.EvaluateAsync(Tenant, UserNId, Session, 3, Required, CancellationToken.None);

        Assert.False(result.Allowed);
        Assert.Equal(AuthorizationDenialReason.AccountDisabled, result.Reason);
    }

    [Fact]
    public async Task EvaluateAsync_DisabledUserWithPermission_StillDenied_NoPrivilegeBypass()
    {
        // 持有权限但已禁用:状态门禁优先于权限,禁止特权绕过
        var dataStore = new FakeAuthorizationDataStore { Snapshot = Snapshot(UserStatus.Disabled, [Required]) };
        var denialSink = new FakeDenialSink();
        var evaluator = CreateEvaluator(dataStore, denialSink: denialSink);

        var result = await evaluator.EvaluateAsync(Tenant, UserNId, Session, 3, Required, CancellationToken.None);

        Assert.False(result.Allowed);
        Assert.Equal(AuthorizationDenialReason.AccountDisabled, result.Reason);
        Assert.Equal(AuthorizationDenialReason.AccountDisabled, Assert.Single(denialSink.Denials).Reason);
    }

    [Fact]
    public async Task EvaluateAsync_UnknownUser_DeniesSessionInvalid()
    {
        var dataStore = new FakeAuthorizationDataStore { Snapshot = null };
        var evaluator = CreateEvaluator(dataStore);

        var result = await evaluator.EvaluateAsync(Tenant, UserNId, Session, 3, Required, CancellationToken.None);

        Assert.False(result.Allowed);
        Assert.Equal(AuthorizationDenialReason.SessionInvalid, result.Reason);
    }

    [Fact]
    public async Task EvaluateAsync_DataStoreUnavailable_ThrowsSecurityStoreUnavailable()
    {
        var dataStore = new FakeAuthorizationDataStore { ThrowOnGet = true };
        var evaluator = CreateEvaluator(dataStore);

        await Assert.ThrowsAsync<SecurityStoreUnavailableException>(() =>
            evaluator.EvaluateAsync(Tenant, UserNId, Session, 3, Required, CancellationToken.None));
    }

    [Fact]
    public async Task EvaluateAsync_CacheHitWithPermission_AllowsWithoutDataStore()
    {
        // 版本化缓存命中 → 快速裁决,不落数据库
        var cache = new FakePermissionCache
        {
            UserSnapshot = new UserSecurityCacheEntry(UserStatus.Active),
            Permissions = [Required],
        };
        var dataStore = new FakeAuthorizationDataStore { Snapshot = Snapshot(Active(), [Required]) };
        var evaluator = CreateEvaluator(dataStore, cache);

        var result = await evaluator.EvaluateAsync(Tenant, UserNId, Session, 3, Required, CancellationToken.None);

        Assert.True(result.Allowed);
        Assert.Equal(0, dataStore.GetCount);
    }

    [Fact]
    public async Task EvaluateAsync_CacheHitWithoutPermission_DeniesMissingPermissionWithoutDataStore()
    {
        var cache = new FakePermissionCache
        {
            UserSnapshot = new UserSecurityCacheEntry(UserStatus.Active),
            Permissions = ["identity.user.update"],
        };
        var dataStore = new FakeAuthorizationDataStore { Snapshot = Snapshot(Active(), [Required]) };
        var denialSink = new FakeDenialSink();
        var evaluator = CreateEvaluator(dataStore, cache, denialSink: denialSink);

        var result = await evaluator.EvaluateAsync(Tenant, UserNId, Session, 3, Required, CancellationToken.None);

        Assert.False(result.Allowed);
        Assert.Equal(AuthorizationDenialReason.MissingPermission, result.Reason);
        Assert.Equal(0, dataStore.GetCount);
        Assert.Single(denialSink.Denials);
    }

    [Fact]
    public async Task EvaluateAsync_CacheHitDisabledStatus_DeniesAccountDisabledWithoutDataStore()
    {
        var cache = new FakePermissionCache
        {
            UserSnapshot = new UserSecurityCacheEntry(UserStatus.Disabled),
            Permissions = [Required],
        };
        var dataStore = new FakeAuthorizationDataStore { Snapshot = Snapshot(Active(), [Required]) };
        var evaluator = CreateEvaluator(dataStore, cache);

        var result = await evaluator.EvaluateAsync(Tenant, UserNId, Session, 3, Required, CancellationToken.None);

        Assert.False(result.Allowed);
        Assert.Equal(AuthorizationDenialReason.AccountDisabled, result.Reason);
        Assert.Equal(0, dataStore.GetCount);
    }

    [Fact]
    public async Task EvaluateAsync_PermissionsCacheMiss_DegradesToDataStore()
    {
        // 用户条目命中但权限条目缺失 → 降级数据库装载
        var cache = new FakePermissionCache { UserSnapshot = new UserSecurityCacheEntry(UserStatus.Active) };
        var dataStore = new FakeAuthorizationDataStore { Snapshot = Snapshot(Active(), [Required]) };
        var evaluator = CreateEvaluator(dataStore, cache);

        var result = await evaluator.EvaluateAsync(Tenant, UserNId, Session, 3, Required, CancellationToken.None);

        Assert.True(result.Allowed);
        Assert.Equal(1, dataStore.GetCount);
    }

    [Fact]
    public async Task EvaluateAsync_CacheUnavailable_DegradesToDataStore()
    {
        // 缓存抛异常(Redis 不可用)→ 降级数据库装载,不影响裁决
        var cache = new FakePermissionCache { ThrowOnRead = true };
        var dataStore = new FakeAuthorizationDataStore { Snapshot = Snapshot(Active(), [Required]) };
        var evaluator = CreateEvaluator(dataStore, cache);

        var result = await evaluator.EvaluateAsync(Tenant, UserNId, Session, 3, Required, CancellationToken.None);

        Assert.True(result.Allowed);
        Assert.Equal(1, dataStore.GetCount);
    }

    [Fact]
    public async Task EvaluateAsync_CacheWriteFailure_DoesNotBlockDecision()
    {
        var cache = new FakePermissionCache { ThrowOnSet = true };
        var dataStore = new FakeAuthorizationDataStore { Snapshot = Snapshot(Active(), [Required]) };
        var evaluator = CreateEvaluator(dataStore, cache);

        var result = await evaluator.EvaluateAsync(Tenant, UserNId, Session, 3, Required, CancellationToken.None);

        Assert.True(result.Allowed);
    }

    [Fact]
    public async Task EvaluateAsync_DenialAuditFailure_StillReturnsDenied()
    {
        var dataStore = new FakeAuthorizationDataStore { Snapshot = Snapshot(Active(), ["identity.user.update"]) };
        var denialSink = new FakeDenialSink { ThrowOnWrite = true };
        var evaluator = CreateEvaluator(dataStore, denialSink: denialSink);

        var result = await evaluator.EvaluateAsync(Tenant, UserNId, Session, 3, Required, CancellationToken.None);

        // 审计失败不阻断拒绝裁决
        Assert.False(result.Allowed);
        Assert.Equal(AuthorizationDenialReason.MissingPermission, result.Reason);
    }

    private static UserStatus Active() => UserStatus.Active;

    private static AuthorizationSnapshot Snapshot(UserStatus status, IReadOnlyList<string> permissions, int authVersion = 3)
        => new(Tenant, UserNId, status, authVersion, permissions);

    private static PermissionEvaluator CreateEvaluator(
        FakeAuthorizationDataStore? dataStore = null,
        FakePermissionCache? cache = null,
        FakeRevocationStore? revocation = null,
        FakeDenialSink? denialSink = null)
        => new(
            revocation ?? new FakeRevocationStore(),
            dataStore ?? new FakeAuthorizationDataStore(),
            cache ?? new FakePermissionCache(),
            denialSink ?? new FakeDenialSink(),
            NullLogger<PermissionEvaluator>.Instance);

    private sealed class FakeAuthorizationDataStore : IAuthorizationDataStore
    {
        public AuthorizationSnapshot? Snapshot { get; set; }
        public bool ThrowOnGet { get; set; }
        public int GetCount { get; private set; }

        public async Task<AuthorizationSnapshot?> GetSnapshotAsync(string tenantNId, string userNId, CancellationToken cancellationToken)
        {
            GetCount++;
            if (ThrowOnGet)
            {
                await Task.Yield();
                throw new InvalidOperationException("data store unavailable");
            }

            return Snapshot;
        }
    }

    private sealed class FakePermissionCache : IPermissionCache
    {
        public UserSecurityCacheEntry? UserSnapshot { get; set; }
        public IReadOnlyList<string>? Permissions { get; set; }
        public bool ThrowOnRead { get; set; }
        public bool ThrowOnSet { get; set; }
        public int SetCount { get; private set; }
        public List<AuthorizationSnapshot> SetSnapshots { get; } = [];

        public async Task<UserSecurityCacheEntry?> TryGetUserSnapshotAsync(string tenantNId, string userNId, int authVersion, CancellationToken cancellationToken)
        {
            if (ThrowOnRead)
            {
                await Task.Yield();
                throw new InvalidOperationException("cache unavailable");
            }

            return UserSnapshot;
        }

        public async Task<IReadOnlyList<string>?> TryGetPermissionsAsync(string tenantNId, string userNId, int authVersion, CancellationToken cancellationToken)
        {
            if (ThrowOnRead)
            {
                await Task.Yield();
                throw new InvalidOperationException("cache unavailable");
            }

            return Permissions;
        }

        public async Task SetAsync(AuthorizationSnapshot snapshot, CancellationToken cancellationToken)
        {
            if (ThrowOnSet)
            {
                await Task.Yield();
                throw new InvalidOperationException("cache unavailable");
            }

            SetCount++;
            SetSnapshots.Add(snapshot);
        }

        public Task InvalidateAsync(string tenantNId, string userNId, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    private sealed class FakeRevocationStore : ISessionRevocationStore
    {
        public HashSet<string> Revoked { get; } = [];
        public bool FailOnCheck { get; set; }

        public Task RevokeAsync(string sessionNId, TimeSpan ttl, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public async Task<bool> IsRevokedAsync(string sessionNId, CancellationToken cancellationToken)
        {
            if (FailOnCheck)
            {
                await Task.Yield();
                throw new SecurityStoreUnavailableException();
            }

            return Revoked.Contains(sessionNId);
        }
    }

    private sealed class FakeDenialSink : IAuthorizationDenialSink
    {
        public List<AuthorizationDenial> Denials { get; } = [];
        public bool ThrowOnWrite { get; set; }

        public async Task RecordDenialAsync(AuthorizationDenial denial, CancellationToken cancellationToken)
        {
            if (ThrowOnWrite)
            {
                await Task.Yield();
                throw new InvalidOperationException("audit unavailable");
            }

            Denials.Add(denial);
        }
    }
}
