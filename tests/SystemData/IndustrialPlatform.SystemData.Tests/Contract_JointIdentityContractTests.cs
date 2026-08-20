using System.Reflection;
using IndustrialPlatform.Identity.Contracts.Management;
using IndustrialPlatform.SystemData.Contracts.Administration;

namespace IndustrialPlatform.SystemData.Contract.Tests;

/// <summary>
/// SystemData 与真实 Identity 契约联合验证(TASK-SD-006)。
/// 基线:提交 <c>48c5374</c>(feat: add identity management sso and platform shell)。
/// SystemData 不直接依赖 Identity.Contracts(生产);本测试仅以测试期引用真实
/// Identity <see cref="UserSummary"/> 契约核对 <see cref="IdentityUserDirectoryEntryV1"/>
/// 的字段映射,防止 Identity 契约演进后 SystemData 目录视图静默失配。
/// 映射字段:UserNId / LoginName / Name / Status(枚举名字符串)。
/// </summary>
public sealed class JointIdentityContractTests
{
    private const string IdentityBaselineSha = "48c5374";

    /// <summary>SystemData 目录视图声明映射到真实 Identity 契约的字段名。</summary>
    private static readonly (string SystemDataField, string IdentityField)[] MappedFields =
    [
        ("UserNId", "UserNId"),
        ("LoginName", "LoginName"),
        ("Name", "Name"),
        ("Status", "Status"),
    ];

    private static ParameterInfo[] IdentityUserSummaryParameters =>
        typeof(UserSummary).GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .OrderByDescending(constructor => constructor.GetParameters().Length)
            .First()
            .GetParameters();

    private static Dictionary<string, Type> IdentityUserSummaryTypes =>
        IdentityUserSummaryParameters.ToDictionary(
            parameter => parameter.Name!,
            parameter => parameter.ParameterType,
            StringComparer.Ordinal);

    [Fact]
    public void DirectoryEntryMappedFieldsExistInRealIdentityUserSummary()
    {
        var identityParameters = IdentityUserSummaryParameters.Select(parameter => parameter.Name);

        foreach (var (systemDataField, identityField) in MappedFields)
        {
            Assert.True(
                identityParameters.Contains(identityField, StringComparer.Ordinal),
                $"{nameof(IdentityUserDirectoryEntryV1)}.{systemDataField} 声称映射 Identity {nameof(UserSummary)}.{identityField},但基线 {IdentityBaselineSha} 的真实契约无此字段。");
        }
    }

    [Fact]
    public void DirectoryEntryMappedFieldsShareCompatibleTypes()
    {
        foreach (var (systemDataField, identityField) in MappedFields)
        {
            var systemDataType = typeof(IdentityUserDirectoryEntryV1).GetProperty(systemDataField)!.PropertyType;

            Assert.True(
                systemDataType == typeof(string),
                $"{nameof(IdentityUserDirectoryEntryV1)}.{systemDataField} 应为 string,实际 {systemDataType}。");
            IdentityUserSummaryTypes.TryGetValue(identityField, out var identityType);
            Assert.True(
                identityType is not null && identityType == systemDataType,
                $"{nameof(IdentityUserDirectoryEntryV1)}.{systemDataField} 与 Identity {nameof(UserSummary)}.{identityField} 类型不一致({systemDataType} vs {(identityType is null ? "<missing>" : identityType)})。");
        }
    }

    [Fact]
    public void DirectoryEntryStatusIsEnumNameStringNotEnumValue()
    {
        // Identity UserSummary.Status 是 UserStatus 枚举名字符串("Active"/"Disabled"),
        // SystemData 目录视图保持同形状,不引入 JSON 数字依赖。
        Assert.Equal(typeof(string), typeof(IdentityUserDirectoryEntryV1).GetProperty("Status")!.PropertyType);
        Assert.Equal(typeof(string), IdentityUserSummaryTypes["Status"]);
    }

    [Fact]
    public void DirectoryEntryAuthVersionIsSystemDataAdditiveNullable()
    {
        // AuthVersion(SystemData 侧缓存/失效判断)是目录视图增量字段,
        // 不要求 Identity 契约提供,且声明为可空。
        var property = typeof(IdentityUserDirectoryEntryV1).GetProperty("AuthVersion")!;
        Assert.Equal(typeof(string), property.PropertyType);
        Assert.Equal(
            NullabilityState.Nullable,
            new NullabilityInfoContext().Create(property).ReadState);
    }
}
