# PF03 前平台能力收束功能开发 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在不进入 PF-03/PF-04+ 业务实现的前提下，把现有品牌、PC Shell、导航、Tabs、黄金样板页、多语言、统一查询/AppDataTable、受控 OData 和权限体验收束成可供 PF-03～PF-11 大量开发复用的稳定平台基线，并完成开发者自验证。

**Architecture:** 保留 Vue 3 + Element Plus + VXE、现有 Clean Architecture、SqlSugar 和服务数据所有权。前端只新增成熟 i18n 运行时和平台内聚的查询适配层；后端新增无 Web/ORM 依赖的 `IndustrialPlatform.Querying` 契约层，OData 解析归 `IndustrialPlatform.Web`，SqlSugar 执行归 `IndustrialPlatform.Infrastructure`。Identity Users 是唯一 OData 纵向样板，用户管理是唯一视觉黄金样板。

**Tech Stack:** .NET 10、ASP.NET Core、SqlSugar、`Microsoft.AspNetCore.OData@9.5.0`、Vue 3、TypeScript、Pinia、Vue Router、Element Plus、`vxe-table@4.15.13`、`vue-i18n@11.4.10`、Vitest、Playwright、xUnit。

**Spec:** `docs/superpowers/specs/2026-08-30-pf03-pre-platform-convergence-design.md`

## Developer execution checklist (2026-08-30)

- [x] Task 1 — 启动基线、保护性 WIP 快照与基线测试（`d1539f0`）
- [x] Task 2 — 国际化运行时和资源契约（`b67479b`）
- [x] Task 3 — Logo 资产和统一品牌组件（`45af0b4`）
- [x] Task 4 — PC Header、环境、业务上下文和全局搜索（`9dbae11`）
- [x] Task 5 — 业务域导航和三种状态（`ea62e0e`）
- [x] Task 6 — PC 生产操作模式与简洁宫格首页（`bc221ce`）
- [x] Task 7 — Tabs、路由标题和会话页状态国际化（`51d7b3b`）
- [x] Task 8 — QueryDescriptor 和受控 OData 序列化器（`e4872b4`）
- [x] Task 9 — AppPage/AppQueryPanel/AppDataTable 契约冻结（`5cec4de`）
- [x] Task 10 — Querying/OData/SqlSugar BuildingBlock（`f8c53f6`）
- [x] Task 11 — Identity Users 唯一 OData 纵向样板（`cad3279`）
- [x] Task 12 — 用户管理视觉黄金样板（`708339f`）
- [x] Task 13 — 权限目录、Gate 和稳定错误码（`283730f`）
- [x] Task 14 — 公共文案与能力边界规范（`b73799f`）
- [x] Task 15 — 开发者完整自验证、真实环境缺口和交付封存（`4a6dbe7` + final evidence commit）

## Global Constraints

- 以任务启动时的 `develop` 工作树为输入；其中既有未提交修改和未跟踪的 `AppDataTable`、导出、锁屏、测试是受保护 WIP，不得 reset、checkout、覆盖式重写或丢弃。
- `DSH.md`、`CLAUDE.md`、`AGENTS.md`、`docs/prototypes/` 保持不修改、不暂存、不提交；`docs/prototypes/**/node_modules`、`dist`、`bin`、`obj`、`TestResults`、日志和缓存不得进入提交。
- 不实现 PF-03 ReferenceData 业务，不实现 PF-04 Audit/File/Notification，不创建 PF-05～PF-11 或 MES 功能、假上下文、假服务状态、假通知入口。唯一允许的未来功能展示是生产操作模式注册表中明确禁用、标注“待实现”且不导航/不请求 API 的八个占位卡片。
- 不替换 Vue、Pinia、Router、Element Plus、VXE、SqlSugar，不引入第二套 UI/表格/权限/查询框架，不迁入 Admin.NET/MalusAdmin/TMom/Vben/ABP/Furion 整体架构。
- `AppDataTable.vue` 只能小步提取纯逻辑和适配器；公开 API 先锁定再拆分，禁止推倒重写。业务页面不得直接导入 `vxe-table` 或接触 VXE 实例。
- OData 只允许 Identity 自有 Users read model 的受控只读入口；不得使用 `[EnableQuery]` 暴露 `IQueryable`，不得跨库、跨服务、应用于 Command 或绕过租户/数据范围/软删/权限。
- 每个任务遵循 RED → GREEN → REFACTOR；只在对应定向测试通过后提交。提交不得包含任务外 WIP，开发任务不 push。
- 用户已授权实施期间不询问。遇到不改变边界的细节，采用本计划中最保守的方案并记录；遇到真实外部基础设施不可达，保留证据并继续所有不依赖该基础设施的验证。

---

### Task 1: 冻结输入基线和受保护 WIP

**Files:**

- Create: `docs/evidence/pf03-pre-platform-convergence-baseline-2026-08-30.md`
- Read only: `docs/superpowers/plans/2026-08-27-platform-management-experience-remediation.md`
- Read only: `docs/evidence/platform-management-experience-remediation-2026-08-27.md`
- Read only: `docs/prototypes/user-management-table-layout/`
- Read only: `DSH.md`

- [ ] 记录任务绝对工作树、分支、`HEAD`、`origin/develop`、ahead/behind、Node/pnpm/.NET 版本，并保存 `git status --short`、tracked/untracked/ignored 数量。
- [ ] 将当前 WIP 按“平台管理体验输入 / 本轮设计与计划 / 生成物 / 明确排除”分类；特别列出未跟踪的 `AppDataTable.*`、Identity/SystemData `Export/`、锁屏和已有测试。
- [ ] 运行现有高风险定向基线测试，区分“任务启动前已失败”和“本轮回归”：

```powershell
cd src/frontend
pnpm.cmd test:unit -- tests/components/AppDataTable.spec.ts tests/components/IdentityUsersPage.spec.ts tests/components/PcLayout.spec.ts
cd ../..
dotnet test tests/BuildingBlocks/IndustrialPlatform.BuildingBlocks.Tests/IndustrialPlatform.BuildingBlocks.Tests.csproj --configuration Release
```

- [ ] 在证据文档中写明失败数、通过数、退出码和已有外部依赖缺口；不得为了获得绿色基线修改无关代码。
- [ ] 验证 `DSH.md` 的初始内容哈希并在后续每个提交前比对。
- [ ] Commit（只提交证据文档；若基线测试暴露的是启动前失败，也只记录不修复）：

```powershell
git add docs/evidence/pf03-pre-platform-convergence-baseline-2026-08-30.md
git commit -m "docs: record PF03 pre-convergence baseline"
```

### Task 2: 建立国际化运行时和稳定资源契约

**Files:**

- Modify: `src/frontend/package.json`
- Modify: `src/frontend/pnpm-lock.yaml`
- Modify: `src/frontend/src/app/createIndustrialApp.ts`
- Modify: `src/frontend/index.html`
- Create: `src/frontend/src/localization/i18n.ts`
- Create: `src/frontend/src/localization/types.ts`
- Create: `src/frontend/src/localization/preferences.ts`
- Create: `src/frontend/src/localization/formatters.ts`
- Create: `src/frontend/src/locales/zh-CN.ts`
- Create: `src/frontend/src/locales/en-US.ts`
- Create: `src/frontend/src/stores/localizationStore.ts`
- Create: `src/frontend/src/components/localization/LocaleControl.vue`
- Create: `src/frontend/tests/unit/localizationResources.spec.ts`
- Create: `src/frontend/tests/unit/localizationPreferences.spec.ts`
- Create: `src/frontend/tests/unit/localizationFormatters.spec.ts`
- Create: `src/frontend/tests/components/LocaleControl.spec.ts`

- [ ] 先写失败测试：两种 locale 具有完全相同的资源键；未知键回退 `zh-CN`；语言、时区、日期/数字格式和 `metric` 单位偏好独立保存；切换语言更新 `<html lang>`；品牌名保持不翻译。
- [ ] 精确安装 `vue-i18n@11.4.10`，由 `createIndustrialApp` 在 Router 前安装；不得自研插值、复数或 fallback 引擎。
- [ ] 固定类型：

```ts
export type SupportedLocale = 'zh-CN' | 'en-US'

export interface LocalePreferences {
  locale: SupportedLocale
  timeZone: string
  dateFormat: 'yyyy-MM-dd' | 'MM/dd/yyyy'
  numberLocale: SupportedLocale
  unitSystem: 'metric'
}
```

- [ ] 资源键按 `common.*`、`shell.*`、`identity.*`、`systemData.*` 分区；资源对象必须由 TypeScript 约束同构，测试禁止缺键和直接把中文文本作为 key。
- [ ] 使用 `Intl.DateTimeFormat`、`Intl.NumberFormat` 和显式时区格式化；禁止切割 ISO 字符串或在语言切换时修改时区。
- [ ] `LocaleControl` 只更改当前用户 UI 偏好并有 aria-label/Tooltip；服务端用户偏好同步不在本轮伪造。
- [ ] 运行：

```powershell
cd src/frontend
pnpm.cmd test:unit -- tests/unit/localizationResources.spec.ts tests/unit/localizationPreferences.spec.ts tests/unit/localizationFormatters.spec.ts tests/components/LocaleControl.spec.ts
pnpm.cmd typecheck
```

- [ ] Commit：

```powershell
git add src/frontend/package.json src/frontend/pnpm-lock.yaml src/frontend/index.html src/frontend/src/app/createIndustrialApp.ts src/frontend/src/localization src/frontend/src/locales src/frontend/src/stores/localizationStore.ts src/frontend/src/components/localization src/frontend/tests/unit/localizationResources.spec.ts src/frontend/tests/unit/localizationPreferences.spec.ts src/frontend/tests/unit/localizationFormatters.spec.ts src/frontend/tests/components/LocaleControl.spec.ts
git commit -m "feat(frontend): add platform localization foundation"
```

### Task 3: 固化 Logo 资产和统一品牌组件

**Files:**

- Create: `src/frontend/public/brand/mark.svg`
- Create: `src/frontend/public/brand/horizontal-light.svg`
- Create: `src/frontend/public/brand/horizontal-dark.svg`
- Create: `src/frontend/public/brand/monochrome.svg`
- Modify: `src/frontend/public/favicon.svg`
- Create: `src/frontend/src/components/brand/PlatformBrand.vue`
- Modify: `src/frontend/src/app/appInfo.ts`
- Modify: `src/frontend/src/layouts/PcLayout.vue`
- Modify: `src/frontend/src/layouts/MobileLayout.vue`
- Modify: `src/frontend/src/pages/public/LoginPage.vue`
- Modify: `src/frontend/src/pages/sso/SsoLoginPage.vue`
- Create: `src/frontend/tests/components/PlatformBrand.spec.ts`

- [ ] 先写组件测试，锁定 `light | dark | mark | monochrome`、`compact`、`showName`、尺寸、可访问名称和无图片时的降级。
- [ ] 以当前 favicon 三层连接几何为母版生成四个同源 SVG；不引入第二个无关符号，不把 SVG 内容复制到 Vue 页面。
- [ ] `PlatformBrand` 的唯一公开契约为设计规格中的 `PlatformBrandProps`；PC、Mobile、登录、SSO 都改用组件，PDA/极窄场景使用 `mark`。
- [ ] `Industrial Platform` 继续由 `appInfo.ts` 提供，不进入翻译资源；辅助说明使用资源 key。
- [ ] 运行：

```powershell
cd src/frontend
pnpm.cmd test:unit -- tests/components/PlatformBrand.spec.ts tests/components/PcLayout.spec.ts
pnpm.cmd lint
```

- [ ] Commit：

```powershell
git add src/frontend/public/brand src/frontend/public/favicon.svg src/frontend/src/components/brand src/frontend/src/app/appInfo.ts src/frontend/src/layouts/PcLayout.vue src/frontend/src/layouts/MobileLayout.vue src/frontend/src/pages/public/LoginPage.vue src/frontend/src/pages/sso/SsoLoginPage.vue src/frontend/tests/components/PlatformBrand.spec.ts src/frontend/tests/components/PcLayout.spec.ts
git commit -m "feat(frontend): formalize platform brand assets"
```

### Task 4: 收束 PC Header、环境、业务上下文和全局搜索

**Files:**

- Modify: `src/frontend/src/config/runtimeConfig.ts`
- Modify: `src/frontend/.env.example`
- Modify: `src/frontend/src/components/shell/PlatformTopBar.vue`
- Modify: `src/frontend/src/layouts/PcLayout.vue`
- Create: `src/frontend/src/components/shell/PlatformCommandSearch.vue`
- Create: `src/frontend/src/components/shell/PlatformEnvironmentBadge.vue`
- Create: `src/frontend/src/components/shell/PlatformContextSwitcher.vue`
- Create: `src/frontend/src/components/shell/PlatformServiceStatus.vue`
- Modify: `src/frontend/src/components/systemData/SystemDataRuntimeStatus.vue`
- Create: `src/frontend/tests/components/shell/PlatformCommandSearch.spec.ts`
- Create: `src/frontend/tests/components/shell/PlatformEnvironmentBadge.spec.ts`
- Create: `src/frontend/tests/components/shell/PlatformContextSwitcher.spec.ts`
- Create: `src/frontend/tests/components/shell/PlatformServiceStatus.spec.ts`
- Modify: `src/frontend/tests/unit/runtimeConfig.spec.ts`
- Modify: `src/frontend/tests/components/PcLayout.spec.ts`

- [ ] 先写失败测试，固定 Header 左/中/右三区域、`DEV|TEST|UAT|PROD` 严格解析、`Ctrl+K`、授权菜单/最近访问/快捷命令搜索、真实租户上下文和无数据隐藏语义。
- [ ] 在运行配置增加必填且受控的 `deploymentEnvironment`；生产缺值或非法值直接配置错误，不把 `DEV` 当静默生产默认。
- [ ] 复用 `PlatformTopBar` 既有槽位：左侧品牌/终端/环境，中间 tenant 上下文和命令搜索，右侧 SystemData 真实运行状态/语言/主题/浏览器全屏/经验证锁屏/用户。
- [ ] `PlatformCommandSearch` 递归搜索当前已授权的真实导航和最近页签；不得搜索不存在的物料、工单、用户或跨服务数据。
- [ ] `PlatformContextSwitcher` 只展示认证会话中真实 tenant；公司/工厂/车间契约尚不存在时隐藏，不放静态选项。
- [ ] `PlatformServiceStatus` 只投影已有 SystemData runtime 事实；保留原降级详情，不将其宣传为全平台健康。
- [ ] 不添加 Notification 假铃铛、假角标；锁屏只有既有 `AppLockOverlay` 正反路径测试通过才保留。
- [ ] 运行定向测试并人工键盘检查 `Ctrl+K`、Escape、焦点回收：

```powershell
cd src/frontend
pnpm.cmd test:unit -- tests/unit/runtimeConfig.spec.ts tests/components/shell/PlatformCommandSearch.spec.ts tests/components/shell/PlatformEnvironmentBadge.spec.ts tests/components/shell/PlatformContextSwitcher.spec.ts tests/components/shell/PlatformServiceStatus.spec.ts tests/components/PcLayout.spec.ts tests/unit/lockStore.spec.ts
```

- [ ] Commit：`feat(frontend): converge the PC platform header`。

### Task 5: 将一级/二级导航收束为业务域和三种状态

**Files:**

- Modify: `src/frontend/src/components/navigation/types.ts`
- Modify: `src/frontend/src/components/navigation/navigation.ts`
- Modify: `src/frontend/src/systemData/runtime/navigation.ts`
- Modify: `src/frontend/src/components/shell/PlatformToolRail.vue`
- Modify: `src/frontend/src/components/shell/PlatformFunctionTree.vue`
- Modify: `src/frontend/src/layouts/PcLayout.vue`
- Modify: `src/frontend/src/theme/types.ts`
- Modify: `src/frontend/src/stores/themeStore.ts`
- Modify: `src/frontend/src/styles/foundation.css`
- Modify: `src/frontend/src/styles/themes.css`
- Modify: `src/frontend/tests/components/shell/PlatformToolRail.spec.ts`
- Modify: `src/frontend/tests/components/shell/PlatformFunctionTree.spec.ts`
- Modify: `src/frontend/tests/unit/navigation.spec.ts`
- Modify: `src/frontend/tests/unit/themePreferences.spec.ts`
- Modify: `src/frontend/tests/unit/themeStore.spec.ts`
- Modify: `src/frontend/tests/unit/systemDataRuntime.spec.ts`
- Modify: `src/frontend/tests/e2e/pc-shell.spec.ts`

- [ ] 先写失败测试，覆盖 `labelKey + fallbackLabel + displayOrder`、递归 children、权限/feature 过滤、动态导航稳定排序和三种导航状态迁移。
- [ ] 将导航类型升级为：

```ts
export interface NavigationText {
  labelKey: string
  fallbackLabel: string
}

export type PcNavigationMode = 'expanded' | 'secondary-collapsed' | 'compact'
```

- [ ] 一级导航只保留已有真实路由的“工作台”和“平台管理”业务域；Identity/SystemData 是平台管理下的能力，不作为微服务一级入口。没有真实功能的制造/物料/质量/称量/设备/数据中心不得出现空入口。
- [ ] `PlatformToolRail` 默认宽度 96px、图标+文字、英文最多两行；`compact` 才使用 52px 图标态。二级菜单宽度 208px，包含菜单搜索；收起时搜索一起收起并支持按钮展开聚焦。
- [ ] 将旧 `pcFunctionTreeCollapsed` 偏好迁移到 version 2 的 `pcNavigationMode`，不得丢失现有用户主题偏好。
- [ ] SystemData 动态 DTO 暂无资源 key 时使用 `fallbackLabel`，但 id/route/排序不得依赖中文 label；Shell 继续只消费适配器输出。
- [ ] 在 1280×720、1440×900 和窄窗口验证三态，无横向溢出、无 hover-only 导航、键盘焦点可见。
- [ ] 运行：

```powershell
cd src/frontend
pnpm.cmd test:unit -- tests/components/shell/PlatformToolRail.spec.ts tests/components/shell/PlatformFunctionTree.spec.ts tests/components/PcLayout.spec.ts tests/unit/navigation.spec.ts tests/unit/themePreferences.spec.ts tests/unit/themeStore.spec.ts tests/unit/systemDataRuntime.spec.ts
pnpm.cmd exec playwright test --config=playwright.config.ts tests/e2e/pc-shell.spec.ts
```

- [ ] Commit：`feat(frontend): align navigation with business domains`。

### Task 6: 增加 PC 生产操作模式、权限切换和简洁宫格首页

**Reference:** 用户提供的 1260×632 参考截图只用于“3×3 大卡片、一跳入口、高对比图标文字”的布局思路；不得复制其品牌、精确配色、字幕、视频水印或把截图直接用作页面背景。

**Files:**

- Create: `src/frontend/src/operation/types.ts`
- Create: `src/frontend/src/operation/launchers.ts`
- Create: `src/frontend/src/stores/pcExperienceStore.ts`
- Create: `src/frontend/src/components/shell/PcExperienceModeControl.vue`
- Create: `src/frontend/src/layouts/OperationLayout.vue`
- Create: `src/frontend/src/pages/pc/PcOperationHomePage.vue`
- Modify: `src/frontend/src/router/routes.ts`
- Modify: `src/frontend/src/router/meta.ts`
- Modify: `src/frontend/src/router/guards.ts`
- Modify: `src/frontend/src/layouts/PcLayout.vue`
- Modify: `src/frontend/src/permissions/catalog.ts`
- Modify: `src/backend/src/Services/Identity/IndustrialPlatform.Identity.Domain/Permissions/PermissionCatalog.cs`
- Modify: `src/backend/src/Services/Identity/IndustrialPlatform.Identity.Application/Bootstrap/BootstrapSeedCatalog.cs`
- Modify: `src/frontend/src/styles/foundation.css`
- Modify: `src/frontend/src/styles/themes.css`
- Create: `src/frontend/tests/unit/pcExperienceStore.spec.ts`
- Create: `src/frontend/tests/unit/operationLaunchers.spec.ts`
- Create: `src/frontend/tests/components/PcExperienceModeControl.spec.ts`
- Create: `src/frontend/tests/components/OperationLayout.spec.ts`
- Create: `src/frontend/tests/components/PcOperationHomePage.spec.ts`
- Modify: `src/frontend/tests/unit/routerGuards.spec.ts`
- Modify: `tests/Identity/IndustrialPlatform.Identity.Tests/Domain_PermissionCatalogTests.cs`
- Create: `src/frontend/tests/e2e/pc-operation-mode.spec.ts`

- [ ] 先写失败测试，固定两种模式和授权规则：`platform.home.view` 允许管理模式，`platform.operation.view` 允许生产模式；同时有两项才显示切换，只有一项自动进入可用模式，已保存模式失权时安全回退。
- [ ] 使用以下薄注册模型；占位卡片不得伪造 route、permission、feature 或 API：

```ts
export type PcExperienceMode = 'management' | 'operation'

export interface OperationLauncher {
  id: string
  titleKey: string
  fallbackTitle: string
  icon: Component
  state: 'available' | 'coming-soon'
  routeName?: string
  permission?: string
  featureNId?: string
}
```

- [ ] `pcExperienceStore` 按 `tenant + user + device` 保存偏好，切换不登出、不清管理 Tabs/查询/滚动状态；不根据前端 role 名、姓名或文化程度推断模式。
- [ ] `OperationLayout` 使用独立简洁 Shell：隐藏管理一级/二级导航、Tabs 和密集工具栏；顶栏只保留统一品牌、真实 tenant/已接入上下文、环境、语言、全屏、当前用户和有权时的模式切换。
- [ ] `PcOperationHomePage` 采用现有主题 token 的大面积实色卡片，不照搬第三方蓝色。>=1280px 为 3 列、900～1279px 为 2 列，卡片最小高 176px、图标 56～72px、标题 24～28px、间距 12～16px；最多 9 个首屏入口，无 hover-only 交互。
- [ ] 用 `@element-plus/icons-vue` 的最接近图标，不手绘 SVG/CSS 图标。V1 注册八个 `coming-soon`：任务执行、工单作业、投料作业、称量作业、投料统计、物料集中、物料接收、配方查看；每项显示本地化“待实现”、`aria-disabled=true`，点击不导航、不发送网络请求。
- [ ] 第九个“界面设置”标为 `available`，只打开已有语言、主题、浏览器全屏和模式设置，不创建新的后端配置或客户端管理业务。
- [ ] 生产模式仅显示当前 locale 的一个主标题，避免中英双文案增加认知负荷；状态、焦点、错误采用大字和高对比，技术 trace 不放主界面。
- [ ] 路由守卫直接使用权限目录；直接访问 `/pc/operation` 无权限必须 403。新增权限进入 Identity 权威目录和 seed，但不创建 MES 权限或业务端点。
- [ ] 组件测试同时断言：八个占位无 href/route/请求；设置卡可用；键盘 Enter/Space 不激活禁用项；模式切换保留认证和管理 state；两种语言资源完整。
- [ ] 执行设计 QA：用与参考图相同宽屏状态并排检查信息密度、卡片比例、图标/标题层级和一跳语义，再检查现有 Industrial Platform 主题一致性。只要求继承布局思路，不追求第三方像素克隆。
- [ ] 运行：

```powershell
dotnet test tests/Identity/IndustrialPlatform.Identity.Tests/IndustrialPlatform.Identity.Tests.csproj --configuration Release --filter FullyQualifiedName~PermissionCatalog
cd src/frontend
pnpm.cmd test:unit -- tests/unit/pcExperienceStore.spec.ts tests/unit/operationLaunchers.spec.ts tests/components/PcExperienceModeControl.spec.ts tests/components/OperationLayout.spec.ts tests/components/PcOperationHomePage.spec.ts tests/unit/routerGuards.spec.ts
pnpm.cmd exec playwright test --config=playwright.config.ts tests/e2e/pc-operation-mode.spec.ts
```

- [ ] Commit：`feat(frontend): add the PC production operation mode`。

### Task 7: Tabs、路由标题和会话内页面状态国际化

**Files:**

- Modify: `src/frontend/src/router/meta.ts`
- Modify: `src/frontend/src/router/routes.ts`
- Modify: `src/frontend/src/router/guards.ts`
- Modify: `src/frontend/src/workspace/types.ts`
- Modify: `src/frontend/src/workspace/identity.ts`
- Modify: `src/frontend/src/workspace/persistence.ts`
- Create: `src/frontend/src/workspace/pageState.ts`
- Modify: `src/frontend/src/stores/workspaceTabsStore.ts`
- Modify: `src/frontend/src/components/shell/PcWorkspaceTabs.vue`
- Modify: `src/frontend/src/components/shell/WorkspaceTabLimitDialog.vue`
- Modify: `src/frontend/tests/unit/routerGuards.spec.ts`
- Modify: `src/frontend/tests/unit/workspaceTabsStore.spec.ts`
- Modify: `src/frontend/tests/unit/workspacePersistence.spec.ts`
- Create: `src/frontend/tests/unit/workspacePageState.spec.ts`
- Modify: `src/frontend/tests/components/shell/PcWorkspaceTabs.spec.ts`
- Modify: `src/frontend/tests/e2e/workspace-tabs.spec.ts`

- [ ] 先写失败测试：Route meta 和 Tab 只保存 `titleKey/fallbackTitle`；切换 locale 后 document title、页签和菜单同步更新；旧 v1 快照可迁移；无权/非法路由被 prune。
- [ ] 支持业务页签固定/取消固定，关闭全部保留 pinned 和固定工作台；pinned 不显示关闭按钮；现有刷新、左右/其他/全部、重新打开、专注规则不得回退。
- [ ] `pageState.ts` 只在当前浏览器会话内按 tab identity 保存查询、分页、排序、滚动；关闭页签清理。业务筛选值、权限、Token、个人资料不得写入长期 localStorage。
- [ ] AppDataTable 的列宽/顺序/密度继续使用既有“用户 + 路由 + tableKey”偏好，不复制一套页签偏好。
- [ ] 运行：

```powershell
cd src/frontend
pnpm.cmd test:unit -- tests/unit/routerGuards.spec.ts tests/unit/workspaceTabsStore.spec.ts tests/unit/workspacePersistence.spec.ts tests/unit/workspacePageState.spec.ts tests/components/shell/PcWorkspaceTabs.spec.ts
pnpm.cmd exec playwright test --config=playwright.config.ts tests/e2e/workspace-tabs.spec.ts
```

- [ ] Commit：`feat(frontend): localize and retain workspace tab state`。

### Task 8: 建立单一前端 QueryDescriptor 和受控 OData 序列化器

**Files:**

- Create: `src/frontend/src/querying/types.ts`
- Create: `src/frontend/src/querying/normalize.ts`
- Create: `src/frontend/src/querying/odata.ts`
- Create: `src/frontend/src/querying/index.ts`
- Create: `src/frontend/tests/unit/queryDescriptor.spec.ts`
- Create: `src/frontend/tests/unit/odataAdapter.spec.ts`

- [ ] 按设计规格原样建立 `QueryOperator`、`QueryFilter`、`QuerySort`、`QueryDescriptor`，并使数组只读/规范化输出稳定；不得再定义第二套 skip/take 页面模型。
- [ ] 先写 serializer 失败测试：字段名必须来自资源 schema；字符串正确转义；日期/布尔/数字不加错误引号；`between/in/contains/startsWith` 映射稳定；空值和非法操作符被拒绝。
- [ ] `normalizeQueryDescriptor` 固定 `pageIndex >= 1`、`1 <= pageSize <= 100`、排序最多 3、重复筛选合并/拒绝规则和稳定 tie-break；不得把 UI 展示 label 作为查询字段。
- [ ] `toODataQuery` 只生成 `$filter/$select/$orderby/$top/$skip/$count`，明确拒绝 expand/apply/compute/search/batch；只负责序列化，不发送请求、不拥有服务 URL。
- [ ] 顶部查询、表头查询、快速搜索都是 UI 输入源，必须在页面边界归一为同一 descriptor；OData 不是第二套状态。
- [ ] 运行并提交：

```powershell
cd src/frontend
pnpm.cmd test:unit -- tests/unit/queryDescriptor.spec.ts tests/unit/odataAdapter.spec.ts
pnpm.cmd typecheck
git add src/frontend/src/querying src/frontend/tests/unit/queryDescriptor.spec.ts src/frontend/tests/unit/odataAdapter.spec.ts
git commit -m "feat(frontend): add the shared query descriptor"
```

### Task 9: 冻结并瘦身 AppPage、AppQueryPanel 和 AppDataTable 契约

**Files:**

- Modify: `src/frontend/src/components/base/AppPage.vue`
- Modify: `src/frontend/src/components/management/AppQueryPanel.vue`
- Modify: `src/frontend/src/components/management/AppDataTable.ts`
- Modify: `src/frontend/src/components/management/AppDataTable.vue`
- Create: `src/frontend/src/components/management/appDataTable/preferences.ts`
- Create: `src/frontend/src/components/management/appDataTable/exporting.ts`
- Create: `src/frontend/src/components/management/appDataTable/vxeDomAdapter.ts`
- Modify: `src/frontend/src/components/systemData/SystemDataAdminFrame.vue`
- Create: `src/frontend/tests/components/AppPage.spec.ts`
- Create: `src/frontend/tests/components/AppQueryPanel.spec.ts`
- Modify: `src/frontend/tests/components/AppDataTable.spec.ts`
- Create: `src/frontend/tests/unit/appDataTablePublicApi.spec.ts`
- Create: `src/frontend/tests/unit/vxeUpgradeContract.spec.ts`

- [ ] 在实现前把现有 props/events/slots、偏好 key 和 loader/exporter 请求结构写成契约测试；测试必须先证明改名或泄露 VXE 会失败。
- [ ] `AppPage` 现有 header 扩展 actions/meta/breadcrumb 槽，不另造第二套 PageHeader；`AppQueryPanel` 增加标准 submit/reset/actions、响应式网格和折叠文案，不建设 schema 表单设计器。
- [ ] AppQueryPanel 输出 `QueryDescriptor`；AppDataTable loader 和 Excel exporter 接收同一个 descriptor，exporter 只额外接收列、数量、culture、timeZone。
- [ ] 从 3,800+ 行组件中只提取偏好、导出和 VXE DOM 适配纯模块；VXE 私有 DOM/CSS 访问必须全部位于 `vxeDomAdapter.ts`，业务页和其他平台组件零直接依赖。
- [ ] 保留已验证的普通/明细/树形、异步子节点、选择、分组、筛选、排序、分页、列宽、密度、全屏、快速导出、服务端 Excel、打印和错误/空/加载行为。
- [ ] 运行：

```powershell
cd src/frontend
pnpm.cmd test:unit -- tests/components/AppPage.spec.ts tests/components/AppQueryPanel.spec.ts tests/components/AppDataTable.spec.ts tests/unit/appDataTablePublicApi.spec.ts tests/unit/vxeUpgradeContract.spec.ts
pnpm.cmd typecheck
```

- [ ] Commit：`refactor(frontend): freeze shared management page contracts`。

### Task 10: 建立纯查询 BuildingBlock、Web OData 解析器和 SqlSugar 适配器

**Files:**

- Modify: `Directory.Packages.props`
- Modify: `src/backend/IndustrialPlatform.slnx`
- Create: `src/backend/src/BuildingBlocks/IndustrialPlatform.Querying/IndustrialPlatform.Querying.csproj`
- Create: `src/backend/src/BuildingBlocks/IndustrialPlatform.Querying/Descriptors/QueryDescriptor.cs`
- Create: `src/backend/src/BuildingBlocks/IndustrialPlatform.Querying/Descriptors/QueryFilter.cs`
- Create: `src/backend/src/BuildingBlocks/IndustrialPlatform.Querying/Descriptors/QueryOperator.cs`
- Create: `src/backend/src/BuildingBlocks/IndustrialPlatform.Querying/Descriptors/QuerySort.cs`
- Create: `src/backend/src/BuildingBlocks/IndustrialPlatform.Querying/Schema/QueryResourceDefinition.cs`
- Create: `src/backend/src/BuildingBlocks/IndustrialPlatform.Querying/Schema/QueryFieldDefinition.cs`
- Create: `src/backend/src/BuildingBlocks/IndustrialPlatform.Querying/Validation/QueryLimits.cs`
- Create: `src/backend/src/BuildingBlocks/IndustrialPlatform.Querying/Validation/QueryValidationError.cs`
- Modify: `src/backend/src/BuildingBlocks/IndustrialPlatform.Web/IndustrialPlatform.Web.csproj`
- Create: `src/backend/src/BuildingBlocks/IndustrialPlatform.Web/Querying/ODataQueryDescriptorParser.cs`
- Create: `src/backend/src/BuildingBlocks/IndustrialPlatform.Web/Querying/ODataQueryValidationSettings.cs`
- Create: `src/backend/src/BuildingBlocks/IndustrialPlatform.Web/Extensions/QueryingServiceCollectionExtensions.cs`
- Modify: `src/backend/src/BuildingBlocks/IndustrialPlatform.Infrastructure/IndustrialPlatform.Infrastructure.csproj`
- Create: `src/backend/src/BuildingBlocks/IndustrialPlatform.Infrastructure/Querying/SqlSugarQueryAdapter.cs`
- Create: `src/backend/src/BuildingBlocks/IndustrialPlatform.Infrastructure/Querying/SqlSugarQueryFieldMap.cs`
- Modify: `src/backend/src/BuildingBlocks/README.md`
- Modify: `tests/BuildingBlocks/IndustrialPlatform.BuildingBlocks.Tests/IndustrialPlatform.BuildingBlocks.Tests.csproj`
- Create: `tests/BuildingBlocks/IndustrialPlatform.BuildingBlocks.Tests/QueryDescriptorTests.cs`
- Create: `tests/BuildingBlocks/IndustrialPlatform.BuildingBlocks.Tests/ODataQueryDescriptorParserTests.cs`
- Create: `tests/BuildingBlocks/IndustrialPlatform.BuildingBlocks.Tests/SqlSugarQueryAdapterTests.cs`
- Modify: `tests/BuildingBlocks/IndustrialPlatform.BuildingBlocks.Tests/ProjectReferenceArchitectureTests.cs`
- Modify: `tests/BuildingBlocks/IndustrialPlatform.BuildingBlocks.Tests/AssemblyBoundaryTests.cs`

- [ ] 先写架构失败测试：Querying 不得引用 ASP.NET、OData、SqlSugar 或任何业务服务；Web 可以引用 Querying/OData；Infrastructure 可以引用 Querying/SqlSugar；服务之间不得新增编译引用。
- [ ] 在中央版本文件锁定 `Microsoft.AspNetCore.OData` 9.5.0；不采用 preview，不引入额外动态 LINQ/表达式包。
- [ ] Querying 定义与前端同语义的不可变 records、字段 schema 和稳定验证错误，不承载本地化文本。
- [ ] OData parser 只接受 `$filter/$select/$orderby/$top/$skip/$count`；MaxTop=100、filter nodes=20、function depth=5、sort fields=3，禁用 expand/apply/compute/search/batch、any/all、导航、算术和 cast。
- [ ] 平台内部继续使用 `pageIndex/pageSize`；为保持 `PageResult` 精确语义，V1 明确要求 `$top` 存在且 `$skip % $top == 0`，不对齐时返回 `PLATFORM_QUERY_PAGING_ALIGNMENT_REQUIRED` 400。不得用整数除法静默丢失余数，也不得为 V1 新增第二套 Offset/Limit 状态。
- [ ] SqlSugar adapter 只接受服务注册的 `SqlSugarQueryFieldMap`；字段不得从客户端字符串直接拼进 SQL。服务 base query 必须先施加 tenant/data scope/soft delete/field permission，adapter 只追加白名单 filter/order/paging/count。
- [ ] 10 秒超时由调用端 linked cancellation token 落实；默认排序追加稳定 `NId` tie-break。
- [ ] 运行：

```powershell
dotnet test tests/BuildingBlocks/IndustrialPlatform.BuildingBlocks.Tests/IndustrialPlatform.BuildingBlocks.Tests.csproj --configuration Release
dotnet build src/backend/IndustrialPlatform.slnx --configuration Release
```

- [ ] Commit：`feat(querying): add controlled OData and SqlSugar adapters`。

### Task 11: 用 Identity Users 打通唯一 OData 纵向样板

**Files:**

- Modify: `src/backend/src/Services/Identity/IndustrialPlatform.Identity.Api/IndustrialPlatform.Identity.Api.csproj`
- Modify: `src/backend/src/Services/Identity/IndustrialPlatform.Identity.Application/IndustrialPlatform.Identity.Application.csproj`
- Modify: `src/backend/src/Services/Identity/IndustrialPlatform.Identity.Infrastructure/IndustrialPlatform.Identity.Infrastructure.csproj`
- Modify: `src/backend/src/Services/Identity/IndustrialPlatform.Identity.Api/Modules/IdentityModule.cs`
- Create: `src/backend/src/Services/Identity/IndustrialPlatform.Identity.Application/Management/UserQueryResource.cs`
- Modify: `src/backend/src/Services/Identity/IndustrialPlatform.Identity.Application/Management/IUserManagementService.cs`
- Modify: `src/backend/src/Services/Identity/IndustrialPlatform.Identity.Application/Management/UserManagementService.cs`
- Modify: `src/backend/src/Services/Identity/IndustrialPlatform.Identity.Application/Management/ManagementStoreContracts.cs`
- Modify: `src/backend/src/Services/Identity/IndustrialPlatform.Identity.Infrastructure/Management/ManagementStore.cs`
- Modify: `src/backend/src/Services/Identity/IndustrialPlatform.Identity.Api/Controllers/UsersController.cs`
- Modify: `src/backend/src/Services/Identity/IndustrialPlatform.Identity.Api/Export/StreamingXlsxExport.cs`
- Modify: `src/frontend/src/api/identity/management/types.ts`
- Modify: `src/frontend/src/api/identity/management/managementApi.ts`
- Modify: `src/frontend/src/pages/pc/identity/IdentityUsersPage.vue`
- Create: `tests/Identity/IndustrialPlatform.Identity.Tests/Api_UserODataEndpointTests.cs`
- Create: `tests/Identity/IndustrialPlatform.Identity.Tests/Infrastructure_UserQueryStoreTests.cs`
- Modify: `tests/Identity/IndustrialPlatform.Identity.Tests/Api_ManagementEndpointTests.cs`
- Modify: `src/frontend/tests/components/IdentityUsersPage.spec.ts`
- Create: `src/frontend/tests/contract/identityUserQueryApi.spec.ts`

- [ ] 先写后端失败测试：401、无 `identity.user.view` 的 403、tenant/soft-delete 绕过失败、非法字段/禁用 option/超限 400、filter/select/order/top/skip/count、稳定 tie-break、取消和超时。
- [ ] 新增服务自有 `GET /api/v1/odata/users`；Controller 只解析成 descriptor，返回 read model/PageResult，不返回 `IQueryable`，不使用 `[EnableQuery]`。
- [ ] 端点继续使用平台 `ApiResult<PageResult<...>>` 信封，不返回裸 OData `value/@odata.count`；`$select` 在最多 100 条稳定 User read model 上由 API 末端按白名单投影，不为 V1 建动态 SqlSugar 投影框架。
- [ ] `tenantNId/includeDeleted/group/role` 属于服务端 base scope；客户端 OData 不得修改。现有 legacy GET query 暂时保留并薄映射成同一 descriptor，避免破坏已有调用。
- [ ] User list 与服务端 Excel 使用相同 `UserQueryResource`、descriptor、字段白名单、权限和数据范围；删除两条链之间的复制筛选/排序逻辑。
- [ ] 前端 Identity Users loader 使用 `toODataQuery`；Command/CRUD/角色分配/状态/删除恢复/密码继续走现有 API。导出传同一 descriptor、列、数量、culture/timeZone。
- [ ] 运行：

```powershell
dotnet test tests/Identity/IndustrialPlatform.Identity.Tests/IndustrialPlatform.Identity.Tests.csproj --configuration Release
cd src/frontend
pnpm.cmd test:unit -- tests/components/IdentityUsersPage.spec.ts tests/contract/identityUserQueryApi.spec.ts tests/unit/odataAdapter.spec.ts
```

- [ ] Commit：`feat(identity): add the controlled users OData sample`。

### Task 12: 冻结用户管理为唯一视觉黄金样板

**Files:**

- Modify: `src/frontend/src/pages/pc/identity/IdentityUsersPage.vue`
- Modify: `src/frontend/src/pages/pc/identity/components/TemporaryPasswordDialog.vue`
- Modify: `src/frontend/src/pages/pc/identity/shared.ts`
- Modify: `src/frontend/src/components/base/AppPage.vue`
- Modify: `src/frontend/src/components/management/AppQueryPanel.vue`
- Modify: `src/frontend/src/components/management/AppDataTable.vue`
- Modify: `src/frontend/tests/components/IdentityUsersPage.spec.ts`
- Create: `src/frontend/tests/e2e/user-management-golden.spec.ts`
- Modify: `src/frontend/tests/e2e/identity-pages.spec.ts`
- Modify: `src/frontend/tests/e2e/visual-matrix.spec.ts`
- Update after human review: `src/frontend/tests/e2e/snapshots/visual-matrix.spec.ts/*`

- [ ] 先扩展组件测试，覆盖中文/英文、无权限、空数据、加载、业务错误、网络错误、窄窗口、键盘焦点、行操作溢出、表格偏好、服务端导出和全部既有 CRUD。
- [ ] 页面固定为 `AppPage.header → AppQueryPanel → AppDataTable card → Drawer/Dialog`；主操作只在 PageHeader 或 table business-actions，删除重复 Card/刷新/标题，不改业务规则。
- [ ] 保留原型已确认的工具栏顺序、表头独立筛选行、日期范围、选择摘要、多字段分组；原型目录只读。
- [ ] 在 `zh-CN/en-US`、三套现有主题、明/暗、舒适/紧凑、1280×720 和 1440×900 生成候选截图；逐张人工检查后才更新基线，不以“测试生成成功”替代视觉验收。
- [ ] 黄金页达到稳定 80 分基线后冻结；其他页面只能继承契约，不再单独重新设计。
- [ ] 运行：

```powershell
cd src/frontend
pnpm.cmd test:unit -- tests/components/IdentityUsersPage.spec.ts tests/components/AppPage.spec.ts tests/components/AppQueryPanel.spec.ts tests/components/AppDataTable.spec.ts
pnpm.cmd exec playwright test --config=playwright.config.ts tests/e2e/user-management-golden.spec.ts
pnpm.cmd exec playwright test --config=playwright.real.config.ts tests/e2e/identity-pages.spec.ts
```

- [ ] Commit：`feat(frontend): freeze the user management golden sample`。

### Task 13: 补齐权限目录一致性、SystemData 操作 Gate 和稳定错误码

**Files:**

- Modify: `src/frontend/src/permissions/catalog.ts`
- Create: `src/frontend/src/permissions/catalog.generated.ts`
- Modify: `src/frontend/src/permissions/PermissionGate.vue`
- Modify: `src/frontend/src/pages/public/ForbiddenPage.vue`
- Modify: `src/frontend/src/components/systemData/OrganizationsAdminPage.vue`
- Modify: `src/frontend/src/components/systemData/AssignmentsAdminPage.vue`
- Modify: `src/frontend/src/components/systemData/NavigationAdminPage.vue`
- Modify: `src/frontend/src/components/systemData/FeaturesAdminPage.vue`
- Modify: `src/frontend/src/components/systemData/ServicesAdminPage.vue`
- Modify: `src/frontend/src/components/systemData/ThemesAdminPage.vue`
- Modify: `src/frontend/src/components/systemData/ServiceInitializationAdminPage.vue`
- Modify: `src/backend/src/Services/Identity/IndustrialPlatform.Identity.Domain/Permissions/PermissionCatalog.cs`
- Modify: `src/backend/src/Services/Identity/IndustrialPlatform.Identity.Application/Bootstrap/BootstrapSeedCatalog.cs`
- Modify: `src/backend/src/Services/Identity/IndustrialPlatform.Identity.Api/Authorization/PermissionPolicies.cs`
- Modify: `src/backend/src/Services/SystemData/IndustrialPlatform.SystemData.Api/Authorization/SystemDataPermissionPolicies.cs`
- Modify: `src/backend/src/BuildingBlocks/IndustrialPlatform.Web/Results/ApiResult.cs`
- Modify: `src/backend/src/BuildingBlocks/IndustrialPlatform.Web/Results/ApiResultOfT.cs`
- Modify: `src/backend/src/BuildingBlocks/IndustrialPlatform.Web/Middleware/ExceptionMiddleware.cs`
- Create: `src/backend/src/BuildingBlocks/IndustrialPlatform.Web/Errors/ApiErrorDescriptor.cs`
- Modify: `src/frontend/src/api/httpClient.ts`
- Create: `src/frontend/tests/components/PermissionGate.spec.ts`
- Create: `src/frontend/tests/unit/permissionCatalog.spec.ts`
- Create: `src/frontend/tests/components/SystemDataActionPermissions.spec.ts`
- Modify: `tests/Identity/IndustrialPlatform.Identity.Tests/Domain_PermissionCatalogTests.cs`
- Modify: `tests/Identity/IndustrialPlatform.Identity.Tests/Api_PermissionAuthorizationTests.cs`
- Modify: `tests/SystemData/IndustrialPlatform.SystemData.Tests/Authorization_SystemDataPermissionAuthorizationHandlerTests.cs`
- Create: `tests/SystemData/IndustrialPlatform.SystemData.Tests/PermissionCatalogContractTests.cs`
- Modify: `tests/BuildingBlocks/IndustrialPlatform.BuildingBlocks.Tests/ExceptionTests.cs`

- [ ] 先写目录契约测试，比对 Identity 权威目录、Bootstrap seed、Identity policies、SystemData manifest/policies 和前端生成目录；服务之间不新增项目引用，测试/生成步骤在边界外比对稳定 NId。
- [ ] 将 `platform.operation.view` 纳入权威目录、seed、前端生成目录和路由契约；保留 `platform.home.view` 的管理模式含义，不用破坏性重命名迁移既有权限。
- [ ] 前端补齐 organization create/update/move/status、position create/update/status、assignment manage、navigation manage/publish/rollback、feature/service/theme manage、service initialization register/plan/apply/approve/backup/cancel gate。
- [ ] 每个按钮在无权时隐藏/禁用；直接 API 仍由既有 handler 返回 403。功能权限、操作权限和数据范围不得合成一个布尔值。
- [ ] `ApiResult` 只增加向后兼容的可选 `parameters` 和 `traceId`，并用 `JsonIgnoreCondition.WhenWritingNull` 保持既有成功响应不新增 null 字段；新查询错误使用 `PLATFORM_QUERY_INVALID`、`PLATFORM_QUERY_OPTION_NOT_ALLOWED`、`PLATFORM_QUERY_FIELD_NOT_ALLOWED`、`PLATFORM_QUERY_LIMIT_EXCEEDED`、`PLATFORM_QUERY_PAGING_ALIGNMENT_REQUIRED`。前端优先按 code 翻译，后端 message 仅 fallback。
- [ ] 不在本轮大规模重写所有服务异常；只集中新查询链和触及页面的既有通用错误，保持未迁移端点 JSON 兼容。
- [ ] 运行：

```powershell
dotnet test tests/BuildingBlocks/IndustrialPlatform.BuildingBlocks.Tests/IndustrialPlatform.BuildingBlocks.Tests.csproj --configuration Release
dotnet test tests/Identity/IndustrialPlatform.Identity.Tests/IndustrialPlatform.Identity.Tests.csproj --configuration Release
dotnet test tests/SystemData/IndustrialPlatform.SystemData.Tests/IndustrialPlatform.SystemData.Tests.csproj --configuration Release
cd src/frontend
pnpm.cmd test:unit -- tests/components/PermissionGate.spec.ts tests/unit/permissionCatalog.spec.ts tests/components/SystemDataActionPermissions.spec.ts tests/components/SystemDataAdminPage.spec.ts
```

- [ ] Commit：`feat(platform): align permissions and localized errors`。

### Task 14: 迁移本轮公共文案并形成能力边界规范

**Files:**

- Modify: `src/frontend/src/components/shell/*.vue`
- Modify: `src/frontend/src/components/base/AppPage.vue`
- Modify: `src/frontend/src/components/management/AppQueryPanel.vue`
- Modify: `src/frontend/src/components/management/AppDataTable.vue`
- Modify: `src/frontend/src/pages/pc/identity/IdentityUsersPage.vue`
- Modify: `src/frontend/src/components/systemData/SystemDataAdminFrame.vue`
- Modify: `src/frontend/src/components/systemData/*AdminPage.vue`
- Create: `docs/frontend/platform-shell-and-list-page-standard.md`
- Create: `docs/architecture/capability-delivery-boundaries.md`
- Create: `docs/architecture/pf04-redecision-gate.md`
- Create: `src/frontend/tests/unit/noHardcodedPlatformCopy.spec.ts`

- [ ] Shell、导航、Tabs、AppPage、QueryPanel、AppDataTable、黄金页和本轮触及的 SystemData 公共文案改用稳定 key；测试允许业务示例数据和 `fallbackLabel`，禁止新增硬编码按钮/状态/错误文案。
- [ ] 文档固定 Logo、Header 三段、96/208 导航、三态、PC 管理/生产双模式、生产宫格及待实现规则、Tabs/PageHeader、唯一管理黄金样板、查询/表格/OData、权限、可访问性和新页面准入规则。
- [ ] 能力文档把每项能力标为 NuGet/BuildingBlock、前端组件/模块、可嵌入领域模块、独立服务或独立产品，并写清必需/可选/可替换依赖、数据所有权、健康和独立启动验收。
- [ ] PF-04 门禁只写 Audit/File/Notification 的暂定边界和实施前 Build/Adopt/Wrap/Skip 决策步骤，不安装依赖、不创建生产接口、不做未来实现承诺。
- [ ] `rg` 检查业务页无直接 `vxe-table`、无 PF-04+ 新代码、无空领域菜单、无 `TBD/TODO` 交付占位。
- [ ] 运行定向资源键测试、文档链接检查和 `git diff --check`。
- [ ] Commit：`docs: define reusable platform capability boundaries`。

### Task 15: 开发者完整自验证和交付封存

**Files:**

- Create: `docs/evidence/pf03-pre-platform-convergence-development-verification-2026-08-30.md`
- Modify only after evidence: `docs/superpowers/plans/2026-08-30-pf03-pre-platform-convergence-feature-development.md`

- [ ] 先停止自己启动的 Vite/UnifiedHost/testhost 进程；确认没有锁定 `bin/obj/node_modules/.tmp`。源码已变更时必须 fresh Release build，不能用旧 `--no-build` 结果代替。
- [ ] 后端标准门禁：

```powershell
dotnet build src/backend/IndustrialPlatform.slnx --configuration Release
dotnet test src/backend/IndustrialPlatform.slnx --configuration Release --no-build
```

- [ ] 前端标准门禁：

```powershell
cd src/frontend
pnpm.cmd lint
pnpm.cmd typecheck
pnpm.cmd test:unit
pnpm.cmd build
```

- [ ] Mock/视觉目标门禁：

```powershell
pnpm.cmd exec playwright test --config=playwright.config.ts tests/e2e/pc-shell.spec.ts tests/e2e/pc-operation-mode.spec.ts tests/e2e/workspace-tabs.spec.ts tests/e2e/user-management-golden.spec.ts tests/e2e/systemdata-admin.spec.ts
```

- [ ] 启动真实 UnifiedHost 后运行 Identity Users OData 与权限代表路径；优先使用仓库现有 real/unified config。记录登录主体、端点、HTTP 状态、测试数量、退出码和日志路径。外部 PostgreSQL/Redis/RabbitMQ 不可达时分别记录 DNS/TCP/端口证据，不能写成通过。
- [ ] 人工逐张复核最终 1280×720、1440×900、中英文、明暗、三态管理导航、生产操作宫格和黄金页截图；生产宫格需与用户参考图并排核对布局思路，同时确认八个待实现项无导航/网络请求。记录可访问性键盘路径、控制台错误和网络错误。
- [ ] 运行静态边界检查：

```powershell
rg -n "from ['\"]vxe-table|from ['\"]xe-utils" src/frontend/src --glob "!components/management/AppDataTable.vue" --glob "!app/createIndustrialApp.ts"
rg -n "Audit|File|Notification|ReferenceData|MES" src/backend/src src/frontend/src
git diff --check
git status --short
```

- [ ] 核对 `DSH.md` 哈希未因本任务改变；检查 staged/unstaged/untracked/ignored，移除本任务生成的报告、截图候选、缓存和构建产物，只保留经确认基线与证据。
- [ ] 证据文档按门禁列出命令、开始/结束时间、退出码、passed/failed/skipped、截图、外部缺口和未解决项。计划只有在对应证据确实通过后才勾选。
- [ ] 提交最终证据和计划状态，不 push：

```powershell
git add docs/evidence/pf03-pre-platform-convergence-development-verification-2026-08-30.md docs/superpowers/plans/2026-08-30-pf03-pre-platform-convergence-feature-development.md
git commit -m "test: record PF03 pre-convergence self-verification"
```

- [ ] 向独立验收任务和当前任务报告：绝对工作树、分支、启动基线、最终 commit、提交列表、`git status`、每个门禁结果、真实环境缺口和明确未触碰的 PF-03/PF-04+ 范围。不得只说“全部通过”。

## Completion Contract

开发任务只有同时满足以下条件才可声明可验收：Task 1～14 的生产/测试/文档完成且对应定向验证通过；Task 15 的 fresh 后端与前端门禁执行；目标 Playwright 和视觉证据可审查；真实路径通过或有可复现的外部阻塞证据；没有范围外代码、生成物、`DSH.md`/原型修改或远端 push。独立验收任务会不信任本报告并重新执行验收。
