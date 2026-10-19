# Industrial Platform 按需工程笔记

本文件保存跨任务复用但不应自动加载的工程陷阱。只有任务卡涉及对应领域时才读取相关条目。

所有任务的长期约定入口：[开发协作与页面统一约定](开发协作与页面统一约定.md)，持续维护Skill、黄金页面、布局、协作、唯一配置及验证边界；开始或恢复任务先读最新版本，领域细节按需读取。

PF06聊天媒体增量开发前，读取 [PF06开发注意事项：减少联调返工](PF06开发注意事项-减少联调返工.md)，复用现有工作包执行顺序和验收矩阵。

## .NET 与测试环境

- 仓库 `global.json` 使用 feature-band roll-forward；本机 SDK 不匹配时先确认实际解析版本，不要用陈旧产物代替构建。
- 新基础设施包先核对直接/传递依赖、生成器和驱动下限，再用最小真实调用探针确认兼容；restore成功不代表能接入现有工程。固定已验证版本，不能为适配新增依赖而压掉无关编译规则或扩大业务改造。
- 遇到 `obj`/CS2012 访问拒绝，先检查 `dotnet`、`testhost`、`VBCSCompiler` 残留；释放锁后重新构建。
- 外部 PostgreSQL/Redis 测试使用环境变量门控和 `Category=E2E`；未启用时早退，不使用不兼容的动态 Skip。
- 测试后关闭 build server、清理本轮残留编译进程和 `%TEMP%/industrial-platform-*.db`，但不得影响活动的 IDE 调试会话。

## SqlSugar 与数据库

- SqlSugar SQLite 的 `IsAnyTable` 可能受进程级表清单缓存影响；验证表存在优先直接查询 `sqlite_master`。
- SQLite 复合外键在 `pragma_foreign_key_list` 中按列展开，统计外键使用 `COUNT(DISTINCT id)`。
- 父表软删的 `ON UPDATE CASCADE` 会先更新子表父级删除影子列；关系软删集和清理集不得按父级影子未删过滤。
- 新迁移步骤 ID 必须全局唯一，不能多个步骤复用同一账本 ID。
- 三Provider迁移分别验证实际DDL和重复初始化；SQL Server涉及多列的CHECK使用表级约束，不能由SQLite/PostgreSQL通过推定SQL Server也通过。原生存储由固定供应商脚本负责结构，沿服务受控初始化执行，运行查询不得顺手补库。
- PostgreSQL 默认标识符上限为63字节，超长索引名会被数据库截断；物理漂移校验须按数据库实际名称语义比较，不能用完整DDL名称误判索引缺失，也不能用字符数截断多字节名称。已应用迁移的DDL/编号/描述/账本保持不变；先确认是名称误报、真实缺失，还是校验失败导致整步事务回滚，再选择修复范围。SQLite没有相同截断行为，须分别验证。
- ModuleKey 派生数据库标识符必须经过统一 sanitize，不能直接拼接带连字符的键。
- PostgreSQL `timestamptz` 读回可能改变偏移并截断微秒；幂等时间断言比较时间点并使用小容差。
- PF05实证：SQLite/SqlSugar无offset的TEXT时间读回可能按本机+08解释；审批截止/执行期限/租约必须做真实仓储UTC round-trip回归。SQLite归一化不得改变PostgreSQL语义，不能用延长时限消除误过期。

## Identity

- 用户有效角色为直接角色与有效用户组继承角色的并集，权限快照、登录和刷新必须同源。
- 用户组成员、角色或状态变化需要推进受影响用户 AuthVersion、撤销会话并失效权限缓存。
- 用户和用户组恢复采用墓碑语义：恢复为 Disabled，不自动恢复授权、凭据或会话。
- 权限目录、迁移种子、策略和测试计数必须同步更新。

## SystemData

- `DatabaseTopology` 类型与命名空间避免同名，命名空间使用 `Topology`。
- 聚合保存的乐观并发 expected version 是上次持久化版本，不是 `Touch` 后当前版本。
- 目标适配器首次 Apply 应先确保账本表存在，再读取已应用 ID。
- 清单版本升级时，既有内置资源的一致性种子也必须升级；修复种子版本应随目标清单版本变化，保留历史账本。升级回归必须保留前一版已应用账本，并覆盖失败后的再次启动；删除旧账本后通过不能证明真实升级通过。
- Infrastructure 内部契约被测试直接使用时，按测试程序集精确配置 `InternalsVisibleTo`。

## 测试代码

- xUnit `Assert.Throws<T>` 要求精确异常类型。
- 测试工厂在不需要抽象时返回具体类型，避免 CA1859。
- 已发生领域变更的内存替身应在变更完成后登记版本，避免伪造乐观并发冲突。
- SQLite临时库清理在测试宿主/客户端/连接完成释放后进行；Windows仍短暂占用时，只对本测试精确路径做有界重试，持续占用应保留失败。清理异常与业务断言失败分开记录，不删除其他测试或用户的数据库。

## 前端公共控件

- Vue的`h()`/`render()`使用标准`'onUpdate:modelValue'`事件键。挂到原生表头等父VNode树外的控件，应明确本地响应值和外部回写同步，不能假设初始props会随父状态更新；验证选中值保留及真正发出的查询参数。
- 日期/浮层回归应模拟实际焦点与选择流程，并锁定本次挂载的面板；测试只发click未聚焦、或全局选到前一测试残留面板，都可能制造与浏览器不同的结果。专项通过后至少在该测试文件完整上下文中验证隔离性。

## SqlSugarScope 配置与构造（2026-09-30）

- 不在依赖注入构造期间访问 `SqlSugarScope.Ado` 或 `CurrentConnectionConfig`。这会提前创建底层上下文，并可能让后续异步操作共用映射集合；本轮实证出现 `Collection was modified` 和接口 500。
- 连接超时等客户端设置放入 `SqlSugarScope` 的客户端创建回调；迁移构造器通过 `SqlSugarDbContext.DbType` 判断 Provider，避免为读取类型而创建底层上下文。
- 最小回归使用 8 路异步操作，同时验证上下文隔离和命令超时。修复前仅有 1 个上下文，修复后为 8 个；不能仅凭某次接口复测通过就忽略后台集合异常。
- 实现见 [SqlSugarDbContext.cs](../../src/backend/src/BuildingBlocks/IndustrialPlatform.Infrastructure/Database/SqlSugarDbContext.cs)，回归见 [SqlSugarContextScopeTests.cs](../../tests/BuildingBlocks/IndustrialPlatform.BuildingBlocks.Tests/SqlSugarContextScopeTests.cs)。
