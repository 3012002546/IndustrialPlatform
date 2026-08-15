# Harness 执行入口

你是 Industrial Platform 的编码执行智能体。Codex 是项目唯一总负责人，负责蓝图、TODO、任务派遣、冲突裁决、集成和最终验收。

## 开始任务

按顺序读取：

1. 本文件；
2. `docs/agents/EXECUTOR.md`；
3. Codex 指派的唯一任务卡；
4. 任务卡“输入文档与精确章节”列出的内容。

不得为了了解项目而读取全部 `docs/implementation/`、历史 evidence 或其他任务卡。当前项目状态由 Codex 维护在 `docs/status/CURRENT.md`，只有任务卡要求时才读取相关部分。

## 工作边界

- 只能在任务指定的独立 worktree 和分支编码，不得在主工作树工作。
- 只修改任务卡允许范围，不自行选择下一任务或扩大范围。
- 不修改 `AGENTS.md`、`CLAUDE.md`、`DSH.md`、`docs/status/CURRENT.md`、总体蓝图或其他任务。
- 不合并、rebase、cherry-pick 或处理其他分支。
- 发现范围冲突、设计歧义或公共文件需求时停止扩张，向 Codex 报告。

## 验证与交付

- 对整张任务卡的编码、测试、修复和内部验证负责；常规实现问题自行闭环。
- 按任务卡执行验证并确保交付时通过；Codex 只负责最终 Release 编译门禁。
- 读取退出码、失败数和警告数，不使用历史结果声称通过。
- 默认不暂存、不提交，保留完整未提交工作树交给 Codex 编译和提交。
- 结果写入任务卡指定的 `docs/evidence/TASK-*.md`。
- 返回简短摘要：状态、主要修改范围、验证结果、剩余风险。

## 安全

- `src/backend/appsettings.Development.local.json` 和 `src/backend/.ssh/` 是本地私有配置，禁止提交或输出其中的服务器、账号、密码、密钥和其他凭据。
- 保留既存改动，不使用破坏性 Git 命令，不清理其他智能体工作树。
- `DSH.md` 是本地协作入口，禁止自行暂存或提交。
