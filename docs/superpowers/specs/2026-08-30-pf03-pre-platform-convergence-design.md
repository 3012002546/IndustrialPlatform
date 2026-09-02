# PF03 前平台能力收束 V1 设计

## 1. 背景与状态基线

Industrial Platform 已完成 BuildingBlocks、PF-00 Identity、PF-01 视觉主题与平台外壳、PF-02 SystemData 的当前范围。PF-03 ReferenceData 仍只有服务骨架，尚未开始业务实现。当前工作树同时保留平台管理体验整改 WIP，包括 `AppDataTable`、统一查询区、表单抽屉、树表布局、Identity/SystemData 服务侧导出、锁屏和相应测试；这些文件是本轮输入，不得当作可丢弃的试验代码，也不得在未核对差异的情况下覆盖。

本轮在 PF-03 之前插入“平台能力收束 V1”，吸收以下已确认输入：

- 产品品牌、Logo、顶部 Header、一级/二级导航、Tabs、PageHeader、黄金样板页、多语言和 OData 建议；
- 能力按 NuGet、前端包/模块、服务和独立产品归类，避免所有能力都被做成微服务；
- 现有权限、Shell、SystemData、初始化管线和统一表格方向继续保留；
- 成熟开源方案只承担通用机制，平台自研工业语义和稳定适配层；
- PF-04 Audit/File/Notification 及以后只保留边界和实施前决策门禁，本轮不开发、不锁死具体技术方案；
- PF-11 完成后才进入 MES 业务开发。

## 2. 目标

1. 把现有品牌方向整理为可在登录、Shell、移动端、打印和图标场景复用的正式资产体系。
2. 把 PC Shell 固定为品牌与环境、业务上下文与全局搜索、全局工具三段式 Header，并把一级导航改为业务领域的“图标 + 文字”入口。
3. 将用户管理页冻结为唯一视觉黄金样板页，建立 PageHeader、QueryPanel、DataTable、行操作、状态和分页的组合规范。
4. 建立中英文、时区、日期/数字格式和工业单位偏好的基础设施；静态 UI、动态导航、Tabs、错误、导出均使用稳定资源键或稳定代码。
5. 收敛一个平台内部查询模型；QueryPanel、AppDataTable、列表 API、服务端导出和 OData 适配共享同一语义。
6. 采用官方 ASP.NET Core OData 作为协议解析器，仅开放受控只读查询；SqlSugar 通过平台适配层执行白名单查询，不暴露任意表或 `IQueryable`。
7. 保留现有后端权限裁决，补齐前端操作级权限和数据范围表达，消除页面、导航和权限目录之间的手写漂移。
8. 形成能力交付形态、可替换依赖和独立部署验收规范，为 PF-03～PF-11 及后续 MES 模块复用。
9. 建立三级质量责任链：开发任务自验证、独立验收任务承担主要验收测试和复验、当前任务负责边界/设计/计划及验收结论审阅。
10. 在 PC 端增加与现有管理布局并列的“生产操作模式”，先交付安全的模式切换、简洁宫格首页和明确禁用的待实现入口，为以后操作工业务页面保留稳定 Shell，而不提前实现 MES 业务。

## 3. 非目标

- 不实现 PF-03 ReferenceData 业务功能。
- 不实现 PF-04 Audit/File/Notification、PF-05～PF-11 或任何 MES 领域功能。生产操作模式中允许展示本设计明确列出的禁用占位入口，但这些入口必须标注“待实现”、不得导航、不得调用 API、不得被描述为已完成功能。
- 不迁入 Admin.NET、MalusAdmin、TMom、Vue Vben Admin 或 ABP/Furion 的整体架构。
- 不引入第二套 UI、表格、路由、状态管理或权限框架。
- 不把 OData 建成中央跨库查询服务，不开放任意 SQL、任意表、`$expand`、`$apply`、`$compute`、`$search` 或 `$batch`。
- 不把前端菜单隐藏当作授权，不降低现有服务端权限、租户和数据归属约束。
- 不为尚未出现的 PF-04+ 场景预先安装 Audit、对象存储、通知、调度、监控、低代码或 IoT 依赖。
- 不修改、暂存或提交 `DSH.md`；不提交 `bin/`、`obj/`、`TestResults/`、构建输出、运行日志、缓存或原型目录下的 `node_modules/`。
- 不推送远端，除非用户另行明确要求。

## 4. 技术与依赖决策

### 4.1 保留

- .NET 10、Clean Architecture、SqlSugar、PostgreSQL、Redis、RabbitMQ、Serilog/Seq、YARP。
- Vue 3、TypeScript、Vite、Pinia、Vue Router、Element Plus。
- `vxe-table@4.15.13` 与 `xe-utils@3.5.31`，但仅允许 `AppDataTable` 和应用装配层直接依赖。
- 现有 `ApiResult`、`PageResult`、异常中间件、权限 Policy/Handler、服务自有迁移/初始化、UnifiedHost 显式模块目录和 Gateway 边界。

### 4.2 新增

- `vue-i18n@11.4.10`：Vue 3 国际化运行时。
- `Microsoft.AspNetCore.OData@9.5.0`：OData 解析、EDM 与验证；采用稳定版，不采用 10.0 preview。

### 4.3 引入原则

任何新增依赖必须同时满足：许可证允许商用、仍受维护、支持当前运行时、通过最小 PoC、位于平台适配层之后、具备升级/移除测试。成熟方案不等于一次性安装全家桶。

## 5. 品牌与 Logo

### 5.1 资产

以已确认的工业、平台、数据连接、模块化、数字化方向为准。若原始会话图片在执行工作树中不可读取，沿用当前 `favicon.svg` 的稳定三层连接几何，不重新设计一个无关符号，也不暂停询问用户。

正式资产至少包括：

1. `mark.svg`：独立图形，用于 favicon、PWA、桌面快捷方式、移动端、导航紧凑态。
2. `horizontal-light.svg`：图形 + `Industrial Platform` 深色字，用于登录、文档、关于页面和浅色背景。
3. `horizontal-dark.svg`：图形 + 白色字，用于深色或渐变 Header。
4. `monochrome.svg`：单色，用于打印、报表、批记录页脚和水印。

品牌名 `Industrial Platform` 固定，不随语言切换翻译；辅助说明可以国际化。

### 5.2 组件契约

`PlatformBrand` 只接受以下公开属性：

```ts
export type PlatformBrandVariant = 'light' | 'dark' | 'mark' | 'monochrome'

export interface PlatformBrandProps {
  variant: PlatformBrandVariant
  compact?: boolean
  showName?: boolean
}
```

组件必须有可访问名称、明确尺寸和降级行为；业务页面不得直接拼接 Logo 与产品名。

## 6. Shell 与信息架构

### 6.1 Header

PC Header 固定为三段：

- 左侧品牌区：Logo、`Industrial Platform`、终端标识 `PC/PDA/Mobile`、环境标识 `DEV/TEST/UAT/PROD`。
- 中间业务上下文：只展示真实可用的租户/公司/工厂/车间；未接入的数据隐藏或显示明确“未配置”，不制造假上下文。全局搜索首期只搜索已授权菜单、最近访问和快捷命令，保留 `Ctrl+K`；不伪造用户、物料或工单搜索。
- 右侧全局工具：服务状态、语言、主题、浏览器全屏、通知预留入口、用户菜单。锁屏只有在现有安全锁定流程完整可验证时保留，并必须提供 Tooltip、可访问名称和明确状态。

### 6.2 一级导航

- PC 默认宽度在 88～104px 内，由设计 Token 固定为 96px。
- 默认显示图标与文字，不依赖 Hover 才能理解。
- 表达业务领域而不是微服务；V1 只注册已有真实功能，其领域框架允许后续扩展到工作台、基础数据、制造执行、物料管理、质量管理、称量执行、设备管理、数据中心和平台管理。
- 英文允许两行，但切换语言不改变导航总宽度。
- 三种状态：正常（一级图标文字 + 二级菜单）、收起二级菜单（一级仍有图标文字）、极限紧凑（一级仅图标）。极限紧凑只由窄窗口、PDA 或用户主动触发。

### 6.3 二级导航

- 二级菜单宽度由 Token 固定为 208px。
- 按当前业务领域分组显示真实授权入口。
- 菜单搜索属于二级菜单；收起二级菜单时搜索区一并收起，点击搜索图标可展开并聚焦。
- 动态 SystemData 导航继续通过现有适配器注入，不允许 Shell 直接调用后端或复制权限判断。

### 6.4 Tabs 与 PageHeader

- Tabs 保存路由标识和 `titleKey`，不保存翻译后的标题。
- 保留固定工作台、刷新、关闭当前/左右/其他/全部、重新打开、固定和专注页规则。
- 返回列表页时保留查询、分页、滚动、排序、表格布局和列宽。
- PageHeader 统一展示标题、说明、辅助状态和右侧主操作；新建/保存等主操作不得单独占用一张 Card。

### 6.5 PC 双体验模式

PC 端固定支持两种体验，不把两类用户折中进同一复杂布局：

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

- `management` 继续使用本设计的 Header、一级/二级导航、Tabs 和 PageHeader，服务管理人员、工程师和平台管理员。
- `operation` 面向生产操作工，使用独立的简洁 Shell：不显示一级/二级管理导航、不显示多页签、不显示密集工具栏；首屏为参考图思路的高对比大图标宫格，一次点击进入一个任务。
- 两种模式共用认证会话、后端权限、租户、主题、语言和真实运行状态，切换不登出、不复制授权逻辑。`platform.home.view` 允许管理模式，新增 `platform.operation.view` 允许生产操作模式；只有同时拥有两项权限才显示模式切换，只有一项时直接进入被授权模式。
- 偏好按 `tenant + user + device` 保存；已保存模式失权时回退到仍被授权的模式。不得根据文化程度、姓名或前端角色标签自行推断；后续如需角色默认值，由服务端稳定用户偏好契约提供。
- 生产模式保留一条简洁顶栏，只展示 Logo/产品、真实租户或已接入的工厂/车间、环境、语言、全屏、当前用户和模式切换；不展示假工单、假产线、假告警、假健康或假通知。
- 参考图只作为布局思路：桌面宽屏为 3 列、最多 9 个首屏入口，大面积实色卡片、统一线性图标、当前语言的大字号标题和清晰状态；不复制其品牌、颜色、截图字幕或双语堆叠。
- V1 占位入口固定为任务执行、工单作业、投料作业、称量作业、投料统计、物料集中、物料接收、配方查看，全部 `coming-soon`；“界面设置”可以使用现有语言、主题、全屏和模式能力而标为 `available`。占位项使用真实 Element Plus 图标，显示“待实现”徽标、`aria-disabled=true`，点击不跳转、不发请求。
- 3 列宽屏时卡片最小高度 176px、图标 56～72px、标题 24～28px、间距 12～16px；900～1279px 为 2 列，小于 900px 只做可用降级而不是新的 PDA 设计。交互不依赖 hover，键盘焦点和触控目标清晰。
- 将来某项业务完成时，只有注册真实 route、permission、feature 和可验收页面后才能把 `coming-soon` 改为 `available`；生产模式注册表是展示入口，不是业务完成状态的替代品。

### 6.6 用户复核后的 Shell 细化（2026-08-30）

- 用户提供的 `C:\Users\DONG\Downloads\ChatGPT Image 2026年8月30日 13_57_09.png` 成为品牌母版。顶部使用紧边界横向 Logo；浏览器 favicon、PWA/紧凑态使用从同一母版左侧确定性裁切的图形标，不得重新绘制或生成相似 Logo。浏览器标题后缀继续为 `Industrial Platform`。
- 顶栏仍为左/中/右三区。左区依次为 Logo、`终端 PC`、环境标识，PC 与环境标识间距固定为 4px；中区为真实上下文和全局命令搜索；右区整体贴近右边，保留最多 4px 安全边距。
- 搜索结果、主题和语言面板必须完整显示在 viewport 内，不能被 Header 的 `overflow` 裁切；浮层采用 body teleport 或等价的可视边界定位，并通过 1280×720、1440×900 右边界测试。
- 语言入口改为与全屏、锁定一致的 32px 图标按钮，通过 Tooltip/aria-label 表达含义，点击后显示中文/English 菜单；不在顶栏长期占用下拉选择框宽度。
- 顶栏增加消息通知图标，但本轮只提供 Shell 接缝、真实空状态和无假数字徽标，不创建 Notification 服务、Provider、事件持久化或业务 API；真实通知仍受 PF-04 决策门控制。
- 消息通知旁增加独立的“在线用户”图标按钮，不再放在用户下拉中；仅具备 `identity.session.view` 时显示。点击后从右侧打开响应式抽屉，以统一表格展示有效登录会话：序号、账号、姓名、登录时间、最近刷新时间、过期时间、当前会话标记和操作。口径仍为未删除、未撤销、未消费且未过期的 refresh session，页面必须说明“有效登录会话，不代表实时页面活跃”；不展示 Token、IP、UserAgent 原值或哈希，不为复刻参考截图扩大敏感数据存储。
- 在线用户抽屉打开时加载、支持手动刷新、空态/失败重试。单会话“强制退出”属于 Identity 会话撤销，使用独立 `identity.session.revoke` 权限、租户范围校验和二次确认；撤销当前会话后立即执行本地退出。参考截图中的“发送消息”仅保留禁用图标和“待实现”提示，本轮不创建 PF-04 Notification 后端、消息模型或假成功。
- 顶栏移除独立“锁定工作区”图标。右侧用户菜单按“个人中心、清理缓存、锁定工作区、退出登录”排列并全部使用既有图标库：个人中心新增认证用户可访问的真实页面，展示 `/auth/me` 已有字段并复用现有修改密码入口；清理缓存只清除当前用户的 Tabs、页面状态、表格偏好等非安全 UI 缓存，必须保留认证会话、租户、语言、主题、终端和 PC 模式偏好，禁止调用服务端 Redis/业务缓存清理；锁定复用现有 `lockStore.lock`，退出登录复用现有流程。
- 一级导航默认图标在上、文字在下。使用 `ResizeObserver` 或等价方式根据可用高度计算可见项；溢出时底部固定显示“更多”，其弹层列出其余授权一级域并能正确显示/切换当前处于溢出区的活动项。
- 二级菜单搜索与菜单列表属于同一个视觉面板；二级菜单收起时搜索和列表整体收起。顶栏搜索是全局命令/授权菜单/最近访问，二级搜索只过滤当前一级域并使用“筛选当前菜单”文案；Tabs 不再保留第三个重复菜单搜索入口。
- 静态和动态导航都以 `labelKey + fallbackLabel` 为输入；一级、二级、子菜单、“更多”、搜索结果、Tabs 和 document title 在语言切换后即时更新。只有缺少资源键的动态服务数据允许 fallback。
- 用户管理顶部查询视觉与用户组管理一致，优先通过 `AppQueryPanel` 的统一紧凑样式收敛，不复制一份页面专属 CSS。列头查询切换和点击必须无异常，顶部/列头查询仍互斥且复用同一 QueryDescriptor。
- 终端预览页使用与 PC 首页相同的外层页面边距和卡片间距；不得在 `PcLayout` 内容 padding 之上再次叠加整页 padding。
- 真实页面上的系统管理员必须能看到生产操作模式切换。`SYSTEM_ADMIN` 的版本化种子/权限补齐应包含 `platform.operation.view`；既有会话不能因权限目录新增而永久静默隐藏入口，认证刷新或重新登录后必须获得最新授权。验收必须在 `http://localhost:5173/pc/identity/users` 的真实登录页面确认可切换到 `/pc/operation` 并无损返回管理模式。
- 用户管理列头查询的验收以真实请求为准：切换后不得出现“加载用户列表失败”。必须记录并修复失败请求的 URL、状态码和响应体/服务端日志，不得仅以“无 JavaScript 异常”判定通过。

## 7. 黄金样板与页面规范

用户管理是管理类列表页唯一视觉黄金样板。生产操作宫格是另一种 PC Shell 的入口样板，不是第二套管理列表规范。完成标准是形成稳定的 80 分基线并冻结，不继续无边界追求单页 100 分。

固定组合：

```text
AppPage / PageHeader
  ├─ title + description + primary actions
  ├─ AppQueryPanel
  │    ├─ common fields
  │    ├─ advanced fields
  │    └─ search/reset
  └─ AppDataTable
       ├─ business actions
       ├─ table tools
       ├─ rows/status/row actions
       └─ pagination/selection summary
```

黄金样板必须覆盖：中文、英文、无权限、空数据、加载、业务错误、网络错误、窄窗口、键盘焦点、行操作溢出、表格偏好和导出。树表和异步任务页可以保留功能样板，但视觉必须继承用户管理。

## 8. 国际化与本地化

### 8.1 Locale

首期固定支持 `zh-CN` 和 `en-US`，默认 `zh-CN`。用户偏好分别保存：

```ts
export interface LocalePreferences {
  locale: 'zh-CN' | 'en-US'
  timeZone: string
  dateFormat: 'yyyy-MM-dd' | 'MM/dd/yyyy'
  numberLocale: 'zh-CN' | 'en-US'
  unitSystem: 'metric'
}
```

语言切换不自动修改时区。浏览器中保存的是稳定偏好；服务器生成导出时请求必须携带 `culture` 和 `timeZone`。

### 8.2 资源键

资源键采用 `领域.模块.页面.字段` 或 `common.类别.名称`：

```text
common.action.search
common.action.reset
shell.navigation.workspace
identity.user.title
identity.user.loginName
```

静态文本位于前端语言包；动态导航、字典和后续 ReferenceData 使用稳定 `TitleKey/DefaultTitle` 与翻译表。状态查询使用稳定代码，例如 `Enabled`，不得使用“启用”作为数据库业务值。

### 8.3 错误

API 继续返回稳定 `code`，并新增可选参数字典；前端优先使用 `code` 查找本地化消息，后端 `message` 只作为兼容降级。TraceId/CorrelationId 必须保留。未经本轮明确迁移的既有端点保持向后兼容。

## 9. 查询、表格与导出

### 9.1 单一内部模型

前端公共查询模型固定为：

```ts
export type QueryOperator =
  | 'eq'
  | 'ne'
  | 'contains'
  | 'startsWith'
  | 'gt'
  | 'ge'
  | 'lt'
  | 'le'
  | 'between'
  | 'in'

export interface QueryFilter {
  field: string
  operator: QueryOperator
  value: unknown
}

export interface QuerySort {
  field: string
  direction: 'asc' | 'desc'
}

export interface QueryDescriptor {
  filters: QueryFilter[]
  orderBy: QuerySort[]
  select: string[]
  pageIndex: number
  pageSize: number
  search?: string
}
```

后端使用同语义的不可变记录。`AppQueryPanel` 输出 `QueryDescriptor`；`AppDataTable` loader、服务端列表、Excel 导出和 OData 适配共享它。OData 只是输入/输出适配协议，不是第二套内部查询引擎。

### 9.2 AppDataTable

- 继续作为唯一表格出口，保留现有用户偏好键、分页、筛选、排序、列设置、密度、树形/展开、导出、打印和全屏行为。
- 冻结公开 props/events/slots；不得向业务页泄露 VXE 实例、配置或事件。
- 将查询映射、偏好、导出/打印和 VXE DOM 适配拆成聚焦模块；不重写表格，不改变已验证行为。
- 依赖 VXE 内部 DOM/CSS 的代码集中到一个适配文件，并用升级契约测试锁定。
- 业务页面禁止直接导入 `vxe-table`。

### 9.3 导出

- 列表和 Excel 使用同一 `QueryDescriptor`、字段白名单、权限和数据范围。
- 服务继续拥有自己的数据与导出端点，不建设中央接收 rows 的导出服务。
- 快速 CSV/HTML/XML/TXT 仍只处理当前已加载数据；服务端 Excel 不在浏览器加载全量数据。
- 导出请求携带 `culture`、`timeZone`、列和数量；全量导出继续要求二次确认并受上限、超时和权限限制。

## 10. OData

### 10.1 V1 能力

只支持：

```text
$filter  $select  $orderby  $top  $skip  $count
```

关闭：

```text
$expand  $apply  $compute  $search  $batch
```

固定限制：`MaxTop=100`、过滤节点最多 20、函数嵌套最多 5、排序字段最多 3、查询超时 10 秒。每个资源显式注册可选择、可筛选和可排序字段。

### 10.2 执行顺序

```text
Tenant/DataScope/SoftDelete/FieldPermission
  → service-owned base query
  → OData parse and validation
  → normalized QueryDescriptor
  → SqlSugar adapter
  → PageResult/read model
```

客户端条件永远不能覆盖平台基础过滤。OData 只作用于服务自有 read model；Command、状态流转、审批和权限变更继续走应用服务。

### 10.3 首个纵向样板

Identity Users 是首个资源。端点归 Identity 所有，权限仍为 `identity.user.view`，返回稳定 read model。该样板证明协议解析、字段白名单、租户/权限先行、SqlSugar 查询、分页总数、前端 QueryDescriptor 转换、AppDataTable 加载和导出一致性。PF-03 后续复用该 BuildingBlock，而不是复制 Users 实现。

## 11. 权限与数据范围

- 后端 `PermissionCatalog`、Policy 和 Handler 是权威。
- 前端补齐 SystemData 现有 create/update/move/status/manage/publish/rollback/initialize 等操作权限常量和 `PermissionGate`。
- 新增 `platform.operation.view` 作为生产操作模式入口权限；模式切换和路由守卫引用权限目录，不把“operator”前端字符串当授权。
- 路由、导航和按钮只引用权限目录，不出现散落字符串。
- 增加前后端目录一致性契约测试；差异必须阻断构建。
- 功能权限、操作权限和数据范围分开。当前没有完整工厂/产线范围时不伪造范围，只保留明确接口和租户/组织已有规则。
- 前端隐藏只改善体验；直接调用 API 仍必须得到 403。

## 12. 能力形态与可移植边界

每项能力必须标记一种主要交付形态：

| 形态 | 当前示例 | 约束 |
| --- | --- | --- |
| NuGet/BuildingBlock | ApiResult、查询/OData、缓存、事件总线 | 无独立运行时，不拥有业务数据 |
| 前端组件/模块 | Shell、QueryPanel、AppDataTable、i18n 资源 | 可注册，不要求微前端 |
| 可嵌入领域模块 | ReferenceData 等 | 独立契约与数据归属，可进入 UnifiedHost |
| 独立服务 | Identity、未来真正独立的领域服务 | 自有配置、初始化、健康与部署入口 |
| 独立产品 | Server Monitor、称量工作站 | 独立前后端、部署包和产品入口 |

本轮只产出分类、契约和验收规则，不重构 PF-04+ 的部署形态。任何计划独立部署的服务都必须声明必需/可选/可替换依赖，并通过脱离其他业务服务的启动与核心冒烟测试。

## 13. PF-04 及以后决策门禁

Audit/File/Notification 暂定边界：Audit 为拦截/事件接口加可选 Provider 或服务；File 为存储抽象加可选 Provider/服务；Notification 为 Provider 加可选服务，不能形成业务强制启动链。

从 PF-04 开始，每阶段实施前必须重新完成：

1. 当前代码与产品需求复核；
2. GitHub/Gitee 成熟方案比较；
3. 许可证、商用、维护与安全检查；
4. Build/Adopt/Wrap/Skip 决策；
5. 独立部署、可替换性与数据所有权评估；
6. 必要 PoC；
7. 用户最终确认。

未经过该门禁不得按本设计中的候选方案直接实施。

## 14. 工作线与文件所有权

### 14.1 功能开发任务

负责生产代码、测试、依赖、品牌资产、规范文档和开发侧自验证。允许修改本设计明确列出的前端、BuildingBlocks.Querying、Identity OData 样板和权限/错误兼容文件。不得实现 PF-03/PF-04+。

开发任务完成每个工作包后必须自行运行相应单测；最终运行 fresh Release build、后端全量测试、前端 lint/typecheck/unit/build、目标 Playwright、视觉截图和真实 UnifiedHost 代表路径。外部依赖不可达时记录具体端点和命令，不把未执行写成通过。

### 14.2 独立验证任务

等待功能开发任务完成，读取其绝对工作树路径、分支、基线和最终提交；不信任开发报告，重新审查 diff、文件范围、许可证、依赖、边界和测试结果，并在同一开发工作树上独立复跑验收。默认不修改生产代码；发现阻断项时直接向功能开发任务发送精确返工要求，等待修复与自验证后重新验收，不询问用户。

### 14.3 当前任务

负责边界收束、功能设计和详细计划。读取功能开发与独立验收的最终证据，复核阻断项是否关闭；只有证据矛盾、边界疑似越界或结论无法复现时才执行针对性抽查，不重复整套验收测试。独立验收任务承担主要验收执行，当前任务向用户审阅并报告最终结论。

## 15. 总体验收标准

- Logo 四种资产与统一组件可在 Shell、登录和 favicon 场景使用。
- PC Header 三段职责清晰；一级导航默认图标+文字，二级菜单可搜索，三种收缩状态可验证。
- PC 管理/生产操作模式可按权限安全切换；生产模式为 3 列优先的大卡片宫格、无管理导航/Tabs，八个未来入口均真实禁用并标注“待实现”，界面设置可用，切换不丢认证或管理页面状态。
- 用户管理黄金页结构冻结，中英文和视觉基线通过；其他页面没有被无边界重做。
- Vue I18n 安装并覆盖 Shell、导航、Tabs、通用页面组件、黄金页、表格/查询公共文案和错误降级。
- QueryDescriptor 是唯一查询语义；AppDataTable API 已冻结并保持现有功能；业务页无 VXE 直接依赖。
- Identity Users OData 样板只开放允许能力，权限/租户先行，非法字段、超限、禁用操作和绕过尝试被拒绝。
- SystemData 操作权限前端 Gate 与服务端 403 契约一致。
- 能力归类和可移植验收规则有正式文档；PF-04+ 没有被提前实现或锁定。
- 功能开发任务完成自验证；独立验收任务完成主要验收和重新验证；当前任务完成边界、设计、计划及验收结论审阅。
- 未触碰 `DSH.md`，未提交生成物，未推送远端。
