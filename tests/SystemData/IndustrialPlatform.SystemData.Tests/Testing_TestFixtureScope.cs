using IndustrialPlatform.SystemData.Application.DatabaseOrchestration.Runner;
using IndustrialPlatform.SystemData.Contracts.DatabaseOrchestration;
using IndustrialPlatform.SystemData.Infrastructure.DatabaseOrchestration.Runner;
using Microsoft.Extensions.Options;

namespace IndustrialPlatform.SystemData.Testing;

/// <summary>
/// fixture 组合根(TASK-SD-004):临时产物根目录 + SQLite 目标库 + 签名产物存储/校验 +
/// 两类执行器 + 假 Secret Provider(隔离 sink)。驱动执行器/双账本路径用 <see cref="Database"/>;
/// 驱动注册/幂等清单经 <see cref="BuildManifest"/>。Dispose 释放临时产物与临时库(不触仓库文件)。
/// </summary>
public sealed class TestFixtureScope : IDisposable
{
    /// <summary>初始化组合根(临时产物目录与临时目标库)。</summary>
    public TestFixtureScope()
    {
        ArtifactRoot = Path.Combine(Path.GetTempPath(), $"industrial-platform-artifacts-{Guid.NewGuid():N}");
        Directory.CreateDirectory(ArtifactRoot);
        Database = new TestTargetDatabase();
        ArtifactStore = new FileSystemArtifactStore(
            Options.Create(new DatabaseOperationRunnerOptions { ArtifactRootPath = ArtifactRoot }));
        Verifier = new MigrationArtifactChecksumVerifier();
        SqlBundleExecutor = new SqlSeedBundleExecutor();
        InitializerExecutor = new ServiceInitializerExecutor();
        SecretProvider = new TestSeedSecretProvider();
    }

    /// <summary>临时产物根目录(FileSystemArtifactStore 读取)。</summary>
    public string ArtifactRoot { get; }

    /// <summary>SQLite 目标库与双账本路径帮助器。</summary>
    public TestTargetDatabase Database { get; }

    /// <summary>签名产物文件存储(迁移/种子)。</summary>
    public FileSystemArtifactStore ArtifactStore { get; }

    /// <summary>迁移/种子产物校验(内容派生校验和 + 签名引用)。</summary>
    public MigrationArtifactChecksumVerifier Verifier { get; }

    /// <summary>签名 SQL seed bundle 执行器。</summary>
    public SqlSeedBundleExecutor SqlBundleExecutor { get; }

    /// <summary>服务 initializer 执行器(白名单)。</summary>
    public ServiceInitializerExecutor InitializerExecutor { get; }

    /// <summary>假 Secret Provider(隔离 sink,secretRef=SeedKey)。</summary>
    public TestSeedSecretProvider SecretProvider { get; }

    /// <summary>写入模块全部产物并返回含校验和的产物包。</summary>
    public TestArtifactBundle WriteModule(TestModuleSpec module) => TestArtifactWriter.WriteModule(ArtifactRoot, module);

    /// <summary>由模块与已写入产物构建注册清单(v2,含 SeedSets 声明;checksum/签名与产物一致)。</summary>
    public static ServiceInitializationManifestV2 BuildManifest(TestModuleSpec module, TestArtifactBundle bundle) => new()
    {
        ServiceKey = TestInitializableService.ServiceKey,
        ModuleKey = module.ModuleKey,
        LogicalDatabaseName = TestInitializableService.LogicalDatabaseName,
        Provider = "Sqlite",
        TopologyMode = "PerService",
        MigrationArtifactId = module.MigrationArtifactId,
        RequestedVersion = module.RequestedVersion,
        ArtifactChecksum = bundle.Migration.Checksum,
        ArtifactSignature = TestInitializableService.SignatureRef,
        DesiredState = "SourceOfTruth",
        AutoProvision = true,
        AutoMigrate = true,
        UsesServiceInitializer = false,
        SeedSets = bundle.Seeds
            .Select(seedArtifact =>
            {
                var spec = module.Seeds.Single(seed => seed.SeedArtifactId == seedArtifact.ArtifactId);
                return new SeedSetV1
                {
                    SeedKey = spec.SeedKey,
                    SeedVersion = spec.SeedVersion,
                    SeedClass = spec.SeedClass.ToString(),
                    Scope = spec.Scope.ToString(),
                    SeedArtifactId = spec.SeedArtifactId,
                    SeedChecksum = seedArtifact.Checksum,
                    SeedSignature = TestInitializableService.SignatureRef,
                    RequiredForReadiness = spec.RequiredForReadiness,
                    AllowedEnvironments = null,
                    DependsOnMigrationVersion = module.RequestedVersion,
                    DependsOnSeedKeys = null,
                    BootstrapPolicy = spec.BootstrapPolicy.ToString(),
                };
            })
            .ToList(),
    };

    /// <inheritdoc />
    public void Dispose()
    {
        Database.Dispose();
        if (Directory.Exists(ArtifactRoot))
        {
            Directory.Delete(ArtifactRoot, recursive: true);
        }
    }
}
