using SqlSugar;

namespace IndustrialPlatform.SystemData.Infrastructure.Persistence.Migrations;

/// <summary>
/// 描述一个幂等的数据库迁移步骤。
/// </summary>
/// <param name="Id">全局唯一且稳定的迁移标识,用于账本去重;创建后不得修改。</param>
/// <param name="Description">迁移说明。</param>
/// <param name="Apply">在事务内执行的迁移动作。</param>
public sealed record SchemaMigrationStep(
    string Id,
    string Description,
    Func<ISqlSugarClient, CancellationToken, Task> Apply);
