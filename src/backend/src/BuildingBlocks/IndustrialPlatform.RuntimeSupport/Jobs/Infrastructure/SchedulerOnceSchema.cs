using IndustrialPlatform.SystemData.Infrastructure.Persistence.Migrations;
using IndustrialPlatform.Infrastructure.Database;
using SqlSugar;
namespace IndustrialPlatform.RuntimeSupport.Jobs;

/// <summary>仅追加OneShot所有权列，旧SCHED步骤及DDL保持不变。</summary>
public static class SchedulerOnceSchema
{
    public static SchemaMigrationStep Step { get; } = new("SCHED-004-OneShot", "业务一次性任务固定归属-v1", ApplyAsync);
    public static IReadOnlyList<string> Statements(DbType provider)
    {
        var text = provider == DbType.SqlServer ? "nvarchar" : "varchar";
        string Key(int size) => provider == DbType.Sqlite ? "TEXT" : $"{text}({size})";
        const string index = "ux_scheduler_job_owner";
        var statements = new List<string> { $"ALTER TABLE scheduler_job ADD job_kind {Key(16)} NOT NULL DEFAULT 'Recurring' CHECK(job_kind IN ('Recurring','OneShot'));",
            $"ALTER TABLE scheduler_job ADD owner_service {Key(64)} NULL;", $"ALTER TABLE scheduler_job ADD owner_object_n_id {Key(128)} NULL;",
            provider == DbType.SqlServer ? $"IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.scheduler_job') AND name='{index}') CREATE UNIQUE INDEX {index} ON dbo.scheduler_job(tenant_n_id,owner_service,owner_object_n_id) WHERE owner_service IS NOT NULL AND owner_object_n_id IS NOT NULL;"
                : $"CREATE UNIQUE INDEX IF NOT EXISTS {index} ON scheduler_job(tenant_n_id,owner_service,owner_object_n_id) WHERE owner_service IS NOT NULL AND owner_object_n_id IS NOT NULL;" };
        foreach (var (column, description) in new[] { ("job_kind", "任务触发类型：历史任务默认周期；一次性任务不能编辑周期或启用"), ("owner_service", "一次性任务所属受信服务；普通周期任务为空"), ("owner_object_n_id", "所属服务的业务运行业务标识；同租户同服务唯一防重，非跨库外键") })
        {
            if (provider == DbType.PostgreSQL) statements.Add($"COMMENT ON COLUMN scheduler_job.{column} IS '{description}';");
            if (provider == DbType.SqlServer) statements.Add($"IF NOT EXISTS(SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.scheduler_job') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.scheduler_job'),N'{column}',N'ColumnId') AND name=N'MS_Description') EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'{description}',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'scheduler_job',@level2type=N'COLUMN',@level2name=N'{column}';");
        }
        return statements;
    }
    public static async Task ApplyAsync(ISqlSugarClient db, CancellationToken token)
    {
        var existing = db.DbMaintenance.GetColumnInfosByTableName("scheduler_job", false);
        var statements = Statements(db.CurrentConnectionConfig.DbType);
        var names = new[] { "job_kind", "owner_service", "owner_object_n_id" };
        for (var index = 0; index < statements.Count; index++)
            if (index >= 3 || !existing.Any(field => field.DbColumnName.Equals(names[index], StringComparison.OrdinalIgnoreCase)))
                await db.Ado.ExecuteCommandAsync(statements[index], parameters: null, cancellationToken: token);
    }
}
