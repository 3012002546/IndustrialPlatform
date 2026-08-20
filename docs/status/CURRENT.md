# Industrial Platform 当前状态

> 由 Codex 维护。只保留当前有效状态；历史细节进入任务归档、evidence、实施方案或 Git 历史。

## 集成基线

- 分支：`develop`
- 架构收敛整改开始基线：`6000bb5`
- 远端关系：整改开始时 `develop` 领先 `origin/develop` 3 个本地提交。
- 2026-08-20 Work Package 1 基线：Release Build 0 警告/0 错误；后端解决方案测试 1219/1219 通过；前端 Vitest 因沙箱无法写入 `node_modules/.vite-temp`（EPERM）阻塞，作为既存环境基线记录，不在文档工作包修复。

## 服务状态

- BuildingBlocks：基线与公共组件已完成。
- 统一前端与 PF-01：已完成；外部真机 safe-area 项仍待验收。
- Identity/PF-00：TASK-ID-001～023 当前范围已完成并合入；真实 PostgreSQL/Redis 联合链路保留为外部整体测试项。
- SystemData：TASK-SD-001～006 已完成并合入；当前实现范围与 Git 一致。架构收敛后其控制面只保留 Topology、Orchestration、Policy、Observation，服务初始化实现归各服务。
- ReferenceData：保持服务骨架，业务功能未实施。
- 部署入口：默认统一部署使用 UnifiedHost（组合 Identity、SystemData、ReferenceData 并托管生产 SPA）；分布式部署使用 Gateway（YARP 反向代理）。两者不互相替代。

## 进行中

- 架构收敛整改：Work Package 1～4 均已完成；Work Package 4 本次提交完成服务初始化所有权、初始化端口/适配器与本地 readiness 对齐。完成后继续 PF-03，不新增 PF 或子计划。

## 固定工作线

- PF-00：Harness，长期复用 `IndustrialPlatform-worktrees/pf-00` 和当前分支 `task/pf-00-id-019`；当前范围已合入，待工作线同步。
- PF-02：历史工作线保留；`TASK-SD-007+` 在架构收敛整改完成前不继续。
- 现有分支名称暂时保留；后续任务不因此创建新分支。

## 内部执行序列

- 架构收敛整改已按已批准计划完成 Work Package 1 → 2 → 3 → 4；后续继续 PF-03，不新增 PF 或子计划。

## 阻塞与待决策

- Identity 事件命名以既有 `Identity.UserCreated.v1` 风格为当前实现；若要采用小写连字符风格，需要单独契约变更任务。
- Identity 最后管理员守卫当前使用权威持有者计数，并对组外持有路径做精确放行。
- 云端真实登录 E2E 需要稳定、可重复创建或重置的测试账号夹具；现有一次性 bootstrap 不适合作为持久数据库测试种子。
- 当前前端 Vitest 在受限沙箱写入 `src/frontend/node_modules/.vite-temp` 时返回 EPERM；这是环境权限阻塞，不是本次文档变更导致的测试失败。

## 最近验收

- WP4（本次提交）：fresh Release Build 0 警告/0 错误；静态审查修正后的常规后端 1227/1227；前端 Vitest 受限沙箱 EPERM；IntegrationTests 8/8 为未启用外部环境门控后的早退，不代表真实外部链路通过；real-login E2E 因 Playwright runner 不可用未执行。
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
