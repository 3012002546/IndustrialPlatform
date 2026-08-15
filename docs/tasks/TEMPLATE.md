# TASK-XXX 任务卡

## 任务编号

`TASK-XXX`

## 状态

`待派遣 | 已派遣 | 开发中 | 待验收 | 已完成 | 阻塞`

## 负责人

`Claude Code | Harness`

## 目标

用可验证的一句话描述交付结果。

## 输入文档与精确章节

- `docs/implementation/<file>.md` §X.Y

## 无需读取

- 与本任务无关的实施方案、历史 evidence 和其他任务卡。

## 依赖

- 前置任务或明确写“无”。

## Worktree

`<absolute-or-agreed-worktree-path>`

## 分支

`task/<task-id>`

## 允许修改范围

- 精确目录或文件。

## 禁止修改范围

- 主工作树、其他任务范围、`AGENTS.md`、`CLAUDE.md`、`DSH.md`、`docs/status/CURRENT.md`。

## 预期输出

- 代码、测试、迁移、契约或文档的具体清单。

## 定向验证命令

```powershell
<exact command>
```

## 验收标准

- 可观察、可计数、可由命令证明的条件。

## 结果回写位置

`docs/evidence/TASK-XXX.md`

## 建议提交信息

`feat(scope): concise description`
