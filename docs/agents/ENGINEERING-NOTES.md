# Industrial Platform 按需工程笔记

本文件保存跨任务复用但不应自动加载的工程陷阱。只有任务卡涉及对应领域时才读取相关条目。

## .NET 与测试环境

- 仓库 `global.json` 使用 feature-band roll-forward；本机 SDK 不匹配时先确认实际解析版本，不要用陈旧产物代替构建。
- 遇到 `obj`/CS2012 访问拒绝，先检查 `dotnet`、`testhost`、`VBCSCompiler` 残留；释放锁后重新构建。
- 外部 PostgreSQL/Redis 测试使用环境变量门控和 `Category=E2E`；未启用时早退，不使用不兼容的动态 Skip。
- 测试后关闭 build server、清理本轮残留编译进程和 `%TEMP%/industrial-platform-*.db`，但不得影响活动的 IDE 调试会话。

## SqlSugar 与数据库

- SqlSugar SQLite 的 `IsAnyTable` 可能受进程级表清单缓存影响；验证表存在优先直接查询 `sqlite_master`。
- SQLite 复合外键在 `pragma_foreign_key_list` 中按列展开，统计外键使用 `COUNT(DISTINCT id)`。
- 父表软删的 `ON UPDATE CASCADE` 会先更新子表父级删除影子列；关系软删集和清理集不得按父级影子未删过滤。
- 新迁移步骤 ID 必须全局唯一，不能多个步骤复用同一账本 ID。
- ModuleKey 派生数据库标识符必须经过统一 sanitize，不能直接拼接带连字符的键。
- PostgreSQL `timestamptz` 读回可能改变偏移并截断微秒；幂等时间断言比较时间点并使用小容差。

## Identity

- 用户有效角色为直接角色与有效用户组继承角色的并集，权限快照、登录和刷新必须同源。
- 用户组成员、角色或状态变化需要推进受影响用户 AuthVersion、撤销会话并失效权限缓存。
- 用户和用户组恢复采用墓碑语义：恢复为 Disabled，不自动恢复授权、凭据或会话。
- 权限目录、迁移种子、策略和测试计数必须同步更新。

## SystemData

- `DatabaseTopology` 类型与命名空间避免同名，命名空间使用 `Topology`。
- 聚合保存的乐观并发 expected version 是上次持久化版本，不是 `Touch` 后当前版本。
- 目标适配器首次 Apply 应先确保账本表存在，再读取已应用 ID。
- Infrastructure 内部契约被测试直接使用时，按测试程序集精确配置 `InternalsVisibleTo`。

## 测试代码

- xUnit `Assert.Throws<T>` 要求精确异常类型。
- 测试工厂在不需要抽象时返回具体类型，避免 CA1859。
- 已发生领域变更的内存替身应在变更完成后登记版本，避免伪造乐观并发冲突。
