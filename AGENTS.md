# Industrial Platform 智能体总控规则

## 总负责人

Codex 是本项目唯一总负责人，负责蓝图设计、开发 TODO、依赖分析、整张任务卡派遣、冲突裁决、最终 Release 编译门禁、提交和集成。

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
- 每个 PF 工作线/执行智能体长期使用一个独立 Git worktree 和分支；不是每张任务新建一次。
- 当前固定工作线：Harness 使用 PF-00，Claude Code 使用 PF-02；两者不得在主工作树或同一工作树内并行编码。
- 同一工作线的任务串行执行。上一张任务由 Codex 提交并合入 `develop` 后，Codex 在下一张任务派遣前把原工作线同步到最新 `develop`。
- 派遣前检查允许修改范围是否重叠；公共文件存在冲突时，由 Codex 排定先后顺序。
- 编码智能体不得合并、rebase、cherry-pick 或处理其他任务分支。
- 编码执行者完成实现和内部验证后保留未提交工作树；Codex 通过最终 Release 编译门禁后统一提交和集成。

## 派遣与交付

Codex 使用 `docs/tasks/TEMPLATE.md` 创建完整任务卡，至少明确目标、输入章节、依赖、所属 PF 工作线、允许/禁止修改范围和执行者内部验证命令。一个 PF 当前任务原则上整张交给一个执行智能体，不再由 Codex 拆出大量内部子任务或反复审核；连续任务复用原 worktree 和分支。

编码智能体全权负责本任务的编码、测试、修复和内部验证。交付只需包含：状态、主要修改范围、验证命令与结果、剩余风险；evidence 保持短小，不复述完整设计。

## 验证与提交门禁

- 编码智能体负责运行任务卡要求的测试并解决失败；Codex 信任其验证结果，不重复运行测试，也不再逐条重审已交付规格。
- Codex 只运行与任务对应的新鲜 Release 编译。后端默认命令：

```powershell
dotnet build src/backend/IndustrialPlatform.slnx --configuration Release
```

- 若出现 CS2012 或 `obj` 访问拒绝，先检查 `dotnet`、`testhost`、`VBCSCompiler` 进程；锁释放后必须重建。
- Release 编译成功后，Codex 只做提交卫生检查（范围、私有配置、构建产物），随后直接提交并按依赖顺序合入 `develop`。
- Release 编译失败时退回原执行智能体修复，不另开复核子任务。
- 只有安全风险、明显越界、数据破坏风险或用户明确要求时，Codex 才增加专项检查。

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

- Codex：`AGENTS.md`、`CURRENT.md`、蓝图、TODO、任务卡、优先级、最终编译门禁、提交和集成。
- Claude Code/Harness：被指派的任务代码与对应 evidence。
- `CLAUDE.md`、`DSH.md`、`EXECUTOR.md` 由 Codex 制定，编码智能体只读。
