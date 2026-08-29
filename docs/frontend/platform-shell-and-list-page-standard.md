# Platform Shell 与列表页标准

本标准冻结 PF-03 之前的平台公共体验。它约束布局、交互契约和验收方式，不替代领域业务规则。

## 品牌与 Header

- 品牌资产使用现有 `PlatformBrand` 与 `Industrial Platform` 名称。Logo 在登录、PC Shell、生产模式、打印和文档场景使用对应的现有变体；禁止在页面内复制一套颜色或图标规则。
- PC Header 固定三段：左侧品牌与终端/环境，中间真实业务上下文与全局搜索，右侧服务状态、语言、主题、全屏、可用的锁屏和用户菜单。
- 中间上下文只显示真实租户、公司、工厂或车间。尚未接入的数据用“未配置”表达或隐藏，不使用假上下文。

## 导航与模式

- 一级导航 Token 固定为 `96px`，默认同时显示 Element Plus 图标和文字；二级功能树 Token 固定为 `208px`。
- 导航表达业务领域，不按微服务拆菜单。动态菜单由现有导航适配器提供，Shell 不直接请求后端、不复制权限裁决。
- 支持三态：正常（一级图标文字 + 二级菜单）、收起二级菜单（一级仍有图标文字）、极限紧凑（一级仅图标）。收起和紧凑都必须保留可访问名称，不能依赖 hover 才能理解。
- 管理模式使用完整 Shell、Tabs、PageHeader 和密集工具栏；生产操作模式使用简洁顶栏和一跳入口，不显示管理导航、Tabs 或管理表格工具栏。
- 生产操作首屏使用 3 列宽屏宫格（窄窗口降为 2 列），卡片有大图标、清晰标题和明确状态。V1 的八个未实现入口只展示 `coming-soon`、`aria-disabled=true`，没有 route、API 或网络请求；“界面设置”才可复用已有设置能力。

## Tabs 与 PageHeader

- 工作台 Tab 固定在第一个位置且不可关闭；业务 Tab 支持关闭、关闭左右/其他/全部、刷新、固定和专注模式。Tab 标题来自稳定资源键，领域动态标题可提供 `fallbackTitle`。
- 页面标题区由 `AppPage` 统一提供：面包屑、标题/说明、状态或统计 meta、主操作四个槽位。业务页不重复创建第二个标题卡片。
- 新页面必须使标题、按钮、状态、错误、Tooltip、placeholder 和 aria 文案走 locale resource；动态业务名称可来自 API，不能把后端状态显示文字当作代码值。

## 唯一管理黄金样板

Identity Users 是管理类列表唯一视觉黄金样板，固定组合为：

`AppPage.header → AppQueryPanel → AppDataTable card → Dialog/Drawer`

黄金页必须覆盖中英文、无权限、空数据、加载、业务/网络错误、窄窗口、键盘焦点、行操作溢出、表格偏好、分页和导出。其他管理列表继承这套组合，不另起 UI 或表格框架。

## 查询、表格与 OData

- `AppQueryPanel`、`AppDataTable` loader、服务端列表和导出共享 `QueryDescriptor`；页面不复制第二套筛选、排序或分页语义。
- `AppDataTable` 是唯一直接接触 VXE DOM 的适配层。业务页只使用列、slot、loader、exporter、selection 和 descriptor 契约。
- OData 仅作为 Identity Users 的受控只读输入适配；API 解析为 descriptor 后返回平台 `ApiResult<PageResult<...>>`，不返回 `IQueryable`、不使用 `EnableQuery`、不跨服务查询。
- 导出沿用同一字段白名单、权限、数据范围、culture 和 timeZone。全量或自定义导出没有服务端能力时必须明确不可用。

## 权限与可访问性

- 菜单可见性不等于授权。页面操作使用 `PermissionGate`，后端 Policy/Handler 仍是最终裁决；功能权限、操作权限和数据范围不能合并为一个布尔值。
- 所有图标按钮有稳定 `aria-label` 和 Tooltip；禁用入口说明原因，键盘焦点可见，表单错误与加载状态可被辅助技术读取，触控目标不依赖 hover。
- 交互状态至少包含 available、coming-soon、loading、empty、error 和 forbidden；不为未接入服务填充假健康状态、假告警或假数据。

## 新页面准入

新页面进入平台前必须：

1. 复用现有 Shell、`AppPage`、`AppQueryPanel`、`AppDataTable`、权限和 locale 契约。
2. 明确真实 route、permission、数据所有权、加载/错误/空状态和独立测试边界。
3. 证明无重复 UI、表格、路由、权限或查询框架，并通过中英文、键盘、窄窗口和网络失败检查。
4. 只有真实页面、API、权限和验收证据齐全时，才能将生产模式入口从 `coming-soon` 改为 `available`。
