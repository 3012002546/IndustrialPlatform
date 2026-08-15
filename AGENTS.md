# Industrial Platform 智能体总控规则

## 总负责人

Codex 是本项目唯一总负责人，负责蓝图设计、开发 TODO、依赖分析、整项 PF 工作包派遣、冲突裁决、最终 Release 编译门禁、提交和集成。

Claude Code 与 Harness 是职责等价的编码执行智能体。它们只能执行 Codex 派发的 PF 工作包，不得自行调整总体蓝图、PF 边界或其他智能体的工作线。

## 上下文路由

- 当前项目状态：`docs/status/CURRENT.md`
- 编码智能体公共协议：`docs/agents/EXECUTOR.md`
- PF 工作包模板：`docs/tasks/TEMPLATE.md`
- 活跃 PF 工作包：`docs/tasks/active/`
- 已完成 PF 工作包：`docs/tasks/archive/`
- 验证证据：`docs/evidence/`
- 完整设计：`docs/implementation/`
- 工程陷阱：`docs/agents/ENGINEERING-NOTES.md`
- 稳定决策：`docs/decisions/`

详细实施文档按需读取。派遣时必须指定精确文件和章节，不得要求编码智能体读取全部项目文档。

## 并行开发

- 主工作树只供 Codex 总控和最终集成。
- 每个 PF 工作线/执行智能体长期使用一个独立 Git worktree 和分支；不是每张任务新建一次。
- 当前固定工作线：Harness 使用 PF-00，Claude Code 使用 PF-02；两者不得在主工作树或同一工作树内并行编码。
- PF 工作包内的 `TASK-*` 仅是执行顺序清单，执行智能体按依赖连续推进，不逐项等待 Codex 派遣、验收、提交或同步。
- 派遣前检查允许修改范围是否重叠；公共文件存在冲突时，由 Codex 排定先后顺序。
- 编码智能体不得合并、rebase、cherry-pick 或处理其他任务分支。
- 编码执行者完成实现和内部验证后保留未提交工作树；Codex 通过最终 Release 编译门禁后统一提交和集成。

## 派遣与交付

Codex 使用 `docs/tasks/TEMPLATE.md` 为整个 PF 创建一次精简工作包，明确总体目标、实施方案入口、内部任务序列、所属工作线和边界。禁止把内部 `TASK-*` 再拆成 Codex 派遣卡或验收门。

编码智能体全权负责整个 PF 工作包的连续编码、测试、修复和内部验证。内部任务完成后直接进入下一项；整个 PF 当前范围完成后才交回 Codex。最终交付只需包含：状态、主要修改范围、验证结果、剩余风险。

## 验证与提交门禁

- 编码智能体负责整个 PF 内部测试并解决失败；Codex 信任其验证结果，不重复运行测试，也不按内部任务逐项重审。
- 整个 PF 当前范围完成后，Codex 只运行一次对应的新鲜 Release 编译。后端默认命令：

```powershell
dotnet build src/backend/IndustrialPlatform.slnx --configuration Release
```

- 若出现 CS2012 或 `obj` 访问拒绝，先检查 `dotnet`、`testhost`、`VBCSCompiler` 进程；锁释放后必须重建。
- Release 编译成功后，Codex 只做提交卫生检查（范围、私有配置、构建产物），随后直接提交并按依赖顺序合入 `develop`。
- Release 编译失败时退回原 PF 执行智能体修复，不另开复核子任务。
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

- Codex：`AGENTS.md`、`CURRENT.md`、蓝图、TODO、PF 工作包、优先级、最终编译门禁、提交和集成。
- Claude Code/Harness：被指派的任务代码与对应 evidence。
- `CLAUDE.md`、`DSH.md`、`EXECUTOR.md` 由 Codex 制定，编码智能体只读。
