# Industrial Platform 当前状态

> 由 Codex 维护。只保留当前有效状态；历史细节进入任务归档、evidence、实施方案或 Git 历史。

## 集成基线

- 分支：`develop`
- 本次整理开始基线：`226c345`
- 远端关系：本次整理开始时领先 `origin/develop` 2 个本地文档提交。
- 当前没有运行中的 Claude Code 或 Harness 任务。

## 服务状态

- BuildingBlocks：基线与公共组件已完成。
- 统一前端与 PF-01：已完成；外部真机 safe-area 项仍待验收。
- Identity：TASK-ID-001～018 已完成；ID-017 为用户组授权，ID-018 为用户与用户组安全删除恢复。
- SystemData：TASK-SD-001～004 已完成；云端 PostgreSQL 种子账本语义已验证。
- ReferenceData：保持服务骨架，业务功能未实施。

## 进行中

无。新任务必须由 Codex 创建任务卡并分配独立 worktree/分支后开始。

## 待派遣

- TASK-ID-019：接入 SystemData 编排与正式 admin 引导；依赖 ID-017、ID-018 和 SD-004，当前依赖已具备。
- TASK-ID-020～023：按 Identity 实施方案 §29A 后续任务派遣。
- TASK-SD-005+：组织、岗位、任职及后续 SystemData 能力。
- PF-00、PF-02 等前端工作以 Codex 最新蓝图和新任务卡为准，不沿用已停止智能体的会话状态。

## 阻塞与待决策

- Identity 事件命名以既有 `Identity.UserCreated.v1` 风格为当前实现；若要采用小写连字符风格，需要单独契约变更任务。
- Identity 最后管理员守卫当前使用权威持有者计数，并对组外持有路径做精确放行。
- 云端真实登录 E2E 需要稳定、可重复创建或重置的测试账号夹具；现有一次性 bootstrap 不适合作为持久数据库测试种子。

## 最近验收

- `83a00d1`：TASK-ID-018 用户与用户组安全删除恢复。
- `07d6863`：TASK-ID-017 用户组授权模型。
- `05fe591`：TASK-SD-004 初始化握手、NotReady 契约与验收夹具。
- `61753dc`：TASK-SD-003 数据库编排 Runner。

## 更新规则

- 派遣时加入“进行中”，记录负责人、任务卡、worktree 和分支。
- 完成后从“进行中”删除，任务卡移入 archive，结果写入 evidence。
- 不累计会话快照、完整变更清单或历史测试日志。
