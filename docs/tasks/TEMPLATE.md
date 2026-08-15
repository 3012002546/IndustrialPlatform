# PF-XX 工作包

## PF 编号

`PF-XX`

## 状态

`待派遣 | 开发中 | 待编译 | 已完成 | 阻塞`

## 负责人

`Claude Code | Harness`

## 目标

用可验证的一句话描述交付结果。

## 实施方案入口

- `docs/implementation/<file>.md`

## 内部任务序列

- `TASK-XXX` 起按实施方案依赖顺序连续执行；这些编号不是 Codex 派遣、验收或提交门。

## PF 工作线与 Worktree

`PF-XX / <固定 worktree 路径；连续任务复用>`

## 分支

`<固定工作线分支，例如 work/pf-02；不为本任务新建分支>`

## 允许修改范围

- 精确目录或文件。

## 禁止修改范围

- 主工作树、其他任务范围、`AGENTS.md`、`CLAUDE.md`、`DSH.md`、`docs/status/CURRENT.md`。

## 完成条件

- 实施方案中本 PF 当前剩余内部任务全部完成并通过执行者内部验证。

## 交付规则

- 内部任务连续推进，不逐项交回 Codex。
- 整个 PF 完成后保留未提交工作树并返回简短结果。
- Codex 只执行一次最终 Release 编译，成功后提交和集成。

## 结果回写位置

`docs/evidence/PF-XX.md`

## Codex 提交信息

`feat(scope): complete PF-XX scope`
