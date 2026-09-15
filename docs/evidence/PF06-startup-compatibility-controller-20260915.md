# PF06 启动回归主控核对（2026-09-15）

本记录仅覆盖两处启动回归，不恢复整包 PASS，不代表真实云数据库或用户 IDE 宿主已重新启动。

- 非 Standalone 的 ReferenceData `LocalTargetIdentity` 已恢复 HEAD 的三段公式；Standalone 分支保留此前四段公式。迁移账本匹配与漂移拒绝仍保留。
- 独立私有配置已补齐，配置缺失或缺 Database 节在入口明确失败，不回退平台配置。实际宿主启动仍待确认，不能将配置路径存在说成旧数据库文件已存在。
- 开发报告 fresh Release 构建通过，以及配置 5/5、历史标识重复初始化与默认参数比较 2/2；主控未重跑这些命令。

## PostgreSQL 默认 SQL 的历史源码等价核对

验收指出现有 `Default_reference_data_sql_remains_byte_compatible_with_explicit_default_schema` 仅比较当前 SQLite 两种调用，确实不能证明历史 PostgreSQL 文本。主控另行执行只读源码比较，基线为本仓库 HEAD。

覆盖 ReferenceDataMigrations 调度表、其全部十个迁移类，以及 UnitOfMeasureSystemSeed，共 12 个文件。仅将当前源码中的 schema 参数特化为历史默认值：

1. 去掉 `, string schema = "reference_data"` 参数声明。
2. 将 `$"{schema}.` 替换为 `"reference_data.`。
3. 将索引名插值 `{schema}_` 替换为 `reference_data_`。
4. 将 `Sql(postgres, schema)` 替换为 `Sql(postgres)`。

只统一源码文件 CRLF/LF 及文件末尾换行，不删除 SQL 内容或空格。结果：`HEAD default-schema source equivalence: 12 files, 0 mismatches`，命令退出码 0。因此默认 schema 下迁移/seed 生成代码与 HEAD 等价，PostgreSQL 与 SQLite 的其余分支及 SQL 字面量均保持原样。此项为历史源码等价证据，不冒充实际 PostgreSQL 运行或 golden 测试。

本次主控未修改生产源码、测试或数据库；不新增黄金常量测试和编译循环。实际启动若仍失败，应依据新的具体堆栈定位，不更改历史账本或跳过漂移校验。
