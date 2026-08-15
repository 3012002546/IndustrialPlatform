namespace IndustrialPlatform.SystemData.Domain.Organizations;

/// <summary>
/// 组织移动预览结果(05 方案 §7.2):移动前返回影响摘要,提交命令必须携带摘要对应的
/// <see cref="OrganizationRevision"/> 与双并发版本,过期则 409。
/// </summary>
/// <param name="NId">被移动组织业务标识。</param>
/// <param name="OrganizationRevision">移动成功后树修订号(当前修订 + 1)。</param>
/// <param name="SubtreeOrganizationCount">待移动子树包含的组织数(含移动根)。</param>
/// <param name="SubtreePositionCount">待移动子树包含的岗位数。</param>
/// <param name="SubtreeAssignmentCount">待移动子树包含的任职数。</param>
/// <param name="AffectedCount">影响总数(组织 + 岗位 + 任职)。</param>
/// <param name="PreviewedOn">预览生成时间。</param>
public sealed record OrganizationMovePreview(
    string NId,
    long OrganizationRevision,
    long SubtreeOrganizationCount,
    long SubtreePositionCount,
    long SubtreeAssignmentCount,
    long AffectedCount,
    DateTimeOffset PreviewedOn);
