# TASK-ID-018 验证证据

## 状态

历史验收通过。

## 提交哈希

`83a00d1 feat(identity): add safe user and group lifecycle`

## 修改范围

- 用户与用户组墓碑删除、恢复及领域事件。
- 仓储、管理存储、Outbox、权限、审计和 API。
- 删除恢复用例与 Domain、Infrastructure、Application、Contract 测试。

## 关键决策

- 删除保留 NId、规范化 NId 和登录名；恢复仅回到 Disabled。
- 恢复不自动恢复角色、组关系、凭据或会话。
- 删除禁止当前用户自删、内置 ADMIN 删除和最后管理员删除。
- 会话失效同时使用 refresh 撤销、AuthVersion 推进和权限缓存失效。

## 验证命令与结果

历史记录：Release 全量构建 0 警告、0 错误；全量测试 877/877 通过。本次上下文整理未重新运行测试。

## 剩余风险

无。

## 范围外发现

正式 admin 引导属于 TASK-ID-019。
