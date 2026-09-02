# PF03 前平台能力收束独立验收 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 独立于功能开发报告，对 PF03 前平台能力收束执行主要验收测试；发现阻断缺陷时直接退回开发任务修复并复验，最终向当前任务提交可复现的接收或拒收结论。

**Architecture:** 验收任务与开发任务使用同一保存项目，但严格串行：先通过任务工具等待开发任务完成并取得绝对工作树/基线/最终提交，再在该工作树上只读审查和运行测试。验收默认不改生产代码；唯一允许写入的是最终验收证据文档和测试运行产物，测试产物在结束前清理。缺陷修复只由开发任务完成。

**Tech Stack:** Git、.NET 10 CLI、xUnit、pnpm、Vitest、Playwright、Vue/TypeScript 静态检查、浏览器视觉与可访问性检查。

**Spec:** `docs/superpowers/specs/2026-08-30-pf03-pre-platform-convergence-design.md`

**Development Plan:** `docs/superpowers/plans/2026-08-30-pf03-pre-platform-convergence-feature-development.md`

## Global Constraints

- 任务提示中提供的功能开发任务 ID 是唯一返工对象。先 `wait_threads` 等待其交付；开发仍在写入时不得在同一目录运行 build/test、格式化、清理或任何可能写锁文件的命令。
- 不信任“已通过”“已完成”等文字结论；从具体基线、最终 commit、工作树和命令重新取证。
- 不修改生产代码、不自行修缺陷、不提交开发变更、不 push、不询问用户。阻断项以精确复现消息发送给开发任务，等待其新一轮修复和自验证后从受影响检查点复验。
- 不因真实外部依赖失败把代码验收写成通过，也不把外部不可达泛化为凭据、代码或整机网络故障。分别记录 DNS、TCP、端口、进程、HTTP 和日志证据。
- `DSH.md`、`CLAUDE.md`、`AGENTS.md`、`docs/prototypes/` 不属于交付修改；生成物、缓存、日志、临时截图、`bin/obj/TestResults` 不得作为源码交付。
- 验收范围截止 PF03 前平台能力收束；PF-03、PF-04 Audit/File/Notification、PF-05～PF-11 和 MES 业务代码一律视为越界。生产操作模式中八个明确 `coming-soon`、禁用且不导航/不请求 API 的展示卡片是唯一例外，不得被误判为业务实现，也不得伪装成可用。
- 当前任务只审阅最终证据和必要抽查；本任务承担主要验收执行，因此每个“通过”必须附命令/断言/截图或静态证据。

---

### Task 1: 等待开发交付并锁定验收对象

**Files:**

- Read: `docs/evidence/pf03-pre-platform-convergence-baseline-2026-08-30.md`
- Read: `docs/evidence/pf03-pre-platform-convergence-development-verification-2026-08-30.md`
- Read: `docs/superpowers/specs/2026-08-30-pf03-pre-platform-convergence-design.md`
- Read: `docs/superpowers/plans/2026-08-30-pf03-pre-platform-convergence-feature-development.md`
- Create only after final disposition: `docs/evidence/pf03-pre-platform-convergence-independent-acceptance-2026-08-30.md`

- [ ] 使用任务提示中的具体开发任务 ID 调用 `wait_threads`，长等待且仅在状态变化时读取；需要上下文时 `read_thread`，不高频轮询。
- [ ] 开发交付后记录其 thread ID、绝对工作树、分支、启动 baseline、最终 commit、提交列表、ahead/behind、staged/unstaged/untracked/ignored 状态和自验报告路径。
- [ ] 验证报告中的最终 commit 在指定工作树可解析，`HEAD` 与报告一致；若开发留下未提交源码，纳入验收对象并标记交付完整性风险，不能悄悄忽略。
- [ ] 在运行任何写构建产物的命令前确认开发任务处于完成/等待返工状态，且没有 Vite、UnifiedHost、dotnet/testhost 正在占用目录。
- [ ] 若缺少工作树、基线、最终 commit 或自验证证据，立即向开发任务发送缺项清单并等待补充；不向用户提问。

### Task 2: 审查 Git 范围、WIP 保全和依赖边界

**Files:**

- Read: complete diff from development baseline to final state
- Read: `Directory.Packages.props`
- Read: `src/frontend/package.json`
- Read: `src/frontend/pnpm-lock.yaml`
- Read: all changed `.csproj`, `.slnx`, source, test and documentation files

- [ ] 使用 baseline→final commit diff、final commit→working tree diff 和 untracked 清单构成完整交付；按 Task/规格建立“文件 → 要求 → 测试”矩阵。
- [ ] 确认原有平台管理 WIP 没被 reset/覆盖：`AppDataTable.*`、Identity/SystemData `Export/`、锁屏、相关测试仍可追溯；`AppDataTable` 是小步提取而非整体替换。
- [ ] 计算并比对 `DSH.md` 初始/最终哈希；确认 `CLAUDE.md`、`AGENTS.md`、`docs/prototypes/` 没有任务修改，提交中没有 `node_modules/dist/bin/obj/TestResults/log/cache`。
- [ ] 检查仅新增 `vue-i18n@11.4.10` 和 `Microsoft.AspNetCore.OData@9.5.0` 两项批准依赖；锁文件/中央包版本一致，无 preview、无第二 UI/表格/动态 LINQ/权限框架。
- [ ] 检查 Querying/Web/Infrastructure 依赖方向、服务间引用和许可证记录；`IndustrialPlatform.Querying` 不得引用 Web、OData、SqlSugar 或业务服务。
- [ ] 用 `rg` 检查业务页没有直接 VXE/XE import，没有 `[EnableQuery]`/`IQueryable` API，没有 PF-03/PF-04+ 实现、管理导航空领域入口、跨服务中央查询或假数据；生产宫格的 `coming-soon` 注册必须无 route/API，不能借此豁免任何业务代码。
- [ ] 对任何越界、丢 WIP、生成物入库、未经批准依赖或架构倒置直接判阻断并进入 Task 8 返工环。

### Task 3: 逐条完成规格覆盖和静态契约验收

**Files:**

- Read: all implementation and test files mapped by the feature plan
- Update: `docs/evidence/pf03-pre-platform-convergence-independent-acceptance-2026-08-30.md`

- [ ] 建立以下矩阵，每格只允许 `PASS / FAIL / BLOCKED-EXTERNAL / NOT-APPLICABLE`，并附文件、测试或截图证据：

| 能力 | 必验内容 |
| --- | --- |
| 品牌 | 四种同源 SVG、统一组件、PC/Mobile/Login/SSO/favicon、可访问名称、品牌名不翻译 |
| Header | 左中右三段、环境严格配置、真实 tenant、Ctrl+K 授权菜单搜索、真实状态、无假通知/上下文 |
| 导航 | 96/208 Token、一级图标文字、二级搜索、expanded/secondary-collapsed/compact、递归权限和动态导航 |
| PC 生产操作模式 | `platform.operation.view`、双模式安全切换、独立简洁 Shell、3 列优先大卡片、八项待实现真实禁用、设置可用、无假 API/路由、管理状态不丢失 |
| Tabs/PageHeader | titleKey、语言热切换、pin/关闭规则、会话态保留、长期偏好不含敏感筛选值、统一页面结构 |
| i18n | zh-CN/en-US 键同构、时区独立、Intl 格式、Shell/公共组件/黄金页/错误/导出资源化 |
| Query/Table | 单一 QueryDescriptor、顶部/列头/loader/export 一致、公开 API 冻结、VXE 私有访问隔离、既有能力无回退 |
| OData | 只读 Users、平台信封、白名单 options/字段/运算、分页对齐、上限、超时、无 IQueryable/EnableQuery |
| 权限 | Identity 权威目录、跨清单契约、SystemData 操作 Gate、直接 API 403、tenant/data scope/soft-delete 不可绕过 |
| 能力边界 | NuGet/前端模块/嵌入模块/服务/产品分类、独立启动规则、PF04 决策门、无提前依赖 |

- [ ] 检查代码与计划没有 `TBD/TODO/placeholder/以后补` 作为交付条件；合法既有技术债必须在证据中明确且不影响本范围。
- [ ] 检查 ApiResult 新增可选字段使用 null 忽略，既有成功 JSON 没被无意改变；前端按 code 翻译且保留 message fallback/traceId。
- [ ] 检查 `$skip % $top != 0` 返回稳定 400；`$select` 在最多 100 条稳定 read model 上白名单投影，没有为此引入动态 ORM 投影框架。
- [ ] 任一规格项只有实现没有测试，或只有测试名字没有有效断言，均不得判 PASS。

### Task 4: 独立执行后端新鲜构建、全量测试和安全定向测试

**Files:**

- Read/run: backend solution and tests
- Append evidence: `docs/evidence/pf03-pre-platform-convergence-independent-acceptance-2026-08-30.md`

- [ ] 确认没有锁进程后先运行 fresh Release build，并记录 exit code、warning/error 数：

```powershell
dotnet build src/backend/IndustrialPlatform.slnx --configuration Release
```

- [ ] 只有 fresh build 成功后运行全量 `--no-build`：

```powershell
dotnet test src/backend/IndustrialPlatform.slnx --configuration Release --no-build
```

- [ ] 独立运行三组重点项目，保存 passed/failed/skipped：

```powershell
dotnet test tests/BuildingBlocks/IndustrialPlatform.BuildingBlocks.Tests/IndustrialPlatform.BuildingBlocks.Tests.csproj --configuration Release
dotnet test tests/Identity/IndustrialPlatform.Identity.Tests/IndustrialPlatform.Identity.Tests.csproj --configuration Release
dotnet test tests/SystemData/IndustrialPlatform.SystemData.Tests/IndustrialPlatform.SystemData.Tests.csproj --configuration Release
```

- [ ] 审查并运行 OData 安全测试：未认证 401、无权 403、租户隔离、soft-delete、非法/未知字段、禁用 options、filter node/function/sort/top 上限、未对齐 skip、稳定排序、取消/10s 超时、select/count/filter/order/paging。
- [ ] 审查列表和 Excel 使用同一 descriptor/schema/data scope；构造至少一组相同条件，验证列表 total/顺序/字段与导出数据一致。
- [ ] 审查 SystemData action 无权直接调用 API 确实 403，前端 Gate 不替代后端 handler。
- [ ] 审查 `platform.operation.view` 已进入 Identity 权威目录/seed/前端目录；无权直接访问生产模式路由得到 403，只有管理权限或只有生产权限时自动进入唯一可用模式。
- [ ] 构建或测试失败若可由当前 diff 复现，记录首个根因、完整命令、相关文件和期望结果，进入 Task 8；不得用旧结果、过滤测试或重复运行掩盖。

### Task 5: 独立执行前端静态、单元、契约和构建门禁

**Files:**

- Read/run: `src/frontend`
- Append evidence: `docs/evidence/pf03-pre-platform-convergence-independent-acceptance-2026-08-30.md`

- [ ] 依次运行并分别记录退出码；前一步失败仍可继续不依赖它的诊断，但最终不得 PASS：

```powershell
cd src/frontend
pnpm.cmd lint
pnpm.cmd typecheck
pnpm.cmd test:unit
pnpm.cmd build
```

- [ ] 单独运行高风险定向组，以便失败可定位：

```powershell
pnpm.cmd test:unit -- tests/unit/localizationResources.spec.ts tests/unit/localizationPreferences.spec.ts tests/unit/localizationFormatters.spec.ts tests/unit/queryDescriptor.spec.ts tests/unit/odataAdapter.spec.ts tests/unit/appDataTablePublicApi.spec.ts tests/unit/vxeUpgradeContract.spec.ts tests/unit/permissionCatalog.spec.ts
pnpm.cmd test:unit -- tests/components/PlatformBrand.spec.ts tests/components/PcLayout.spec.ts tests/components/shell/PlatformToolRail.spec.ts tests/components/shell/PlatformFunctionTree.spec.ts tests/components/shell/PcWorkspaceTabs.spec.ts tests/components/PcExperienceModeControl.spec.ts tests/components/OperationLayout.spec.ts tests/components/PcOperationHomePage.spec.ts tests/components/AppPage.spec.ts tests/components/AppQueryPanel.spec.ts tests/components/AppDataTable.spec.ts tests/components/IdentityUsersPage.spec.ts tests/components/SystemDataActionPermissions.spec.ts
```

- [ ] 通过源码和测试验证两种 locale 完整同构、切换后 menu/tab/document title/table/query/error 同步，时区不改变，未知 code fallback 可见。
- [ ] 验证 AppDataTable 所有既有普通/明细/树、异步、选择、分组、筛选、排序、分页、偏好、导出、打印、全屏、错误/空/加载行为；确认业务页无 VXE 泄露。
- [ ] 验证页面状态只在会话内，关闭 tab 后清理；长期 localStorage 不包含业务筛选、Token、权限或用户资料。
- [ ] 验证生产模式偏好按 tenant+user+device 隔离；模式切换不登出、不丢管理 Tabs/查询/滚动；八个 `coming-soon` 在 mouse/Enter/Space 下均不导航、不发请求，第九个设置只使用现有语言/主题/全屏/模式能力。
- [ ] 任何 lint/type/typecheck/build/test 回归进入 Task 8；仓库启动前已知失败只有在基线证据可证明且本轮未扩大时才可标为“既有非阻断”，仍需在最终报告单列。

### Task 6: 执行 Mock E2E、视觉矩阵和可访问性验收

**Files:**

- Run: `src/frontend/tests/e2e/pc-shell.spec.ts`
- Run: `src/frontend/tests/e2e/pc-operation-mode.spec.ts`
- Run: `src/frontend/tests/e2e/workspace-tabs.spec.ts`
- Run: `src/frontend/tests/e2e/user-management-golden.spec.ts`
- Run: `src/frontend/tests/e2e/systemdata-admin.spec.ts`
- Review: current visual snapshots and Playwright artifacts

- [ ] 运行目标 Mock E2E：

```powershell
cd src/frontend
pnpm.cmd exec playwright test --config=playwright.config.ts tests/e2e/pc-shell.spec.ts tests/e2e/pc-operation-mode.spec.ts tests/e2e/workspace-tabs.spec.ts tests/e2e/user-management-golden.spec.ts tests/e2e/systemdata-admin.spec.ts
```

- [ ] 至少人工检查 1280×720 与 1440×900 的 zh-CN/en-US、明/暗、expanded/secondary-collapsed/compact、生产操作宫格和用户管理黄金页；只查看当前 final commit 重新生成的截图，不沿用无法证明版本的旧图。
- [ ] 检查 Logo 清晰、Header 三区不挤压、一级文字无需 hover、英文两行不改变总宽、二级搜索聚焦、Tabs/PageHeader/Query/Table 层级、空/加载/错误/无权状态、行操作溢出和窄窗口无横向溢出。
- [ ] 将生产宫格截图与用户提供的参考图并排检查：保留 3×3 大卡片、一跳入口、统一线性图标和高对比层级，但必须使用 Industrial Platform 主题、单一当前语言并移除第三方品牌/字幕；卡片尺寸、图标 56～72px、标题 24～28px 和“待实现”状态清晰。
- [ ] 键盘验证 `Ctrl+K`、Escape、Tab 顺序、焦点环、菜单/对话框关闭后焦点回收；按钮/图标均有 aria-label 或可见文字，颜色对比和 44px 触控目标在关键操作可接受。
- [ ] 检查浏览器 console、pageerror、失败网络请求；非预期异常、VXE DOM 错误、缺翻译 key 或重复请求视为阻断。
- [ ] 视觉快照只有在人工确认设计一致后可接受；开发任务若直接更新快照掩盖差异，退回其恢复并修正实现。

### Task 7: 执行真实 UnifiedHost/OData/权限代表路径

**Files:**

- Read/run: existing UnifiedHost and real Playwright configurations
- Append evidence: `docs/evidence/pf03-pre-platform-convergence-independent-acceptance-2026-08-30.md`

- [ ] 使用仓库既有 `appsettings.Development.local.json` 自动发现和真实配置启动 UnifiedHost；不创建假的本地服务、不改凭据、不把 Mock 结果算真实结果。
- [ ] 先验证 `/health/ready` 和所需 PostgreSQL/Redis/RabbitMQ 依赖，再运行仓库现有真实 Identity/UnifiedHost Playwright 配置和新 Users OData 代表场景。
- [ ] 真实路径至少覆盖：admin 登录、管理/生产模式权限切换与路由守卫、用户列表首屏、filter/order/paging/count/select、非法 option 400、无权 403、tenant scope、同 descriptor Excel、语言/时区导出参数和既有用户 CRUD 不回退。生产占位卡不需要 MES 后端，但必须证明零请求。
- [ ] 记录启动/停止命令、进程 ID、端口、HTTP 状态、测试数、日志路径；结束后正常停止自己启动的所有进程并确认端口释放。
- [ ] 若外部依赖不可达，分别检查主机解析、TCP 端口和应用日志；将该行标为 `BLOCKED-EXTERNAL`，继续完成 Tasks 2～6，不能把真实路径标 PASS，也不因外部阻塞跳过代码安全测试。

### Task 8: 缺陷退回、等待修复和受影响范围复验

**Files:**

- No production edits in validator
- Update working acceptance notes only

- [ ] 对每个阻断项形成一条可执行消息：严重度、规格条款、绝对/仓库相对文件、行或符号、最小复现命令、实际结果、期望结果、必要回归范围。
- [ ] 通过 `send_message_to_thread` 发给唯一开发任务，不向用户提问，不在验收任务里直接修代码。
- [ ] 使用 `wait_threads` 等待开发任务完成新一轮修复和自验；读取新增 commit、diff、命令和证据。开发写入期间停止同目录测试。
- [ ] 先复跑最小复现，再复跑受影响组件/项目；若改动影响公共 Shell、Querying、AppDataTable、权限或应用装配，复跑 Tasks 4～7 的完整相关门禁。
- [ ] 每个缺陷状态记录为 Open → Fixed by commit → Retest PASS/FAIL；失败继续同一循环，不新增平行修复线程。
- [ ] 仅当缺陷确属规格外且修复会扩大到 PF-03/PF-04+ 时标记“拒绝扩范围”，按现有边界验收；不自行扩大授权。

### Task 9: 形成独立验收报告并交还当前任务

**Files:**

- Create/update: `docs/evidence/pf03-pre-platform-convergence-independent-acceptance-2026-08-30.md`

- [ ] 报告首行给出唯一结论：`ACCEPT`、`REJECT` 或 `ACCEPT WITH EXTERNAL GAP`；代码/安全/边界缺陷不得使用第三种结论，只有已证明的外部联调不可达才可使用。
- [ ] 报告包含：开发 thread ID、绝对工作树、baseline/final commits、最终 status、依赖变化、范围矩阵、全部命令/退出码/passed/failed/skipped、截图索引、真实路径、缺陷闭环、外部缺口和剩余风险。
- [ ] 明确列出：四 Logo、Header、一级/二级导航、PC 管理/生产双模式及参考图对照、Tabs/PageHeader、黄金页、i18n、QueryDescriptor/AppDataTable、Users OData、权限/数据范围、能力分类和 PF04 门禁各自证据。
- [ ] 最后再次运行 `git diff --check`、`git status --short`、生成物扫描和 `DSH.md` 哈希比对；清理本任务生成的非交付报告、trace、screenshots、cache、test result。
- [ ] 验收报告本身保持未提交，交由当前任务审阅；不得把报告 commit 混入已验收的开发 commit，也不得 push。
- [ ] 向当前任务发送简洁最终消息：结论、阻断项数量、最终 commit、后端/前端/Playwright/真实环境摘要、报告绝对路径。只有该证据消息发出后，本验收任务才结束。

## Disposition Rules

- `ACCEPT`：规格矩阵全部 PASS；fresh backend/front gates、目标 E2E/视觉和真实代表路径通过；无边界/安全/交付完整性问题。
- `ACCEPT WITH EXTERNAL GAP`：所有可离线/本地验证项 PASS，唯一缺口是已分别取证的外部 PostgreSQL/Redis/RabbitMQ/网络条件，且没有证据显示代码缺陷。
- `REJECT`：任一代码、权限、租户/数据范围、OData 白名单、i18n、视觉、WIP 保全、范围或标准门禁失败；必须先退回开发任务，只有无法在既定范围内修复时才最终拒收。

## 用户第二轮复核验收附录（2026-08-30）

- [ ] 对照用户上传的品牌母版逐像素/透明边界检查 Header 横向标和 favicon 图形标，确认未重新绘制造型、未保留大块透明边、document title 正确。
- [ ] 在 1280×720、1440×900 打开全局搜索、主题、语言、消息和“更多”弹层；对每个浮层断言 bounding box 位于 viewport、无 Header 裁切，右区距 viewport 右侧不超过 4px，PC 与 DEV 间距为 4px。
- [ ] 验证消息入口无假 badge/假数据/PF-04 API；消息旁“在线用户”图标只对 `identity.session.view` 可见且打开右侧抽屉。核对有效 refresh session 口径、加载/刷新/空态/失败重试、当前会话标记以及无 token/IP/UserAgent 原值或哈希泄露；截图中的“发送消息”必须禁用并明确待 PF-04，不得出现假成功或网络调用。
- [ ] 分别以 view-only、revoke、无权限和跨租户场景验收会话撤销；强制退出需二次确认、单会话生效、重复请求幂等，当前会话被撤销后本地退出。不得借在线用户抽屉获得其他 Identity 管理权限。
- [ ] 顶栏不再存在独立锁定按钮；用户菜单顺序和图标为“个人中心、清理缓存、锁定工作区、退出登录”。个人中心真实显示当前认证用户并可进入现有改密流程；清理缓存后 Tabs/page-state/table-preferences 被清除且 UI 同步刷新，同时 auth、租户、语言、主题、终端覆盖和 PC 模式偏好均保持，网络中不得出现服务端缓存清理请求；锁定与退出回归通过。
- [ ] 用不同 viewport 高度和至少 12 个一级导航测试自适应与“更多”；一级 icon 上/文字下，溢出活动项可见，二级收起同时隐藏搜索/列表，Tabs 无第三个菜单搜索。
- [ ] 切换 zh-CN/en-US 后逐项检查一级/二级/子菜单、更多、全局搜索结果、Tabs、document title；除动态缺键 fallback 外不得残留旧语言。
- [ ] 将用户管理与用户组管理顶部查询并排检查统一紧凑视觉；真实点击列头查询、输入/清空、顶部/列头互斥、分页和导出，浏览器 console/pageerror 必须为零。
- [ ] 使用真实系统管理员会话打开 `http://localhost:5173/pc/identity/users`，确认生产操作模式入口实际可见；切换到 `/pc/operation` 后八个业务卡片均为真实禁用“待实现”，再返回管理模式并验证原路由/页签/查询状态不丢失。额外用缺少 `platform.operation.view` 的账号确认入口与直达路由都被拒绝。
- [ ] 对列头查询同时检查 UI、console/pageerror 和网络：切换、输入、清空、分页、导出均不得出现“加载用户列表失败”或 4xx/5xx；保存请求 URL、参数与响应证据，不能把“没有 JavaScript exception”当作通过。
- [ ] 将终端预览与首页并排测量外层 padding 和 card gap；PC 内容区不得叠加双层 padding，预览内部卡片间距保持 token 一致。
- [ ] 开发修复后重跑受影响组件/契约、frontend 全门禁、fresh backend build/full tests、Mock 与可用真实 E2E；任何仅靠快照更新、隐藏报错或把外部阻塞当通过的处理都退回整改。
