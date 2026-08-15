namespace IndustrialPlatform.SystemData.Domain.Organizations;

/// <summary>
/// 行政组织类型(05 方案 §2.3.1):固定为公司、部门、科室、班组四类。
/// 父子矩阵:公司只能作为根;部门可位于公司/部门下,科室可位于部门/科室下,
/// 班组可位于部门/科室/班组下;禁止倒置和循环。
/// </summary>
public enum AdministrativeOrganizationType
{
    /// <summary>公司(仅可作为根节点)。</summary>
    Company = 0,

    /// <summary>部门。</summary>
    Department = 1,

    /// <summary>科室。</summary>
    Section = 2,

    /// <summary>班组。</summary>
    Team = 3,
}
