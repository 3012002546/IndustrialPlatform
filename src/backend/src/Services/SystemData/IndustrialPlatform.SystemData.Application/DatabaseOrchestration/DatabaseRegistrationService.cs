using IndustrialPlatform.SharedKernel.Exceptions;
using IndustrialPlatform.SystemData.Application.DatabaseOrchestration.Internal;
using IndustrialPlatform.SystemData.Contracts.DatabaseOrchestration;
using IndustrialPlatform.SystemData.Domain.DatabaseOrchestration;
using IndustrialPlatform.SharedKernel.Topology;
using IndustrialPlatform.SystemData.Domain.Topology;

namespace IndustrialPlatform.SystemData.Application.DatabaseOrchestration;

/// <summary>注册清单用例端口。</summary>
public interface IRegistrationService
{
    /// <summary>注册/重注册版本化清单(幂等:同清单校验和返回现有)。</summary>
    Task<DatabaseRegistrationV1> RegisterAsync(string tenantNId, string actorUserNId, DatabaseRegistrationManifestV1 manifest, CancellationToken cancellationToken);

    /// <summary>v2:注册/重注册带模块与种子声明的初始化清单(按 (ServiceKey, ModuleKey) 粒度)。</summary>
    Task<DatabaseRegistrationV1> RegisterModuleAsync(string tenantNId, string actorUserNId, ServiceInitializationManifestV2 manifest, CancellationToken cancellationToken);

    /// <summary>按服务键查询注册清单;不存在抛 404。</summary>
    Task<DatabaseRegistrationV1> GetAsync(string tenantNId, string serviceKey, CancellationToken cancellationToken);

    /// <summary>v2:按 (ServiceKey, ModuleKey) 查询注册清单;不存在抛 404。</summary>
    Task<DatabaseRegistrationV1> GetModuleAsync(string tenantNId, string serviceKey, string moduleKey, CancellationToken cancellationToken);

    /// <summary>更新期望数据状态。</summary>
    Task<DatabaseRegistrationV1> UpdateDesiredStateAsync(string tenantNId, string actorUserNId, string serviceKey, string desiredState, CancellationToken cancellationToken);

    /// <summary>分页查询注册清单(ServiceKey/ModuleKey 可选过滤;moduleKey 缺省不限模块)。</summary>
    Task<DatabaseOrchestrationPageResult<DatabaseRegistrationSummaryV1>> ListAsync(
        string tenantNId,
        string? serviceKey,
        int pageIndex,
        int pageSize,
        CancellationToken cancellationToken,
        string? moduleKey = null);
}

/// <summary>
/// 注册清单用例(05 方案 §7.1.1、§9.2 注册端点)。环境与拓扑身份只来自可信拓扑,
/// 物理目标由 <see cref="DatabaseTopologyResolver"/> 解析;重注册冲突在应用层裁决
/// (同清单校验和幂等 / 同版本不同产物校验和 SD_DB_ARTIFACT_INVALID / 版本不同则更新)。
/// </summary>
public sealed class DatabaseRegistrationService : IRegistrationService
{
    private readonly IDatabaseOrchestrationStore _store;
    private readonly IDatabaseTopologyProvider _topologyProvider;

    public DatabaseRegistrationService(
        IDatabaseOrchestrationStore store,
        IDatabaseTopologyProvider topologyProvider)
    {
        _store = store;
        _topologyProvider = topologyProvider;
    }

    /// <inheritdoc />
    public async Task<DatabaseRegistrationV1> RegisterAsync(
        string tenantNId,
        string actorUserNId,
        DatabaseRegistrationManifestV1 manifest,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        var topology = _topologyProvider.GetTopology();
        var environmentNId = topology.EnvironmentName;
        var serviceKey = DatabaseOrchestrationInput.Require(manifest.ServiceKey, "服务键不能为空。");
        var logicalDatabaseName = DatabaseOrchestrationInput.Require(manifest.LogicalDatabaseName, "逻辑库名不能为空。");
        var requestedVersion = DatabaseOrchestrationInput.Require(manifest.RequestedVersion, "请求版本不能为空。");
        var artifactChecksum = DatabaseOrchestrationInput.Require(manifest.ArtifactChecksum, "产物校验和不能为空。");

        var provider = ResolveProvider(manifest.Provider, topology);
        var providerEnum = ParseProviderEnum(provider);
        var topologyMode = topology.Mode.ToString();
        EnsureTopologyModeMatch(manifest.TopologyMode, topologyMode);
        var target = ResolveTarget(topology, serviceKey, providerEnum, logicalDatabaseName);

        var topologyRevision = DatabaseTopologyFingerprint.ComputeTopologyRevision(topology);
        var manifestChecksum = RequestHasher.HashManifest(manifest);
        var desiredState = ParseDesiredState(manifest.DesiredState);
        var autoProvision = manifest.AutoProvision ?? false;
        var autoMigrate = manifest.AutoMigrate ?? false;
        var ownerNId = string.IsNullOrWhiteSpace(manifest.OwnerNId) ? actorUserNId : manifest.OwnerNId.Trim();
        var manifestVersion = string.IsNullOrWhiteSpace(manifest.ManifestVersion) ? "1" : manifest.ManifestVersion.Trim();
        var migrationArtifactId = string.IsNullOrWhiteSpace(manifest.MigrationArtifactId)
            ? $"default-{serviceKey}"
            : manifest.MigrationArtifactId.Trim();

        var existing = await _store.GetRegistrationAsync(tenantNId, environmentNId, serviceKey, cancellationToken);
        if (existing is not null)
        {
            if (string.Equals(existing.ManifestChecksum, manifestChecksum, StringComparison.Ordinal))
            {
                return ToRegistrationV1(existing);
            }

            if (string.Equals(existing.MigrationVersion, requestedVersion, StringComparison.Ordinal)
                && !string.Equals(existing.ArtifactChecksum, artifactChecksum, StringComparison.Ordinal))
            {
                throw new ArtifactInvalidException();
            }

            var expectedOptimisticVersion = existing.OptimisticVersion;
            var expectedConcurrencyVersion = existing.ConcurrencyVersion;
            existing.ReRegister(
                provider,
                logicalDatabaseName,
                target.PhysicalDatabaseName,
                target.IsSharedPhysicalDatabase,
                topologyMode,
                topologyRevision,
                migrationArtifactId,
                requestedVersion,
                artifactChecksum,
                manifest.ArtifactSignature,
                desiredState,
                autoProvision,
                autoMigrate,
                manifestVersion,
                manifestChecksum);
            existing.ClearDomainEvents();
            await WriteGuard.ExecuteAsync(
                () => _store.UpdateRegistrationAsync(existing, expectedOptimisticVersion, expectedConcurrencyVersion, cancellationToken));
            return ToRegistrationV1(existing);
        }

        var registration = DatabaseRegistration.Register(
            tenantNId,
            environmentNId,
            serviceKey,
            provider,
            logicalDatabaseName,
            target.PhysicalDatabaseName,
            target.IsSharedPhysicalDatabase,
            topologyMode,
            topologyRevision,
            migrationArtifactId,
            requestedVersion,
            artifactChecksum,
            manifest.ArtifactSignature,
            ownerNId,
            desiredState,
            autoProvision,
            autoMigrate,
            manifestVersion,
            manifestChecksum);
        registration.ClearDomainEvents();
        await WriteGuard.ExecuteAsync(
            () => _store.AddRegistrationAsync(registration, cancellationToken));
        return ToRegistrationV1(registration);
    }

    /// <inheritdoc />
    public async Task<DatabaseRegistrationV1> RegisterModuleAsync(
        string tenantNId,
        string actorUserNId,
        ServiceInitializationManifestV2 manifest,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        var topology = _topologyProvider.GetTopology();
        var environmentNId = topology.EnvironmentName;
        var serviceKey = DatabaseOrchestrationInput.Require(manifest.ServiceKey, "服务键不能为空。");
        var moduleKey = DatabaseOrchestrationInput.Require(manifest.ModuleKey, "模块标识不能为空。");
        var logicalDatabaseName = DatabaseOrchestrationInput.Require(manifest.LogicalDatabaseName, "逻辑库名不能为空。");
        var requestedVersion = DatabaseOrchestrationInput.Require(manifest.RequestedVersion, "请求版本不能为空。");
        var artifactChecksum = DatabaseOrchestrationInput.Require(manifest.ArtifactChecksum, "产物校验和不能为空。");
        var seedSets = MapSeedSets(manifest.SeedSets);
        EnsureSampleEnvironmentAllowed(environmentNId, seedSets);

        var provider = ResolveProvider(manifest.Provider, topology);
        var providerEnum = ParseProviderEnum(provider);
        var topologyMode = topology.Mode.ToString();
        EnsureTopologyModeMatch(manifest.TopologyMode, topologyMode);
        var target = ResolveTarget(topology, serviceKey, providerEnum, logicalDatabaseName);

        var topologyRevision = DatabaseTopologyFingerprint.ComputeTopologyRevision(topology);
        var manifestChecksum = RequestHasher.HashManifestV2(manifest);
        var desiredState = ParseDesiredState(manifest.DesiredState);
        var autoProvision = manifest.AutoProvision ?? false;
        var autoMigrate = manifest.AutoMigrate ?? false;
        var ownerNId = string.IsNullOrWhiteSpace(manifest.OwnerNId) ? actorUserNId : manifest.OwnerNId.Trim();
        var manifestVersion = string.IsNullOrWhiteSpace(manifest.ManifestVersion) ? "1" : manifest.ManifestVersion.Trim();
        var migrationArtifactId = string.IsNullOrWhiteSpace(manifest.MigrationArtifactId)
            ? $"default-{serviceKey}"
            : manifest.MigrationArtifactId.Trim();

        var existing = await _store.GetRegistrationAsync(tenantNId, environmentNId, serviceKey, moduleKey, cancellationToken);
        if (existing is not null)
        {
            if (string.Equals(existing.ManifestChecksum, manifestChecksum, StringComparison.Ordinal))
            {
                return ToRegistrationV1(existing);
            }

            if (string.Equals(existing.MigrationVersion, requestedVersion, StringComparison.Ordinal)
                && !string.Equals(existing.ArtifactChecksum, artifactChecksum, StringComparison.Ordinal))
            {
                throw new ArtifactInvalidException();
            }

            EnsureNoSeedDrift(existing.SeedSets, seedSets);

            var expectedOptimisticVersion = existing.OptimisticVersion;
            var expectedConcurrencyVersion = existing.ConcurrencyVersion;
            existing.ReRegister(
                provider,
                logicalDatabaseName,
                target.PhysicalDatabaseName,
                target.IsSharedPhysicalDatabase,
                topologyMode,
                topologyRevision,
                migrationArtifactId,
                requestedVersion,
                artifactChecksum,
                manifest.ArtifactSignature,
                desiredState,
                autoProvision,
                autoMigrate,
                manifestVersion,
                manifestChecksum,
                seedSets);
            existing.ClearDomainEvents();
            await WriteGuard.ExecuteAsync(
                () => _store.UpdateRegistrationAsync(existing, expectedOptimisticVersion, expectedConcurrencyVersion, cancellationToken));
            return ToRegistrationV1(existing);
        }

        var registration = DatabaseRegistration.Register(
            tenantNId,
            environmentNId,
            serviceKey,
            provider,
            logicalDatabaseName,
            target.PhysicalDatabaseName,
            target.IsSharedPhysicalDatabase,
            topologyMode,
            topologyRevision,
            migrationArtifactId,
            requestedVersion,
            artifactChecksum,
            manifest.ArtifactSignature,
            ownerNId,
            desiredState,
            autoProvision,
            autoMigrate,
            manifestVersion,
            manifestChecksum,
            seedSets,
            moduleKey);
        registration.ClearDomainEvents();
        await WriteGuard.ExecuteAsync(
            () => _store.AddRegistrationAsync(registration, cancellationToken));
        return ToRegistrationV1(registration);
    }

    /// <inheritdoc />
    public async Task<DatabaseRegistrationV1> GetAsync(string tenantNId, string serviceKey, CancellationToken cancellationToken)
    {
        var topology = _topologyProvider.GetTopology();
        var registration = await _store.GetRegistrationAsync(tenantNId, topology.EnvironmentName, serviceKey, cancellationToken);
        return registration is null
            ? throw new RegistrationNotFoundException()
            : ToRegistrationV1(registration);
    }

    /// <inheritdoc />
    public async Task<DatabaseRegistrationV1> GetModuleAsync(
        string tenantNId,
        string serviceKey,
        string moduleKey,
        CancellationToken cancellationToken)
    {
        var topology = _topologyProvider.GetTopology();
        var registration = await _store.GetRegistrationAsync(
            tenantNId, topology.EnvironmentName, serviceKey, moduleKey, cancellationToken);
        return registration is null
            ? throw new RegistrationNotFoundException()
            : ToRegistrationV1(registration);
    }

    /// <inheritdoc />
    public async Task<DatabaseRegistrationV1> UpdateDesiredStateAsync(
        string tenantNId,
        string actorUserNId,
        string serviceKey,
        string desiredState,
        CancellationToken cancellationToken)
    {
        var topology = _topologyProvider.GetTopology();
        var registration = await _store.GetRegistrationAsync(tenantNId, topology.EnvironmentName, serviceKey, cancellationToken)
            ?? throw new RegistrationNotFoundException();

        var expectedOptimisticVersion = registration.OptimisticVersion;
        var expectedConcurrencyVersion = registration.ConcurrencyVersion;
        registration.UpdateDesiredState(ParseDesiredState(desiredState));
        registration.ClearDomainEvents();
        await WriteGuard.ExecuteAsync(
            () => _store.UpdateRegistrationAsync(registration, expectedOptimisticVersion, expectedConcurrencyVersion, cancellationToken));
        return ToRegistrationV1(registration);
    }

    /// <inheritdoc />
    public async Task<DatabaseOrchestrationPageResult<DatabaseRegistrationSummaryV1>> ListAsync(
        string tenantNId,
        string? serviceKey,
        int pageIndex,
        int pageSize,
        CancellationToken cancellationToken,
        string? moduleKey = null)
    {
        var filter = new RegistrationListFilter(
            tenantNId,
            string.IsNullOrWhiteSpace(serviceKey) ? null : serviceKey.Trim(),
            string.IsNullOrWhiteSpace(moduleKey) ? null : moduleKey.Trim(),
            pageIndex,
            pageSize);
        var page = await _store.QueryRegistrationsAsync(filter, cancellationToken);
        return new DatabaseOrchestrationPageResult<DatabaseRegistrationSummaryV1>(
            page.Items.Select(ToSummaryV1).ToList(),
            page.Total,
            page.PageIndex,
            page.PageSize);
    }

    private static DatabaseRegistrationSummaryV1 ToSummaryV1(DatabaseRegistration registration) => new()
    {
        ServiceKey = registration.ServiceKey,
        ModuleKey = registration.ModuleKey,
        LogicalDatabaseName = registration.LogicalDatabaseName,
        Provider = registration.Provider,
        MigrationVersion = registration.MigrationVersion,
        DesiredState = registration.DesiredState.ToString(),
        Status = registration.Status.ToString(),
        TopologyRevision = registration.TopologyRevision,
        RegisteredOn = registration.CreatedOn,
        LastUpdatedOn = registration.LastUpdatedOn,
    };

    private static DatabaseRegistrationV1 ToRegistrationV1(DatabaseRegistration registration) => new()
    {
        TenantNId = registration.TenantNId,
        EnvironmentNId = registration.EnvironmentNId,
        ServiceKey = registration.ServiceKey,
        ModuleKey = registration.ModuleKey,
        Provider = registration.Provider,
        LogicalDatabaseName = registration.LogicalDatabaseName,
        PhysicalDatabaseName = registration.PhysicalDatabaseName,
        IsSharedPhysicalDatabase = registration.IsSharedPhysicalDatabase,
        TopologyMode = registration.TopologyMode,
        TopologyRevision = registration.TopologyRevision,
        MigrationArtifactId = registration.MigrationArtifactId,
        MigrationVersion = registration.MigrationVersion,
        ArtifactChecksum = registration.ArtifactChecksum,
        ArtifactSignature = registration.ArtifactSignature,
        OwnerNId = registration.OwnerNId,
        DesiredState = registration.DesiredState.ToString(),
        AutoProvision = registration.AutoProvision,
        AutoMigrate = registration.AutoMigrate,
        ManifestVersion = registration.ManifestVersion,
        ManifestChecksum = registration.ManifestChecksum,
        Status = registration.Status.ToString(),
        SeedSets = registration.SeedSets.Select(ToSeedSetV1).ToList(),
        RegisteredOn = registration.CreatedOn,
        LastUpdatedOn = registration.LastUpdatedOn,
    };

    private static SeedSetV1 ToSeedSetV1(SeedSet seed) => new()
    {
        SeedKey = seed.SeedKey,
        SeedVersion = seed.SeedVersion,
        SeedClass = seed.SeedClass.ToString(),
        Scope = seed.Scope.ToString(),
        SeedArtifactId = seed.SeedArtifactId,
        SeedChecksum = seed.SeedChecksum,
        SeedSignature = seed.SeedSignature,
        RequiredForReadiness = seed.RequiredForReadiness,
        AllowedEnvironments = seed.AllowedEnvironments,
        DependsOnMigrationVersion = seed.DependsOnMigrationVersion,
        DependsOnSeedKeys = seed.DependsOnSeedKeys,
        BootstrapPolicy = seed.BootstrapPolicy.ToString(),
    };

    /// <summary>v2:契约种子声明 → 领域值对象(逐条校验;缺省类别 SystemBaseline/作用域 System)。</summary>
    private static List<SeedSet> MapSeedSets(IReadOnlyCollection<SeedSetV1>? seedSets)
    {
        if (seedSets is null || seedSets.Count == 0)
        {
            return [];
        }

        return seedSets.Select(seed =>
        {
            var seedClass = DatabaseOrchestrationInput.ParseOptionalEnum<SeedClass>(seed.SeedClass, nameof(SeedClass))
                            ?? SeedClass.SystemBaseline;
            return new SeedSet(
                DatabaseOrchestrationInput.Require(seed.SeedKey, "种子键不能为空。"),
                DatabaseOrchestrationInput.Require(seed.SeedVersion, "种子版本不能为空。"),
                seedClass,
                DatabaseOrchestrationInput.ParseOptionalEnum<SeedScope>(seed.Scope, nameof(SeedScope)) ?? SeedScope.System,
                DatabaseOrchestrationInput.Require(seed.SeedArtifactId, "种子产物标识不能为空。"),
                DatabaseOrchestrationInput.Require(seed.SeedChecksum, "种子产物校验和不能为空。"),
                DatabaseOrchestrationInput.TrimOrNull(seed.SeedSignature),
                seed.RequiredForReadiness ?? seedClass == SeedClass.SystemBaseline,
                seed.AllowedEnvironments ?? string.Empty,
                DatabaseOrchestrationInput.TrimOrNull(seed.DependsOnMigrationVersion),
                DatabaseOrchestrationInput.TrimOrNull(seed.DependsOnSeedKeys),
                DatabaseOrchestrationInput.ParseOptionalEnum<BootstrapPolicy>(seed.BootstrapPolicy, nameof(BootstrapPolicy)) ?? BootstrapPolicy.FailClosed);
        }).ToList();
    }

    /// <summary>同 SeedKey/SeedVersion 不同 checksum → 种子漂移拒绝(幂等仅由清单整体校验和判定)。</summary>
    private static void EnsureNoSeedDrift(IReadOnlyCollection<SeedSet> existing, IReadOnlyCollection<SeedSet> incoming)
    {
        var incomingByKey = incoming.ToDictionary(seed => seed.SeedKey, StringComparer.Ordinal);
        foreach (var seed in existing)
        {
            if (incomingByKey.TryGetValue(seed.SeedKey, out var next)
                && string.Equals(seed.SeedVersion, next.SeedVersion, StringComparison.Ordinal)
                && !string.Equals(seed.SeedChecksum, next.SeedChecksum, StringComparison.Ordinal))
            {
                throw new SeedChecksumDriftException(seed.SeedKey);
            }
        }
    }

    /// <summary>EnvironmentSample 注册层拒绝 Staging/Production(蓝图 §12.3 三层门禁第一层)。</summary>
    private static void EnsureSampleEnvironmentAllowed(string environmentNId, IReadOnlyCollection<SeedSet> seedSets)
    {
        var kind = EnvironmentPolicyResolver.ParseEnvironmentKind(environmentNId);
        if (kind is DatabaseEnvironmentKind.Staging or DatabaseEnvironmentKind.Production
            && seedSets.Any(seed => seed.SeedClass == SeedClass.EnvironmentSample))
        {
            throw new SampleEnvironmentForbiddenException();
        }
    }

    private static string ResolveProvider(string? requested, DatabaseTopology topology)
    {
        if (!string.IsNullOrWhiteSpace(requested))
        {
            var trimmed = requested.Trim();
            if (trimmed is "Sqlite" or "PostgreSQL")
            {
                return trimmed;
            }

            throw new ProviderUnsupportedException(requested);
        }

        return string.Equals(topology.EnvironmentName, "Development", StringComparison.Ordinal)
               && !string.IsNullOrWhiteSpace(topology.SharedSqliteFile)
            ? "Sqlite"
            : "PostgreSQL";
    }

    private static DatabaseProvider ParseProviderEnum(string provider) => provider switch
    {
        "Sqlite" => DatabaseProvider.Sqlite,
        "PostgreSQL" => DatabaseProvider.PostgreSQL,
        _ => throw new ProviderUnsupportedException(provider),
    };

    private static void EnsureTopologyModeMatch(string? requested, string topologyMode)
    {
        if (!string.IsNullOrWhiteSpace(requested)
            && !string.Equals(requested.Trim(), topologyMode, StringComparison.Ordinal))
        {
            throw new TopologyUnsupportedException($"请求的拓扑模式与受信任拓扑不一致:期望 {topologyMode}。");
        }
    }

    private static ResolvedDatabaseTarget ResolveTarget(
        DatabaseTopology topology,
        string serviceKey,
        DatabaseProvider provider,
        string logicalDatabaseName)
    {
        if (topology.Mode == DatabaseTopologyMode.Shared)
        {
            if (!string.Equals(topology.EnvironmentName, "Development", StringComparison.Ordinal))
            {
                throw new SharedEnvironmentForbiddenException("Shared 拓扑仅允许 Development 环境。");
            }

            var physicalName = provider == DatabaseProvider.Sqlite ? topology.SharedSqliteFile : topology.SharedDatabaseName;
            if (string.IsNullOrWhiteSpace(physicalName))
            {
                throw new SharedTargetMissingException("Shared 拓扑缺少目标库名。");
            }
        }

        try
        {
            return DatabaseTopologyResolver.Resolve(topology, serviceKey, provider, logicalDatabaseName);
        }
        catch (ValidationException)
        {
            throw new TopologyUnsupportedException("不支持的数据库拓扑或提供程序。");
        }
        catch (BusinessException ex)
        {
            throw new SharedEnvironmentForbiddenException(ex.Message);
        }
    }

    private static DesiredState ParseDesiredState(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? DesiredState.SourceOfTruth
            : DatabaseOrchestrationInput.ParseEnum<DesiredState>(value, nameof(DesiredState));
}
