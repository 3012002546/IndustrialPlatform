# PF-03 管理页 R1/R2 与顶栏主题独立复验（2026-09-02，最终）

## 1. 唯一结论

**ACCEPT**。

本轮只复验上一版报告顶部的 R1（Identity 六页前端自有交互文案本地化）与 R2（loader 页面查询/重置分页同步），并执行用户新增的顶栏主题硬门槛；不沿用旧 REJECT，也不重跑全部历史 PF-03。开发候选 `7fdd448017bfde2a2d9743a6058399b605306674` 经独立验收发现 reset 页面级测试缺口后，原开发任务仅补测试提交 `185045ce13ed885fe2e4175a3f5b213f1b430e4a`。最终生产代码未因补测再次变化，R1/R2 均以源代码、页面级行为测试和真实管理员页面证据闭环；顶栏三配色、明暗切换及刷新保持也在真实页面通过，没有以静态原型替代。

本节至下一条分隔线是本文件唯一有效的最新结论；以下旧 REJECT/ACCEPT 章节仅保留历史审计。

## 2. 验收对象、提交与边界

- 工作树：`D:\Code\Industrial Platform\IndustrialPlatform`
- 分支：`develop`
- 本轮修复 baseline：`efd7114b7b687d364b84db065106e01f562dda9b`
- 功能修复提交：`7fdd448017bfde2a2d9743a6058399b605306674`（`fix(identity): localize management interactions and sync pagination`）
- 最终提交：`185045ce13ed885fe2e4175a3f5b213f1b430e4a`（`test(identity): cover reset pagination contract`）
- baseline→final：11 个唯一文件，1,668 insertions / 306 deletions；9 个 locale/type/页面文件和 2 个组件测试文件。最终补交仅给 `IdentityManagementPagination.spec.ts` 增加 25 行，没有改生产代码。
- `git diff --check efd7114..185045c` exit 0。对 `src/backend`、`src/frontend/src/api`、`src/frontend/src/router`、`package.json`、`pnpm-lock.yaml` 的同区间 diff 为 0；没有后端、API URL/参数/payload、路由、权限、依赖或锁文件变化。
- 没有修改或提交 `AGENTS.md`、`CLAUDE.md`、`DSH.md`、`docs/prototypes/**`；没有提交 dist/bin/obj/TestResults/cache/log/临时截图；没有 push。
- 独立验收没有修改生产代码、没有创建业务数据、没有确认任何写操作、没有启停调试服务。

## 3. 阻断项闭环

### R1：六页交互文案本地化 — CLOSED

- 完整审查六页当前源码：`IdentityUserGroupsPage`、`IdentityRolesPage`、`IdentityPermissionsPage`、`IdentityAuditsPage`、`SsoProvidersPage`、`SsoClientsPage`。中文字符扫描只命中源码注释，不再命中运行时字符串；validation、`ElMessage`、`ElMessageBox`、错误 fallback、成功/失败、启停、删除/恢复/解绑/移除确认均绑定 `localeMessages[locale].identity.management.*.feedback` 或现有 common copy。
- 表单规则改为 `computed<FormRules>`，切换语言后读取当前 locale，而不是在 setup 时冻结旧语言。审计结果使用 locale 的 success/failed；`auditSuccessFilter` 继续把 `success/true` 与 `failed/false` 正确映射到布尔查询值。
- SSO Provider 连接测试仍原样显示后端动态 `result.message`，只本地化前端拥有的标题/按钮/fallback；没有翻译动态业务数据。管理 API 调用、参数、payload、并发版本和 PermissionGate 未改变。
- 真实已登录管理员 Chrome：切到 `en-US` 后，`/pc/identity/sso/clients` 空表单点击 Save 显示 **`Enter a client name`**；`/pc/identity/roles` 空表单显示 **`Enter a role name`**。两次均因前端校验拦截，没有提交写请求。Shell、导航、页面标题同步英文，动态角色名称“系统管理员”仍作为后端业务数据保留。
- 真实环境 UserGroups、SSO Provider/Client 当前均为 0 行，无法在不制造业务数据的前提下打开行级启停/删除/解绑/移除确认框；该项由逐符号 diff、运行时中文扫描、locale 类型检查及组件测试覆盖，不伪造真实行数据。

### R2：查询/重置分页同步 — CLOSED

- Users、UserGroups、Roles、Audits 四个 loader 页面均向 AppDataTable 传入 `:initial-page-index="pageIndex"`；AppDataTable 的既有 watcher 在父级 2→1 时只同步内部 pager，不自行 reload。
- `IdentityManagementPagination.spec.ts` 在 UserGroups/Roles/Audits 三页分别先调用真实表格 `onPageChange(2)`，再触发顶部查询，断言 UI pager 回 1、最后请求 `pageIndex: 1`、该动作只新增一次 list 请求。
- 独立验收发现 reset 未被页面级测试直接证明后，已退回原开发任务；`185045c` 为三页分别新增同结构 reset case。最终定向运行中，查询 3 case + reset 3 case 全部通过；Users 的既有 `initialPageIndex` 接线与相关回归保持通过。
- 真实环境 UserGroups 0 条、Roles 3 条，分页器没有第 2 页；因此不通过操纵 DOM 或制造数据伪造真实 page-2 证据，页面级挂载测试是本项可执行的行为证据。

## 4. 顶栏主题与既有功能退化门槛

真实管理员页面 `http://localhost:5173/pc/identity/roles` 中，三套配色分别得到不同的顶栏渐变，文字均为白色：

- industrial-cyan：`rgb(0,100,135) → rgb(8,124,159)` 五段渐变；
- technology-blue：`rgb(30,58,138) → rgb(59,130,246)` 四段渐变；
- neutral-gray：`rgb(31,41,55) → rgb(107,114,128)` 四段渐变。

真实切换到 technology-blue + dark 后，刷新前后 `data-ip-palette=technology-blue`、`data-ip-color-mode=dark` 与完整 `background-image` 完全一致；随后显式切到 light 也生效。最终已恢复用户原状态 neutral-gray + system，系统当前解析为 light，URL 恢复 `/pc/identity/users`、`zh-CN`，页面 `role=alert` 为 0。

本轮 final 没有主题生产代码 diff；fresh 全单测包含 `themeContrast`、theme store/preferences/resolver 等契约，production build 也消费真实主题资源。以上是真实主题能力与刷新链路，不是静态原型截图。真实浏览器本轮新增 console error 为 0；仍可见历史 Vue“reactive Component”性能 warning，本提交未引入或扩大该路径，不作为 R1/R2 回归。

角色操作列在真实 3 行数据中仍为“编辑角色 + 更多”，系统角色编辑按钮仍 disabled；提交沿用的窄列行为没有删除原操作，也没有改变权限或写入处理函数。未发现本轮增量造成的既有功能退化。

## 5. Fresh 独立门禁

| 命令/范围 | Exit | 结果 |
|---|---:|---|
| `pnpm.cmd exec vitest run`（InteractionCopy、Pagination、AppDataTable、Roles、Users、AccessComposition、AppFormDrawer） | 0 | **7 files / 118 tests passed** |
| `pnpm.cmd test:unit` | 0 | **100 files / 756 tests passed** |
| `pnpm.cmd lint` | 0 | PASS |
| `pnpm.cmd typecheck` | 0 | PASS |
| `pnpm.cmd build` | 0 | **2,459 modules**，production build PASS；仅既有 >500k chunk warning |
| `git diff --check efd7114..185045c` | 0 | PASS |
| backend/API/router/package/lock 区间 diff | 0 | 空 diff，边界 PASS |

本轮最终增量是前端页面/locale/test，未重复运行与之无因果关系的历史后端 full gate、完整 Mock Playwright 或真实外部依赖矩阵；按交接只执行一个有界已登录 Chrome 会话，未启停服务。上述未运行项不是本轮 ACCEPT 的替代证据，也不存在外部环境缺口结论。

## 6. 最终状态

- 唯一结论：**ACCEPT**
- 开发最终 commit：`185045ce13ed885fe2e4175a3f5b213f1b430e4a`
- 阻断项：R1、R2 均 CLOSED；reset 测试缺口通过原开发任务补交并独立复验
- 报告：`D:\Code\Industrial Platform\IndustrialPlatform\docs\evidence\pf03-pre-platform-convergence-independent-acceptance-2026-08-30.md`
- 报告保持 untracked / 未提交；未 push
- 分支相对 `origin/develop` ahead 63；共享工作树仍含其它受保护 WIP，不能称为 clean

---

以下内容均为更早轮次历史记录，已被上述 `185045c` **ACCEPT** 结论替代。

# PF-03 管理页统一与小问题收束独立复验（2026-09-02，当前工作树候选）

## 1. 唯一结论

**REJECT**。

本轮按最新交接把 `efd7114b7b687d364b84db065106e01f562dda9b` 后的当前未提交工作树作为稳定候选，不要求新增 commit，也不沿用下方历史“纯样式越界”结论。已批准的 loader/OData、25 条分页和服务端导出能力本轮视为保留能力。角色窄列、AppDataTable 可见错误/重试、审计布尔映射、SSO 子抽屉上下文标题及用户黄金页表面均通过；但仍有两个独立阻断：英文交互实际出现中文校验文案，且角色、用户组、审计三个 loader 页面没有把父级 `pageIndex=1` 传回 AppDataTable，导致从第 2 页执行顶部查询/重置后内部页码仍停在第 2 页。代码与真实页面失败不能归为 external gap。

本节至下一条历史分隔线是本文件唯一有效的最新结论。

## 2. 验收对象与范围

- 工作树：`D:\Code\Industrial Platform\IndustrialPlatform`
- 分支：`develop`；HEAD：`efd7114b7b687d364b84db065106e01f562dda9b`
- 候选形态：HEAD 后未提交 WIP；没有要求开发整理、覆盖、清理或补 commit
- 本轮目标增量：`AppDataTable.vue`、`IdentityRolesPage.vue`、`IdentityAuditsPage.vue`、两个 SSO 页面、locale/type 与相关组件测试；共享工作树其余后端、文档、Shell、SystemData、截图等 WIP 未被本验收修改或归因
- 没有修改生产代码、没有提交、没有 push、没有停止/重启/接管 VS、VS Code 或调试服务

## 3. 已通过项目

### 3.1 角色权限操作列

- 源码和组件测试确认操作区 `inline-flex + white-space: nowrap`，AppDataTable 把真实操作列宽传给 action slot。
- 真实管理员 Chrome 页面在约 128px 渲染宽度时，每行显示“编辑角色 + 更多”，行内区域约 101.6px、`white-space=nowrap`；点击“更多”只显示剩余“分配权限”。普通角色菜单项可用，SYSTEM_ADMIN 对应菜单项 `aria-disabled=true`。
- Escape 后焦点回到“更多”触发器；恢复到约 165px 后“编辑角色 + 分配权限”均直接显示且无“更多”。组件测试同时覆盖 120px/220px 目标状态。
- 权限仍由 `identity.role.update` 与 `identity.role.assign-permission` 决定；直接项和更多项均保留 PermissionGate；系统角色直接按钮和菜单项均禁用。现有编辑/分配处理函数及写入 payload 未改变。

### 3.2 黄金页与统一表面

- 真实 `/pc/identity/users`：页面标题已唯一标识数据集；工具栏没有冗余“用户列表”。列头查询只保留输入控件的可访问名称，没有重复可见 label；业务按钮与表格排序/分组/下载/打印/列设置等工具分组独立。
- 用户、用户组、角色、审计继续使用共享 AppPage/AppQueryPanel/AppDataTable；权限目录保持只读树；SSO 结构化操作使用 AppFormDrawer。
- 审计 `auditSuccessFilter` 对 `true/'true'/'success'` 返回 true，对 `false/'false'/'failed'` 返回 false；列表、loader、export 三条路径共用该映射，结果单元格改用 locale copy。
- SSO Provider 标题为 `账号抽屉标题 · provider.name`，SSO Client 标题为 `端点抽屉标题 · client.name`。真实环境当前两个列表无可用父行，因此真实打开子抽屉记为 SKIP，源码与定向契约通过。
- 语言菜单在中英文状态下均显示语言自己的名称“中文 / English”，真实切换后 `html.lang=en-US` 且 Shell/导航/页面静态标题即时英文。

### 3.3 AppDataTable loader 错误

- loader 抛错后设置 `loadError`、发出 `load-error`，渲染 `role=alert` 的本地化错误面和键盘可达“重试”按钮。
- 重试重新执行 loader；成功后清除错误面。定向测试验证首次失败、第二次成功、loader 精确调用 2 次及错误事件。
- `initialPageIndex` prop 变化只同步内部 pager，不触发 loader；测试验证父级 2→1 后页码为 1、调用数不增加。

## 4. 阻断项

### R1 英文交互仍有中文硬编码

真实已登录管理员页面切换 `en-US` 后进入 `/pc/identity/sso/clients`，页面标题、导航、字段和按钮均为英文；打开 “Create client” 并在空表单点击 “Save”，真实校验错误为 **`请输入 Client 名称`**。这是无网络写入的前端校验，直接证明“中英文无硬编码交互文案”未完成。

静态复查还发现用户组、角色、权限目录、审计、SSO Provider/Client 的 `ElMessage`、`ElMessageBox`、错误 fallback 和校验规则存在大量中文字符串，例如角色“角色创建成功/请输入角色名称”、Provider“连接测试成功/请输入登录源名称”、Client“移除端点确认/请输入端点地址”。新增 locale 只覆盖表面标签、`more` 与表格 load error，不能关闭交互本地化要求。

### R2 三个 loader 页面的顶部查询/重置没有同步内部页码

AppDataTable 新增的 `initialPageIndex` watcher本身正确，并保证同步时不额外 reload。用户管理传入 `:initial-page-index="pageIndex"`，因此黄金页可在父级查询/重置时把内部 pager 从 2 回到 1且不重复请求。

但当前所有带 loader 的页面中：

- `IdentityUsersPage.vue`：已传 `initial-page-index`；
- `IdentityUserGroupsPage.vue`：未传；
- `IdentityRolesPage.vue`：未传；
- `IdentityAuditsPage.vue`：未传。

后三页的 `search/resetQuery` 只把父级 ref 设为 1并直接调用 legacy list；AppDataTable 内部 `currentPage` 不会观察该 ref，仍显示第 2 页。下一次表格刷新/排序又会按内部第 2 页请求，形成 UI、数据与请求页码不一致。现有测试只模拟“组件收到 prop 后”的同步，没有挂载后三个页面证明它们实际传入该 prop，因此该必验项未通过。

## 5. Fresh 门禁与真实环境

| 命令/范围 | Exit | 结果 |
|---|---:|---|
| `pnpm.cmd exec vitest run AppDataTable IdentityRoles IdentityUsers IdentityAccessPageComposition` | 0 | **4 files / 90 tests passed** |
| `pnpm.cmd test:unit` | 0 | **98 files / 740 tests passed** |
| `pnpm.cmd lint` | 0 | PASS |
| `pnpm.cmd typecheck` | 0 | PASS |
| 目标 tracked diff `git diff --check` | 0 | PASS；只有 Git 的 LF→CRLF 提示 |
| 真实 Chrome | — | 已登录 admin；Users 数据 3 条、Roles 数据 3 条、SSO Client 0 条；真实角色窄列/更多/禁用/键盘与英文校验已执行 |

按效率补充未重跑历史 PF03 后端、旧截图矩阵、移动端焦点与 SystemData real 用例；它们与当前目标增量无直接因果，记为 NOT RUN，而非本轮失败。

## 6. 最终状态

- 结论：**REJECT**；R1、R2 未闭环
- HEAD 未改变：`efd7114b7b687d364b84db065106e01f562dda9b`
- 报告：`D:\Code\Industrial Platform\IndustrialPlatform\docs\evidence\pf03-pre-platform-convergence-independent-acceptance-2026-08-30.md`，保持 untracked / 未提交
- 未修改生产代码、未提交、未 push；真实 Chrome 已恢复 `/pc/identity/users`、`zh-CN`

---

以下内容均为更早轮次历史记录，已被上述当前工作树 **REJECT** 结论替代。

# PF-03 前平台能力收束独立验收（2026-09-02，Identity 六页纯呈现收束最新轮）

## 1. 唯一结论

**REJECT**。

本轮不沿用任何旧 ACCEPT / ACCEPT WITH EXTERNAL GAP。开发最终提交为 `efd7114b7b687d364b84db065106e01f562dda9b`，父提交与本轮冻结 baseline 均为 `0d80c7fb34262253cd302054d15e3007371c8fae`。独立验收确认提交完成了六个 Identity 页面共享页面、查询、表格和抽屉表面的视觉收束，但存在会改变请求/查询/导出语义的越界代码、审计成功筛选映射错误、英文界面残留中文，以及两个 SSO 子表丢失父对象上下文标题。以上均为代码/契约/可用性阻断，不能归类为外部环境缺口。

真实 Identity 环境同时不可用：5173 正常监听并显示登录页，`localhost` DNS 同时解析为 `::1` 与 `127.0.0.1`，但最终取证时 5041 无 LISTENING 记录；真实 Playwright 登录两次均在 60 秒内未离开 `/login`，页面显示“网络不可用,请检查网络连接后重试”。按 EFF-01～05 在首个真实失败后终止剩余 14 个用例，没有循环探测、重启 VS/云 Docker 或另起替代服务。即使外部服务恢复，下述代码阻断仍要求 REJECT。

本节至下一条历史分隔线是本文件唯一有效的最新结论。

## 2. 验收对象、提交与增量

- 工作树：`D:\Code\Industrial Platform\IndustrialPlatform`
- 分支：`develop`
- baseline：`0d80c7fb34262253cd302054d15e3007371c8fae`
- 开发 final：`efd7114b7b687d364b84db065106e01f562dda9b`（`style(identity): unify access management pages`）
- final 增量：10 files，1,541 insertions / 597 deletions；6 个页面、3 个 locale/type 文件、1 个新组件测试
- 没有提交后端、router、package/lock、`AGENTS.md`、`CLAUDE.md`、`DSH.md` 或 `docs/prototypes/**`；没有新增依赖、PF-04 Notification/File、MES 或其他业务模块；未 push
- baseline 冻结时六个目标页已存在共享工作树 WIP；验收保存了逐文件 SHA256。最终提交后六个被开发提交的页面均与 HEAD 一致，用户页及其测试仍是验收前已有未提交 WIP。结论基于 final 的实际行为与 final commit 完整 diff，不把开发报告或浅层字符串测试当作行为证据。

## 3. 阻断项

### B1 请求、分页、查询与导出契约被纯呈现任务改变

本轮硬边界规定 API URL/方法/参数/payload、调用次数/顺序、分页/排序/筛选/导出语义必须保持。final 却包含下列功能变化：

- `IdentityUserGroupsPage.vue`、`IdentityRolesPage.vue`、`IdentityAuditsPage.vue` 将默认 `pageSize` 从 20 改为 25，并新增 AppDataTable loader。loader 不再只发送原页面字段，而是新增 `keyword`、描述/哈希/时间区间等过滤参数及 `sortField/sortOrder`。
- 上述三页新增服务端导出调用；`SsoProvidersPage.vue`、`SsoClientsPage.vue` 及其子表也新增 exporter，点击统一表格导出会产生此前页面没有的网络请求。
- 这不是 CSS、布局或无障碍属性变更，而是明确改变请求参数、分页和导出调用面的功能扩张；新测试没有锁定 URL、方法、参数、payload、次数或顺序。

### B2 登录审计“成功”筛选在分页/刷新/导出路径被反转

`IdentityAuditsPage.vue` 顶部下拉保存值为字符串 `success` / `failed`。旧 `loadAudits()` 正确使用 `query.success === 'success'`，但新 `loadAuditsTable()` 与 `exportAudits()` 使用 `success === true || success === 'true'`。因此选择“成功”后，首次顶部查询请求 `success=true`，随后表格分页、刷新或导出却会发送 `success=false`；这直接破坏查询与导出一致性。

### B3 中英文输出不完整

- 审计结果单元格仍硬编码 `row.success ? '成功' : '失败'`，切到 `en-US` 后仍显示中文，尽管 locale 已提供 `Success` / `Failed`。
- 用户组、角色、SSO Provider、SSO Client 的成功提示、错误兜底、确认对话框、按钮文字和表单校验仍大量硬编码中文。新测试只断言六个 locale section 存在，不渲染页面并验证真实英文输出。

### B4 两个 SSO 子表丢失父对象上下文

- Provider 旧标题包含 ``绑定账号 · ${accountsProvider.name}``；final 改为静态 `copy.accountTitle`，抽屉内标题也只有“绑定账号 / Bound accounts”。
- Client 旧标题包含 ``端点管理 · ${endpointsClient.name}``；final 改为静态 `copy.endpointTitle`，抽屉内标题也只有“端点管理 / Manage endpoints”。

这违反“绑定账号子表/端点子表必须有上下文标题”，用户无法确认当前正在操作哪个 Provider 或 Client。

### B5 完整 Mock 门禁未全绿

完整 Mock Playwright fresh 运行结果为 **120 passed / 8 failed，exit 1**：1 个 Mobile Tab 焦点顺序失败、6 个既有 PC/PDA/Mobile 像素快照超出 1% 阈值、1 个误纳入 Mock 配置的 SystemData real 用例登录超时。六页 final commit 没有直接改动这些文件，故不将它们伪装成六页新增缺陷；但“既有正常功能不得退化”和“完整门禁必须通过”的验收条件客观未满足，不能写成全绿。针对最新 THEME-01、Shell 和生产模式的独立定向 Mock 为 25/25 通过，详见第 6 节。

## 4. 六页及用户黄金页逐页结论

| 页面 | 表面规范证据 | 功能/契约证据 | 结论 |
|---|---|---|---|
| 用户管理（黄金页） | 继续使用 `AppPage + AppQueryPanel + AppDataTable`；顶部 6 个自解释控件无可见 label 且保留本地化 aria-label；单表无冗余 toolbar title | 文件 SHA256 与 baseline 相同；52 项定向组件测试包含该页并通过；真实 OData 因 5041 不可用未执行 | 表面静态通过；真实路径未验 |
| 用户组 | 使用共享 Page/Query/DataTable/FormDrawer，单表无冗余标题 | pageSize 20→25；新增 loader 参数、排序和 export 请求；业务写权限 gate 保留 | **阻断** |
| 角色权限 | 使用共享 Page/Query/DataTable/FormDrawer；系统角色编辑/分配仍禁用；半选父节点仍与 checked keys 合并提交 | pageSize 20→25；新增 loader 参数、排序和 export 请求 | **阻断** |
| 权限目录 | 使用共享 Page/Query，保持只读 `el-tree`、祖先链过滤与统计，没有误改为第二张表 | final 未新增写操作、路由或权限能力；真实数据因 5041 不可用未验 | 静态通过；真实路径未验 |
| 登录审计 | 使用共享 Page/Query/DataTable；安全字段仍只显示摘要 | pageSize/请求/导出越界；“成功”在 loader/export 映射为 false；英文结果仍中文 | **阻断** |
| SSO Provider | 使用共享 Page/DataTable/FormDrawer；`ssoManage` 与 `ssoTest` gate 仍分离；密钥抽屉打开时引用为空，不回显 secret | 新增 Provider/账号导出请求；账号子表标题丢失 Provider 名；交互提示/校验残留中文 | **阻断** |
| SSO Client | 使用共享 Page/DataTable/FormDrawer；启停、端点增删的既有写入方法和并发版本参数仍在 | 新增 Client/端点导出请求；端点子表标题丢失 Client 名；交互提示/校验残留中文 | **阻断** |

新增 `IdentityAccessPageComposition.spec.ts` 是源码字符串断言：验证是否包含共享组件、locale section 和是否移除 `el-dialog/el-drawer`。它没有挂载页面，也没有覆盖 API 契约、请求次数/顺序、分页/筛选/导出、权限、表单 payload、i18n 运行输出、确认框或子表上下文，因此不能关闭 B1～B4。

## 5. Fresh 独立门禁

| 门禁 | Exit | 结果 |
|---|---:|---|
| `git diff --check 0d80c7f..efd7114` | 0 | PASS |
| `dotnet build src/backend/IndustrialPlatform.slnx --configuration Release` | 0 | 0 warning / 0 error |
| `dotnet test src/backend/IndustrialPlatform.slnx --configuration Release --no-build` | 0 | **1,366 passed / 0 failed / 3 conditional skipped**（BuildingBlocks 158、Gateway 14、ReferenceData 14、SystemData 545、Identity 604、UnifiedHost 20、Integration 11+3 skip） |
| `pnpm.cmd lint` | 0 | PASS |
| `pnpm.cmd typecheck` | 0 | PASS；首次受限沙箱仅因 `.tmp/tsconfig.app.tsbuildinfo` EPERM 在编译前退出，获准写缓存后成功 |
| 定向 Vitest：Users、六页 composition、AppFormDrawer | 0 | **3 files / 52 tests passed** |
| `pnpm.cmd test:unit` | 0 | **97 files / 734 tests passed** |
| `pnpm.cmd build` | 0 | 2,459 modules，production build PASS；仅既有 >500k chunk warning |
| 定向 Mock Playwright：theme + pc-shell + pc-operation-mode | 0 | **25/25 passed** |
| 完整 Mock Playwright | 1 | **120 passed / 8 failed**；见 B5 |
| 真实 `identity-pages.spec.ts` | 1（人工按 EFF 中止） | 第 1/15 用例及 retry 均在 60 秒停留 `/login`，alert 为“网络不可用”；剩余 14 项未执行 |

## 6. THEME-01 与既有 Shell 能力

新增必验项已独立执行，不以静态原型替代：`theme.spec.ts` 的“顶栏背景跟随配色,明暗与刷新不覆盖既有渐变”通过，同时覆盖三配色、light/dark/system、刷新保持、首帧暗色和可读性；`pc-shell.spec.ts` 与 `pc-operation-mode.spec.ts` 同批通过，合计 25/25。该证据证明 Mock 运行时的主题链路仍工作。

由于 5041 无监听，无法在真实管理员会话上复验主题刷新、管理/生产往返保持以及六页 1280×720 / 1440×900 中英文明暗截图；这些项目明确记为 **REAL-RUNTIME NOT EXECUTED**，不由 Mock 或静态原型替代，也不是本轮 REJECT 的唯一原因。

## 7. 边界、许可证与仓库卫生

- final commit 没有后端、route、权限目录、模型、依赖或锁文件变化；没有整体重写 AppDataTable 或直接导入/泄露 VXE；没有 PF-04/后续 PF/MES 扩张。
- final commit 内没有 `bin/`、`obj/`、`dist/`、`TestResults/`、`test-results/`、日志或临时截图。
- 但 B1 的页面级 loader/exporter 接线仍属于明确的功能/请求扩张，不能因“仅修改前端文件”而视为纯呈现。
- 完整 Playwright 会改写仓库中既有证据截图；本轮新增改写均保持未暂存、未提交。受共享工作树保护约束，没有覆盖验收前已脏的 PC 截图或其他任务文件。

## 8. 最终状态

- HEAD：`efd7114b7b687d364b84db065106e01f562dda9b`
- 开发阻断闭环：**未闭环**（B1～B4）；本轮按最新交接不再向开发任务回传，也未修改生产代码
- 报告：`D:\Code\Industrial Platform\IndustrialPlatform\docs\evidence\pf03-pre-platform-convergence-independent-acceptance-2026-08-30.md`
- 报告保持 untracked / 未提交；未 push
- 共享工作树仍包含验收前保护 WIP、本报告及 Playwright 截图差异，不能称为 clean；最终 porcelain 清单见本任务最终消息

---

以下内容均为更早轮次历史记录，已被上述 `efd7114` 最新 **REJECT** 结论替代。

# PF-03 前平台能力收束独立验收最终报告（2026-09-01，性能、登录背景与 THEME-01 最新轮）

## 1. 唯一结论

**ACCEPT WITH EXTERNAL GAP**。

本轮不沿用此前任何 ACCEPT/REJECT。开发稳定最终提交为 `0d80c7fb34262253cd302054d15e3007371c8fae`。独立验收在最终 diff、边界、提交卫生、fresh 前端门禁、Mock Playwright 和无需认证宿主的真实 Chrome 视觉/键盘路径中没有留下代码、安全、权限、范围或视觉失败。唯一未闭环的是用户云 Docker 调试现场的认证/数据请求路径：5041 的 health 可达，但 `/identity/api/v1/auth/login` 以及 Users OData 在本轮窗口超时；原管理员 SPA 会话在候选热更新后失效，无法在不重启用户服务、不读取浏览器存储、不重新取得敏感凭据的前提下对最终提交重新执行登录后的真实性能、主题往返和 OData 矩阵。

该缺口按交接 EFF-01～05 归类为外部运行条件，不以 Mock 冒充真实通过，也不误报为 DNS/TCP/端口失败。本节至下一条历史分隔线是本文件唯一有效的最新处置。

## 2. 验收对象、提交与边界

- 工作树：`D:\Code\Industrial Platform\IndustrialPlatform`
- 分支：`develop`
- 本轮 baseline：`3d710595147967f3048f68e6412d208f13b1b286`
- 开发最终提交：`0d80c7fb34262253cd302054d15e3007371c8fae`
- 本轮提交：`5834122`、`56c1438`、`0d80c7f`，共 3 个 commit；16 files，244 insertions / 46 deletions
- 相对 `origin/develop`：behind 0 / ahead 60；未 push
- baseline→final 对 `AGENTS.md`、`CLAUDE.md`、`DSH.md`、`docs/prototypes/**` 的已提交 diff 为 0；生成物/日志/cache/TestResults 路径入 commit 为 0
- 没有新增依赖、锁文件或后端项目引用；没有 PF-03、PF-04 Audit/File/Notification、PF-05～PF-11 或 MES 实现
- AppDataTable 仅增量修正列头 DOM 同步，没有整体重写或向业务页泄露 VXE；本轮没有改变 OData、租户、权限、数据范围、软删和在线会话安全边界
- 登录背景生产资产与用户静态参考 SHA256 均为 `AD3F42FD2974B11C906B98A0E11FBBBB25ED5A1489D774641F5421E7F613858D`，不是重新绘制或近似替代

## 3. 阻断闭环

| 阻断 | 初始实际结果 | 修复提交 | 独立复验 |
|---|---|---|---|
| HDR-TEXT-01 搜索主文字未变淡 | `5834122` 真实 computed main=`rgb(255,255,255)`，与旧 baseline 相同；placeholder=`rgba(255,255,255,.82)` | `56c1438`：main 改用 secondary，placeholder 改用 muted `.62` | 静态 token 审查；714/714 unit；theme/pc-shell Mock 29/29，三配色保持 gradient |
| PERF-01 暖路由 2 秒级停顿 | 修复前真实 Users→Groups p95 2225ms，Groups→Users p95 2766ms，并出现网络不可用 alert | `56c1438`：同 access token 只在首次 restore 请求 `/auth/me`，新 token/登出重置；避免每次 route guard 等待云端超时 | `authStore` 同 token 成功/失败均只请求一次；router guard 同 scope source load 一次且测试 `<500ms`。最终真实重测因管理员会话失效及 auth API 超时列外部缺口 |
| LOGIN-CENTER-01 390×844 卡片贴顶 | 修复前 `.login-card` x=15.2、y=16、360×416，`centerDy=-198px` | `0d80c7f`：移动端 `align-items:safe center`，低高度内容仍可安全滚动 | 最终真实 Chrome x=15.2、y=214、360×416，`centerDx=+0.2px`、`centerDy=0`、scrollWidth=390；9/9 login Playwright |

## 4. 新鲜门禁

### 前端静态、单元与构建

| 候选 | 命令/范围 | Exit | 结果 |
|---|---|---:|---|
| `56c1438` | `pnpm.cmd test:unit -- ...` | 0 | **96 files / 714 tests passed**；包脚本执行全量 Vitest |
| `56c1438` | `pnpm.cmd lint` / `pnpm.cmd typecheck` | 0 / 0 | PASS / PASS |
| `0d80c7f` | `pnpm.cmd lint` / `pnpm.cmd typecheck` | 0 / 0 | PASS / PASS |
| `0d80c7f` | `pnpm.cmd build` | 0 | 2,459 modules；production build PASS；仅既有 2.048MB chunk warning |
| `3d71059..0d80c7f` | `git diff --check` | 0 | PASS |

Vitest/Vite 在受限沙箱中首次因 `node_modules/.vite-temp` EPERM 在配置加载前退出；同一命令获准写临时配置后成功，未把启动失败算作产品失败或测试通过。

本轮 3 个提交全部是前端代码/测试/静态资产，没有后端生产代码变化；因此没有为无后端改动重复运行后端 full gate。前一稳定候选的 fresh backend Release build/full tests 仍作为未改变后端的基线证据，不冒充本轮新鲜执行。

### Mock Playwright

- `56c1438`：`theme.spec.ts + pc-shell.spec.ts + login-i18n.spec.ts` 为 **29/29 passed**，覆盖三配色、light/dark/system、刷新保持、200% 缩放、顶栏背景渐变、搜索文字层级、Ctrl+K、1024/1280/宽屏布局及登录响应式。
- `0d80c7f`：`login-i18n.spec.ts` 为 **9/9 passed**，覆盖 1280×720、1440×900、360×800、390×844，中英文、明暗与新增中心几何断言。
- `5834122` 初始完整相关 Mock：Shell/生产模式/theme/login/pc/tabs 50/50，visual matrix 49/49。后续提交只触及 auth restore、搜索颜色 token 和登录移动对齐，并由上述增量门禁覆盖。

## 5. 真实 Chrome、视觉与键盘

证据目录：`C:\Users\DONG\.codex\visualizations\2026\08\29\01a04e87-b2e3-73e1-94f8-49c4024a8573\pf03-performance-visual-round`

- 登录桌面实现与 1672×941 用户参考并排人工检查：工厂位于左侧，蓝白轨迹从左下延伸，右侧高亮留白；没有第三方品牌、字幕或水印。实现卡片 431.6×416，`centerDx=0`、`centerDy=-0.2px`。
- `02-login-implementation-1672x941.png` 是实现截图；`03-login-reference-vs-implementation.png` 为同尺寸左右并排；`11-login-mobile-centered-clean-reset-390x844.png` 为最终移动端中心帧。
- 登录 Canvas CSS 1672×941.6，bitmap 1672×942，DPR≈1；相隔 500ms 两帧 SHA256 不同，证明动画在更新。隐藏、reduced-motion 和 unmount 清理由 `LoginBackground.spec.ts` 覆盖。
- 真实登录页中英文即时切换，`document.lang` 同步；登录方式面板英文完整，Escape 关闭后焦点回到入口；表单在最终刷新后为空，alert=0。
- 最终 Chrome 已恢复 **2048×1090**、`zh-CN`，URL 为 `/login?redirect=/pc/identity/user-groups`，页面 alert=0。

### 需要认证宿主的真实矩阵：EXTERNAL GAP

- 本轮轻量预检只执行一次：`GET /health`=200、`/health/live`=200、`/health/ready`=200 Healthy，说明 5041 正在监听且 TCP/端口正常。
- 同一 5041 的错误/正确认证 POST 均在约 70 秒无 HTTP 状态或响应体；最小 real-login Playwright 两次得到“请求超时,请稍后重试”，不是预期错误密码响应。Users OData 真实交互也出现请求超时/“网络不可用”。按交接没有循环探测、重启 VS/UnifiedHost、启用其他端口或另起替代服务。
- 修复前真实管理员页面已证明列头模式在 reload 后自动恢复，顶部查询 DOM 不存在，列头 controls 可见/可聚焦；但最终候选的真实查询输入、清空、分页、导出、零失败网络，以及管理员/受限账号生产模式、在线会话和主题往返无法在已失效会话上继续执行，全部明确记为 SKIPPED，不由 Mock 代替。
- THEME-01 的最终代码证据为 `PlatformTopBar` 继续消费 `--ip-shell-topbar-background`，三套 palette 各自定义 gradient；fresh theme Mock 验证三配色、light/dark/system、刷新保持和可读性。真实最终登录后切换因上述 auth 外部条件未补跑。

## 6. 最终仓库状态

- HEAD：`0d80c7fb34262253cd302054d15e3007371c8fae`
- index：0 staged
- 工作树：43 tracked unstaged、16 untracked；这些包含验收前保护 WIP与本未提交报告，未称为 clean
- 已提交 diff 中没有 `bin/`、`obj/`、`dist/`、`TestResults/`、`test-results/`、日志、缓存或临时截图
- 报告绝对路径：`D:\Code\Industrial Platform\IndustrialPlatform\docs\evidence\pf03-pre-platform-convergence-independent-acceptance-2026-08-30.md`
- 未 push

---

以下内容均为更早轮次的历史记录，已被上述 `0d80c7f` 最新结论替代。

# PF-03 前平台能力收束独立验收最终报告（2026-09-01，最新交接补充）

## 1. 唯一结论

**ACCEPT WITH EXTERNAL GAP**。

开发最终提交为 `3d710595147967f3048f68e6412d208f13b1b286`。本轮没有沿用此前结论，而是按设计 §6.6、用户第二轮复核验收附录及交接文件顶部最新边界，在该提交上重新审查完整增量并执行 fresh 后端、前端、Mock Playwright、登录页真实 Chrome 和 UnifiedHost 运行时探测。

代码、安全、权限、查询契约、主题、响应式、键盘和范围边界没有未闭环失败。唯一缺口是用户已明确关闭 IDE 调试，默认 UnifiedHost `localhost:5041` 没有监听；`/health`、`/health/live`、`/health/ready` 均为连接拒绝。因此需要认证宿主的真实管理员/受限账号 Shell、Users OData、在线会话与生产模式复验在本轮记为 **SKIPPED / real-runtime unavailable**，没有启动宿主、没有构造 5080/5042/62311 四宿主拓扑，也没有把 MSW 字符串或配置测试当作真实服务。该缺口是外部运行条件，不是代码验收失败。

本节及其后至“历史记录分隔线”的内容是本文件唯一有效处置；后续旧 `REJECT`/`ACCEPT` 章节只保留审计历史，不构成本轮结论。

## 2. 验收对象、提交与边界

- 工作树：`D:\Code\Industrial Platform\IndustrialPlatform`
- 分支：`develop`
- 开发 baseline：`eadad6224622635db9f0cc91792ae07c2bf05179`
- 本轮补充开始提交：`b4a42fb34b2aedb17b8b5d723f9f3a629662a6b5`
- 开发最终提交：`3d710595147967f3048f68e6412d208f13b1b286`
- 本轮三个提交：`02c13de`（Shell 导航与 UnifiedHost 默认收束）、`dfd0758`（无障碍登录背景）、`3d71059`（导航精确文案与测试清理）
- baseline→final：57 commits，336 files changed，23,797 insertions / 1,271 deletions；`origin/develop...HEAD = 0 57`，未 push
- `baseline..final` 对 `AGENTS.md`、`CLAUDE.md`、`DSH.md`、`docs/prototypes/**` 的已提交 diff 为空；本轮验收没有修改生产代码或测试代码
- `b4a42fb..final` 没有依赖清单、包锁或项目引用变化；新增背景使用 Canvas/ResizeObserver/MutationObserver 原生能力
- 未引入 PF-03、PF-04 Audit/File/Notification、PF-05～PF-11 或 MES 业务实现；既有生产操作八张未来卡仍是唯一 coming-soon 入口
- AppDataTable、OData、租户/权限/数据范围/软删、在线会话安全投影、分权撤销、个人中心、缓存白名单等既有实现没有在本轮三个提交中被整体重写或越界改变

## 3. 开发阻断闭环与独立 diff 结论

本轮没有信任开发报告，直接检查提交和测试。开发中间提交 `dfd0758` 曾存在两个阻断：

1. `LoginBackground.spec.ts` 使用 `delete HTMLCanvasElement.prototype.getContext` 导致 `vue-tsc` TS2790；退回唯一开发任务后改为 `Reflect.deleteProperty`，最终独立 `typecheck` 通过。
2. 中文二级父分组为“组织与平台”，不符合新增精确文案“组织域平台”；退回后最终 locale、navigation fallback 和组件测试统一为“组织域平台”。

最终增量审查结论：

- 顶栏恢复消费 `--ip-shell-topbar-background`；工业青、科技蓝、中性灰各有主题渐变，明暗模式不覆盖色板渐变。搜索主文字消费 `--ip-shell-topbar-text`（白色），placeholder 消费 `--ip-shell-topbar-text-secondary`（浅白）。
- 顶栏使用实际左右占用测量和 ResizeObserver 计算搜索轨道；宽屏完整提示、Ctrl+K、1024/1280 连续收窄不覆盖右侧工具均有 Mock 浏览器断言。
- Users 在 `tableQueryMode === 'top'` 时才渲染整个 `AppQueryPanel`；列头模式下顶层查询区 DOM 不存在，切回恢复查询、重置和更多条件。
- “身份与访问 / 组织域平台”渲染为带 Folder 图标、文本、箭头、`aria-expanded`/`aria-controls` 的按钮；默认展开、互相独立。搜索命中临时展开并在清空后恢复局部状态；外层 52px 收窄时忽略分组折叠，保留全部授权入口。
- 登录页只在内容后方新增 `aria-hidden`、无指针事件的 Canvas 背景；表单、认证、redirect、i18n、密码显隐与登录方式面板保持。页面隐藏及 `prefers-reduced-motion` 时停止动画，卸载时清理监听和动画帧。
- UnifiedHost 是默认唯一入口：real Playwright 默认 API base 和 smoke 默认探测均指向 5041；只有显式 `-IndependentServices` 才检查 5080/62311。没有把独立服务模式设成默认。

## 4. Fresh 独立门禁

### 后端与安全

| 命令/范围 | Exit | 结果 |
|---|---:|---|
| `dotnet build src/backend/IndustrialPlatform.slnx --configuration Release` | 0 | 0 warning / 0 error |
| `dotnet test src/backend/IndustrialPlatform.slnx --configuration Release --no-build` | 0 | **1,366 passed / 0 failed / 3 skipped** |
| BuildingBlocks | 0 | 158/158 passed |
| Gateway | 0 | 14/14 passed |
| ReferenceData | 0 | 14/14 passed |
| SystemData | 0 | 545/545 passed |
| Identity | 0 | 604/604 passed |
| UnifiedHost | 0 | 20/20 passed |
| Integration | 0 | 11 passed / 3 conditional skips |

3 个 skip 是需要真实 PostgreSQL、Redis、RabbitMQ 的显式外部依赖测试。完整 Identity/SystemData/UnifiedHost 套件重新覆盖 OData 受控查询、权限/租户/数据范围/软删先行、session view/revoke 分权、跨租户、幂等撤销及宿主组合，不以旧编译产物替代 fresh build。

`tests/scripts/smoke.ps1.Tests.ps1` 独立通过：默认模式只探测 5041；显式 IndependentServices 才探测 5041/5080/62311。

### 前端

| 命令 | Exit | 结果 |
|---|---:|---|
| `pnpm.cmd lint` | 0 | PASS |
| `pnpm.cmd typecheck` | 0 | PASS |
| `pnpm.cmd test:unit` | 0 | **95 files / 708 tests passed** |
| `pnpm.cmd build` | 0 | 2,459 modules；production build PASS |

单元/组件测试明确覆盖本轮新增的登录背景隐藏/减动、顶栏主题 token、搜索文字/placeholder、父分组独立折叠/搜索展开/52px 可达、Users 列头/顶层查询互斥和返回恢复。没有 JavaScript/TypeScript 编译失败。

### Mock Playwright

| Specs | 结果 |
|---|---|
| `pc-shell.spec.ts` + `login-i18n.spec.ts` | **20/20 passed** |
| `pc.spec.ts` + `theme.spec.ts` | **20/20 passed** |

共 **40 passed / 0 failed**。其中 `theme.spec.ts` 实测工业青与科技蓝顶栏 `background-image` 不同，切 dark 后保持科技蓝渐变，刷新后 palette、color mode 与渐变继续保持；这是真实组件主题能力，不是静态原型。`pc-shell` 覆盖 Ctrl+K、2048 完整搜索提示、1024/1280 不重叠/不横溢、英文长账号和 52px 收窄入口可达；`pc.spec` 覆盖认证 Mock、站内安全 redirect、拒绝协议相对 redirect、密码显隐和既有退出/锁定回归。

## 5. 真实 Chrome 与真实运行时

### PASS：无需认证宿主的登录页

- 复用用户现有 Chrome `http://localhost:5173/login`；背景在 2048×1090、1280×720、1024×720 均覆盖视口，登录卡片可见且无文档横向溢出。
- 背景 DOM 为 `aria-hidden=true`、`pointer-events:none`；Canvas 存在。真实页密码字段 `password → text → password`，并已清空输入。
- 登录方式面板保留“当前账号”、域账号/SSO 待实现项与关闭行为；中文切英文后标题和表单翻译生效，再恢复中文。
- 验收结束已显式恢复用户 Chrome 为 **2048×1090**：`innerWidth=2048`、`innerHeight=1090`、`scrollWidth=2048`、URL `/login`、语言 `zh-CN`；背景边界 `x=0,y=0,width=2048,height≈1090.4`。

### SKIPPED：需要 5041 认证宿主的真实系统页

- `http://localhost:5041/health`：连接拒绝（约 4.2s）。
- `http://localhost:5041/health/live`：连接拒绝（约 4.1s）。
- `http://localhost:5041/health/ready`：连接拒绝（约 4.1s）。
- 端口 5041 没有监听；这是“IDE 调试已关闭”的预期外部状态。本验收没有启动/停止 IDE 或服务，也没有检查 5080/5042/62311 作为默认拓扑。
- 因而以下本轮真实项如实记为 skipped，而不是以旧轮真实证据或 Mock 冒充通过：系统管理员生产模式入口与八卡、无 `platform.operation.view` 隐藏/直达拒绝、真实 Users 列头查询/分页/导出和零失败网络、在线用户 view-only/revoke/无权/跨租户/自撤销、个人中心 `/auth/me`。
- 这些路径的最终代码、单元、后端安全测试和 Mock 浏览器门禁全部通过；若恢复 UnifiedHost，可直接按附录矩阵补跑真实验收，无需建立四宿主环境。

## 6. 最终 Git 状态与报告

- HEAD：`3d710595147967f3048f68e6412d208f13b1b286`；`develop...origin/develop [ahead 57]`
- index：**0 staged**；工作树：**43 个 tracked unstaged、16 个顶层 untracked entries**。这些是进入验收前即存在的用户/其他任务 WIP；本任务只更新本报告且保持 untracked，没有称工作树 clean。
- `bin/`、`obj/`、`dist/`、`test-results/` 是本轮门禁产生或复用的 ignored 可再生输出，没有进入 index/commit；没有日志、临时截图、缓存、TestResults 入库。
- 报告绝对路径：`D:\Code\Industrial Platform\IndustrialPlatform\docs\evidence\pf03-pre-platform-convergence-independent-acceptance-2026-08-30.md`
- 未 push。

---

以下内容均为更早轮次的历史记录，已被上述 2026-09-01 最新结论替代。

# PF-03 前平台能力收束独立验收最终处置（2026-09-01，真实环境补验）

## 1. 唯一结论

**REJECT**。

开发最终提交为 `b4a42fb34b2aedb17b8b5d723f9f3a629662a6b5`。本轮已经恢复 5041/5173 与真实管理员、受限账号环境，并完成大部分此前缺失的真实验证；开发也闭环了列头清空可见状态和全局 Bearer 会话撤销的代码缺陷。但是最终提交没有被用户现有 5041 调试宿主加载：同一精确 target sid 被撤销后，最终代码的宿主级测试为 `/auth/me` 401，而现有 5041 仍返回 200。任务明确禁止本验收重启或控制用户 Visual Studio/UnifiedHost，故不能把旧进程行为当作 final commit 的真实安全通过。

此外，环境只提供 `admin/e2e.admin`（view+revoke）与 `e2e.limited`（均无）两种权限组合，没有 view-only 或第二租户真实主体；真实 Users 只有 3 条、UI 最小 25 条/页，无法点击下一页；CSV 导出确认动作完成且无前端错误，但浏览器控制层未交付 download 事件或可复核文件。最新交接要求这些真实矩阵缺口是当前阻断，不能以历史单测或 Mock 代替，因此拒收。

本节是本文件唯一有效的最终处置。后面的 `a695aea` 报告仅为历史证据，不构成本轮 `ACCEPT`。

## 2. 验收对象与 Git 范围

- 工作树：`D:\Code\Industrial Platform\IndustrialPlatform`
- 分支：`develop`
- 开发 baseline：`eadad6224622635db9f0cc91792ae07c2bf05179`
- 本轮真实补验修复 baseline：`3db004e1fb821f379e4bffb946c28f1e60da06b1`
- 开发最终提交：`b4a42fb34b2aedb17b8b5d723f9f3a629662a6b5`
- baseline→final：54 commits，328 files changed，22,767 insertions / 1,162 deletions
- 相对 `origin/develop`：behind 0 / ahead 54；未 push
- 最终 index：0 staged；工作树保留 41 个 tracked unstaged、16 个 untracked，均如实披露，没有称工作树 clean
- `DSH.md`、`CLAUDE.md`、`AGENTS.md`、`docs/prototypes` 在 baseline→final 已提交 diff 中为空；当前受保护 WIP 未覆盖、暂存或提交
- `b4a42fb` 仅修改 5 个文件：JWT Bearer 撤销校验、AppDataTable 原生列头同步及三组定向测试；未扩大 PF-03/PF-04+、MES 或跨服务边界
- 独立验收没有修改生产代码或测试代码；仅更新本报告，保持未提交

## 3. 本轮真实环境证据与阻断闭环

### 已闭环

- `GET http://localhost:5041/health/ready`：200 `Healthy`。Identity/SystemData/ReferenceData 的 PostgreSQL、Redis、RabbitMQ、Outbox 为 Healthy；未启用的 Seq 检查为 skipped。5041 与 5173 均复用用户现有进程，本验收未启动、停止或重启。
- 真实系统管理员 `/pc/identity/users` 可见生产操作与在线用户；进入 `/pc/operation` 后 9 张卡中前 8 张均为 `button disabled`、`aria-disabled=true`、无 href/route、标“待实现”，第 9 张界面设置可用。返回管理模式后 `/pc/identity/users`、用户管理 tab 与查询状态保持。
- 真实 `e2e.limited` 首页隐藏生产操作、在线用户和系统管理；直达 `/pc/operation` 与 `/pc/identity/users` 均落 `/403`，无应用错误日志。
- Users 列头输入 `e2e.limited`：可见输入有值、结果 1 条；点击清空后可见输入为空、结果恢复 3 条、alert/error 为 0。`REAL-ODATA-CLEAR` 由 `b4a42fb` 的 7 行最小同步修复闭环，没有整体重写 AppDataTable 或泄露 VXE。
- 真实 OData：`contains(loginName,'e2e.admin')` 返回 200、total 1；清空过滤返回 200、total 3；`$top=1&$skip=0/1` 分别返回 pageIndex 1/2 且用户不同，证明服务端分页契约生效。
- 在线用户抽屉真实说明“有效登录会话，不代表实时页面活跃”，字段只有 `sessionNId/userNId/loginName/name/loginOn/lastRefreshedOn/expiresOn/isCurrent`；DOM 未出现 token/IP/UserAgent。当前会话标记、手动刷新、抽屉数据均正常。
- PF-04 纸飞机为真实 disabled 控件；`title` 与 `aria-label` 均为“待实现 / PF-04 未接入”，无 Notification API 或假成功。
- 会话真实矩阵：admin view=200；limited view=403、revoke=403；精确解码 target sid 后首次撤销与同 `Idempotency-Key` 重放均 200、`found=true/isCurrent=false`；目标 refresh=401、目标权限保护 Users API=401、目标从 active 列表消失，actor sibling `/auth/me` 与 Users 仍 200。单独 agent 管理员当前会话自撤销后立即跳 `/login` 并显示“会话已撤销”。
- 顶栏主题沿用前轮最终提交后的独立 3×3 DOM 与 8/8 Playwright 证据：industrial-cyan/technology-blue/neutral-gray 各自语义渐变，light/dark/system、刷新保持和管理/生产往返均通过；`b4a42fb` 未修改主题生产代码或快照，因此不存在静态原型替代主题能力或新增退化。

### 最终仍阻断

1. `REAL-SESSION-ME-REVOCATION` 的代码缺陷已由 `b4a42fb` 修复：`JwtBearerEvents.OnTokenValidated` 对所有 Bearer 端点检查 sid，撤销返回 401，安全存储不可用 fail-closed 503；独立宿主级测试通过。但用户现有 5041 未加载新二进制，真实同一 target token 对 `/auth/me` 仍为 200，不能宣称 final commit 的真实闭环。
2. 真实主体缺 view-only 和第二租户账号，不能完成 view-only 隐藏撤销动作与跨租户真实撤销；现有端点/应用测试不能替代强制真实矩阵。
3. Users 真实数据仅 3 条且 UI 最小 25 条/页，下一页真实 disabled；后端 `$skip` 已通过，但 UI 分页交互没有可构造证据。
4. CSV 导出设置真实打开并确认，面板关闭且 alert/console error 为 0；浏览器控制层 `download` 等待超时，用户 Downloads 也没有可核文件，不能证明导出产物。

## 4. 最终提交上的独立门禁

### 后端

- 前轮 final fresh Release build：exit 0，0 warnings，0 errors；full tests 1,364 passed / 0 failed / 3 skipped。
- `b4a42fb` 后定向命令：`dotnet test tests/Identity/IndustrialPlatform.Identity.Tests/IndustrialPlatform.Identity.Tests.csproj --configuration Release --filter "FullyQualifiedName~RevokedBearerAuthenticationTests|FullyQualifiedName~AuthorizationEvaluationEndpointTests|FullyQualifiedName~SessionManagementEndpointTests"`：9 passed / 0 failed / 0 skipped，exit 0。
- 独立 diff 审查确认全局 Bearer 撤销、目标/兄弟会话与 fail-closed 503 均有宿主级覆盖；`git show --check` 无错误。

### 前端

- 前轮 final：lint/typecheck/build 均 exit 0，完整 unit 93 files / 697 tests passed。
- `b4a42fb` 后定向命令两次仅因沙箱内 `node_modules/.vite-temp` EPERM 未进入测试体；允许写临时配置后原命令完成 **93 files / 698 tests passed**，exit 0。
- 真实 Chrome 再次证明列头可见值、查询描述符与页码同步清空，零 alert/error。

### Playwright、视觉、键盘与主题

- 登录 i18n/窄屏 8/8；顶栏主题 8/8；PC Shell/生产模式/Tabs/SystemData Mock 24/24；UiBaseline 13/13；完整 visual matrix 49/49。
- `b4a42fb` 没有修改页面布局、主题、快照、Playwright 阈值或既有功能；本轮按交接仅复验受影响真实路径，没有重复运行这些已冻结门禁。

## 5. 报告与最终仓库状态

- 主报告绝对路径：`D:\Code\Industrial Platform\IndustrialPlatform\docs\evidence\pf03-pre-platform-convergence-independent-acceptance-2026-08-30.md`
- 过程报告：`D:\Code\Industrial Platform\IndustrialPlatform\docs\evidence\pf03-shell-review-independent-acceptance-2026-08-31.md`
- HEAD：`b4a42fb34b2aedb17b8b5d723f9f3a629662a6b5`；`develop...origin/develop [ahead 54]`
- staged 0；tracked unstaged 41；untracked 16。两份报告保持未提交；未 push。

---

以下为 `a695aea` 上轮历史报告，已撤销，不作为当前轮结论。

# PF-03 前平台能力收束独立验收报告

## 1. 验收身份、范围与最终提交

- 验收完成日期：2026-08-31（Asia/Taipei；按任务要求沿用 `2026-08-30` 报告文件名）
- 开发任务：`01a04e86-d2b4-7af2-b61a-43897954c544`
- 独立验收工作树：`D:\Code\Industrial Platform\IndustrialPlatform`
- 分支：`develop`
- 开发 baseline：`eadad6224622635db9f0cc91792ae07c2bf05179`
- 第二轮开始时稳定提交：`3990950539a1afea6a65360f0e1181afb4e5d219`
- 开发最终提交：`a695aea5c110bf124fc18daaf5d602531bfb92fa`
- 相对 `origin/develop`：behind 0 / ahead 34
- 推送：未执行

本报告按独立验收计划 Task 1～9 以及“用户第二轮复核验收附录”执行。开发任务写入期间未在同一工作树运行 build/test；每次阻断都退回唯一开发任务修复，验收任务没有修改生产代码或测试代码。

## 2. 唯一结论

**ACCEPT**。

最终提交在完整 diff、边界、权限和许可证审查后，通过 fresh backend Release build/full tests、frontend lint/typecheck/full unit/build、Mock Playwright、真实四服务 Playwright，以及用户真实 Chrome 的管理壳、生产操作模式、在线会话、个人中心、列头查询、导出、语言与主题验收。所有代码、安全、权限、查询契约、状态恢复、视觉和国际化阻断均已提交修复并独立复验闭环；真实依赖可达，没有外部缺口。

## 3. Task 1：等待、交付与稳定性确认

- 使用任务等待机制等待开发任务停止写入；读取了工作树、最终提交和状态。开发任务最后状态为 idle。
- 开发最后一轮没有产生可读取的文字交付摘要，独立验收没有因此信任中间态；直接核对了 HEAD、commit diff、工作树状态并重新运行全部门禁。
- `3990950` 后真实 Chrome 发现英文导航资源键阻断，退回开发任务；其最终稳定提交为 `a695aea`。
- `a695aea` 提交后目标文件无未提交残留，说明修复已完整入库而非仅停留在工作树。

## 4. Task 2～3：完整 diff、边界与安全审查

`eadad622..a695aea` 共 34 个提交、251 个文件、18,172 insertions / 816 deletions。结论：

- 保护路径 `DSH.md`、`CLAUDE.md`、`AGENTS.md`、`docs/prototypes/**` 未进入本轮提交；`git diff baseline..HEAD --` 对这些路径为空。
- 未引入 PF-03 ReferenceData 业务、PF-04 Audit/File/Notification、PF-05～PF-11 或 MES 业务实现。
- 消息通知只是真实空态；没有 Notification 后端、模型、API、假 badge 或假成功。
- 生产操作模式只有八个 `coming-soon` 禁用卡片和一个现有设置入口；八卡 `aria-disabled=true`、无 route/href、无 API、明确“待实现”。
- AppDataTable 仍是既有封装的增量演进，没有整体重写或向业务页泄露 VXE。
- Users OData 不返回 `IQueryable`、不用 `EnableQuery`、不跨服务；使用受控字段/运算白名单和平台信封。
- tenant、permission/data-scope、soft-delete 先于查询；list/export 使用同一 descriptor/schema/data scope，含取消和超时边界。
- 在线会话只投影账号、姓名、登录/刷新/过期和当前会话标记；无 token、IP、UserAgent 原值或哈希，也未新增此类存储。
- `identity.session.view` 与 `identity.session.revoke` 分权；跨租户、无权、重复撤销幂等、目标单会话失效和当前会话撤销后本地退出均有后端/组件回归。
- 清理缓存实现是明确白名单：只移除当前 tenant/user 的 Tabs、page-state、table-preferences；没有 `localStorage.clear()` / `sessionStorage.clear()`，保留 auth、tenant、locale、theme、terminal override、experience mode，且无服务端缓存请求。
- 最终提交 `a695aea` 只修正导航 `labelKey` 命名空间、静态路由 `titleKey` 和相应测试；没有新增依赖、锁文件或业务路由。

## 5. Task 4：后端 fresh build、全量和安全门禁

最终提交上独立执行：

| 命令/范围 | Exit | 结果 |
|---|---:|---|
| `dotnet build src/backend/IndustrialPlatform.slnx --configuration Release` | 0 | 0 warning / 0 error |
| `dotnet test src/backend/IndustrialPlatform.slnx --configuration Release --no-build` | 0 | 1,352 passed / 0 failed / 3 skipped |
| BuildingBlocks | 0 | 151/151 passed |
| Integration | 0 | 11 passed / 3 conditional skips |
| Gateway | 0 | 14/14 passed |
| ReferenceData | 0 | 14/14 passed |
| SystemData | 0 | 545/545 passed |
| Identity | 0 | 597/597 passed |
| UnifiedHost | 0 | 20/20 passed |

3 个 skip 是显式门控的 PostgreSQL/Redis/RabbitMQ 专项集成环境用例，不是本轮四服务开发拓扑的启动前提；本轮要求的真实 Identity/SystemData/ReferenceData/Gateway 与 Gateway 转发全部可达，因此不形成 external gap。

补充高风险证据：最终 backend 代码在 `52ac5d6` 后未变；该版本独立运行的 Identity session/OData/permission 定向组为 104/104，通过未认证 401、无权 403、tenant/soft-delete、非法 option/字段、filter/order/select/count/page/top/skip、导出一致性、session view/revoke/cross-tenant/idempotency/current logout。最终 597 项 Identity 全量再次包含并通过这些测试。

首次 fresh build 因验收任务此前启动的四个真实服务锁住 Release DLL 而失败（锁 PID 可精确识别）；停止这些验收进程后重新 build，得到上述 0 warning / 0 error。没有用旧产物或 `--no-build` 掩盖锁失败。

## 6. Task 5～6：前端静态、单元、构建与 Mock E2E

| 命令 | Exit | 结果 |
|---|---:|---|
| `pnpm.cmd lint` | 0 | PASS |
| `pnpm.cmd typecheck` | 0 | PASS |
| `pnpm.cmd test:unit` | 0 | 90 files / 635 tests passed |
| `pnpm.cmd build` | 0 | 2,451 modules；production build PASS |
| 计划指定五个 Mock spec | 0 | 17/17 passed |

Vitest/Vite 在受限沙箱内首次因无法写 `node_modules/.vite-temp` 返回 EPERM；使用同一工作树、同一命令在允许写临时配置的环境重新运行后为上述通过结果。构建只保留 Vite 大 chunk warning。Mock 只保留既有 Element Plus checkbox `label`-as-value 3.0 弃用警告；无测试失败。

最终 Mock 命令覆盖：`pc-shell.spec.ts`、`pc-operation-mode.spec.ts`、`workspace-tabs.spec.ts`、`user-management-golden.spec.ts`（Mock 配置按设计排除）、`systemdata-admin.spec.ts`。有效执行 17 项，覆盖生产模式双向切换、无权 403、1280/1440、英文模式、Tabs 上限/恢复/权限 prune、SystemData 七入口和 Shell 结构。

## 7. 用户第二轮复核附录：真实 Chrome 与视觉/键盘

真实用户 Chrome 页面：`http://localhost:5173/pc/identity/users`，SYSTEM_ADMIN 会话。

### 顶栏、通知、在线用户、用户菜单

- 顶栏实际存在独立“消息通知”和“在线用户”按钮；无独立锁定 icon。
- 消息面板真实空态为“暂无通知。Notification 尚未接入。”；无 badge、假数据或 Notification 网络调用。
- 在线用户抽屉仅显示安全字段，说明“有效登录会话，不代表实时页面活跃”；当前会话有 `● 当前会话` 标记。
- 抽屉实现具备 loading、empty、error/retry、refresh；单元回归覆盖安全投影、独立分权和 PF-04 禁用动作，真实页复核了 refresh、当前会话标记、Escape 关闭和焦点回收。
- “发送消息”按钮真实 disabled，title 为“待实现 / PF-04 未接入”；没有网络调用或假成功。
- 1280×720 抽屉 bounding box 为 `[600,0]-[1280,720]`，本体 `scrollWidth=clientWidth=680`；表格超宽由组件内部可滚动容器承载，抽屉不越界。Escape 关闭后焦点回到“在线用户”按钮。
- 用户菜单真实顺序为“个人中心、清理缓存、锁定工作区、退出登录”，每项都有既有图标库 SVG。
- `/pc/profile` 真实显示 `/auth/me` 的账号 `e2e.admin`、姓名、tenant `development`、角色 `SYSTEM_ADMIN`；“修改密码”进入现有 `/change-password` 表单，未提交密码变更。
- `uiCacheStore` 回归明确证明当前用户 Tabs/page-state/table-preferences 清除、其他用户数据和 auth/locale/theme/terminal/experience mode 保持；实现没有全量 storage clear 或服务端调用。
- `identity.session.view` 与 `identity.session.revoke` 独立分权：view-only 不显示撤销，无权限读写均 403，revoke-only 不能读取；后端端点/存储回归覆盖跨租户、重复撤销幂等、仅目标会话失效和当前会话 marker，前端当前会话分支会清本地认证态并跳回登录页。

### 列头查询、导出与网络契约

- “切换列头查询”后列头 filter row 出现，顶部快速搜索 disabled，顶部/列头查询互斥。
- 输入 `e2e.admin` 后页面只显示目标用户且 `Total 1`；清空后恢复三用户且 `Total 3`。
- 服务端日志保存的查询为：`GET /api/v1/odata/users?...&$filter=contains(loginName,'e2e.admin')...`，HTTP 200；清空查询 OData 同为 200。
- 自定义数量 3 的真实服务端 Excel：`GET /api/v1/odata/users/export?...&quantity=3&culture=zh-CN&timeZone=Asia/Taipei`，HTTP 200，content-type 为 XLSX。
- 本轮真实交互期间 JavaScript dialog、console warn/error、pageerror 和非预期 4xx/5xx 均为 0。

### 生产模式、国际化、主题和状态恢复

- SYSTEM_ADMIN 在用户管理真实页可见“生产操作”入口，可进 `/pc/operation`。
- 3×3 共九卡；八个未来卡片全为真实禁用、无 href/route，设置卡可用。1440×900 九卡完整；1280×720 无横向溢出，正常纵向滚动。
- 管理→生产→管理后原路由 `/pc/identity/users`、用户管理 tab、查询 `e2e.admin` 和结果 1 行全部恢复。
- 最终 `a695aea` 前真实 Chrome 发现一级/二级导航仍中文；根因为生成键 `navigation.*` 而资源位于 `shell.navigation.*`。修复后重新加载：Workspace/System management、全部二级菜单、Tabs、用户页、document title 均即时为英文；切回中文同步恢复。
- 英文 title 为 `User management · Industrial Platform`，中文为 `用户管理 · Industrial Platform`。
- 浅色/暗色和 Industrial cyan 主题真实可切换；实现保持 Industrial Platform 品牌与 token，没有复制第三方品牌、字幕、水印或精确配色。
- 缺 `platform.operation.view` 的真实受限账号在最终 real Playwright 中菜单隐藏、直达路由 403；真实后端权限而非 Mock。

### 视觉证据

证据目录：`C:\Users\DONG\.codex\visualizations\2026\08\29\01a04e87-b2e3-73e1-94f8-49c4024a8573\pf03-second-round`

| 文件 | 内容 |
|---|---|
| `03-operation-fixed-1440x900.png` | 生产操作九卡、紧凑顶栏、无裁切 |
| `04-operation-fixed-1280x720.png` | 1280 生产操作与正常纵向滚动 |
| `05-online-users-1280x720.png` | 在线用户抽屉真实数据、安全字段与禁用操作 |
| `06-profile-1280x720.png` | 从个人中心进入的现有修改密码流程 |
| `07-users-en-light-1440x900.png` | 英文浅色用户管理与全英文导航 |
| `08-users-en-dark-1440x900.png` | 英文暗色、主题浮层边界与统一视觉 token |

## 8. Task 7：真实四服务、权限与 Playwright

从 final commit 的 fresh Release 输出启动：Identity 5041、SystemData 5042、ReferenceData 62311、Gateway 5080；四个 `/health` 和 Gateway→Identity bootstrap 均为 HTTP 200。

第一次最终 real E2E 启动时，Gateway 从 backend 根目录启动，未加载项目 `appsettings.Development.json`，导致 `/identity/*` 404；直接 Identity bootstrap 为 200。该轮被主动终止，明确归类为验收编排错误，不计产品失败或通过。随后四服务各自从项目 content root 重新启动，Gateway 路由和所有 health 独立确认为 200。

最终命令：

```powershell
playwright test --config=playwright.real.config.ts
```

结果：exit 0，**24/24 passed（2.6m）**。

覆盖范围：

- 15 个 Identity 用户/角色/权限/审计/用户组三主题真实快照；
- 错误密码 401 与正确登录；
- bootstrap 暗色无闪烁、用户主题持久化和用户隔离；
- 管理员与受限账号真实权限、菜单隐藏和直达 403；
- Tabs 刷新恢复和跨账号越权标签 prune；
- 生产操作壳 1280×720 / 1440×900；
- 注销无敏感日志；
- 用户管理黄金页共享结构、键盘和窄窗口。

测试中唯一 401 是“错误密码统一错误”的预期安全断言；没有非预期失败网络、console/pageerror 或后端 5xx。

## 9. Task 8：阻断缺陷闭环

| 阻断项 | 退回/修复提交 | 独立复验 |
|---|---|---|
| 消息/在线会话入口、权限分离、安全字段、profile/cache 边界缺口 | `e3ef3ef`、`52ac5d6` | 597 Identity、635 frontend、真实 Chrome 抽屉/profile/cache 审查 |
| Shell/终端预览间距 | `904056e` | Mock、15 真实视觉、人工截图 |
| 列头查询出现“加载用户列表失败”、OData/JWKS/overflow/security/cache 边界 | `a5c9344` 后仍失败；最终 `52ac5d6` | OData 200 exact URL、export 200、104/104 高风险、full gates |
| 15 个 Identity 视觉基线版本不一致 | `9ddc6a3` | 最终真实 15/15 视觉项 |
| 生产操作顶栏用户 SVG 185.6px、1440 第三行卡片裁切 | `665988a` | icon 18×18；1440 九卡完整；1280 无横向溢出 |
| 返回管理模式丢失原路由/tab/query | `bb001e3` | 真实 Chrome `/pc/identity/users` + query + 1 行全部恢复 |
| Mobile/PDA greeting 测试依赖实时时钟 | `3990950` | full unit 630/630，当轮；最终 635/635 |
| 英文一级/二级导航因错误 labelKey 回退中文 | `a695aea` | 真实 Chrome 全英文导航/title；新增一级/二级/更多/Tabs/搜索测试；635/635 |

没有开放阻断项。

## 10. Task 9：仓库卫生和最终状态

- final HEAD：`a695aea5c110bf124fc18daaf5d602531bfb92fa`
- branch：`develop`，behind 0 / ahead 34
- staged：0
- 最终 `git status --porcelain`：56 项（41 tracked unstaged + 15 untracked）
- 报告保持未提交：`?? docs/evidence/pf03-pre-platform-convergence-independent-acceptance-2026-08-30.md`
- 现有 dirty worktree 是开发启动前保护 WIP 和本报告；未把它误称为 clean。
- 删除了本轮生成的 `dist/`、`test-results/`、三个 Playwright report/result 目录、`src/backend/logs/` 和两张临时 golden screenshot。
- 4173/5041/5042/5080/5173/62311 最终无监听；所有验收启动的服务已停止。
- 提交中没有 `bin/`、`obj/`、`TestResults/`、dist、cache、log 或临时截图。
- 未 push。

## 11. 非阻断维护项

1. Element Plus checkbox `label` 作为 value 的兼容 API 在 3.0 将弃用；当前 Mock/真实行为与全部测试通过。
2. Vite 生产构建仍提示单个大 chunk；不影响本轮正确性，可作为后续性能工作。
3. 用户工作树保留大量未提交保护 WIP；本轮没有覆盖、清理或提交这些文件。

以上不构成本轮代码、安全、权限、边界、视觉或外部条件缺口。
