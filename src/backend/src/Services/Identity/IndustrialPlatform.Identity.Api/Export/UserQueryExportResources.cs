using System.Globalization;

namespace IndustrialPlatform.Identity.Api.Export;

internal static class UserQueryExportResources
{
    private static readonly Dictionary<string, IReadOnlyDictionary<string, string>> Titles =
        new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["zh-CN"] = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["userNId"] = "用户标识",
                ["loginName"] = "登录名",
                ["name"] = "姓名",
                ["email"] = "邮箱",
                ["phone"] = "手机号",
                ["status"] = "状态",
                ["tenantNId"] = "租户标识",
                ["createdOn"] = "创建时间",
                ["lastLoginOn"] = "最近登录",
                ["mustChangePassword"] = "需改密",
                ["directRoleNIds"] = "直接角色",
                ["groupRoleNIds"] = "组角色",
                ["effectiveRoleNIds"] = "有效角色",
                ["effectiveRoleCount"] = "有效角色数",
                ["optimisticVersion"] = "乐观版本",
                ["concurrencyVersion"] = "并发版本",
                ["isDeleted"] = "已删除",
            },
            ["en-US"] = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["userNId"] = "User ID",
                ["loginName"] = "Login name",
                ["name"] = "Name",
                ["email"] = "Email",
                ["phone"] = "Phone",
                ["status"] = "Status",
                ["tenantNId"] = "Tenant ID",
                ["createdOn"] = "Created on",
                ["lastLoginOn"] = "Last login",
                ["mustChangePassword"] = "Password change required",
                ["directRoleNIds"] = "Direct roles",
                ["groupRoleNIds"] = "Group roles",
                ["effectiveRoleNIds"] = "Effective roles",
                ["effectiveRoleCount"] = "Effective role count",
                ["optimisticVersion"] = "Optimistic version",
                ["concurrencyVersion"] = "Concurrency version",
                ["isDeleted"] = "Deleted",
            },
        };

    internal static string GetTitle(string field, CultureInfo culture)
        => Titles[culture.Name].TryGetValue(field, out var title) ? title : field;
}
