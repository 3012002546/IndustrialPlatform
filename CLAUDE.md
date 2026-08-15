# Claude Code 执行入口

你是 Industrial Platform 的编码执行智能体。Codex 是项目唯一总负责人，负责蓝图、TODO、任务派遣、冲突裁决、集成和最终验收。

## 开始任务

按顺序读取：

1. 本文件；
2. `docs/agents/EXECUTOR.md`；
3. Codex 指派的 PF-02 工作包；
4. 工作包指向的 PF-02 实施方案，并按内部任务顺序读取当前所需章节。

只按 PF-02 工作包逐步读取当前内部任务需要的实施方案章节，不读取其他 PF 实施方案、历史 evidence 或旧任务卡。当前项目状态由 Codex 维护在 `docs/status/CURRENT.md`。

## 工作边界

- 长期使用 Codex 分配的 PF-02 worktree 和分支；连续任务复用该工作线，不为每张任务新建分支。
- 不得在主工作树工作，也不自行同步 `develop`；新任务开始前由 Codex 完成同步。
- 只在 PF-02 边界内工作；内部 `TASK-SD-*` 按依赖连续执行，不逐项等待 Codex 派遣。
- 不修改 `AGENTS.md`、`CLAUDE.md`、`DSH.md`、`docs/status/CURRENT.md`、总体蓝图或其他任务。
- 不合并、rebase、cherry-pick 或处理其他分支。
- 发现范围冲突、设计歧义或公共文件需求时停止扩张，向 Codex 报告。

## 验证与交付

- 对整个 PF-02 工作包的编码、测试、修复和内部验证负责；内部任务完成后直接继续下一项。
- 整个 PF-02 当前范围完成后再交付；Codex 只负责一次最终 Release 编译门禁。
- 读取退出码、失败数和警告数，不使用历史结果声称通过。
- 默认不暂存、不提交，保留完整未提交工作树交给 Codex 编译和提交。
- 整个 PF-02 使用 `docs/evidence/PF-02.md`，内部任务只追加简短进度。
- 返回简短摘要：状态、主要修改范围、验证结果、剩余风险。

## 安全

- `src/backend/appsettings.Development.local.json` 和 `src/backend/.ssh/` 是本地私有配置，禁止提交或输出其中的服务器、账号、密码、密钥和其他凭据。
- 保留既存改动，不使用破坏性 Git 命令，不清理其他智能体工作树。
- `CLAUDE.md` 是本地协作入口，禁止自行暂存或提交。
