# Industrial Platform 智能体总控规则

## 总负责人

Codex 是本项目唯一总负责人，负责蓝图设计、开发 TODO、依赖分析、任务派遣、冲突裁决、代码审查、集成和最终验收。

Claude Code 与 Harness 是职责等价的编码执行智能体。它们只能执行 Codex 派发的任务卡，不得自行调整总体蓝图、优先级、跨模块边界或其他智能体的任务。

## 上下文路由

- 当前项目状态：`docs/status/CURRENT.md`
- 编码智能体公共协议：`docs/agents/EXECUTOR.md`
- 任务卡模板：`docs/tasks/TEMPLATE.md`
- 活跃任务：`docs/tasks/active/`
- 已完成任务：`docs/tasks/archive/`
- 验证证据：`docs/evidence/`
- 完整设计：`docs/implementation/`
- 工程陷阱：`docs/agents/ENGINEERING-NOTES.md`
- 稳定决策：`docs/decisions/`

详细实施文档按需读取。派遣时必须指定精确文件和章节，不得要求编码智能体读取全部项目文档。

## 并行开发

- 主工作树只供 Codex 总控和最终集成。
- 一个活跃任务对应一个负责人、一个独立 Git worktree 和一个 `task/<task-id>` 分支。
- Claude Code 与 Harness 不得在主工作树或同一工作树内并行编码。
- 派遣前检查允许修改范围是否重叠；公共文件存在冲突时，由 Codex 排定先后顺序。
- 编码智能体不得合并、rebase、cherry-pick 或处理其他任务分支。
- Codex 验收执行者提交后，按依赖顺序集成并执行最终门禁。

## 派遣与交付

Codex 使用 `docs/tasks/TEMPLATE.md` 创建任务卡，至少明确目标、输入章节、无需读取、依赖、worktree、分支、允许/禁止修改范围、验证命令和 evidence 位置。

编码智能体交付固定包含：状态、提交哈希、修改文件、关键决策、验证命令与结果、剩余风险。详细结果写入对应 evidence，不在消息中复述完整设计。

## 构建与测试门禁

- 源码可能变化或曾发生 `bin/obj` 锁定时，先执行新鲜 Release 构建，再运行测试。
- 后端正常门禁：

```powershell
dotnet build src/backend/IndustrialPlatform.slnx --configuration Release
dotnet test src/backend/IndustrialPlatform.slnx --configuration Release --no-build
```

- `dotnet test --no-build` 只验证已有编译产物，不得用于证明未重新构建的源码。
- 若出现 CS2012 或 `obj` 访问拒绝，先检查 `dotnet`、`testhost`、`VBCSCompiler` 进程；锁释放后必须重建。
- 执行者运行任务定向测试；Codex 集成后集中运行全量 Release 门禁。
- 测试后释放测试自产进程和临时库，不影响活动的 Visual Studio 调试会话。

## 提交边界

- 提交前检查 tracked、untracked、ignored、staged 和 unstaged 文件。
- `bin/`、`obj/`、`TestResults/`、前端构建输出、缓存和运行日志不得提交。
- `CLAUDE.md` 是本地 Claude Code 协作入口，除非用户明确要求，不得暂存或提交。
- `DSH.md` 默认作为 Harness 本地入口，未明确要求纳入版本控制前不得暂存。
- 精确暂存任务文件，不得裹入其他智能体或协作方的 WIP。
- 禁止强制推送。

## 推送验证

- GitHub 网站可访问不能证明 Git smart-HTTP 正常。
- 推送异常时分别检查 DNS、TCP 443、网站与 `*.git/info/refs`、Git/系统代理、SSL backend、HTTP 版本和 SSH 22/443。
- 保留本地提交，报告准确 ahead/behind；没有证据不得归因于代码、凭据或整机网络。
- 推送成功后验证远端分支哈希，并确认 `HEAD...origin/<branch>` 为 `0 0`。

## 所有权

- Codex：`AGENTS.md`、`CURRENT.md`、蓝图、TODO、任务卡、优先级和最终验收。
- Claude Code/Harness：被指派的任务代码与对应 evidence。
- `CLAUDE.md`、`DSH.md`、`EXECUTOR.md` 由 Codex 制定，编码智能体只读。
