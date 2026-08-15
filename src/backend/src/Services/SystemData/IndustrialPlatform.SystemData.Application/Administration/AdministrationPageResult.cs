namespace IndustrialPlatform.SystemData.Application.Administration;

/// <summary>
/// 管理用例分页查询结果(05 方案 §9.3 过滤/分页)。与编排层
/// <see cref="DatabaseOrchestration.DatabaseOrchestrationPageResult{T}"/> 同模式,
/// 由 Api 控制器转换为 <c>IndustrialPlatform.Web.Results.PageResult{T}</c> 信封。
/// </summary>
/// <typeparam name="T">列表元素类型。</typeparam>
public sealed record AdministrationPageResult<T>(IReadOnlyList<T> Items, long Total, int PageIndex, int PageSize);
