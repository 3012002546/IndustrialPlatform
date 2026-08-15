# TASK-ID-020 补齐用户与用户组管理 API 契约

## 状态

`开发中`

## 负责人

Harness

## 目标

交付用户与用户组查询、详情、创建、编辑、状态、成员、角色、删除恢复、随机临时密码重置和 bootstrap 状态的完整管理 API。

## 输入文档与精确章节

- `docs/implementation/03-Industrial Platform Identity Service开发实施方案.md` §16、§17、§19、§27、§29A.2～§29A.6、§31 `TASK-ID-020`。
- `docs/agents/ENGINEERING-NOTES.md` 的 Identity、.NET 与数据库条目。

## 依赖

- TASK-ID-017～019 已完成并进入最新 `develop`。

## PF 工作线与 Worktree

`PF-00 / D:\Code\Industrial Platform\IndustrialPlatform-worktrees\pf-00`

## 分支

复用当前 PF-00 分支 `task/pf-00-id-019`，不得新建任务分支。

## 允许修改范围

- `src/backend/src/Services/Identity/IndustrialPlatform.Identity.Contracts/**`
- `src/backend/src/Services/Identity/IndustrialPlatform.Identity.Application/**`
- `src/backend/src/Services/Identity/IndustrialPlatform.Identity.Api/**`
- Identity OpenAPI、契约测试和管理 API 测试。
- 为完成公开端口所必需的 Identity Infrastructure 最小适配。
- `docs/evidence/TASK-ID-020.md`

## 禁止修改范围

- 不新增用户组直接权限或组织、岗位字段。
- 不修改 SystemData、前端、ReferenceData、总体蓝图、全局状态和其他 PF 工作线。
- 不提交私有配置、Secret、内部数据库 Guid 或构建产物。

## 预期输出

- 稳定 camelCase DTO、分页与组合过滤、详情和角色来源投影。
- 用户/用户组完整管理 API，以及创建/重置后只返回一次的随机临时凭据。
- 13 个新增权限码、幂等键、双版本并发、统一错误信封、操作审计和 OpenAPI 契约。

## 执行者内部验证

- Harness 自行完成相关 Contract、Application、Api 测试与必要的 Infrastructure 测试。
- 覆盖成功、权限拒绝、跨租户、非法角色/成员、并发、幂等冲突、Secret/内部 Guid 不泄漏和契约序列化。
- 完成后不暂存、不提交，只回报主要修改范围、测试结果和剩余风险。

## Codex 门禁与提交

- Codex 只运行后端全量 Release 编译；成功后提交并合入 `develop`。
- 提交信息：`feat(identity): complete user group management api`

## 结果回写位置

`docs/evidence/TASK-ID-020.md`
