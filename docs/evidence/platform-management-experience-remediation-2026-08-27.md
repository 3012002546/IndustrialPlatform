# 平台管理体验整改证据（2026-08-27）

状态：`verified_with_known_exclusions`

## 已通过

- 后端 fresh Release build：`dotnet build src/backend/IndustrialPlatform.slnx --configuration Release`，0 warning / 0 error。
- 后端全量 Release 测试：`dotnet test src/backend/IndustrialPlatform.slnx --configuration Release --no-build`，1,301 passed / 3 skipped / 0 failed：Gateway 14、BuildingBlocks 131、Integration 10、ReferenceData 14、SystemData 542、Identity 570、UnifiedHost 20；跳过项为 Redis、PostgreSQL、RabbitMQ 真实依赖。
- Identity xlsx：`Users_Export_WithPermission_ReturnsStreamingXlsx` 1/1，通过真实 xlsx ZIP 与用户内容校验。
- 前端 `pnpm.cmd lint`、`pnpm.cmd typecheck`、`pnpm.cmd build`：均通过。
- 前端 Vitest：62 files / 498 tests passed。
- UnifiedHost 真实 Playwright：`pnpm.cmd exec playwright test --config=playwright.unified.real.config.ts`，1/1 passed，完成管理员组织/岗位真实 CRUD 与状态闭环；使用 Development 自动发现的 `src/backend/appsettings.Development.local.json`，不依赖 Docker CLI。
- SystemData 管理页定向 Mock Playwright：`pnpm.cmd exec playwright test --config=playwright.config.ts tests/e2e/systemdata-admin.spec.ts`，2/2 passed；仅保留 Element Plus 既有 checkbox label 弃用 warning。
- 本轮目标 Mock Playwright：`pnpm.cmd exec playwright test tests/e2e/pc-shell.spec.ts tests/e2e/workspace-tabs.spec.ts tests/e2e/systemdata-admin.spec.ts`，13/13 passed；覆盖外壳折叠、页签右键/恢复、SystemData 管理入口及无新增 Vue warning（仅既有 Element Plus checkbox label 弃用 warning）。

## 导出边界证据

`AppDataTable` 只有一个 `data-testid="app-data-table-export"` 导出按钮，打开一个菜单并分组展示；当前范围内 15 个 `AppDataTable` 实例（服务分组按实际渲染复用）均绑定 exporter：

1. 快速导出：CSV、HTML、XML、TXT，调用 vxe-table 内核，仅使用当前已加载表格数据；数据范围提示明确说明不会请求全部数据，并提供当前页/选中行选择。
2. 导出 Excel（`.xlsx`）：提交当前服务端查询、排序、可见列和数量；默认 10000，可自定义正整数或选择全部，全部二次确认。

业务页面没有直接导入或调用 vxe-table API；vxe 只在 `AppDataTable` 与应用安装层使用。

Excel 服务归属：Identity 用户/角色/用户组/登录审计及 SSO 使用 Identity 自有受权限保护端点重新读取 Identity 数据；SystemData 组织/岗位/任职/功能/服务/初始化使用 SystemData 自有端点重新读取本服务应用数据。没有接收前端 rows 的通用 SystemData 导出端点，导出请求中的 `rows` 保持 `undefined`。

## 未决项

- 需要 Gateway/真实账号的 Identity 真实页面截图场景本轮未执行；UnifiedHost 真实代表场景已通过。
- 全量 Mock Playwright 已修复登录按钮严格匹配问题，但仍有仓库既有首页文案、视觉快照及需要真实后端的场景失败，故不标记全量 Mock 通过。
- `pnpm.cmd format:check` 仍报告仓库既有 19 个文件格式差异；未大范围格式化用户文件。构建仅有大 chunk 提示。
- 后端导出使用有界 `Pipe` + `FileStreamResult`，按服务端事实分页写 OpenXML，浏览器和服务端均不暂存完整 xlsx；Identity TestHost 定向测试已验证真实 xlsx 内容。

## RabbitMQ 停止竞态整改证据

- 根因：`CloseAsync()` 触发 `ConnectionShutdownAsync` 后，回调将共享 `_connection` 清空，旧实现随后再次读取该字段调用 `Dispose()`，在 UnifiedHost 正常 Ctrl+C 停止时产生 `NullReferenceException`。
- 修复：`DisposeAsync` 使用 `Interlocked.Exchange` 捕获局部连接并在 `finally` 释放；连接创建/销毁共用 `_syncRoot`，释放后拒绝新建通道；shutdown 回调使用 `CompareExchange`，不会用旧连接回调清空新连接。
- 回归测试：`RabbitMqConnectionTests.DisposeAsync_WhenCloseRaisesShutdown_ReleasesTheOriginalConnection`，验证 CloseAsync 触发 shutdown 后仍完成原连接释放。
- 验证：fresh Release build 0 warning / 0 error；BuildingBlocks 定向 Release 测试 1/1；后端全量 Release 测试 1,301 passed / 3 skipped / 0 failed。

## 第三轮静态验收整改证据

- `AppDataTable` 新增 `query-mode-change` 契约；活动模式外的 toolbar slot 不渲染，切换时清除非活动内部条件。Identity 用户、角色、用户组、审计页顶部查询控件受活动模式控制，loader/export 根据 `request.queryMode` 选择唯一过滤源，未保留 `filters ?? query` 双来源 fallback；用户页已绑定 `query-change`。
- `AppDataTable` 使用内部 `loaderLoading` ref 包住 loader 的 `try/catch/finally`；加载态由平台遮罩呈现，不再向未注册 `vxe-loading` 的 VxeTable 传递 `loading=true`，真实浏览器控制台无 vxe-table 错误。
- 本地筛选按 `filter.kind` 实现：select 精确匹配（boolean/number/string 语义稳定）、date-range 区间、text 模糊；新增 Vitest 覆盖上述模式槽位、空结果 loading、异常 finally 和 select 精确匹配。
- 表格布局/交互：新增 action slot、表头内筛选第二行、表格区域全屏、vxe 列宽拖动及用户/路由/tableKey 宽度偏好；页签新增右键命令、关闭显示策略和页面专注入口，顶栏不再显示页面专注按钮。
- Identity/SSO 主表及 SystemData 组织树新增/主操作已接入 action slot；组织页单测 8/8 通过，岗位详情的移动/状态等非表格流程仍保留原位置。
- 最终命令 `pnpm.cmd test:unit`：62 files / 498 tests passed；`pnpm.cmd typecheck`、`pnpm.cmd lint`、`pnpm.cmd build` 均以退出码 0 完成。目标 Mock Playwright 13/13 passed。

## UnifiedHost 最终真实回归

- 以 `dotnet run --project src/backend/src/Hosts/IndustrialPlatform.UnifiedHost/IndustrialPlatform.UnifiedHost.csproj --configuration Release --no-build -- --urls http://localhost:5041` 启动 Development UnifiedHost；自动发现 `src/backend/appsettings.Development.local.json`。
- `/health/ready` 返回 200，Identity/SystemData/ReferenceData 的 PostgreSQL、Redis、RabbitMQ 检查均 Healthy（Seq 按配置跳过）。
- `pnpm.cmd exec playwright test --config=playwright.unified.real.config.ts`：1/1 passed，真实管理员组织/岗位新增、编辑、停用闭环通过。
- Ctrl+C 正常停止，日志尾部记录 RabbitMQ 连接正常关闭（`Goodbye`），无 `NullReferenceException`；5041 端口已释放。

未提交、未推送；`DSH.md` 保持用户原有未提交改动，未被修改或暂存。

## 独立静态验收收尾证据

- `AppDataTable` 卡片已覆盖 toolbar、列设置、VxeTable、footer；顶部查询区仍在卡片前。查询、清空、刷新、全屏、列设置、导出使用既有 Element Plus 图标并保留无障碍名称。
- `workspaceTabsStore.closeAll()` 仅保留固定工作台并激活它；`PcLayout` 的非当前页右键刷新和专注通过 store 激活/持久化；右键菜单视口限界并响应 Escape。
- Identity Users/Roles/UserGroups/Audits 的 server-loader 列定义已显式关闭后端不支持的列头筛选；用户页测试验证 email、phone、lastLoginOn 不出现筛选输入。
- 定向 Vitest：5 files / 62 tests passed；最终全量 Vitest：62 files / 498 tests passed；typecheck、lint、build 均退出码 0。相关 Mock Playwright：13/13 passed。
- 一次性真实视觉验收 1/1 passed：1440×900 下验证默认顶部查询、列头查询切换、列宽拖动、标签关闭显隐/右键菜单、表格全屏；最终复拍确认工具图标可见，action slot 业务按钮继承平台样式。临时用例、配置和 HTML 报告均已删除，不进入仓库。
- 六个 `src/frontend/tests/e2e/screenshots/{mobile,pda,pc}*.png` 本轮生成修改已恢复；`src/frontend/playwright-report-unified-real/` 已删除。5041/4173 端口释放，未发现本轮 UnifiedHost/Vite/Playwright 残留服务。

### 第五轮续作收尾（2026-08-28）

- 继续沿用当前 checkout 的第五轮实现，没有回滚正确改动；本轮没有修改或暂存 `DSH.md`，没有提交或推送。
- AppDataTable/Identity 查询与导出契约的 TDD 证据：AppDataTable 第五轮新增行为先 RED 4 项失败后 GREEN 16/16；快速搜索/原生打印先 RED 2 项后 GREEN 18/18；文件名契约先 RED 1 项后 GREEN 18/18；Identity Users 服务端字段映射先 RED 2 项后 GREEN 5/5。最新定向组合 4 文件 41/41 通过。
- 前端最终门禁：`vue-tsc --noEmit --pretty false` 通过；`eslint .` 通过；Vitest 62 files / 506 passed / 0 failed；Vite fresh build（临时目录，2396 modules）通过。仓库默认 `vue-tsc --build` 与默认 Vite 输出仍受既有 `node_modules/.tmp`、`node_modules/.vite-temp`、`dist` 文件锁影响，未将其写成通过；隔离临时输出的 build 已通过。
- 后端最终门禁：`dotnet build src/backend/IndustrialPlatform.slnx --configuration Release` 0 warning / 0 error；`dotnet test ... --configuration Release --no-build` 1,301 passed / 3 skipped / 0 failed（BuildingBlocks 131、Gateway 14、ReferenceData 14、SystemData 542、Identity 570、UnifiedHost 20、Integration 10 passed；3 个真实 Redis/PostgreSQL/RabbitMQ 集成依赖跳过）。
- Mock E2E 使用隔离缓存的 Vite Development server，并确认单用例 `无 SystemData 管理权限直达 PC 管理页被路由守卫阻断` 1/1 通过。随后同配置 8 个目标用例均进入执行，但在 `[8/8]` 后 teardown 无汇总且 4173 已停止；按要求终止本轮进程，记为未完成，不计入通过。生产 `vite preview` 不再重复：认证契约明确禁止 production mock。
- 本轮真实 UnifiedHost 启动尝试未通过：Development 自动发现 `src/backend/appsettings.Development.local.json`，但 PostgreSQL 云依赖连接失败导致 Host 启动退出，因此本轮真实 Playwright 为未执行/0 通过，不能标记真实验收完成；未启动 Docker CLI。
- 清理结果：第五轮临时 Playwright 配置、隔离 Vite build、Mock build、Playwright 输出、预览配置和缓存均已删除；4173/5041/5080 已无本轮监听；`src/frontend/tests/e2e/screenshots` 无 Git 改动，基线图未被修改；既有 `src/frontend/test-results/.last-run.json` 未碰。

### 第五轮面板收尾整改（2026-08-28）

- 原生列设置与平台表格设置已拆为两个相邻工具入口：前者仅触发 vxe `custom`，后者仅打开序号/边框/密度平台面板；打开任一方会关闭另一方。组件测试覆盖职责分离与互斥。
- 平台设置、下载、打印均按 trigger/panel 路径处理外部点击；点击表格内部其他区域和 Escape 会关闭平台弹层，原生 vxe custom 保留其自身 click-outside 行为。新增测试覆盖表格内部点击关闭。
- 下载改为带遮罩的主题化对话式面板，保留 CSV/HTML/XML/TXT 快速导出与所属服务 `.xlsx` 参数入口；打印新增当前已加载筛选数据的可见列选择，确认后调用 vxe 原生 `print`。
- 使用隔离 Mock 浏览器配置执行四面板截图尝试；原生 custom 面板和三个平台面板均已实际写出 1440×900 截图并人工检查，未观察到 vxe missing/unsupported 控制台错误。Playwright 在 teardown 无进展，已终止本轮 Node 进程，命令退出码 1，按 0/1 计，不宣称通过。
- 本轮 TDD：新增行为测试初始 RED 为 18 项中 5 项失败；实现后 GREEN 为 18/18。门禁为 AppDataTable 定向 Vitest 1 file/18 passed、全量 Vitest 62 files/506 passed、`vue-tsc --noEmit --pretty false` 退出 0、`eslint .` 退出 0、隔离输出 Vite build 2396 modules/退出 0。
- 截图路径：`C:/Users/DONG/.codex/visualizations/2026/08/20/01a01dcb-73a2-7581-b97b-5c3432123b5c/app-data-table-column-settings-1440x900.png`、`app-data-table-table-settings-1440x900.png`、`app-data-table-download-1440x900.png`、`app-data-table-print-1440x900.png`。临时 spec/config、隔离缓存、build 输出已清理；未修改或暂存 `DSH.md`，未提交/推送。

### 排序与最终视觉复核（2026-08-28）

- vxe 4.15.13 默认排序布局由 vertical 改为官方 `sortConfig.iconLayout: 'horizontal'`；AppDataTable 仅通过组件作用域 `:deep` 调整真实 `.vxe-cell--sort-horizontal-layout` 的同行布局、标题右侧间距及浅色 token，活动排序仍使用平台主色。新增测试验证 vxe 真实 horizontal wrapper，不再只断言自制标题节点。
- DEV UI 基线的 AppDataTable 使用无副作用 `demoExporter`，仅用于展示所属服务 Excel 配置面板，不下载、不调用后端、不改变生产导出所有权。完整下载面板已验证文件名、保存类型、快速导出范围、字段、10000/自定义/全部参数。
- 1440×900 Chromium 视觉用例最终 `1/1 passed`；执行时滚动真实 `.ip-pc-main` 容器，五张当前版本截图均已更新并人工核验：列设置、表格设置、列头查询、下载、打印。列设置面板完整可见，列头筛选为标题下独立一行，排序箭头位于标题右侧；捕获 vxe/unsupported/缺少组件错误为 0。
- 本轮门禁：AppDataTable 定向 Vitest 19/19 passed；临时 tsBuildInfo 路径下 `pnpm run typecheck` 退出 0；`eslint .` 退出 0；Vite build 2396 modules、退出 0（仅既有 chunk size 提示）。临时截图 spec/config、Vite 缓存、Playwright 输出及 build 输出均已清理，5173 无本轮残留监听。

### 当前会话独立最终验收（2026-08-28）

- 前端：等价无写入 `vue-tsc --noEmit` 退出 0；ESLint 退出 0；Vitest 62 files / 507 passed / 0 failed；隔离输出 Vite build 2396 modules / 退出 0。标准 `vue-tsc --build` 仍仅受活动进程锁定 `node_modules/.tmp/tsconfig.app.tsbuildinfo` 影响，未将该命令写成通过。
- 后端：fresh Release build 0 warning / 0 error；全量 1,301 passed / 3 skipped / 0 failed。
- 视觉：复核五张 1440×900 当前截图，原生列设置、平台表格设置、列头筛选、完整下载和打印列选择均可辨认；排序箭头已与标题同行并采用浅色。
- 真实联调：`100.77.108.0` 的 PostgreSQL 5432、Redis 6379、RabbitMQ 5672 当前均不可达；UnifiedHost 因 PostgreSQL 连接失败退出，真实 Playwright 未执行/0 通过。结论保持“本地与视觉通过，云依赖真实联调待恢复”，不标记最终完成。
