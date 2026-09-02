# 平台管理体验整改实施计划

> 日期：2026-08-27
> 状态：第三轮交互整改及第四轮停止竞态修复已完成；本地门禁、目标 Mock Playwright、UnifiedHost Development 真实 SystemData 关键路径及 RabbitMQ 停止竞态回归通过；全量 Mock 视觉/首页基线仍有既有失败
> 第五轮状态（2026-08-28）：本地实现与视觉验收通过；本轮 UnifiedHost 真实复验因云依赖端口不可达而未执行，保持“已验证但有外部联调缺口”，不标记最终完成。
> 范围：SystemData 运行状态、PC 平台外壳、统一数据表格，以及“用户管理—服务初始化编排”现有管理页面。不得扩展到新业务模块。

## 一、已确认的技术边界

- Element Plus 继续作为平台主 UI 组件库，负责表单、树、树选择、单选、复选、Dialog 和 Drawer。
- 仅为高级表格引入并锁定一个经验证的 `vxe-table` Vue 3 版本；业务页面不得直接依赖其 API。
- 平台统一出口为 `AppDataTable`。它覆盖普通表格、明细展开和树形表格；明细展开与树形展开在同一实例中互斥。
- 顶部查询与列头查询互斥，切换模式时清除另一模式条件；筛选、排序和分页均作用于服务端完整数据集。
- AppDataTable 只有一个“导出”菜单：快速导出提供 vxe 原生 CSV/HTML/XML/TXT（仅当前已加载数据，可选当前页/选中行），Excel 提供平台真实 `.xlsx` 服务端导出。Excel 默认 10,000 条，可修改数量或选择全部；全部导出必须二次确认，本轮不建设异步导出任务系统。
- 表格偏好按“当前用户 + 路由 + tableKey”保存在浏览器；本轮不建设跨设备同步。
- 不引入 Ant Design Vue、TDesign、PrimeVue 或其他完整 UI 体系，不预先封装尚无复用需求的控件。

## 二、工作包

### WP-01 SystemData 运行状态语义整改

**目标**

消除首次未配置导航或主题时的错误降级提示，同时保留真实基础设施故障的降级保护。

**实施**

1. 将“尚未发布导航”和“尚未配置主题”建模为可用但 `configured=false` 的正常运行态；前端继续使用内置授权导航和默认主题。
2. 数据库、Redis、网络或缓存读取失败仍返回真实降级/不可用状态，不用默认值掩盖故障。
3. 收敛 `runtimeStore` 刷新入口，避免同一页面装载期间重复请求 navigation/theme-policy。
4. 更新运行时 DTO、API 契约和状态提示文案，不改变管理端发布/配置规则。

**检查点**

- 未配置导航/主题：页面可用且不显示“运行策略降级”。
- Redis 或后端真实故障：仍显示降级/不可用并保留 TraceId。
- 单次页面初始化不重复请求同一运行时资源。

### WP-02 PC 外壳、全屏与锁屏整改

**目标**

统一管理页面留白，并提供两种边界清晰的全屏及安全锁屏。

**实施**

1. 在 PC 内容工作区统一提供上下左右边距，覆盖用户管理至服务初始化编排及未来路由页面，避免逐页复制样式。
2. 页面全屏由外壳进入专注模式：隐藏顶栏、标签栏、工具轨和功能树，只保留当前业务页面；支持按钮和 `Esc` 退出。
3. 顶栏主题按钮左侧依次加入浏览器全屏和锁定按钮；浏览器全屏调用标准 Fullscreen API。
4. 锁定时保留当前路由和标签状态、清除本地可用认证会话但不调用服务端注销，使用全屏遮罩阻止页面操作；固定当前用户名，只允许输入当前密码通过既有 Identity 登录接口解锁；不保存密码。

**检查点**

- 所有目标页面四周留白一致，窗口尺寸变化无横向溢出。
- 页面全屏与浏览器全屏彼此独立，退出路径明确。
- 错误密码不能解锁；正确密码恢复原路由和工作区；刷新、Token 过期和服务端不可用状态有明确行为。

### WP-03 `AppDataTable` 平台组件

**目标**

建立后续管理页面唯一允许使用的数据表格契约，不重复实现成熟表格能力。

**实施**

1. 用一个隔离验证页确认锁定版本的 Vue 3、TypeScript、Vite、主题变量、弹窗内布局和按需加载兼容性，再写入依赖锁文件。
2. 建立类型化列描述和数据源契约，统一服务端分页、排序、顶部查询、列头模糊/下拉/日期区间筛选及错误态。
3. 提供清空条件、刷新、列显示/顺序/固定、序号列、边框、默认/中等/紧凑密度和偏好恢复。
4. 支持三种数据形态：普通列表、明细展开、树形展开；树形模式支持多层级、异步子节点、展开状态和单选/复选，复选可配置父子联动或独立选择。
5. 一个导出菜单分组提供快速 CSV/HTML/XML/TXT 与平台 Excel；快速格式只调用表格内核导出当前已加载数据，Excel 只提交当前有效查询、排序、列和数量到后端导出端点；禁止前端先加载全部数据再生成 Excel。
6. 只对外导出平台类型和组件，不泄露 `vxe-table` 实例、事件或配置对象。

**检查点**

- 组件测试覆盖查询模式互斥、列设置恢复、密度、展开、树异步加载、单选/复选和导出参数。
- 契约测试证明筛选作用于完整数据集，不是只过滤当前页。
- 依赖升级或移除时，业务页面无需修改其查询和列定义。

### WP-04 现有页面迁移与联合验收

**目标**

将“用户管理—服务初始化编排”所有真实表格迁移到统一契约，并确保既有增删改查不回退。

**实施**

1. 先迁移一个 Identity 普通列表和一个 SystemData 树形/展开列表作为纵向样板，完成当前检查点后再批量迁移其余页面。
2. 逐页补齐服务端查询映射与导出端点；没有对应字段语义的列不得伪造筛选条件。
3. 删除迁移后不再使用的页面内表格状态、重复样式和测试，不改动无关业务规则。
4. 更新前端工程规范：后续业务表格必须使用 `AppDataTable`；新增平台组件仍执行“现有 Element Plus 能力优先、成熟专用内核次之、平台薄封装”的准入规则。

**检查点**

- 每迁移一个页面，验证加载、筛选、清空、刷新、分页、排序、列设置、密度、展开/选择、导出和原有 CRUD。
- 使用 UnifiedHost + 云 Docker 依赖完成 admin 真实登录关键路径和代表性 CRUD/导出 E2E。
- 最终执行 fresh Release build、后端全量测试、前端 lint/typecheck/build/Vitest 和目标 Playwright；未通过不得标记完成。

## 三、执行顺序与状态规则

```text
WP-01 → 检查 → WP-02 → 检查 → WP-03 → 检查 → WP-04 → 最终验收
```

- 每个工作包完成后单独记录命令、退出码、通过/失败/跳过数和真实运行证据。
- 只有当前检查点通过后才进入下一工作包；失败只修复本工作包阻塞，不顺带扩展范围。
- 计划完成不等于代码完成。代码、文档状态和 Todo 只能在对应验证通过后回写为“已完成”。
- 本轮不提交、不推送，除非用户另行明确授权。

## 四、本轮整改执行记录（2026-08-27）

- 第二轮阻断已修复：`AppDataTable` 明确区分 server loader 与完整 local rows；迁移页均绑定 query-change/loader 或真实本地筛选排序分页；Identity 用户/角色/用户组/审计及 SSO 的筛选、排序、分页请求映射已接通。
- Excel 导出已按服务所有权补齐：Identity 与 SSO 由 Identity 自有受权限保护端点重新读取 Identity 数据；组织、岗位、任职、功能、服务、初始化由 SystemData 自有端点重新读取 SystemData 应用服务数据。没有通用 rows 接收端点，`AppDataTable` 导出请求的 `rows` 保持 `undefined`，不存在跨服务或客户端伪造导出。
- 单一“导出”菜单保留快速 CSV/HTML/XML/TXT（仅已加载数据）与自定义服务端 `.xlsx` 两个分组；Excel 默认 10000、自定义/全部和全部二次确认均已保留。
- 真实环境使用 `src/backend/appsettings.Development.local.json` 的 UnifiedHost Development 自动发现，不依赖 Docker CLI 或 `deploy/cloud-dev/.env`。

- WP-01：已完成。后端运行时 DTO 增加 `configured` 语义；未发布导航/未配置主题保持可用，真实读取故障仍走降级；runtime 单飞协调已接入。SystemData 运行时与前端协调测试通过。
- WP-02：已完成本地实现。PC 四周留白、专注全屏与浏览器 Fullscreen API 分离、锁屏覆盖层及既有 Identity 密码解锁已实现；锁屏只清理本地会话，不注销服务端。PcLayout/lock 相关测试包含在前端 496 个通过测试中。
- WP-03：已完成本地组件与契约实现。锁定 `vxe-table@4.15.13`；业务页面只使用 `AppDataTable`。当前唯一“导出”菜单分为快速 CSV/HTML/XML/TXT（已加载数据）和 Excel（服务端参数、10000/自定义/全部确认）；组件测试覆盖入口、数据范围提示、Excel 参数、本地筛选排序分页和无 exporter 不显示空操作。Identity/SystemData/SSO 导出端点均由所属服务重新读取数据；后端通过有界 `Pipe` + `FileStreamResult` 分页流式输出，不在浏览器或服务端暂存完整 xlsx。
- WP-04：代码迁移与真实 SystemData 关键路径已通过。用户管理至服务初始化编排的 15 个 `AppDataTable` 实例（服务分组按实际渲染复用）均具备所属服务 exporter；UnifiedHost Development 真实管理员组织/岗位 CRUD/状态闭环 Playwright 1/1 通过。Identity 真实页面场景仍依赖 Gateway/真实账号，未将其伪标记为通过。

### 门禁证据

- 后端：`dotnet build src/backend/IndustrialPlatform.slnx --configuration Release` 通过（0 警告、0 错误）；`dotnet test src/backend/IndustrialPlatform.slnx --configuration Release --no-build` 通过，SystemData 542、Identity 570、UnifiedHost 20、Gateway 14、ReferenceData 14、BuildingBlocks 131、Integration 10 通过，Integration 3 跳过；跳过项为 Redis/PostgreSQL/RabbitMQ 真实依赖测试。
- 前端：`pnpm.cmd lint`、`pnpm.cmd typecheck`、`pnpm.cmd build`、`pnpm.cmd test:unit` 均通过；Vitest 62 个文件/487 个测试通过。构建仅有大 chunk 提示。
- 真实 Playwright：UnifiedHost Development `pnpm.cmd exec playwright test --config=playwright.unified.real.config.ts` 1/1 通过，完成真实组织/岗位 CRUD 与状态闭环。
- SystemData 管理页定向 Mock Playwright：`pnpm.cmd exec playwright test --config=playwright.config.ts tests/e2e/systemdata-admin.spec.ts` 2/2 通过；仅保留 Element Plus 既有 checkbox label 弃用 warning。
- 全量 Mock Playwright：登录按钮严格匹配问题已修复；剩余失败为旧首页文案、视觉快照和部分需要真实后端的场景，未据此标记全量 Mock 通过。`pnpm.cmd format:check` 仍受仓库既有 19 个文件格式差异影响，未大范围格式化用户文件。

### 第三轮静态验收整改（2026-08-27）

- 查询模式契约已收敛：`AppDataTable` 发出 `query-mode-change`，仅渲染活动查询槽；Identity 用户、角色、用户组、审计页在切换到列头查询时清空外部顶部查询并隐藏顶部控件，loader/export 按 `request.queryMode` 选择唯一过滤来源，不再使用 `filters ?? query` 双来源 fallback。用户页补齐 `query-change` 分页/排序回写。
- `AppDataTable` 的 server loader 改用内部 `loaderLoading` 生命周期 ref，并在成功、异常和 `finally` 路径结束 loading；空结果成功返回不会保持永久 loading。
- 本地完整数据集筛选按列 `filter.kind` 分派：`select` 精确匹配并兼容 boolean/number/string，`date-range` 保持区间判断，`text` 才使用模糊匹配。
- 回归验证：AppDataTable 新增模式槽位、空结果 loading、select 精确匹配及异常 finally 测试；`pnpm.cmd test:unit` 62 个文件/496 个测试通过；随后 `pnpm.cmd typecheck`、`pnpm.cmd lint`、`pnpm.cmd build` 均通过。

### 第四轮停止竞态整改（2026-08-27）

- 修复 `RabbitMqConnection.DisposeAsync` 的 shutdown/dispose 竞态：局部捕获连接、原子清空共享引用、同步连接创建与销毁、释放后拒绝新建通道，并保证重复 Dispose 幂等。
- 新增 BuildingBlocks 回归测试，模拟 `CloseAsync` 触发 `ConnectionShutdownAsync` 清空字段后仍能安全释放。
- 验证：fresh Release build 通过；定向测试 1/1；后端全量 Release 测试 1,301 passed / 3 skipped / 0 failed。

### 第三轮交互整改与最终复核（2026-08-27）

- 页面专注入口已从 PcLayout 顶栏移至当前页签右键菜单；顶栏保留浏览器全屏、锁定和主题。AppDataTable 的全屏仅作用于表格区域。
- AppDataTable 工具栏已按平台视觉 token 重排：业务 `toolbar-actions` 位于左侧，查询切换、清空、刷新、表格全屏、列设置及统一导出菜单位于右侧；表头筛选作为原生表头单元格内第二行渲染。已启用 vxe 原生列宽拖动并按既有用户/路由/tableKey 偏好持久化。
- 页签右键菜单已覆盖刷新、关闭、关闭左右、关闭其他、关闭全部和当前页专注；当前页签始终显示关闭按钮，其他业务页签仅 hover 显示，固定工作台不可关闭；Store 补充 `closeLeft`/`closeAll`。
- 目标 Identity/SSO 主表及组织树的新增操作已迁移到 AppDataTable action slot；岗位详情中的移动/状态等非表格流程保留原位置。所有 15 个目标 AppDataTable 实例继续绑定所属服务 exporter。
- 本轮最终门禁：前端 TypeScript、lint、Vitest 62/62（最终 498/498）、build 通过；目标 Mock Playwright（pc-shell、workspace-tabs、systemdata-admin）13/13 通过；后端 fresh Release build 0/0、全量 1,301 passed / 3 skipped / 0 failed。
- UnifiedHost Development 使用自动发现的 `src/backend/appsettings.Development.local.json` 启动；`/health/ready` 200 且依赖全 Healthy，真实管理员组织/岗位 CRUD 1/1 通过；Ctrl+C 正常停止尾部仅见 RabbitMQ `Goodbye`，未再出现 `NullReferenceException`，5041 已释放。

### 独立静态验收收尾（2026-08-27）

- `AppDataTable` 的表格卡片现在完整包住 toolbar、列设置、VxeTable 和 footer；顶部查询区仍位于卡片之前。工具栏平台操作改为 Element Plus 图标按钮并保留 aria-label/title，业务 action slot 继续由页面提供文字按钮。
- 页签 `closeAll()` 只保留并激活固定工作台；非当前页右键刷新、当前页专注均通过 store 激活/持久化并按目标页递增 reloadVersion；右键菜单位置按视口限界并支持 Escape。
- Identity server-loader 列表仅为后端实际消费的字段开放列头筛选，Unsupported 展示列显式 `filter:false`；新增测试证明用户 email/phone/lastLoginOn 不生成无效筛选。
- 收尾门禁：定向 Vitest 5 files / 62 tests、最终全量 Vitest 62 files / 498 tests、typecheck、lint、build 均通过；相关 Mock Playwright 13/13 通过。真实视觉验收 1/1 通过，覆盖顶部/列头查询、列宽拖动、标签右键菜单和表格全屏；vxe 加载态控制台错误、空白工具图标和 action slot 原生按钮样式已在组件层收敛。六个本轮生成截图已恢复，临时视觉用例与 UnifiedHost Playwright report 已删除，不作为交付物。

### 第五轮续作验证补记（2026-08-28）

- 已保留第五轮 AppDataTable 原生 custom、分页、快速搜索、列头真实筛选、统一下载与打印实现及 Identity/SystemData 查询契约改动；未扩展新业务模块。
- TDD RED/GREEN：AppDataTable 4→16/16、快速搜索/打印 2→18/18、文件名 1→18/18、Identity Users 映射 2→5/5；最新定向组合 41/41。
- 门禁：frontend `vue-tsc --noEmit`、lint、Vitest 62 files/506 tests、隔离输出 Vite build 2396 modules 通过；backend fresh Release build 0/0、全量 1,301 passed/3 skipped/0 failed。
- 本轮一次 Mock 单用例 1/1 通过；同配置 8 用例在 teardown 无汇总后已安全终止，未计通过。真实 UnifiedHost 本轮因 PostgreSQL 云依赖不可达启动失败，真实 Playwright 未执行，不能把本轮最终验收标记为 complete。临时文件和构建/报告产物已清理，截图基线无改动。

### 第五轮当前会话独立验收（2026-08-28）

- 前端最终门禁：等价无写入 `vue-tsc --noEmit` 退出 0、ESLint 退出 0、Vitest 62 files / 507 passed / 0 failed、隔离输出 Vite build 2396 modules / 退出 0；仓库标准 `vue-tsc --build` 仅因活动进程锁定 `node_modules/.tmp/tsconfig.app.tsbuildinfo` 未能写入，代码类型检查无错误。
- 后端 fresh Release build：0 warning / 0 error；全量测试 1,301 passed / 3 skipped / 0 failed。
- 视觉复核：五张 1440×900 当前版本截图已人工检查，排序箭头与标题同行且为浅色、列头筛选位于标题下新增行、原生列设置完整可见、平台表格设置独立、下载包含原生格式与 Excel 字段/数量、打印支持选择当前可见列。
- 本轮 UnifiedHost 真实复验：`100.77.108.0:5432/6379/5672` 均不可达，UnifiedHost 因 PostgreSQL 连接失败退出，真实 Playwright 未执行；此项为外部联调缺口，未写成通过。

### 第五轮最终布局续作（2026-08-28）

- TDD RED/GREEN：先加入 AppDataTable 工具栏固定顺序、业务操作上置、独立筛选行、单/多选摘要与分组契约；RED 为 22 项中 18 通过、4 失败。完成最小实现后定向 `AppDataTable.spec.ts` 为 22/22 通过。
- AppDataTable 仅在公共组件内调整：左侧查询/排序/分组/快速搜索/下载/打印，右侧清空/刷新/表格全屏/列设置/表格设置；业务 action slot 上置；分组按当前已加载数据生成层级行并按用户选择顺序保留多字段层级；页脚仅显示选择行数和清空选择。
- vxe 4.15.13 没有整表 header slot；组件保留 vxe 标题、排序和列能力，并在其原生 `thead` 中同步独立筛选 `tr`，结构列保留空单元格对齐，筛选事件继续使用统一 query-change 契约。`groupable` 元数据仅补充平台列契约。
- 门禁：临时 tsBuildInfo 路径下 typecheck 通过；ESLint 通过；最终全量 Vitest 62 files / 510 passed / 0 failed；fresh `vue-tsc` + 隔离输出 Vite build 2396 modules / 0 error 通过。默认 build 仍受既有 `node_modules/.tmp` 写锁影响，但等价隔离构建已验证源码。
- 浏览器验证：本轮使用 `VITE_AUTH_MODE=mock` 启动临时开发入口并尝试 Identity/SystemData Playwright；两次 Playwright 启动均超过 60 秒无输出后安全终止，未计为通过。UnifiedHost 真实页面未执行，沿用上一条云依赖不可达缺口，不标记最终完成。现有入口为 `/pc/ui-baseline`；本轮未新增可证明当前 checkout 的截图。
- 临时 config/spec/cache/dist 已清理；未提交、未推送，未修改或暂存 `DSH.md`，未改 Gateway/UnifiedHost/后端模块。
