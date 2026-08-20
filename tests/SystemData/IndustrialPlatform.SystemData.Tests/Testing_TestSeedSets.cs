using IndustrialPlatform.SystemData.Application.DatabaseOrchestration.Runner;
using IndustrialPlatform.SystemData.Domain.DatabaseOrchestration;

namespace IndustrialPlatform.SystemData.Testing;

/// <summary>
/// fixture SeedSets 构造器(TASK-SD-004):按模块规格与已写入产物包构建领域 <see cref="SeedSet"/> 集合,
/// 校验和/签名引用与产物一致、依赖版本绑定模块请求版本(与 <see cref="TestFixtureScope.BuildManifest"/> 同源,
/// 供以领域对象直接驱动注册/计划/Apply 的验收夹具使用)。
/// </summary>
public static class TestSeedSets
{
    /// <summary>按模块规格与产物包构建全部种子集(每个产物匹配同 ArtifactId 的种子规格)。</summary>
    public static IReadOnlyList<SeedSet> Build(TestModuleSpec module, TestArtifactBundle bundle)
    {
        return bundle.Seeds
            .Select(artifact => Build(module, module.Seeds.Single(spec => spec.SeedArtifactId == artifact.ArtifactId), artifact))
            .ToList();
    }

    /// <summary>构建单条种子集(产物校验和/签名引用;绑定模块请求版本)。</summary>
    public static SeedSet Build(TestModuleSpec module, TestSeedSpec spec, SeedArtifact artifact) => new(
        spec.SeedKey,
        spec.SeedVersion,
        spec.SeedClass,
        spec.Scope,
        spec.SeedArtifactId,
        artifact.Checksum,
        TestInitializableService.SignatureRef,
        spec.RequiredForReadiness,
        allowedEnvironments: string.Empty,
        dependsOnMigrationVersion: module.RequestedVersion,
        dependsOnSeedKeys: null,
        spec.BootstrapPolicy);
}
