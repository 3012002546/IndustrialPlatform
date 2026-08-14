using IndustrialPlatform.SharedKernel.Exceptions;

namespace IndustrialPlatform.SystemData.Domain.DatabaseOrchestration;

/// <summary>
/// 种子声明值对象(TASK-SD-004,蓝图 §3)。绑定稳定 SeedKey/SeedVersion、类别/作用域、
/// 签名种子产物标识/校验和/签名引用、readiness 语义、允许环境与依赖。
/// 仅声明非敏感需求,不含种子内容、Secret、SQL 或命令。
/// <see cref="ToChecksumCanonical"/> 供清单校验和纳入种子声明(漂移判定依据)。
/// </summary>
public sealed class SeedSet
{
    /// <summary>SeedKey 最大长度。</summary>
    public const int SeedKeyMaxLength = 128;

    /// <summary>种子版本最大长度。</summary>
    public const int SeedVersionMaxLength = 64;

    /// <summary>种子产物标识最大长度。</summary>
    public const int SeedArtifactIdMaxLength = 128;

    /// <summary>签名引用最大长度。</summary>
    public const int SignatureMaxLength = 512;

    /// <summary>逗号分隔环境串最大长度。</summary>
    public const int EnvironmentsListMaxLength = 256;

    /// <summary>逗号分隔依赖串最大长度。</summary>
    public const int DependsOnSeedKeysListMaxLength = 1024;

    /// <summary>稳定种子键。</summary>
    public string SeedKey { get; }

    /// <summary>不可变种子版本。</summary>
    public string SeedVersion { get; }

    /// <summary>种子类别。</summary>
    public SeedClass SeedClass { get; }

    /// <summary>种子作用域。</summary>
    public SeedScope Scope { get; }

    /// <summary>种子产物标识(签名 SQL seed bundle 或 initializer bundle)。</summary>
    public string SeedArtifactId { get; }

    /// <summary>种子产物校验和(SHA-256 十六进制)。</summary>
    public string SeedChecksum { get; }

    /// <summary>产物签名引用(仅非敏感引用,绝不保存私钥/凭据)。</summary>
    public string? SeedSignature { get; }

    /// <summary>是否影响 readiness(默认 SystemBaseline 为 true)。</summary>
    public bool RequiredForReadiness { get; }

    /// <summary>允许执行的环境种类(逗号分隔;空表默认全环境)。</summary>
    public string AllowedEnvironments { get; }

    /// <summary>前置迁移版本,未达到则拒绝执行。</summary>
    public string? DependsOnMigrationVersion { get; }

    /// <summary>前置 SeedKey(逗号分隔,同模块;顺序执行依赖)。</summary>
    public string? DependsOnSeedKeys { get; }

    /// <summary>bootstrap 交付策略(仅 SecretBootstrap 有意义)。</summary>
    public BootstrapPolicy BootstrapPolicy { get; }

    /// <summary>由注册清单在解析时创建并校验。</summary>
    internal SeedSet(
        string seedKey,
        string seedVersion,
        SeedClass seedClass,
        SeedScope scope,
        string seedArtifactId,
        string seedChecksum,
        string? seedSignature,
        bool requiredForReadiness,
        string allowedEnvironments,
        string? dependsOnMigrationVersion,
        string? dependsOnSeedKeys,
        BootstrapPolicy bootstrapPolicy)
    {
        SeedKey = DatabaseOrchestrationGuard.RequireTrimmedNonEmpty(
            seedKey, "种子键不能为空。", SeedKeyMaxLength, $"种子键长度不能超过 {SeedKeyMaxLength} 个字符。");
        SeedVersion = DatabaseOrchestrationGuard.RequireTrimmedNonEmpty(
            seedVersion, "种子版本不能为空。", SeedVersionMaxLength, $"种子版本长度不能超过 {SeedVersionMaxLength} 个字符。");
        SeedClass = seedClass;
        Scope = scope;
        SeedArtifactId = DatabaseOrchestrationGuard.RequireTrimmedNonEmpty(
            seedArtifactId, "种子产物标识不能为空。", SeedArtifactIdMaxLength, $"种子产物标识长度不能超过 {SeedArtifactIdMaxLength} 个字符。");
        SeedChecksum = DatabaseOrchestrationGuard.RequireSha256Hex(seedChecksum, "种子产物校验和不能为空。");
        SeedSignature = DatabaseOrchestrationGuard.TrimOrNull(seedSignature, SignatureMaxLength, $"签名引用长度不能超过 {SignatureMaxLength} 个字符。");
        RequiredForReadiness = requiredForReadiness;
        AllowedEnvironments = DatabaseOrchestrationGuard.TrimOrNull(allowedEnvironments, EnvironmentsListMaxLength, $"允许环境串长度不能超过 {EnvironmentsListMaxLength} 个字符。") ?? string.Empty;
        DependsOnMigrationVersion = DatabaseOrchestrationGuard.TrimOrNull(
            dependsOnMigrationVersion, DatabaseProvisionPlan.VersionMaxLength, $"依赖迁移版本长度不能超过 {DatabaseProvisionPlan.VersionMaxLength} 个字符。");
        DependsOnSeedKeys = DatabaseOrchestrationGuard.TrimOrNull(
            dependsOnSeedKeys, DependsOnSeedKeysListMaxLength, $"依赖种子键串长度不能超过 {DependsOnSeedKeysListMaxLength} 个字符。");
        BootstrapPolicy = bootstrapPolicy;

        if (seedClass == SeedClass.EnvironmentSample)
        {
            var forbidden = AllowedEnvironments.Length > 0
                && (ContainsEnvironment(AllowedEnvironments, DatabaseEnvironmentKind.Staging)
                    || ContainsEnvironment(AllowedEnvironments, DatabaseEnvironmentKind.Production));
            if (forbidden)
            {
                throw new ValidationException("EnvironmentSample 种子禁止声明 Staging/Production 环境。");
            }
        }

        if (seedClass != SeedClass.SecretBootstrap && bootstrapPolicy != BootstrapPolicy.FailClosed)
        {
            throw new ValidationException("BootstrapPolicy 仅对 SecretBootstrap 种子有意义。");
        }
    }

    /// <summary>持久化层重建专用构造,不重新校验。</summary>
    internal SeedSet(
        string seedKey,
        string seedVersion,
        SeedClass seedClass,
        SeedScope scope,
        string seedArtifactId,
        string seedChecksum,
        string? seedSignature,
        bool requiredForReadiness,
        string allowedEnvironments,
        string? dependsOnMigrationVersion,
        string? dependsOnSeedKeys,
        BootstrapPolicy bootstrapPolicy,
        bool skipValidation)
    {
        SeedKey = seedKey;
        SeedVersion = seedVersion;
        SeedClass = seedClass;
        Scope = scope;
        SeedArtifactId = seedArtifactId;
        SeedChecksum = seedChecksum;
        SeedSignature = seedSignature;
        RequiredForReadiness = requiredForReadiness;
        AllowedEnvironments = allowedEnvironments;
        DependsOnMigrationVersion = dependsOnMigrationVersion;
        DependsOnSeedKeys = dependsOnSeedKeys;
        BootstrapPolicy = bootstrapPolicy;
    }

    /// <summary>该种子是否允许在给定环境种类执行。</summary>
    public bool IsAllowedIn(DatabaseEnvironmentKind environmentKind) =>
        AllowedEnvironments.Length == 0 || ContainsEnvironment(AllowedEnvironments, environmentKind);

    /// <summary>依赖的前置 SeedKey 集合(去重、去空白)。</summary>
    public IReadOnlyCollection<string> DependencySeedKeys => SplitList(DependsOnSeedKeys);

    /// <summary>生成参与清单校验和计算的规范化文本。</summary>
    internal string ToChecksumCanonical() =>
        string.Join("|",
        [
            SeedKey,
            SeedVersion,
            SeedClass.ToString(),
            Scope.ToString(),
            SeedArtifactId,
            SeedChecksum,
            SeedSignature ?? string.Empty,
            RequiredForReadiness ? "1" : "0",
            AllowedEnvironments,
            DependsOnMigrationVersion ?? string.Empty,
            DependsOnSeedKeys ?? string.Empty,
            BootstrapPolicy.ToString(),
        ]);

    private static bool ContainsEnvironment(string list, DatabaseEnvironmentKind kind) =>
        SplitList(list).Contains(kind.ToString(), StringComparer.OrdinalIgnoreCase);

    private static string[] SplitList(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
