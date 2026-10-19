using SqlSugar;

namespace IndustrialPlatform.SystemData.Infrastructure.Persistence.Migrations;

/// <summary>两张自有表的中文物理说明；原生表由供应商管理，不修改其定义。</summary>
internal static class SchedulerSchemaDescriptions
{
    private static readonly IReadOnlyDictionary<string, string> Labels = new Dictionary<string, string>
    {
        ["id"] = "内部主键", ["tenant_n_id"] = "租户业务标识", ["n_id"] = "业务标识", ["created_on"] = "创建时间UTC",
        ["is_frozen"] = "冻结保护", ["is_locked"] = "锁定保护", ["is_deleted"] = "删除墓碑", ["entity_type"] = "领域实体类型",
        ["last_updated_on"] = "最后配置更新时间UTC", ["optimistic_version"] = "乐观版本", ["concurrency_version"] = "并发版本令牌",
        ["name"] = "任务名称", ["description"] = "任务说明", ["handler_key"] = "代码白名单处理器标识", ["parameters_json"] = "处理器参数快照JSON",
        ["status"] = "状态", ["cron_expression"] = "唯一五字段周期计划", ["time_zone_id"] = "周期计划时区", ["timeout_seconds"] = "单次执行超时秒数",
        ["max_attempts"] = "实际尝试总上限含首次", ["retry_safety"] = "代码声明的重放安全性", ["is_system_owned"] = "系统内建任务不可删除",
        ["job_id"] = "所属任务内部主键", ["source"] = "运行来源", ["invocation_key"] = "逻辑调用防重键", ["request_hash"] = "人工命令规范化SHA256",
        ["original_run_n_id"] = "人工重试的原运行业务标识", ["operation_key"] = "业务副作用防重键重试沿用", ["requested_by_user_n_id"] = "人工请求者业务标识",
        ["accepted_on"] = "首次接受时间UTC", ["job_name_snapshot"] = "接受时任务名称快照", ["active_job_id"] = "唯一活动占用不自动过期释放",
        ["execution_token"] = "本次执行资格令牌", ["worker_reference"] = "原生工作器引用", ["attempts_used"] = "实际开始的尝试次数",
        ["started_on"] = "首次实际开始时间UTC", ["deadline_on"] = "本次实际执行截止时间UTC", ["completed_on"] = "已确认结束或未知状态登记时间UTC",
        ["cancel_requested_on"] = "合作取消请求时间UTC", ["result_code"] = "受控业务结果码", ["result_summary_json"] = "结果摘要JSON",
        ["engine_reference"] = "原生逻辑任务引用", ["trace_id"] = "追踪标识", ["state_version"] = "运行状态CAS版本",
    };
    public static string Sql(DbType provider, string table, string columns)
    {
        var title = table == "scheduler_job" ? "调度任务定义及唯一周期计划" : "逻辑运行快照及唯一活动占用";
        if (provider == DbType.Sqlite) return $"-- {title}；字段说明见PF07数据字典。\n";
        var fields = Labels.Where(label => System.Text.RegularExpressions.Regex.IsMatch(columns, $"\\b{label.Key}\\s+(TEXT|INTEGER|uuid|timestamptz|BOOLEAN|BIGINT|uniqueidentifier|datetimeoffset|bit|bigint|nvarchar|varchar)", System.Text.RegularExpressions.RegexOptions.IgnoreCase));
        if (provider == DbType.PostgreSQL) return $"COMMENT ON TABLE {table} IS '{title}';\n" + string.Join('\n', fields.Select(field => $"COMMENT ON COLUMN {table}.{field.Key} IS '{field.Value}';")) + "\n";
        string Description(string? field, string value)
        {
            var column = field is null ? "minor_id=0" : $"minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.{table}'),N'{field}',N'ColumnId')";
            var level2 = field is null ? string.Empty : $", @level2type=N'COLUMN', @level2name=N'{field}'";
            return $"IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.{table}') AND {column} AND name=N'MS_Description') EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'{value}', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'{table}'{level2};\n";
        }
        return Description(null, title) + string.Concat(fields.Select(field => Description(field.Key, field.Value)));
    }
}
