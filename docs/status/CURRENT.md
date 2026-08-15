# Industrial Platform 当前状态

> 由 Codex 维护。只保留当前有效状态；历史细节进入任务归档、evidence、实施方案或 Git 历史。

## 集成基线

- 分支：`develop`
- 本次整理开始基线：`226c345`
- 远端关系：本次整理开始时领先 `origin/develop` 2 个本地文档提交。
- Claude Code 的 TASK-SD-005 已完成并由 Codex 编译验收、合入 `develop`。

## 服务状态

- BuildingBlocks：基线与公共组件已完成。
- 统一前端与 PF-01：已完成；外部真机 safe-area 项仍待验收。
- Identity/PF-00：TASK-ID-001～023 当前范围已完成并合入；真实 PostgreSQL/Redis 联合链路保留为外部整体测试项。
- SystemData：TASK-SD-001～006 已完成并合入；下一步由 Claude Code 在合并基线上执行整体测试。
- ReferenceData：保持服务骨架，业务功能未实施。

## 进行中

- PF-02：Claude Code，工作包 `docs/tasks/active/PF-02.md`；内部从 TASK-SD-006 起连续执行。

## 固定工作线

- PF-00：Harness，长期复用 `IndustrialPlatform-worktrees/pf-00` 和当前分支 `task/pf-00-id-019`；当前范围已合入，待工作线同步。
- PF-02：Claude Code，长期复用 `IndustrialPlatform-worktrees/pf-02` 和当前分支 `task/pf-02-sd-006`；SD-006 已合入，工作线同步后执行整体测试，后续内部任务不再新建分支。
- 现有分支名称暂时保留；后续任务不因此创建新分支。

## 内部执行序列

- PF-02 的 TASK-SD-006+ 由 Claude Code 按依赖连续执行，不再逐项派遣。

## 阻塞与待决策

- Identity 事件命名以既有 `Identity.UserCreated.v1` 风格为当前实现；若要采用小写连字符风格，需要单独契约变更任务。
- Identity 最后管理员守卫当前使用权威持有者计数，并对组外持有路径做精确放行。
- 云端真实登录 E2E 需要稳定、可重复创建或重置的测试账号夹具；现有一次性 bootstrap 不适合作为持久数据库测试种子。

## 最近验收

- `9f48d89`：PF-00 用户与用户组管理 API、前端闭环、首次改密与幂等写入。
- `1b72c6b`：TASK-SD-006 SystemData 组织、岗位与任职管理 API。
- `1b32ae6`：TASK-ID-019 Identity 三层种子与正式 admin 引导。
- `69c49b7`：TASK-SD-005 组织、岗位与时间化任职领域和持久化。
- `83a00d1`：TASK-ID-018 用户与用户组安全删除恢复。
- `07d6863`：TASK-ID-017 用户组授权模型。
- `05fe591`：TASK-SD-004 初始化握手、NotReady 契约与验收夹具。
- `61753dc`：TASK-SD-003 数据库编排 Runner。

## 更新规则

- 派遣单位固定为整个 PF，记录负责人、PF 工作包和固定工作线。
- 内部 `TASK-*` 不是派遣、验收或提交门；执行智能体连续完成整个 PF 后才交回 Codex。
- PF 完成后从“进行中”删除，工作包移入 archive，结果写入一份 PF evidence。
- 不累计会话快照、完整变更清单或历史测试日志。
- 执行智能体负责任务内测试；Codex 只做最终 Release 编译，成功后提交和集成。
- PF 开始或恢复前由 Codex 将原工作线同步到最新 `develop`，不新建内部任务分支。
