# 04-Industrial Platform视觉主题与平台外壳开发实施方案

# Industrial Platform 视觉主题与平台外壳开发实施方案

> 当前里程碑范围：在统一前端第一批真实代码上交付三套配色、明暗／系统模式、无闪烁恢复、PC 新平台外壳、受控业务标签、PDA/Mobile 终端专属编排和通用管理组件；不实现 SystemData 或其他后续模块的领域/API，Identity 稳定前只完成隔离开发与测试。

版本：V1.0

阶段状态：开发详细设计、任务依赖和七张九字段任务卡已获用户批准；当前为“待派遣（仅任务规划）”，尚未开发。除非用户主动明确要求修改代码，否则本会话不得派遣实际编码任务或修改 `src/**`。`TASK-PF01-007` 的最终联合验收仍等待 PF-00 恢复并稳定前端契约。

阶段：PF-01「视觉、主题与平台外壳」；与 PF-00 后半段可并行，最终集成验收等待 PF-00 稳定前端契约，完成后向 PF-02 SystemData、PF-03 ReferenceData 及后续平台模块交付统一前端壳契约。

模块或服务：

```text
Industrial Platform Unified Frontend / Platform Shell
```

技术：

```text
Vue 3.5 + TypeScript 5.9（strict）
Vite 8 + Pinia 3 + Vue Router 4
Element Plus 2.14 + @element-plus/icons-vue 2.3.2
Vitest 4 + Vue Test Utils + Playwright 1.62
CSS Custom Properties / localStorage / matchMedia
```

规格与蓝图依据：

- `docs/blueprint/01-Industrial Platform 总体架构设计 V1.0.md`
- `docs/blueprint/04-Vue3 PCPDAMobile 三端统一架构设计.md`
- `docs/blueprint/05-Industrial Platform平台基础功能与独立模块设计.md`
- `docs/blueprint/09-Industrial Platform开发总TodoList.md`
- `docs/blueprint/28-Industrial Platform前端工程规范.md`
- `docs/superpowers/specs/2026-08-11-pf-01-visual-theme-platform-shell-design.md`
- `docs/implementation/02B-Industrial Platform统一前端第一批开发实施方案.md`
- `docs/implementation/03-Industrial Platform Identity Service开发实施方案.md`

---

# 1. 文档说明

## 1.1 文档目的

本文把已批准 PF-01 阶段规格转换为可直接派遣、独立测试、独立提交和持续回写的开发详细设计。目标读者包括 PF-01 阶段管理会话、被派遣的前端开发任务、Identity 后续前端任务以及 PF-02 以后所有页面开发任务。

本文同时是 PF-01：

- 开发详细设计唯一维护源；
- 任务依赖与九字段任务卡唯一维护源；
- 派遣状态、提交、验证证据和设计偏差回写源；
- 阶段完成后向后续模块输出稳定前端契约的记录源。

阶段管理会话不直接编写业务代码。实际实现由它派遣的任务执行；实现发现本文与当前代码发生具体冲突时，任务状态改为“设计待确认”并回到本会话处理。

## 1.2 当前输入状态

截至 2026-08-11，本方案以提交 `f07977f` 之后的仓库为基线。

已完成的前端历史能力：

- `TASK-FE-001～010` 已完成，现有工程为 Vue 3 + TypeScript + Vite 单包应用。
- `createIndustrialApp()`、Pinia、Router、Element Plus、统一 HTTP/错误层和 `AuthGateway` 边界已存在。
- PC、PDA、Mobile 三个父路由和独立布局已存在。
- PDA 48px、Mobile 44px、安全区、键盘路径、无横向滚动和六视口截图已有历史测试。
- `AppPage`、`AppEmptyState`、`AppErrorAlert`、`MockModeBanner` 已存在。
- 第一批历史证据来自 2026-08-10 的实施 02B：unit 212/212、E2E 35/35、覆盖率四项均高于 90%；该记录不是本轮新鲜验证。

当前尚未实现：

- `tokens.css` 仍固定科技蓝并声明 `color-scheme: light`，没有三配色、暗色、系统模式和密度状态。
- 没有版本化 UI 偏好快照、用户隔离、挂载前主题恢复或系统偏好监听。
- PC 仍为 56px 白色顶栏 + 240/64px 单侧栏，没有 52px 品牌顶栏、52px 工具轨、216px 功能树和 36px 业务标签栏。
- 没有最多 12 个业务标签、上限阻断、关闭／复用和权限恢复治理。
- PDA 480px 显式路由虽然不会被守卫重写，但页面和顶栏仍读取 `deviceStore.terminal`，会显示 Mobile；路由终端与展示终端仍有双事实源。
- 通用查询区、树表、表单抽屉、加载、无权限和降级状态尚未形成稳定组件契约。
- 当前截图使用 `page.screenshot()` 产出证据，没有 `toHaveScreenshot()` 像素回归断言和主题矩阵。

并行工作保护：

- PF-00 已暂停在 `TASK-ID-007`，现有 Identity 提交和未完成范围仍归 PF-00；PF-01 不得触碰、暂存或回退 Identity 后端、契约、测试或相关包配置。
- `docs/implementation/15-Industrial Platform MasterData Service开发实施方案.md` 存在用户未提交改写；PF-01 不得触碰、暂存或回退。
- PF-01 代码任务只修改 `src/frontend` 内批准范围；文档任务只暂存实施 04 及必要的实施索引、总路线和自身执行记录。

## 1.3 执行前置

```text
TASK-FE-001～010（已完成历史基线）
          ↓
批准的 PF-01 阶段规格
          ↓
TASK-PF01-001～006（可与 PF-00 后半段隔离执行）
          ↓
PF-00 TASK-ID-010～012 稳定前端契约
          ↓
TASK-PF01-007 联合集成验收
          ↓
PF-02 SystemData / PF-03 ReferenceData 页面开发
```

PF-00 未稳定不阻止 PF-01 的 Token、布局、组件和隔离自动化开发，但阻止 `TASK-PF01-007` 完成和 PF-01 阶段标记“已完成”。

---

# 2. 定位、目标与职责边界

## 2.1 负责

- Foundation、Semantic、Component、State 四层 Design Token。
- 工业青、科技蓝、中性灰三套配色与明亮、暗色、跟随系统模式。
- PC 舒适／紧凑密度，以及 PDA 48px、Mobile 44px 触控下限。
- 版本化 UI 偏好、本地用户命名空间、挂载前恢复和系统模式监听。
- PC 全宽顶栏、工具轨、功能树、业务标签和内容区。
- PDA/Mobile 对统一主题和页面状态的消费，以及显式路由终端权威。
- 查询区、树表、表单抽屉、加载、空态、错误、无权限和降级状态组件。
- 主题／密度／三端视觉回归、对比度、键盘、缩放和无横向滚动验收。
- 为后续模块提供授权导航视图、页面壳和通用管理组件契约。

## 2.2 不负责

| 场景 | PF-01 负责 | 相邻阶段负责 |
| --- | --- | --- |
| 用户与权限 | 消费 `AuthUser` 和 `hasPermission()`，按结果渲染 | Identity 定义身份、权限目录、会话、API 和刷新逻辑 |
| 导航 | 定义前端渲染视图和静态适配器 | SystemData 定义菜单领域、终端可见性、功能开关和租户默认主题来源 |
| 管理页面 | 提供通用壳、树表、查询、表单和抽屉组件 | 各模块定义字段、用例、API、权限和业务规则 |
| 顶栏全局槽 | 提供布局、溢出和可访问性槽位 | Search、Notification、Collaboration 等模块提供真实数据与交互 |
| 主题 | 三套内置主题、模式、密度与本地偏好 | SystemData 后续提供租户默认值和可选范围；不覆盖用户显式偏好 |
| 数据展示 | 真实状态、明确空态和开发期视觉基线 | Workspace Query 等后续模块提供真实摘要；禁止装饰性假 KPI |

本阶段明确不建设主题设计器、任意色板、业务表单设计器、看板、报表、SystemData 领域/API、Notification/Collaboration 数据或 Identity 管理业务页面。

---

# 3. 前后端及跨服务协作目标

PF-01 是前端平台能力，不新增后端领域、数据库、API 或事件。纵向交付链为：

```text
主题／外壳核心组件
    ↓
Pinia 偏好状态 + Router 外壳治理
    ↓
PC/PDA/Mobile 布局与通用管理组件
    ↓
Identity 页面消费 PF-01 契约（由 TASK-ID-010～012 实现）
    ↓
组件测试 + 视觉契约 + 关键路径 E2E
    ↓
PF-01 / PF-00 联合验收
```

协作规则：

- PF-01 不创建 Identity API DTO，不修改 token、刷新、注销和权限算法。
- Identity 前端任务不复制主题变量、布局或管理组件；必须消费 PF-01 稳定出口。
- SystemData 未实现前使用明确的产品默认值和静态授权导航适配器，不创建假远端 API。
- 后续模块页面只通过路由 meta、授权导航视图和通用组件进入外壳，不直接修改平台布局内部状态。

---

# 4. 总体架构与数据流

```text
index.html 同步预挂载脚本
  └─ localStorage: industrial-platform.ui.bootstrap.v1
       └─ <html data-ip-palette data-ip-color-mode data-ip-density>
                           ↓
createIndustrialApp()
  ├─ Pinia
  │   ├─ AuthStore（Identity 所有）
  │   ├─ ThemeStore（PF-01 所有）
  │   ├─ DeviceStore（现有，PF-01 校准展示事实源）
  │   └─ WorkspaceTabsStore（PF-01 所有）
  ├─ Router Guard
  │   ├─ 恢复 AuthSession
  │   ├─ 绑定用户 UI 偏好命名空间
  │   ├─ 权限判断
  │   ├─ 业务标签上限前置判断
  │   └─ 显式终端路由权威
  └─ RouterView
      ├─ PcLayout
      │   ├─ PlatformTopBar
      │   ├─ PlatformToolRail
      │   ├─ PlatformFunctionTree
      │   ├─ PcWorkspaceTabs
      │   └─ 内容区 / 通用管理组件
      ├─ PdaLayout
      └─ MobileLayout
```

数据权威和事务边界：

- 配色、明暗、密度和功能树状态的本地事实源为用户命名空间 UI 偏好快照。
- 首帧只读取不含用户身份的设备级 bootstrap 外观快照；进入受保护外壳前再原子合并用户偏好。
- 业务标签使用独立用户命名空间快照，不参与首帧主题恢复。
- localStorage 单次写入即为本阶段持久化边界；无数据库事务、Outbox 或跨服务事件。
- `matchMedia('(prefers-color-scheme: dark)')` 只解析有效明暗，不覆盖用户保存的 `mode: 'system'`。

---

# 5. 项目结构与引用关系

目标结构在现有 `src/frontend` 上增量演进：

```text
src/frontend
├── index.html                                  # 同步预挂载主题恢复
├── package.json / pnpm-lock.yaml               # 显式声明已有图标依赖
├── src
│   ├── app/createIndustrialApp.ts              # 安装主题状态，不改变认证所有权
│   ├── theme
│   │   ├── types.ts                            # 主题、模式、密度和偏好类型
│   │   ├── defaults.ts                         # 产品默认值和枚举白名单
│   │   ├── preferences.ts                      # 版本化解析、用户键和安全回退
│   │   ├── resolver.ts                         # 系统模式、优先级和 DOM 属性解析
│   │   ├── contrast.ts                         # WCAG 对比度纯函数
│   │   └── index.ts                            # 稳定公共出口
│   ├── stores
│   │   ├── themeStore.ts                       # 当前偏好、系统监听和用户绑定
│   │   └── workspaceTabsStore.ts               # 12 标签治理和独立持久化
│   ├── workspace
│   │   ├── types.ts                            # WorkspaceTab / OpenTabResult
│   │   ├── identity.ts                         # 稳定页面身份和可持久化路由
│   │   └── persistence.ts                      # 标签快照解析与用户键
│   ├── components
│   │   ├── theme/ThemeControl.vue
│   │   ├── shell/PlatformTopBar.vue
│   │   ├── shell/PlatformToolRail.vue
│   │   ├── shell/PlatformFunctionTree.vue
│   │   ├── shell/PcWorkspaceTabs.vue
│   │   ├── shell/WorkspaceTabLimitDialog.vue
│   │   ├── management/AppQueryPanel.vue
│   │   ├── management/AppTreeTableLayout.vue
│   │   ├── management/AppFormDrawer.vue
│   │   └── base/AppLoadingState.vue
│   │       base/AppPermissionState.vue
│   │       base/AppDegradedState.vue
│   ├── components/navigation
│   │   ├── types.ts                            # 分组 + 功能树授权视图
│   │   └── navigation.ts                       # 当前静态产品适配器
│   ├── device/activeTerminal.ts                # 显式路由终端优先的展示解析
│   ├── layouts/PcLayout.vue
│   │   layouts/PdaLayout.vue
│   │   layouts/MobileLayout.vue
│   ├── pages/dev/UiBaselinePage.vue            # 仅 DEV/E2E 注册的视觉基线页
│   ├── router/meta.ts / routes.ts / guards.ts
│   └── styles
│       ├── foundation.css
│       ├── themes.css
│       ├── density.css
│       ├── element-plus.css
│       ├── tokens.css                          # 兼容出口
│       └── base.css
└── tests
    ├── unit/theme*.spec.ts / workspaceTabsStore.spec.ts / activeTerminal.spec.ts
    ├── components/ThemeControl.spec.ts / management/*.spec.ts / shell/*.spec.ts
    └── e2e/theme.spec.ts / workspace-tabs.spec.ts / visual-matrix.spec.ts
        └── snapshots/**                        # Playwright 像素基线
```

引用方向：

```text
Page/Layout → Store/Component → theme|workspace 纯模块
Router Guard → AuthStore + ThemeStore + WorkspaceTabsStore
ThemeStore → theme 纯模块 + localStorage + matchMedia
WorkspaceTabsStore → workspace 纯模块 + localStorage
```

禁止方向：

- `theme/**`、`workspace/**` 不引用 Vue 组件、Router 实例或业务模块。
- 页面和布局不得直接解析偏好 JSON 或拼接存储键。
- 通用组件不得引用 Identity、SystemData 或业务 API。
- PF-01 不修改 `src/backend/**`、`tests/Identity/**` 或 `Directory.Packages.props`。

---

# 6. 全局技术与实施约束

- 使用现有 Node 24.18.0、pnpm 11.16.0 和锁文件；除将锁文件中已有的 `@element-plus/icons-vue@2.3.2` 声明为直接依赖外，不新增 UI 框架。
- TypeScript 保持 `strict`、`noUncheckedIndexedAccess`、`exactOptionalPropertyTypes` 和 `noUnused*`。
- 组件使用 `<script setup lang="ts">`；状态放 Pinia，纯解析放无 Vue 依赖模块。
- 稳定存储键带版本，非法 JSON、未知版本、非法枚举和存储异常全部安全回退，不阻断启动。
- 所有用户可见颜色由语义 Token 产生；组件禁止新增品牌色魔法值。
- 普通文字对比度不低于 4.5:1，关键非文字控件不低于 3:1。
- PC 顶栏 52px、工具轨 52px、功能树 216px、业务标签 36px；密度不得改变这四项。
- PDA 可操作目标不小于 48×48px；Mobile 不小于 44×44px。
- 工作台固定且不计入配额；业务标签最多 12 个，第 13 个必须在导航前被阻止。
- 显式终端路由是布局和终端文案事实源；视口变化不得重写显式路由。
- 禁止假 KPI、假通知、假聊天、假菜单 API 和未标识 Mock 数据。
- 采用 TDD：先写失败测试并记录失败原因，再实现最小代码并运行对应测试。
- 每个派遣任务只提交自身允许范围；不得使用 `git add .`。

## 6.1 数据建模适用性

PF-01 不使用数据库、不创建领域实体或后端表，因此模板的 `NId`、Entity 生命周期、复合外键、软删除、并发版本、Outbox/Inbox 和 PostgreSQL 物理列规则均不适用。

本地 UI 快照不是业务实体，不分配 `NId`。时间字段使用 ISO 8601 字符串并由 `new Date(now).toISOString()` 生成；它只用于偏好迁移诊断，不参与业务排序或授权。

任务状态统一为：

```text
待细化 → 可派遣 → 已派遣 → 开发中 → 待验收 → 已完成
```

发现设计与当前代码冲突时统一改为：

```text
设计待确认
```

---

# 7. 核心组件详细设计

## 7.1 主题类型与产品默认值

`src/theme/types.ts` 定义稳定类型：

```ts
export type ThemePalette = 'industrial-cyan' | 'technology-blue' | 'neutral-gray'
export type ThemeMode = 'light' | 'dark' | 'system'
export type EffectiveColorMode = 'light' | 'dark'
export type PcDensity = 'comfortable' | 'compact'

export interface UiPreferencesV1 {
  version: 1
  palette: ThemePalette
  mode: ThemeMode
  density: PcDensity
  pcFunctionTreeCollapsed: boolean
  updatedAt: string
}

export interface UserUiScope {
  tenantId: string
  userId: string
}

export interface ResolvedUiAppearance {
  palette: ThemePalette
  mode: ThemeMode
  effectiveColorMode: EffectiveColorMode
  density: PcDensity
}
```

产品默认值固定为：

```ts
export const DEFAULT_UI_PREFERENCES: Readonly<UiPreferencesV1> = {
  version: 1,
  palette: 'industrial-cyan',
  mode: 'system',
  density: 'comfortable',
  pcFunctionTreeCollapsed: false,
  updatedAt: '1970-01-01T00:00:00.000Z',
}
```

默认对象不得在运行时直接修改。写入时生成新对象和真实 `updatedAt`。

## 7.2 版本化偏好、命名空间与优先级

稳定存储键：

```text
industrial-platform.ui.bootstrap.v1
industrial-platform.ui.preferences.v1:<encodeURIComponent(tenantId)>:<encodeURIComponent(userId)>
industrial-platform.pc.tabs.v1:<encodeURIComponent(tenantId)>:<encodeURIComponent(userId)>
industrial-platform.terminal.override.v1             # 保留第一批设备级稳定键
industrial-platform.pc.sidebar.collapsed.v1          # 仅作为一次迁移输入
```

- bootstrap 快照只保存 `palette/mode/density/version`，不保存用户、租户、Token、权限或页面历史。
- 用户快照按 `tenantId + userId` 隔离；标识只出现在键中，不重复进入 JSON 值。
- 用户首次绑定时，优先读取用户快照；没有快照时使用未来租户默认值；当前默认来源返回空对象；最后补产品默认值。
- 用户显式保存后同步更新用户快照和 bootstrap 外观快照。
- 旧 `pc.sidebar.collapsed.v1` 只在用户无新快照时迁移；成功写入新快照后删除旧键，失败则保留并使用产品默认值。
- localStorage 不可用、QuotaExceeded、SecurityError 或 JSON 损坏时返回内存默认值，不抛到页面。

稳定纯函数：

```ts
export function parseUiPreferences(raw: string | null): UiPreferencesV1 | null
export function buildUserUiPreferenceKey(scope: UserUiScope): string
export function mergeUiPreferences(
  productDefaults: UiPreferencesV1,
  tenantDefaults: Partial<Omit<UiPreferencesV1, 'version' | 'updatedAt'>>,
  userPreferences: UiPreferencesV1 | null,
  now: () => number,
): UiPreferencesV1
```

`mergeUiPreferences` 的字段优先级必须逐字段执行：用户显式值 ＞ 租户默认值 ＞ 产品默认值。租户默认来源在本阶段为前端空适配器，不发网络请求。

## 7.3 系统模式与 DOM 应用

稳定解析和应用接口：

```ts
export function resolveEffectiveColorMode(
  mode: ThemeMode,
  systemPrefersDark: boolean,
): EffectiveColorMode

export function applyAppearanceToRoot(
  root: HTMLElement,
  appearance: ResolvedUiAppearance,
): void
```

根节点属性固定为：

```html
<html
  data-ip-palette="industrial-cyan"
  data-ip-theme-mode="system"
  data-ip-color-mode="light"
  data-ip-density="comfortable"
>
```

`applyAppearanceToRoot()` 同时设置 `root.style.colorScheme` 为有效 `light` 或 `dark`。组件只匹配 `data-ip-palette`、`data-ip-color-mode`、`data-ip-density`，不得直接匹配系统媒体查询决定品牌主题。

`index.html` 的 `<head>` 在加载应用模块前执行同步、无依赖、异常吞吐的 bootstrap 脚本：读取 bootstrap 键、校验四个枚举、解析 `matchMedia`、设置根属性。脚本不得读取认证会话或用户键，也不得记录存储内容。

## 7.4 ThemeStore

`useThemeStore` 稳定出口：

```ts
export interface TenantUiDefaultsSource {
  load(scope: UserUiScope): Promise<Partial<Omit<UiPreferencesV1, 'version' | 'updatedAt'>>>
}

export function setTenantUiDefaultsSource(source: TenantUiDefaultsSource): void
export function getTenantUiDefaultsSource(): TenantUiDefaultsSource

export const useThemeStore = defineStore('theme', () => {
  // state: preferences, effectiveColorMode, scope, ready
  async function initialize(): Promise<void>
  async function bindUser(scope: UserUiScope): Promise<void>
  function setPalette(value: ThemePalette): void
  function setMode(value: ThemeMode): void
  function setDensity(value: PcDensity): void
  function setPcFunctionTreeCollapsed(value: boolean): void
  function dispose(): void
  return { /* state + methods */ }
})
```

- `initialize()` 幂等，只注册一个 `matchMedia` change 监听器。
- `bindUser()` 对同一 scope 幂等；切换用户时一次性替换完整偏好后再写 DOM。
- `set*()` 先更新状态与 DOM，再尽力持久化；存储失败不回滚用户可见选择。
- `dispose()` 只供测试和应用销毁，必须移除系统监听器。
- Router Guard 在 `authStore.restore()` 后、进入受保护布局前调用 `themeStore.initialize()`；有用户时调用 `bindUser({ tenantId, userId })`。
- 当前默认 source 的 `load()` 返回空对象；PF-02 只能通过 `setTenantUiDefaultsSource()` 安装适配器，不能绕过 ThemeStore 直接覆盖 DOM 或用户偏好。

## 7.5 CSS Token 映射

文件职责：

| 文件 | 内容 |
| --- | --- |
| `foundation.css` | 原始色板、字号、间距、圆角、阴影、层级和固定尺寸 |
| `themes.css` | 三配色、明暗中性色、状态色及语义 Token |
| `density.css` | 舒适／紧凑内容尺寸，PDA/Mobile 触控覆盖 |
| `element-plus.css` | `--el-*` 到 `--ip-*` 的桥接 |
| `tokens.css` | 按上述顺序导入，并保留第一批变量兼容别名 |

工业青顶栏固定使用已批准渐变：

```css
--ip-shell-topbar-background: linear-gradient(
  90deg,
  #006487 0%,
  #006b91 24%,
  #0077a1 58%,
  #158dac 82%,
  #087c9f 100%
);
```

科技蓝和中性灰按规格映射同一明暗节奏。普通按钮、表格、表单、抽屉和状态标签不得使用渐变。

第一批兼容变量，例如 `--ip-color-primary`、`--ip-color-bg-page`、`--ip-color-bg-container`、`--ip-color-border`，继续存在但只指向新语义 Token。新组件使用新语义名，旧页面可在独立任务中逐步迁移，不能同时维护两套颜色事实。

## 7.6 ThemeControl

`ThemeControl.vue` 是三端共享的用户入口：

```ts
defineProps<{
  terminal: 'pc' | 'pda' | 'mobile'
}>()
```

- PC 入口位于顶栏全局操作区，按钮最小 32px；PDA/Mobile 按各自 48px/44px 触控下限。
- 面板分别选择配色、明暗模式和 PC 密度；PDA/Mobile 隐藏密度选择但继续显示当前主题和模式。
- 所有选项使用原生可聚焦控件或 Element Plus 单选组，具有组名、选中状态和键盘操作。
- 主题切换即时生效；不发 API，不显示“已同步到租户”等虚假状态。

## 7.7 授权导航视图

导航只描述前端渲染输入，不冻结 SystemData DTO：

```ts
export interface NavigationItem {
  id: string
  label: string
  routeName: string
  icon?: Component
  permission?: string
  children?: readonly NavigationItem[]
}

export interface NavigationGroup {
  id: string
  label: string
  icon: Component
  items: readonly NavigationItem[]
}
```

- `PlatformToolRail` 渲染 `NavigationGroup[]`，宽 52px，管理当前分组。
- `PlatformFunctionTree` 只渲染当前组的授权 `items`，展开宽 216px，可完全收起。
- 当前静态适配器只注册真实存在的工作台路由，不注册 SystemData、通知、聊天等假入口。
- 图标使用 `@element-plus/icons-vue@2.3.2`，禁止 Emoji、文本符号和手绘占位图标。
- 权限过滤继续调用 `authStore.hasPermission()`；菜单隐藏不替代 Router Guard 授权。

## 7.8 PC 外壳组件

`PcLayout` 组合四层：

```text
PlatformTopBar 52px
└─ body
   ├─ PlatformToolRail 52px
   └─ function-and-workspace
      ├─ PlatformFunctionTree 216px / 0px
      └─ workspace
         ├─ PcWorkspaceTabs 36px
         └─ RouterView 内容画布
```

`PlatformTopBar` 提供 `brand`、`global-search`、`global-actions`、`user` 四个具名槽。PF-01 当前只填品牌、ThemeControl、Mock 标识和用户菜单；搜索、通知、协作槽为空时不渲染占位按钮。

顶栏文字和图标为白色，折叠按钮移动到功能树标题区。功能树收起状态由 `ThemeStore.pcFunctionTreeCollapsed` 持久化，不再直接读写旧侧栏键。

## 7.9 业务标签模型与路由治理

稳定类型：

```ts
export interface PersistedRouteLocation {
  name: string
  params: Record<string, string | string[]>
  query: Record<string, string | string[]>
}

export interface WorkspaceTab {
  id: string
  title: string
  kind: 'fixed' | 'business'
  route: PersistedRouteLocation
  reloadVersion: number
}

export type OpenTabResult =
  | { kind: 'opened'; tab: WorkspaceTab }
  | { kind: 'activated'; tab: WorkspaceTab }
  | { kind: 'limit-reached'; pending: PersistedRouteLocation }
  | { kind: 'ignored' }

export type TabLimitResolution =
  | { action: 'close-and-open'; tabId: string }
  | { action: 'reuse'; tabId: string }
  | { action: 'cancel' }

export interface WorkspaceRouteCandidate {
  id: string
  title: string
  kind: 'fixed' | 'business'
  route: PersistedRouteLocation
  permission?: string
}
```

Route Meta 增量字段：

```ts
export interface AppRouteMeta {
  title: string
  requiresAuth?: boolean
  permission?: string
  terminal?: TerminalType
  workspace?: 'fixed' | 'business' | 'none'
}
```

`useWorkspaceTabsStore` 稳定动作：

```ts
function bindUser(scope: UserUiScope): void
function requestOpen(candidate: WorkspaceRouteCandidate): OpenTabResult
function closeTab(tabId: string): WorkspaceTab
function closeOthers(tabId: string): void
function closeRight(tabId: string): void
function reloadCurrent(): void
function resolvePending(resolution: TabLimitResolution): PersistedRouteLocation | null
function prune(isAllowed: (tab: WorkspaceTab) => boolean): void
```

- `closeTab()` 返回关闭后应激活的标签；固定工作台传入时原样返回固定工作台且不删除。
- `resolvePending()` 在 close/reuse 成功时清空 pending 并返回需要导航的目标；取消返回 `null`。
- Router Guard 负责把 `RouteLocationNormalized` 归一化为 `WorkspaceRouteCandidate`；Store 不持有 Router 实例。

治理规则：

- `pc-home` 为固定工作台，`workspace: 'fixed'`；公共、PDA、Mobile 路由为 `none`。
- 业务标签 ID 由 `route.name + 排序后的 params + 排序后的 query` 生成；同一身份只激活不重复新增。
- Router Guard 在导航到 PC `workspace: 'business'` 前调用 `requestOpen()`；已达 12 个时返回 `false`，保存 pending route 并打开上限对话框。
- 上限对话框只允许“关闭所选后打开”“复用所选标签”“取消”；不自动驱逐旧标签。
- 关闭当前标签后导航到右邻、左邻或固定工作台；关闭其他／右侧不得关闭固定工作台。
- 恢复时只保留当前 Router 存在、`workspace: 'business'` 且用户仍有权限的路由；非法或未授权项丢弃并显示非阻断提示。
- `reloadCurrent()` 递增 `reloadVersion`；`PcLayout` 以 `route.fullPath + reloadVersion` 作为 RouterView 内容 key，不执行整页刷新。

## 7.10 通用管理组件

稳定组件契约：

```ts
// AppQueryPanel.vue
defineProps<{ title?: string; collapsible?: boolean; collapsed?: boolean }>()
defineEmits<{ 'update:collapsed': [value: boolean] }>()
// slots: default, actions

// AppTreeTableLayout.vue
defineProps<{ treeLabel: string; contentLabel: string; treeWidth?: 'narrow' | 'medium' }>()
// slots: tree, toolbar, default, pagination

// AppFormDrawer.vue
defineProps<{
  modelValue: boolean
  title: string
  size?: 'narrow' | 'medium' | 'wide'
  busy?: boolean
}>()
defineEmits<{
  'update:modelValue': [value: boolean]
  submit: []
  cancel: []
}>()
// slots: default, footer
```

- PC 抽屉宽度分别为 420px、560px、720px；PDA/Mobile 强制全宽。
- `AppFormDrawer` 使用 Element Plus focus trap；关闭后焦点返回触发点，busy 时阻止重复提交。
- `AppLoadingState` 使用 `role="status"` 和可读加载文案。
- `AppPermissionState` 使用无权限语义，不把 403 渲染为空数据。
- `AppDegradedState` 必须说明不可用能力与仍可继续的能力，并允许提供重试槽。
- `AppEmptyState`、`AppErrorAlert` 保留兼容，新增状态组件使用统一状态色和语义 Token。

## 7.11 PDA、Mobile 与路由终端权威

新增纯函数：

```ts
export function resolveActiveTerminal(
  routeTerminal: TerminalType | undefined,
  deviceTerminal: TerminalType,
): TerminalType {
  return routeTerminal ?? deviceTerminal
}
```

PDA/Mobile 布局和首页的终端文案统一消费 `route.meta.terminal` 解析结果，不再直接把 `deviceStore.terminal` 当作显式路由事实源。

- PDA 顶栏保持 48px，主题入口和所有按钮均至少 48×48px；功能导航以后使用覆盖层，不复制 PC 常驻树。
- Mobile 顶栏保持 44px，底部主导航不超过 5 个一级入口；当前仍只显示真实存在的“首页／我的”。
- PC 密度属性可存在于根节点，但 PDA/Mobile 控件使用终端触控 Token 覆盖，绝不降至 30/32px。
- 屏幕旋转只重排当前布局；不修改 URL、不调用 `setOverride()`、不触发跨终端跳转。

---

# 8. 数据与持久化设计

## 8.1 数据库

不适用。PF-01 不创建数据库、表、迁移、缓存服务器或消息存储。

## 8.2 本地快照

| 快照 | 存储 | 数据 | 清理与迁移 |
| --- | --- | --- | --- |
| Bootstrap 外观 | localStorage | version、palette、mode、density | 非法即忽略；用户保存时覆盖 |
| 用户 UI 偏好 | localStorage 用户键 | `UiPreferencesV1` | 未知版本忽略；旧侧栏键一次迁移 |
| PC 标签 | localStorage 用户键 | version、tabs、activeTabId、updatedAt | 恢复时路由与权限双校验，最多 12 个业务项 |
| 终端覆盖 | 现有 localStorage 稳定键 | pc/pda/mobile/auto | 保持第一批解析和非法回退 |

不得持久化：Access Token、Refresh Token、密码、角色／权限列表、页面响应数据、表单草稿或未来模块数据。

---

# 9. API、事件与外部集成契约

## 9.1 HTTP API

不适用。PF-01 不新增 Gateway 路径、后端端点、DTO、HTTP 状态或错误码。

未来 SystemData 只通过 `TenantUiDefaultsSource` 前端端口提供默认值。本文不定义它对应的服务名、URL、Request/Response 或缓存协议；这些由 PF-02 设计并适配，且不得改变 PF-01 已固定的优先级。

## 9.2 事件

不适用。PF-01 不发布或消费 RabbitMQ 事件。系统明暗变化来自浏览器 `matchMedia` 本地事件，不属于集成事件。

## 9.3 Identity 集成

- PF-01 只读取当前 `AuthUser.userId`、`tenantId` 和 permissions 形成本地用户作用域。
- PF-00 如果把字段改为 `userNId` 等稳定契约，`TASK-PF01-007` 只调整作用域映射适配点，不复制 Identity DTO。
- 401、刷新、注销、权限变化和真实登录继续归 `TASK-ID-010～012`。

---

# 10. 页面与交互设计

## 10.1 PC

- `/pc/home`：固定工作台，使用新顶栏、工具轨、功能树、标签和内容画布。
- `/pc/ui-baseline`：仅 `import.meta.env.DEV` 注册，用于通用组件和状态视觉回归；不出现在生产路由和导航。
- 顶栏在 1280px 不产生横向滚动；未来空槽不显示假按钮。
- 工具轨保持 52px；功能树收起后内容区获得 216px，但工具轨不消失。
- 标签达到 12 个时原页面保持可用，对话框聚焦并明确关闭／复用选择。

## 10.2 PDA

- `/pda/home` 在 480×800、800×480 都显示“终端 PDA”。
- 返回、首页、主题和退出操作满足 48px；横屏和竖屏无页面级横向滚动。
- 只显示真实空态，不增加扫码、称量、工单等不可用业务入口。

## 10.3 Mobile

- `/mobile/home`、`/mobile/my` 显式显示“终端 Mobile”。
- 顶栏操作和底部导航满足 44px，并继续消费 safe-area Token。
- 不复制 PC 标签栏；路由页面栈和首页／我的主导航保持现有语义。

## 10.4 主题交互

- 三端均可选择工业青、科技蓝、中性灰，以及明亮、暗色、跟随系统。
- PC 可选择舒适／紧凑；PDA/Mobile 不显示密度控件。
- 主题切换不弹成功 Toast；控件自身状态立即反映结果。
- 系统模式下 OS 改变时只更新有效明暗，选项仍显示“跟随系统”。

## 10.5 状态与可访问性

- Loading、Empty、Error、Permission、Degraded 五种状态具有不同语义和文案。
- 错误只有在真实 TraceId 存在时显示，不制造 TraceId。
- 跳到主内容入口继续是布局内第一个可聚焦元素。
- 主题控件、工具轨、功能树、标签菜单和上限对话框全部可用键盘完成。
- 200% 缩放不遮挡当前页面主要操作；`prefers-reduced-motion` 下取消非必要过渡。

---

# 11. 错误、安全、审计与可观测性

## 11.1 错误处理

| 场景 | 行为 |
| --- | --- |
| 偏好 JSON 损坏／未知版本 | 忽略并使用产品默认值，不阻断启动 |
| localStorage 不可用／写满 | 当前内存选择继续生效，不循环重试，不显示技术异常 |
| matchMedia 不可用 | system 解析为 light，保留用户 mode=system |
| 标签快照路由不存在／无权限 | 丢弃该项，显示非阻断提示并回到固定工作台 |
| 第 13 个业务标签 | 导航前阻断，显示关闭／复用对话框 |
| 主题 Token 缺失 | 自动化失败；不得在组件内用魔法值补救 |

## 11.2 安全与权限

- UI 偏好和标签快照不保存令牌、密码、权限列表或业务数据。
- 标签恢复必须重新经过 Router Guard；持久化路由不能绕过权限。
- 路由名称、params、query 只按 Router 已注册记录恢复，不执行任意 URL、脚本或外部重定向。
- ThemeControl 和外壳显示不是授权边界；权限仍由 Identity 和 Router Guard 判定。
- 日志不得输出 localStorage JSON、用户作用域完整键或标签 query 中的敏感值。

## 11.3 审计

PF-01 的本地主题、密度、折叠和标签操作不写业务审计。租户主题管理和管理员强制策略由未来 SystemData/Audit 设计；本阶段不伪造审计 API。

## 11.4 可观测性

- 不新增后台健康检查或指标。
- 浏览器控制台保持零 error、零未处理 Promise rejection。
- 开发模式可使用不含用户数据的结构化 debug 标记诊断非法快照；生产不输出快照内容。

---

# 12. 自动化测试与验收设计

## 12.1 测试层次

| 层次 | 适用内容 |
| --- | --- |
| Domain / Application / Infrastructure / API | 不适用；PF-01 无后端代码 |
| Unit | 偏好解析、优先级、系统模式、DOM 属性、对比度、路由身份、标签上限、终端权威 |
| Frontend Component | ThemeControl、管理组件、工具轨、功能树、标签栏、上限对话框、三端布局 |
| Contract | 根属性名、存储版本、Navigation/Route Meta/Workspace 类型和 Element Plus Token 映射 |
| E2E | 主题恢复、三端交互、12 标签、触控、键盘、缩放、无横向滚动、控制台和视觉回归 |

## 12.2 单元与组件场景

- 三套 palette、三种 mode、两种 density 的合法／非法解析。
- system 在 OS light/dark 下解析，监听器单例和 dispose。
- 用户、租户、产品默认值逐字段优先级；用户切换不串用。
- bootstrap 与用户快照损坏、未知版本、存储异常和旧侧栏迁移。
- 全部主题文字和状态色对比度；工业青渐变五个停靠点对白字均通过阈值。
- 标签去重、12 上限、pending、关闭当前／其他／右侧、复用、reloadVersion、恢复过滤。
- 显式 PDA/Mobile/PC 路由优先于 deviceStore 终端。
- 管理组件 slots、ARIA、focus trap、busy 防重和终端抽屉宽度。

## 12.3 E2E 与截图矩阵

固定视口：

```text
PC      1280×720 / 1440×900
PDA      480×800 / 800×480
Mobile   360×800 / 390×844
```

视觉基线：

- PC 核心外壳覆盖 `3 palette × 2 effective mode × 2 density = 12` 个状态。
- PDA 和 Mobile 覆盖三套配色的明亮／暗色；PDA 覆盖横竖屏，Mobile 覆盖两种目标宽度。
- system 分别模拟 OS light/dark，DOM 和截图结果必须等价于对应有效模式。
- `UiBaselinePage` 覆盖查询、树表、表单抽屉、Loading、Empty、Error、Permission、Degraded。
- Playwright 使用 `expect(page).toHaveScreenshot(..., { maxDiffPixelRatio: 0.01 })`；不能通过提高阈值掩盖结构变化。

关键行为：

- 首次脚本执行前设置暗色快照，首个页面绘制和 Vue 挂载后均为暗色，无明亮闪烁。
- PC 固定尺寸不随 density 变化；内容控件按舒适／紧凑变化。
- 第 13 个业务路由未进入，关闭或复用后才完成导航。
- `/pda/home` 在 480px 无覆盖键时仍显示 PDA；三端显式路由均不被宽度重写。
- PDA 48px、Mobile 44px、安全区、键盘路径、200% 缩放和无横向滚动通过。
- 登录→PC→PDA→Mobile→退出全程无 console/page error 和敏感日志。

## 12.4 稳定验证命令

每个任务按范围运行定向测试，最终统一运行：

```powershell
Set-Location 'src/frontend'
pnpm format:check
pnpm lint
pnpm typecheck
pnpm test:unit:coverage
pnpm build
pnpm test:e2e
```

证据必须记录命令、退出码、通过／失败／跳过数、四项覆盖率、构建警告、Playwright 报告路径、截图路径和外部限制。真实 safe-area 真机、PF-00 未稳定或客户浏览器环境缺失时标记“待验收”。

---

# 13. 开发任务依赖

```text
TASK-PF01-001 主题 Token、预挂载恢复与偏好状态
        ↓
TASK-PF01-002 主题入口与通用管理组件
        ├─────────────────────┐
        ↓                     ↓
TASK-PF01-003 PC 平台外壳   TASK-PF01-005 PDA/Mobile 外壳校准
        ↓                     │
TASK-PF01-004 受控业务标签    │
        └──────────┬──────────┘
                   ↓
TASK-PF01-006 视觉回归与可访问性验收
                   ↓
PF-00 TASK-ID-010 / 011 / 012 稳定前端契约
                   ↓
TASK-PF01-007 Identity 集成与阶段收口
```

并行与冲突规则：

- `TASK-PF01-003` 与 `TASK-PF01-005` 可并行；前者不修改 PDA/Mobile，后者不修改 PC/Router。
- `TASK-PF01-004` 在 003 后执行，因为两者共享 `PcLayout.vue` 和 PC shell。
- `TASK-PF01-006` 最后集中修改 `router/routes.ts`、Playwright 配置和截图基线，避免与 004/005 冲突。
- `TASK-PF01-007` 只在 Identity 前端任务稳定后执行，不修改 Identity 后端或 Identity-owned auth/API/page 文件。
- 任一任务发现并行 Identity 已修改同一前端文件时停止该文件修改，回到本阶段管理会话确认所有权；不得覆盖对方未提交内容。

---

# 14. 开发任务拆分

七项任务拆分已获用户批准，当前统一保持“待派遣（仅任务规划）”。这是 PF-01 的用户指定冻结状态：只允许继续完善蓝图、详细设计和任务规划，不代表已授权派遣或修改代码。只有用户后续主动明确要求修改代码时，阶段管理会话才能按依赖将满足前置的任务改为“可派遣”并另行执行派遣。

## TASK-PF01-001 主题Token、预挂载恢复与偏好状态

**状态：** 待派遣（仅任务规划）

**目标：** 建立三配色、明暗／系统模式、PC 密度、版本化用户偏好和首帧无闪烁恢复的稳定基础，使后续组件只消费统一语义 Token 与 ThemeStore。

**输入文档：** 本文第 4～9、11、12 节；已批准 PF-01 规格第 4、5、9、12～14 节；实施 02B 的应用装配、Token、AuthStore 和 Router Guard 基线。

**依赖：** `TASK-FE-001～010`（历史已完成）；无 PF-01 前置任务。

**允许修改范围：** 创建 `src/frontend/src/theme/**`、`src/frontend/src/stores/themeStore.ts`、`src/frontend/src/styles/{foundation,themes,density,element-plus}.css` 及对应 unit tests；修改 `src/frontend/index.html`、`src/frontend/src/styles/{tokens,base}.css`、`src/frontend/src/app/createIndustrialApp.ts`、`src/frontend/src/router/guards.ts` 和对应现有测试。禁止修改 `src/backend/**`、Identity auth/API 实现、`Directory.Packages.props` 和实施 15。

**预期输出：**

1. 先新增失败测试，精确覆盖 `parseUiPreferences()`、`mergeUiPreferences()`、`resolveEffectiveColorMode()`、`applyAppearanceToRoot()`、用户切换、监听器 dispose、存储异常和旧侧栏键迁移；失败原因必须是接口或行为尚不存在。
2. 按第 7.1～7.5 节创建类型和纯函数，不在 Store 中重复解析逻辑。
3. 在 `index.html` 添加同步 bootstrap 脚本，并用 E2E 可观察根属性验证它先于 Vue 应用状态生效。
4. 实现 `useThemeStore` 的幂等 initialize/bindUser/set/dispose，Router Guard 在权限和布局进入前完成用户偏好绑定。
5. 拆分四个 CSS 文件并保留第一批兼容变量；Element Plus 主色、表面、文字、边框和控件高度映射到语义 Token。
6. 运行定向 Vitest，确认失败测试转绿；再运行 format、lint、typecheck 和全部 unit，提交仅本任务文件。

**验证与证据：** `pnpm vitest run tests/unit/theme*.spec.ts tests/unit/themeStore.spec.ts tests/unit/routerGuards.spec.ts tests/unit/createIndustrialApp.spec.ts`；随后 `pnpm format:check && pnpm lint && pnpm typecheck && pnpm test:unit`。记录退出码、测试文件数、通过／失败／跳过数；提供 DOM 根属性与暗色 bootstrap 的测试证据，不以肉眼刷新替代。

**结果回写：** 回写最终存储键、版本、类型、根属性、产品默认值、系统监听器生命周期、旧侧栏迁移行为、测试数量、提交号和任何与规格的偏差到本文第 16、17 节。

**建议提交：** `feat(frontend): add theme tokens and pre-mount preferences`

---

## TASK-PF01-002 主题入口与通用管理组件

**状态：** 待派遣（仅任务规划）

**目标：** 提供三端共享 ThemeControl 以及查询、树表、表单抽屉、加载、无权限和降级状态组件，形成后续管理页面可直接消费的稳定契约。

**输入文档：** 本文第 2、3、7.6、7.10、10、11、12 节；已批准规格第 8、12～14 节；TASK-PF01-001 稳定 ThemeStore 和 Token。

**依赖：** TASK-PF01-001。

**允许修改范围：** 创建 `src/frontend/src/components/theme/**`、`src/frontend/src/components/management/**`、`src/frontend/src/components/base/{AppLoadingState,AppPermissionState,AppDegradedState}.vue` 及 `tests/components` 对应测试；按新语义 Token 最小修改现有 `AppPage`、`AppEmptyState`、`AppErrorAlert` 和测试。禁止创建业务页面、业务字段、后端 API、假 KPI 或 SystemData 模型。

**预期输出：**

1. 先为第 7.6、7.10 节全部 props、emits、slots、ARIA 和终端宽度写失败组件测试。
2. 实现 ThemeControl：PC 显示密度，PDA/Mobile 隐藏密度；选择变化调用 ThemeStore 稳定方法并保持键盘可达。
3. 实现 AppQueryPanel、AppTreeTableLayout、AppFormDrawer；抽屉 busy 防重、关闭返回焦点、三档 PC 宽度和小屏全宽必须有确定测试。
4. 实现 Loading、Permission、Degraded 三状态；保留 Empty/Error 兼容并迁移到新语义 Token。
5. 执行组件测试红绿循环，再运行全部 unit、lint 和 typecheck；禁止用快照替代交互断言。

**验证与证据：** `pnpm vitest run tests/components/ThemeControl.spec.ts tests/components/management/*.spec.ts tests/components/App*State.spec.ts tests/components/AppErrorAlert.spec.ts tests/components/AppPage.spec.ts`；随后 `pnpm lint && pnpm typecheck && pnpm test:unit`。记录通过数，并提供键盘、焦点返回、busy 防重和小屏抽屉宽度断言。

**结果回写：** 回写最终组件名、props、emits、slots、ARIA、抽屉尺寸、状态语义、测试数和提交号；后续模块只能依赖回写后的稳定接口。

**建议提交：** `feat(frontend): add theme controls and management primitives`

---

## TASK-PF01-003 实现PC顶栏、工具轨与功能树

**状态：** 待派遣（仅任务规划）

**目标：** 将现有 PC 单侧栏迁移为 52px 渐变顶栏、52px 固定工具轨、216px 可收起功能树和内容工作区，并保持认证、权限、跳过链接和退出路径不退化。

**输入文档：** 本文第 5～7.8、10.1、11、12 节；已批准规格第 4.1、5.5、6、11～14 节；TASK-PF01-001/002 输出。

**依赖：** TASK-PF01-001、TASK-PF01-002。

**允许修改范围：** 创建 `src/frontend/src/components/shell/{PlatformTopBar,PlatformToolRail,PlatformFunctionTree}.vue`；修改 `src/frontend/src/components/navigation/{types,navigation}.ts`、`src/frontend/src/layouts/PcLayout.vue`、`src/frontend/package.json`、`src/frontend/pnpm-lock.yaml` 和对应组件测试。只允许把锁文件已有的 `@element-plus/icons-vue@2.3.2` 声明为直接依赖；禁止修改 Router、PDA/Mobile、业务页面或后端文件。

**预期输出：**

1. 先更新 PcLayout 和新 shell 组件失败测试，断言四区结构、固定尺寸、授权过滤、当前组、功能树收起、具名槽、跳过链接首焦点和用户退出。
2. 将 Navigation 模型升级为第 7.7 节 `NavigationGroup`；静态适配器只保留真实 PC 工作台。
3. 使用正式 Element Plus 图标实现工具轨，删除 `PcNavMenu` 的圆点占位语义；旧组件无消费者后删除并同步测试。
4. 实现已批准工业青顶栏渐变，科技蓝／中性灰由 Token 切换；顶栏空的未来槽不得渲染假入口。
5. 将功能树收起状态接入 ThemeStore，验证旧键迁移由 TASK-PF01-001 负责且本组件不直接访问 localStorage。
6. 完成 1280×720、1440×900 的结构、无溢出和键盘组件/E2E 冒烟验证。

**验证与证据：** `pnpm vitest run tests/components/PcLayout.spec.ts tests/components/shell/*.spec.ts`，随后 `pnpm lint && pnpm typecheck && pnpm test:unit && pnpm build`；定向 Playwright 验证两个 PC 视口、跳过链接和退出。记录依赖变更、构建 chunk 警告、通过数和截图路径。

**结果回写：** 回写最终 PC DOM 区域、组件接口、NavigationGroup 类型、尺寸、功能树状态来源、图标依赖、测试数、提交号和第一批 PcNavMenu 兼容处理。

**建议提交：** `feat(frontend): build industrial pc platform shell`

---

## TASK-PF01-004 实现受控业务标签治理

**状态：** 待派遣（仅任务规划）

**目标：** 交付固定工作台、最多 12 个业务标签、导航前上限阻断、关闭／复用／重载和用户隔离恢复，使后续 PC 模块路由可直接接入。

**输入文档：** 本文第 7.9、8、10.1、11、12 节；已批准规格第 6.4、9、14 节；TASK-PF01-003 的 PC shell。

**依赖：** TASK-PF01-003。

**允许修改范围：** 创建 `src/frontend/src/workspace/**`、`src/frontend/src/stores/workspaceTabsStore.ts`、`src/frontend/src/components/shell/{PcWorkspaceTabs,WorkspaceTabLimitDialog}.vue` 和对应测试；修改 `src/frontend/src/router/{meta,routes,guards}.ts`、`src/frontend/src/layouts/PcLayout.vue` 及对应现有测试。禁止修改 Identity 页面、PDA/Mobile 路由、SystemData 菜单或业务 API。

**预期输出：**

1. 先为稳定路由身份、快照解析、12 上限、pending、关闭当前／其他／右侧、复用、reloadVersion 和恢复权限过滤写失败测试。
2. 实现第 7.9 节类型和纯持久化模块；params/query 排序必须确定，非法值不得抛出。
3. 实现 WorkspaceTabsStore，并把用户 scope 绑定到 ThemeStore 使用的同一稳定作用域语义；标签快照与主题 bootstrap 分离。
4. 在 Router Guard 权限判断后、业务路由确认前执行 `requestOpen()`；limit-reached 返回 `false`，不允许第 13 个页面先渲染。
5. 实现 36px PcWorkspaceTabs 和对话框；固定工作台不可关闭，溢出单行滚动，全部菜单键盘可达。
6. PcLayout 使用 `reloadVersion` 作为 RouterView key，完成关闭后的确定性导航和恢复失效提示。

**验证与证据：** `pnpm vitest run tests/unit/workspace*.spec.ts tests/unit/workspaceTabsStore.spec.ts tests/unit/routerGuards.spec.ts tests/components/shell/PcWorkspaceTabs.spec.ts tests/components/shell/WorkspaceTabLimitDialog.spec.ts tests/components/PcLayout.spec.ts`；新增 Playwright 覆盖 12→13 阻断、关闭后打开、复用、刷新恢复和无权限丢弃。记录全部测试数、存储内容脱敏检查、截图／trace 路径和提交号。

**结果回写：** 回写 Route Meta、WorkspaceTab/OpenTabResult、存储键、页面身份算法、上限行为、恢复过滤、reload 行为、测试数和提交号。

**建议提交：** `feat(frontend): add governed pc workspace tabs`

---

## TASK-PF01-005 校准PDA与Mobile终端外壳

**状态：** 待派遣（仅任务规划）

**目标：** 让 PDA/Mobile 消费统一主题与状态组件，修复显式路由下终端文案双事实源，同时保持触控、安全区、键盘和无假业务入口基线。

**输入文档：** 本文第 7.6、7.11、10.2～10.5、11、12 节；已批准规格第 4.3、5.6、7、11～14 节；TASK-PF01-001/002 输出。

**依赖：** TASK-PF01-001、TASK-PF01-002。

**允许修改范围：** 创建 `src/frontend/src/device/activeTerminal.ts` 和 unit test；修改 `src/frontend/src/layouts/{PdaLayout,MobileLayout}.vue`、`src/frontend/src/pages/{pda,mobile}/**` 及对应 component/E2E tests。禁止修改 PC shell、Router、AuthStore、Identity 页面和后端文件。

**预期输出：**

1. 先写 `resolveActiveTerminal()` 失败测试，覆盖显式路由优先和无显式路由回退设备建议。
2. 更新 PDA/Mobile 布局与首页，通过当前 route meta 解析终端文案；删除测试中为显示正确终端而写入 override 键的依赖。
3. 在三端专属顶栏接入 ThemeControl；PDA 入口至少 48×48px，Mobile 至少 44×44px。
4. 迁移页面表面、空态和个人页到新语义 Token；不增加任务、消息、审批、扫码、称量或工单按钮。
5. 复跑 PDA 横竖屏、Mobile 两视口、safe-area、键盘、退出和无横向滚动测试。

**验证与证据：** `pnpm vitest run tests/unit/activeTerminal.spec.ts tests/components/Pda*.spec.ts tests/components/Mobile*.spec.ts`；Playwright 定向运行 `tests/e2e/pda.spec.ts tests/e2e/mobile.spec.ts tests/e2e/screens.spec.ts`。必须证明 `/pda/home` 在 480px 且无 override 时显示 PDA，并记录触控 bounding box、视口截图、通过数和提交号。

**结果回写：** 回写终端解析接口、各布局 ThemeControl 位置、触控尺寸、safe-area、移除的测试覆盖依赖、截图路径、测试数和提交号。

**建议提交：** `feat(frontend): align pda and mobile platform shells`

---

## TASK-PF01-006 完成视觉回归与可访问性验收基线

**状态：** 待派遣（仅任务规划）

**目标：** 建立可重复的主题／密度／三端像素回归和管理组件视觉基线，并完成对比度、键盘、缩放、控制台和全量前端门禁。

**输入文档：** 本文第 10～12 节；已批准规格第 12～14 节；TASK-PF01-002、004、005 全部输出。

**依赖：** TASK-PF01-002、TASK-PF01-004、TASK-PF01-005。

**允许修改范围：** 创建 `src/frontend/src/pages/dev/UiBaselinePage.vue`、`src/frontend/tests/e2e/{theme,workspace-tabs,visual-matrix}.spec.ts` 和 `tests/e2e/snapshots/**`；修改 `src/frontend/src/router/routes.ts` 仅注册 DEV 基线页、`src/frontend/playwright.config.ts`、现有 `screens.spec.ts`、`console.spec.ts`、截图证据和必要测试配置。禁止添加生产业务入口、放宽 1% 阈值、修改 Identity 或后端。

**预期输出：**

1. 用 DEV-only lazy route 注册 `/pc/ui-baseline`，生产构建中无该路由和导航入口。
2. 基线页用通用组件展示真实静态控件状态，不使用业务名称、假 KPI 或假服务响应。
3. 实现第 12.3 节完整截图矩阵和 `toHaveScreenshot()` 断言；固定字体、动画、视口、时区和测试数据。
4. 以纯函数测试全部主题文字／状态色和顶栏渐变停靠点对比度；E2E 覆盖键盘、200% 缩放、触控和无横向滚动。
5. 运行 clean install 以外的全部稳定门禁；如锁文件在 003 后已稳定且环境允许，再运行 `pnpm install --frozen-lockfile` 验证锁文件。
6. 人工对照已批准顶栏颜色结论；结构或颜色差异不得通过提高截图阈值放行。

**验证与证据：** `pnpm format:check`、`pnpm lint`、`pnpm typecheck`、`pnpm test:unit:coverage`、`pnpm build`、`pnpm test:e2e`；记录每条命令退出码、unit/E2E 数量、四项覆盖率、构建体积和警告、HTML 报告、所有 snapshot 路径及外部设备限制。

**结果回写：** 回写视觉矩阵实际数量、阈值、快照目录、对比度结果、键盘／缩放结果、全量门禁证据、已知偏差和提交号；满足条件后将 TASK-PF01-001～006 移到待验收或已完成。

**建议提交：** `test(frontend): establish pf01 visual acceptance matrix`

---

## TASK-PF01-007 完成Identity集成与PF-01阶段收口

**状态：** 待派遣（仅任务规划）

**目标：** 在 PF-00 真实前端契约稳定后，验证登录、Identity 管理页面和权限导航完整消费 PF-01 主题与外壳，并形成 PF-02/03 可依赖的最终契约。

**输入文档：** 本文全部章节；TASK-PF01-001～006 输出；Identity `TASK-ID-010`、`TASK-ID-011`、`TASK-ID-012` 的稳定接口和页面；总 TodoList PF-01 完成门禁。

**依赖：** TASK-PF01-006、TASK-ID-010、TASK-ID-011、TASK-ID-012；PF-00 前端契约未稳定时保持未完成。

**允许修改范围：** 创建或修改 PF-01 联合验收测试、视觉快照、`src/frontend/README.md`、本文执行记录、`docs/implementation/README.md` 和总 TodoList PF-01 行；仅允许修复 `src/frontend/src/theme/**`、`components/shell/**`、`components/management/**`、`layouts/**` 中发现的 PF-01 缺陷。禁止修改 Identity 后端、`src/frontend/src/auth/**`、`src/frontend/src/api/identity/**`、Identity pages、`Directory.Packages.props` 和实施 15；Identity-owned 缺陷必须回到 PF-00 任务修复。

**预期输出：**

1. 验证真实登录前预挂载主题、登录后用户偏好绑定、刷新、注销和用户切换无主题串用或受保护壳闪烁。
2. 验证 Identity 用户／角色／权限／审计页面位于新 PC 壳、使用通用管理组件并通过三套主题代表状态。
3. 验证真实 permissions 同时驱动 Router Guard、工具轨／功能树和业务标签恢复；菜单隐藏不替代直接路由 403。
4. 复跑六视口认证路径、主题／密度矩阵、控制台敏感日志、前端全门禁，并与 Identity 联合验收结果互相引用。
5. 回写最终稳定类型、组件、Route Meta、存储键、已知限制和 PF-02/03 接入说明；外部环境缺失项保持“待验收”。

**验证与证据：** 运行 Identity 任务提供的真实登录 E2E 环境和本文第 12.4 节前端全门禁；记录后端/前端依赖提交、命令、退出码、测试数、覆盖率、截图、报告、真实浏览器/设备限制。不得以 Mock 登录证明真实集成完成。

**结果回写：** 更新本文第 16、17 节、实施索引和总 TodoList；记录 PF-01 所有任务最终状态、提交、证据、Identity 稳定契约版本、外部待验收项和下一阶段输入契约。

**建议提交：** `docs(pf-01): complete platform shell acceptance`

---

# 15. 完成标准

## 15.1 核心组件

- 三套配色、三种模式、两种 PC 密度均由正交状态表达。
- Foundation、Semantic、Component、State 四层 Token 落地，组件无新增品牌色散落值。
- bootstrap 首帧恢复、ThemeStore 用户绑定、系统监听和安全回退通过自动化。
- PC 顶栏、工具轨、功能树、标签和内容区尺寸与批准规格一致。
- PDA/Mobile 使用专属编排并满足触控下限。
- 通用查询、树表、表单抽屉和五类页面状态组件契约稳定。

## 15.2 数据与事务

- 无数据库、迁移、Outbox 或后端事务变更。
- 本地快照版本、用户隔离、优先级、非法回退和迁移行为有测试。
- 快照不含 Token、密码、权限列表、业务响应或表单草稿。

## 15.3 API、事件与外部集成

- PF-01 未新增或推断 SystemData/后续模块 API 和事件。
- `TenantUiDefaultsSource` 仅为前端端口，当前不发网络请求。
- Identity-owned auth/API/page 未被 PF-01 越界修改；真实集成等待稳定契约。

## 15.4 前端与用户路径

- PC 固定工作台和 12 业务标签治理完整；第 13 个在路由前阻断。
- `/pda`、`/mobile`、`/pc` 显式路由决定布局和终端文案。
- 三端主题选择、键盘、焦点、安全区、200% 缩放和无横向滚动通过。
- 未引入假 KPI、假菜单 API、假通知或假协作入口。

## 15.5 安全、审计和可观测性

- Router Guard 始终重新验证恢复标签权限。
- 控制台无 error、未处理 rejection 和敏感快照日志。
- 本地 UI 偏好不伪造业务审计；未来管理员主题治理边界清晰。

## 15.6 自动化与环境验收

- format、lint、typecheck、unit coverage、build、E2E 全部退出码 0。
- 覆盖率 statements/branches/functions/lines 均不低于 70%，不得低于第一批门禁。
- Playwright 视觉差异阈值不高于 1%，全部批准变化有新基线和评审证据。
- PF-00 未稳定、真实 safe-area 真机或现场浏览器缺失时明确标记“待验收”，PF-01 不伪报阶段完成。

---

# 16. 执行记录

| 任务 | 状态 | 执行者/任务 | 提交 | 验证证据 | 结果回写 |
| --- | --- | --- | --- | --- | --- |
| TASK-PF01-001 | 待派遣（仅任务规划） | - | - | - | - |
| TASK-PF01-002 | 待派遣（仅任务规划） | - | - | - | - |
| TASK-PF01-003 | 待派遣（仅任务规划） | - | - | - | - |
| TASK-PF01-004 | 待派遣（仅任务规划） | - | - | - | - |
| TASK-PF01-005 | 待派遣（仅任务规划） | - | - | - | - |
| TASK-PF01-006 | 待派遣（仅任务规划） | - | - | - | - |
| TASK-PF01-007 | 待派遣（仅任务规划） | - | - | - | - |

执行记录只写实际提交和新鲜验证。第一批 212/212、35/35 仅作为历史基线，不复制为本轮通过证据。

---

# 17. 下一阶段输入契约

PF-02 SystemData、PF-03 ReferenceData、Identity 后续页面和其他平台模块可以稳定依赖：

```text
ThemePalette = industrial-cyan | technology-blue | neutral-gray
ThemeMode = light | dark | system
EffectiveColorMode = light | dark
PcDensity = comfortable | compact
UiPreferencesV1 / UserUiScope / ResolvedUiAppearance
TenantUiDefaultsSource.load(scope)

NavigationItem / NavigationGroup
RouteMeta.workspace = fixed | business | none
WorkspaceTab / OpenTabResult

ThemeControl
PlatformTopBar / PlatformToolRail / PlatformFunctionTree
PcWorkspaceTabs / WorkspaceTabLimitDialog
AppQueryPanel / AppTreeTableLayout / AppFormDrawer
AppLoadingState / AppEmptyState / AppErrorAlert
AppPermissionState / AppDegradedState
```

稳定页面与布局边界：

- `/pc` 使用完整平台壳，后续 PC 管理路由通过 Route Meta 和授权导航视图接入。
- `/pda` 与 `/mobile` 使用各自终端壳，不复制 PC 标签模型。
- `/pc/ui-baseline` 只在 DEV/E2E 存在，生产模块不得链接。
- 业务路由必须使用稳定 route name、title、permission 和 workspace；不得从菜单 label 推断权限。

稳定本地键：

```text
industrial-platform.ui.bootstrap.v1
industrial-platform.ui.preferences.v1:<tenant>:<user>
industrial-platform.pc.tabs.v1:<tenant>:<user>
industrial-platform.terminal.override.v1
```

已知限制：

- 租户默认主题来源要等 PF-02 设计；当前为空适配器和产品默认值。
- 真实 Identity 登录与管理页面由 PF-00 任务实现，PF-01 只提供壳并最终联合验收。
- PF-01 不定义后续模块字段、API、事件、菜单领域、角标或业务数据。
- 主题设计器、任意自定义色板和跨设备服务端偏好同步不在本阶段。

---

# 18. 文档自审清单

- [x] 引用文件真实存在。
- [x] 当前前端结构、历史证据和并行修改状态与文档一致。
- [x] 无模糊占位表达。
- [x] PF-01、Identity、SystemData 和后续模块职责边界明确。
- [x] 数据库、API、事件不适用项均明确说明，未推断未来领域契约。
- [x] 主题、偏好、导航、标签和终端类型前后一致。
- [x] 已批准规格的 Token、三主题、模式、密度、外壳、12 标签、管理组件、触控、恢复、可访问性和视觉回归均有对应任务。
- [x] 七个任务均严格包含九字段。
- [x] 任务依赖图、任务卡和执行记录编号一致。
- [x] 历史证据与本轮验证严格区分。
- [x] Identity 与实施 15 未提交修改不在允许暂存范围。
- [x] Markdown 和 `git diff --check` 通过。
