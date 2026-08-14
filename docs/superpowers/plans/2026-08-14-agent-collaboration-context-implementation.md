# Industrial Platform Agent Collaboration Context Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 将 Industrial Platform 的 Codex、Claude Code、Harness 协作方式迁移为“Codex 总控、编码智能体独立 worktree 执行、薄记忆入口、按任务加载上下文”的可验证结构。

**Architecture:** 使用 `AGENTS.md` 作为 Codex 总控入口，`CLAUDE.md` 与 `DSH.md` 作为两个编码智能体的薄入口，公共执行规则集中在 `docs/agents/EXECUTOR.md`。当前状态、任务卡和验证证据分别存放，完整实施方案只通过任务卡中的精确章节引用按需加载。

**Tech Stack:** Markdown、Git、PowerShell、ripgrep。

## Global Constraints

- Codex 是架构、蓝图、TODO、优先级、任务派遣和最终验收的唯一决策入口。
- Claude Code 与 Harness 职责等价，只在 Codex 指派的独立 worktree 和任务分支内编码。
- 禁止两个编码智能体在主工作目录或同一工作树内并行编码。
- 记忆入口只保存稳定规则和上下文路由，不保存会话快照、完整模块百科和历史测试数字。
- 详细实施方案保留为权威资料，编码智能体只读取任务卡明确列出的章节。
- 保留现有 `CLAUDE.md` 与 `DSH.md` 中仍有效的信息，不覆盖未审查内容。
- `CLAUDE.md` 是本地协作指令文件；除非用户明确要求，不得暂存或提交。
- 构建产物、缓存、日志和运行时文件不得提交。

---

## File Structure

| Path | Responsibility |
| --- | --- |
| `AGENTS.md` | Codex 总控入口、角色边界、派遣和集成硬规则 |
| `CLAUDE.md` | Claude Code 薄入口；只声明身份、加载顺序和禁止事项 |
| `DSH.md` | Harness 薄入口；只声明身份、加载顺序和禁止事项 |
| `docs/agents/EXECUTOR.md` | Claude Code 与 Harness 唯一公共执行协议 |
| `docs/status/CURRENT.md` | 当前基线、进行中、待派遣、阻塞和最近验收 |
| `docs/tasks/TEMPLATE.md` | Codex 创建任务卡时使用的固定模板 |
| `docs/tasks/active/.gitkeep` | 保留活跃任务目录 |
| `docs/tasks/archive/.gitkeep` | 保留任务归档目录 |
| `docs/evidence/TEMPLATE.md` | 编码智能体结构化回写验证证据的模板 |
| `docs/decisions/README.md` | ADR 目录用途与最小格式说明 |

## Task 1: 建立公共执行协议和目录骨架

**Files:**
- Create: `docs/agents/EXECUTOR.md`
- Create: `docs/tasks/TEMPLATE.md`
- Create: `docs/tasks/active/.gitkeep`
- Create: `docs/tasks/archive/.gitkeep`
- Create: `docs/evidence/TEMPLATE.md`
- Create: `docs/decisions/README.md`

**Interfaces:**
- Consumes: `docs/superpowers/specs/2026-08-14-agent-collaboration-context-design.md`
- Produces: 编码智能体共用协议、任务输入契约和 evidence 输出契约。

- [ ] **Step 1: 创建公共执行协议**

`docs/agents/EXECUTOR.md` 必须包含：角色权限、启动读取顺序、worktree 隔离、允许与禁止操作、代码与测试流程、提交边界、结果回写格式、冲突和阻塞处理。

明确写入以下启动顺序：

```text
1. 读取自身入口 CLAUDE.md 或 DSH.md
2. 读取 docs/agents/EXECUTOR.md
3. 读取 Codex 指派的唯一任务卡
4. 只读取任务卡“输入文档与精确章节”列出的内容
5. 检查当前 worktree、分支和允许修改范围后开始工作
```

- [ ] **Step 2: 创建任务卡模板**

`docs/tasks/TEMPLATE.md` 固定包含：

```text
任务编号
状态
负责人
目标
输入文档与精确章节
无需读取
依赖
worktree
分支
允许修改范围
禁止修改范围
预期输出
定向验证命令
验收标准
结果回写位置
建议提交信息
```

- [ ] **Step 3: 创建 evidence 模板**

`docs/evidence/TEMPLATE.md` 固定包含：状态、提交哈希、修改文件、关键决策、验证命令与逐项结果、剩余风险、范围外发现。

- [ ] **Step 4: 创建 ADR 目录说明和空目录保留文件**

ADR 最小格式为标题、状态、背景、决策、后果；不得复制完整实施方案。创建 active/archive 的 `.gitkeep`。

- [ ] **Step 5: 验证模板字段**

Run:

```powershell
rg -n "任务编号|输入文档与精确章节|无需读取|允许修改范围|禁止修改范围|定向验证命令|结果回写位置" docs/tasks/TEMPLATE.md
rg -n "状态|提交哈希|修改文件|验证命令|剩余风险" docs/evidence/TEMPLATE.md
```

Expected: 每个必需字段恰好存在一次，无 `TBD`、`TODO` 或占位说明。

- [ ] **Step 6: 提交公共协议和模板**

```powershell
git add docs/agents docs/tasks docs/evidence docs/decisions
git commit -m "docs: add coding agent execution protocol"
```

不得暂存 `CLAUDE.md` 或其他既存修改。

## Task 2: 建立 Codex 总控入口和当前状态

**Files:**
- Create: `AGENTS.md`
- Create: `docs/status/CURRENT.md`
- Read: `CLAUDE.md`
- Read: `DSH.md`
- Read: `docs/implementation/*.md` only for current task identifiers referenced by the memory files

**Interfaces:**
- Consumes: Task 1 的 `EXECUTOR.md` 和任务模板；现有记忆文件中的当前有效状态。
- Produces: Codex 的稳定入口和单一当前状态页。

- [ ] **Step 1: 从现有记忆文件提取当前状态**

只提取：当前 `develop` 基线、进行中任务、最近完成任务、下一候选任务、明确阻塞和待决策项。历史实现细节、完整测试表和已关闭会话不进入 `CURRENT.md`。

- [ ] **Step 2: 创建 `docs/status/CURRENT.md`**

固定结构：

```markdown
# Industrial Platform 当前状态

## 集成基线
## 进行中
## 待派遣
## 阻塞与待决策
## 最近验收
## 更新规则
```

如果整理时没有运行中的编码智能体，`进行中` 明确写“无”，不得保留已停止任务为运行中。

- [ ] **Step 3: 创建 `AGENTS.md`**

`AGENTS.md` 必须覆盖：Codex 唯一总负责人、任务可并行性判断、独立 worktree/分支、文件重叠处理、派遣协议、审查与集成、Release 最终门禁、提交/推送规则、文档路由和所有权。

使用链接引用 `CURRENT.md`、`EXECUTOR.md` 和任务模板，不复制模块进度或完整执行协议。

- [ ] **Step 4: 验证总控入口大小和路由**

Run:

```powershell
$agentsBytes = (Get-Item AGENTS.md).Length
if ($agentsBytes -gt 10240) { throw "AGENTS.md exceeds 10 KB: $agentsBytes" }
rg -n "Codex|worktree|CURRENT.md|EXECUTOR.md|docs/tasks/TEMPLATE.md|Release" AGENTS.md
```

Expected: `AGENTS.md` 不超过 10 KB，所有关键路由均存在。

- [ ] **Step 5: 提交总控入口和状态页**

```powershell
git add AGENTS.md docs/status/CURRENT.md
git commit -m "docs: add Codex orchestration entrypoint"
```

不得暂存 `CLAUDE.md`。

## Task 3: 将 Claude Code 和 Harness 记忆文件改为薄入口

**Files:**
- Modify: `CLAUDE.md`
- Modify: `DSH.md`
- Read: `docs/agents/EXECUTOR.md`
- Read: `docs/status/CURRENT.md`

**Interfaces:**
- Consumes: Task 1 的公共协议和 Task 2 的状态路由。
- Produces: 两个不包含项目百科和会话历史的编码智能体入口。

- [ ] **Step 1: 保存迁出映射**

在编辑前逐节确认：

- 全局执行规则已进入 `EXECUTOR.md` 或 `AGENTS.md`；
- 当前进度已进入 `CURRENT.md`；
- 长期技术决策仍存在于对应实施方案；
- 调试结论仍存在于 `src/DEBUGGING.md`；
- 当前任务结果可由提交历史、任务归档或 evidence 追踪。

任何无法定位唯一归属的信息暂不删除，先记录在整理报告中。

- [ ] **Step 2: 重写 `CLAUDE.md` 为 Claude Code 入口**

保留：身份、读取顺序、Codex 派遣约束、worktree 约束、范围和提交边界、evidence 回写要求、私有配置安全要求。

移除：模块实施进度、会话快照、完整技术决策、常用命令百科和历史测试数字；这些内容改为链接路由。

- [ ] **Step 3: 重写 `DSH.md` 为 Harness 入口**

结构与 `CLAUDE.md` 同构，仅身份名称和入口说明不同。不得保存 PF-00、PF-02 或 TASK-ID-017 等单次任务状态。

- [ ] **Step 4: 验证入口大小和重复内容**

Run:

```powershell
$claudeBytes = (Get-Item CLAUDE.md).Length
$dshBytes = (Get-Item DSH.md).Length
if ($claudeBytes -gt 5120) { throw "CLAUDE.md exceeds 5 KB: $claudeBytes" }
if ($dshBytes -gt 5120) { throw "DSH.md exceeds 5 KB: $dshBytes" }
rg -n "EXECUTOR.md|任务卡|worktree|Codex|evidence" CLAUDE.md DSH.md
rg -n "会话进度快照|实施进度|当前测试:[0-9]|测试全绿" CLAUDE.md DSH.md
```

Expected: 两个文件均不超过 5 KB；第一条搜索命中必要路由，第二条搜索无命中。

- [ ] **Step 5: 保持本地文件提交边界**

`CLAUDE.md` 不得暂存或提交。`DSH.md` 在用户没有明确要求纳入版本控制前同样保持未暂存；最终报告必须明确列出这两个本地修改。

## Task 4: 全局一致性验证与交付

**Files:**
- Verify: `AGENTS.md`
- Verify: `CLAUDE.md`
- Verify: `DSH.md`
- Verify: `docs/agents/EXECUTOR.md`
- Verify: `docs/status/CURRENT.md`
- Verify: `docs/tasks/TEMPLATE.md`
- Verify: `docs/evidence/TEMPLATE.md`
- Verify: `docs/decisions/README.md`

**Interfaces:**
- Consumes: Tasks 1–3 的全部文档。
- Produces: 可证明的协作治理交付结果。

- [ ] **Step 1: 检查链接目标存在**

```powershell
$required = @(
  'AGENTS.md',
  'CLAUDE.md',
  'DSH.md',
  'docs/agents/EXECUTOR.md',
  'docs/status/CURRENT.md',
  'docs/tasks/TEMPLATE.md',
  'docs/evidence/TEMPLATE.md',
  'docs/decisions/README.md'
)
$missing = $required | Where-Object { -not (Test-Path $_) }
if ($missing) { throw "Missing required files: $($missing -join ', ')" }
```

- [ ] **Step 2: 检查占位符、空白错误和重复所有权**

```powershell
rg -n "TBD|implement later|fill in details|适当处理" AGENTS.md CLAUDE.md DSH.md docs/agents docs/status docs/tasks/TEMPLATE.md docs/evidence/TEMPLATE.md docs/decisions
git diff --check
```

Expected: 占位符扫描无命中，`git diff --check` exit 0。

- [ ] **Step 3: 检查版本控制范围**

```powershell
git status --short --ignored
git diff --cached --name-status
git diff --name-status
```

Expected: 不包含 `bin/`、`obj/`、`TestResults/`、前端构建目录、缓存或日志；`CLAUDE.md` 不在 staged 集合。

- [ ] **Step 4: 检查工作流可执行性**

人工走查 PF-00/PF-02 示例：Codex 能从模板生成两个任务卡，为两者指定不同 worktree/branch，两个执行者只读取薄入口、公共协议、单个任务卡和指定章节，最终分别写入 evidence。

- [ ] **Step 5: 生成最终交付报告**

报告必须包含：创建/修改文件、入口文件整理前后大小、提交哈希、未提交本地文件、未执行的构建测试及原因、下一次 PF 任务的推荐派遣命令或步骤。

本次仅修改协作文档，不修改业务源码，因此不运行 .NET 或前端测试；以链接、大小、字段、占位符、Git 范围和 `git diff --check` 作为验证门禁。
