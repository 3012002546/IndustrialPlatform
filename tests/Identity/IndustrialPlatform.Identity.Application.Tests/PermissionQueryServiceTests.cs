using System.Threading;
using System.Threading.Tasks;
using IndustrialPlatform.Identity.Application.Management;
using IndustrialPlatform.Identity.Contracts.Management;
using IndustrialPlatform.Identity.Domain.Permissions;
using Xunit;

namespace IndustrialPlatform.Identity.Application.Tests;

/// <summary>
/// 权限目录树用例测试(§16.3):根节点归一化排序、子节点递归嵌套、孤儿提升为根。
/// </summary>
public sealed class PermissionQueryServiceTests
{
    [Fact]
    public async Task GetTreeAsync_NestedAndOrphan_ReturnsOrderedTree()
    {
        var store = new FakeManagementStore();
        store.Seed(Permission.Create("b.root", "B Root", PermissionType.Menu, null, null));
        store.Seed(Permission.Create("a.root", "A Root", PermissionType.Menu, null, null));
        store.Seed(Permission.Create("a.child", "A Child", PermissionType.Page, "a.root", null));
        store.Seed(Permission.Create("a.grandchild", "A Grandchild", PermissionType.Action, "a.child", null));
        store.Seed(Permission.Create("c.orphan", "C Orphan", PermissionType.Page, "x.missing", null));
        var service = new PermissionQueryService(store);

        var roots = await service.GetTreeAsync(CancellationToken.None);

        // 根节点按规范化标识升序:a.root、b.root、孤儿 c.orphan 提升为根
        Assert.Equal(3, roots.Count);
        Assert.Equal(["a.root", "b.root", "c.orphan"], NIds(roots));

        var aRoot = roots[0];
        Assert.Equal("Menu", aRoot.Type);
        Assert.Null(aRoot.ParentPermissionNId);
        var aChild = Assert.Single(aRoot.Children);
        Assert.Equal("a.child", aChild.PermissionNId);
        Assert.Equal("a.root", aChild.ParentPermissionNId);
        Assert.Equal("Page", aChild.Type);
        var grandchild = Assert.Single(aChild.Children);
        Assert.Equal("a.grandchild", grandchild.PermissionNId);
        Assert.Equal("Action", grandchild.Type);

        Assert.Empty(roots[1].Children);
        Assert.Empty(roots[2].Children);
    }

    [Fact]
    public async Task GetTreeAsync_EmptyStore_ReturnsEmpty()
    {
        var service = new PermissionQueryService(new FakeManagementStore());

        var roots = await service.GetTreeAsync(CancellationToken.None);

        Assert.Empty(roots);
    }

    private static string[] NIds(IReadOnlyList<PermissionTreeNode> nodes)
    {
        var result = new string[nodes.Count];
        for (var i = 0; i < nodes.Count; i++)
        {
            result[i] = nodes[i].PermissionNId;
        }

        return result;
    }
}
