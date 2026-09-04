# PF-02 行政组织、菜单与管理员初始化修改计划

> 状态：2026-09-03 用户已要求“分发任务开始执行”，本计划转入实施；尚未宣称实现或验收通过。执行轮次：`PF02-20260903-功能整改`。
> 执行者须先读取 `industrial-platform-task-collaboration`、`industrial-platform-management-page` 及选型引用。复用原开发与独立验收任务，稳定交接，不创建逐步骤派遣卡、不逐项询问用户；允许文件及旧纯样式边界的精确例外以 `docs/tasks/active/PF-02.md` 末尾最新执行补充为准。

**Goal:** 修复企业登录源、租户主题策略和服务初始化编排的阻断问题，补齐固定管理员专用的表结构与基础数据初始化，再完成行政组织卡片、可配置菜单及平台入口整理。

**Architecture:** 行政组织、制造结构和权限继续分域。组织页复用现有树数据、选择状态和右侧岗位表；菜单补齐既有草稿/发布/runtime 链。初始化复用 Service Initialization V2 和各服务自有 initializer，SystemData 只编排，不跨域直接建表或写种子；先校验真实表结构、完整迁移集合与基础数据事实，再判定就绪。

**Tech Stack:** 当前仓库的 Vue 3、TypeScript、Pinia、Element Plus、AppDataTable、.NET、现有 SystemData 持久化、Vitest 与 Playwright；不引入新 UI 库或拖拽库。

**Spec:** 本任务中用户确认的业务边界、卡片参考图、管理员初始化要求与三页错误反馈，以及 `docs/blueprint/05-Industrial Platform平台基础功能与独立模块设计.md` §4.2、`docs/implementation/05-Industrial Platform SystemData开发实施方案.md` §7.3～7.6、§9～10、`docs/blueprint/33-Industrial Platform SystemData数据库编排与环境引导.md` V3.1 §2、4～9、12。下列改动均为待实施方案，不宣称当前已修复；错误结论来自代码核查，尚未连接用户数据库核实实表和账本。

## 1. 固定边界与基线

| 领域 | 本轮明确的职责 | 不混入的内容 |
| --- | --- | --- |
| SystemData | 公司、职能部门、可选科室/班组、岗位、任职、菜单及平台配置 | 工厂、车间、楼层，不把部门类型改成制造地点 |
| MasterData | 后续的工厂 → 车间 → 车间楼层；关联所属公司 | 本计划不创建实体、页面、数据库表或启动 MasterData |
| Identity | 用户、角色、权限 | 岗位不自动授予角色；菜单可见性不替代 API 授权 |

- 财务、采购、计划、工程、生产、信息 IT、运维是部门名称，不新增对应枚举，不在真实环境自动填充示例部门。
- 保留既有 Company/Department/Section/Team 数据、父子约束、组织移动、启停依赖、岗位与任职历史；不做人事职级、薪酬、编制或审批流。
- 用户截图只作为“左侧紧凑卡片单选、右侧详情与业务表格”的信息布局参考，不复制物料字段、红色标注、图片配色或工具图标。
- 最新补充优先级：先修三页错误与初始化就绪判断，再落实固定管理员边界及初始化闭环，随后处理卡片、菜单和入口布局。
- 企业登录源缺列属于 Identity 持久化兼容修复，不把 SSO 配置搬到 SystemData。新增管理员限制不等于重做 Identity 授权体系，也不允许岗位/普通角色配置取得初始化特权。
- 按当前用户管理黄金页统一按钮主次、危险色、间距、焦点、主题和表单。黄金页只读，不为降低验收标准修改它。
- 代码检查基线：`develop`，HEAD `65c06d5b4102ec02a1b413ca11b7bd9f43901793`，存在未提交修改及正在进行的视觉返修；正式实施前必须读取稳定交接后的最新差异，不覆盖现有修改。
- 计划编制期仅只读诊断和编写文档；现按用户授权由原开发任务实施并自验证，原独立验收任务在稳定交接后验收。现有用户数据库仅作必要只读诊断，迁移/播种验证使用安全隔离环境；不干扰调试进程、不提交推送。

## 2. 行政组织与岗位：卡片式主从页

### 2.1 左侧组织选择区

- 保持左右主从布局，桌面以现有 `AppTreeTableLayout` 的 320px 主栏为起点；左侧纵向滚动，不能再出现列被裁剪、表格与容器双横向滚动。
- 将窄树表替换为**保留层级的紧凑卡片列表**：公司、部门关系可展开/收起；不是把全部节点无层次平铺，也不是每个部门占半屏的大卡片。
- 卡片内容固定为：组织图标、名称、组织编码、类型、启用状态。子组织补父级路径，长名称换行或截断后可查看完整文本；不加当前 API 未提供的虚假人数、岗位数或 KPI。
- 选中卡片使用平台主题选中色、边框及文字标记；焦点态与选中态分开，不能仅靠颜色。卡片主体只负责选择，不堆编辑、停用、移动按钮。
- 顶部提供按名称/编码查找、刷新、展开/收起、清空选择；搜索保留匹配节点的祖先路径。已有组织导出能力保留在独立工具入口，不把列设置、打印、排序/分组配置器搬进卡片。
- 优先使用 Element Plus 树的自定义节点槽承载卡片，复用现成树导航与键盘能力；需要局部封装时使用 `OrganizationMasterList.vue`，不抽象新的全平台表格/卡片引擎。

### 2.2 右侧组织详情与岗位

- 上方显示当前组织名称、编码、类型、所属路径和状态；下方继续使用 `AppDataTable` 展示该组织岗位，不增加没有数据来源的页签。
- 页级操作是“新建公司”；选中组织的业务操作放右侧上下文区：新增下级组织、编辑组织、移动、启用/停用。新建岗位放岗位表的业务操作区。
- 新建下级时默认采用合法的部门类型；科室、班组保留为按需选项，不要求建满每一层。类型与父组织候选按现有领域规则过滤，后台仍独立验证。
- 岗位只维护编码、名称、描述、顺序、状态及所属组织，不强迫管理员先建立精细岗位体系才能配置系统权限。
- 创建/编辑仍用 `AppFormDrawer`，保留居中/右侧偏好；移动仍有预览与版本校验，启停仍有依赖校验和确认。
- 卡片点击、键盘选择与程序恢复全部进入同一个选择入口。清空选择清空详情、岗位、移动预览；切换过快时旧响应不能覆盖新选择。筛选隐藏选中项时清空选择，避免右侧操作隐含对象。
- 窄屏沿用现有主从堆叠，左侧设合理最大高度，右侧正常浏览；不新增可拖动分隔器。

### 2.3 拟用组件契约

这是计划中的局部选择器契约，不改变组织 API：

```ts
// src/frontend/src/components/systemData/OrganizationMasterList.vue
defineProps<{
  nodes: readonly OrganizationNodeDto[]
  selectedNId: string | null
  loading?: boolean
}>()
defineEmits<{
  select: [nId: string | null]
  refresh: []
}>()
```

`select(nId)` 继续调用 `store.selectOrganization(nId)`；`select(null)` 调用 `store.clearOrganizationSelection()`。当前 store 已有请求序号与空选清理，必须复用，不重复实现。

## 3. 菜单管理：配置 → 预览 → 发布生效

### 3.1 面向管理员的页面

- 页面显示名改为“菜单管理”；保留 `/pc/systemdata/navigation`、route name 和既有权限标识，避免破坏链接与权限。
- 左侧是真实菜单树，展示草稿与启停状态；右侧显示选中节点详情和当前发布版本。新增/编辑使用统一表单，预览按需展开，不把三个长表单固定挤成三栏。
- 用“目录”“菜单页面”“目标页面”“发布生效”替代常规操作中的 Group、Link、ResourceNId、Snapshot。路由名、资源标识、权限回执、版本校验和放只读高级信息。
- 本稿建议首期支持现有外壳能表达的“平台分组 → 业务目录 → 页面菜单”，同时允许平台分组直接包含页面。分组/目录均沿用 Group，页面沿用 Link；不新建第三套资源类型。
- 目录可以由管理员自行命名、增减和调整；本轮不做无限深度目录、拖拽编辑器、任意 URL/脚本、动态生成业务页面。超出支持层级时在编辑和发布时明确提示，不能保存成功后在侧栏静默丢失。已有草稿不自动删除或压平。
- 菜单字段：名称、图标、上级目录、目标页面、显示顺序、可见终端、启停；功能开关关联放高级选项。目录不选择页面和权限，页面只能选择已注册且有效的 Page 资源；类型在创建后固定。
- 新增时自动生成一次稳定菜单编码，失败重试复用，不要求普通管理员了解内部 NId。编辑后点新增必须进入新建状态，不能误更新旧节点。
- 顺序先用序号、上移/下移；移动先用父目录选择，不引入拖拽依赖。图标从当前支持的图标集合选择。

### 3.2 必须补齐的真实功能

| 项目 | 当前代码问题 | 计划完成条件 |
| --- | --- | --- |
| 新增 | 页面未发送后端必填 NodeNId，编辑状态无明确新建复位 | 新建目录/页面可提交，重载存在，重复编码被拒绝 |
| 层级/移动 | 前端扁平化；没有移动用例 | 保存/移动校验父存在、同租户、为目录、不自指、不成环；保留稳定编码 |
| 完整编辑 | Update 只支持名称、图标、顺序，其他可编辑字段未持久化 | 目标页面、功能关联、终端设置均可重载验证；清空图标/功能有明确空值语义 |
| 顺序 | 表单没有顺序控件，runtime mapper 丢失顺序后按 ID 重排 | 管理树、预览、发布后导航、重登均按 DisplayOrder + NId 稳定排序 |
| 启停/恢复 | DELETE 实为停用，没有恢复入口，停用项读取不一致 | 管理端可查看停用项，按原编码恢复；有活动子项不隐式级联停用 |
| 名称/目录 | runtime 丢弃嵌套 Group；内置 locale 可能盖掉自定义名称 | 使用现有 NavigationGroup.sections/sectionId 映射业务目录；管理员名称优先，静态默认名才走内置翻译 |
| 发布后生效 | 发布/回滚仅刷新草稿，当前侧栏依赖后续刷新 | 成功后复用 runtime.refresh 与公开导航端口，当前会话立即显示新版本，无需 WebSocket |
| 编辑并发 | 后端 CAS 未携带客户端读取的草稿版本 | 保存、移动、状态变更、发布校验预期草稿版本；409 保留输入，不静默覆盖或自动重试 |

接口扩展须遵循：创建必要字段在 TypeScript 和后端都明确必填；既有标识与租户来源不可由编辑修改；新完整配置请求明确区分“省略不修改”和“显式清空”。保留旧名称/图标/顺序更新用例和字段语义；将草稿版本前置条件作为契约增量统一升级调用方，不允许旧请求绕过并发校验。移动、状态操作使用明确用例，不借删除重建模拟，不由前端直接操作存储。

本轮使用既有草稿/发布修订、事务和持久化端口；若需要扩充 DTO/领域方法或迁移，采用向后兼容增量，不重置现有快照、菜单或模块清单。

### 3.3 首次配置与发布安全

当前未配置时侧栏来自静态默认菜单，发布后整体换成后台快照。因此“管理草稿为空”不等于“系统没有菜单”。首次只发布一个新菜单可能意外移除其他入口，这必须作为核心验收项。

- 用当前真实路由及受信任模块资源清单形成默认菜单基线；未配置时提供明确的“载入默认菜单草稿”操作，不在读页面时偷偷写入。
- 当前内置种子仅有有限资源声明；需核对资源 RouteName 是否为真实路由名、支持终端是否准确，并补齐可管理入口。普通管理员不能手填组件路径、假路由或伪造权限注册回执。
- 导入是版本化、可预览、幂等的草稿操作，不自动发布，不覆盖已有编辑或已发布配置；资源缺失时明确列出阻塞项，不能把无法打开的菜单包装成成功。
- 首次发布显示相对当前有效菜单的新增、修改、移除摘要；不在没有明确变更的情况下清空原有授权入口。
- 区分“草稿预览”和“当前生效菜单”；草稿预览复用真实权限、功能开关、终端和空目录过滤规则。
- 保存草稿不改变当前导航；发布才切换。回滚恢复上一已发布版本并形成新修订，保留历史，不用本地缓存伪装服务端成功。
- 不重设计 Identity 权限。查看、管理、发布、回滚保持分权；无资源访问权限的用户不能因为配置了菜单而访问目标 API。

## 4. 管理员初始化与三页故障整改（优先）

### 4.1 已确认的代码问题与待核实边界

| 页面/错误 | 代码证据与判断 | 计划处理 |
| --- | --- | --- |
| 企业登录源：`allowed_email_domains` 不存在 | `IdentitySchemaMigrations.cs:326` 的 `ID-004-12` 为 `identity_sso_provider` 建列 `allowed_email_domains_json`，`SsoProviderTable.cs:80` 映射为 `allowed_email_domains`；同表 `jit_default_role_n_ids` 也缺少 DDL 中的 `_json` 后缀。这是已确认的映射不一致，不能只解释成旧库未升级 | 以受控迁移定义为基准对齐同表映射；先查实列与数据，遇历史不同形状使用追加兼容迁移保留数据，不盲目加一列空数据掩盖错误 |
| 服务初始化编排：`uses_service_initializer` 不存在 | 注册表字段已有 `SDM-004-05` 增补迁移 | 与下行一起核对完整待执行迁移集合，补齐升级，不删除重建注册表 |
| 服务初始化编排：`service_requires_apply` 不存在 | 计划表字段已有 `SDM-004-06` 增补迁移 | 保留既有历史计划与 nullable 兼容语义，迁移后验证真实注册/计划查询 |
| 租户主题策略：`[SD_VALIDATION_FAILED] Theme policy was not configured.` | `ThemePolicyControlService.GetAsync` 对空策略抛 ValidationException，Controller 捕获成 400 且该分支不记录异常；store/Frame 将其统称“管理接口不可用”。这能解释用户看到页面错误而后台没有异常日志的现象，不是接口失联的证据 | 管理 GET 复用已存在的 `Configured=false` 契约；补未配置状态和显式首次保存，保留 403/5xx/网络故障反馈 |

SystemData 的共同缺口已可从调用链确定：

- `SystemDataServiceInitializer.InspectAsync` 只读取账本最大的 `MigrationId`，并据此返回 Ready；`PlanAsync` 只比较该值和目标版本。
- UnifiedHost 仅在 `plan.RequiresApply` 时调用 Apply。已有 `SDM-016-01`、但缺少后来补录的 `SDM-004-05/06` 时，最大编号仍匹配，升级会被跳过。
- `SchemaMigrationRunner` 本身会按完整 ID 集合补跑未记录步骤；问题之一是初始化判定没有让它进入执行，而不是缺少两条建列 SQL。当前单测中的 Noop runner 只写最大编号，不能证明数据库结构完整。
- 真实现场仍需只读确认：运行构建版本、当前数据库/Schema 身份、实际列、两条迁移账本记录。若账本已经记录执行但列仍缺失，按结构漂移处理，不删账本重跑，也不擅自更换连接目标。

### 4.2 初始化功能的用户入口

现有页面已包含注册、种子集合、计划、操作和环境策略。补的是可理解、可执行、可验证的管理闭环，不另造一个初始化系统。

- 保留当前路由，显示名使用“服务初始化”，说明明确“检查并补齐服务表结构与基础数据”；默认呈现已注册服务和当前状态。
- 选中一个服务/初始化单元后，显示当前/目标迁移版本、待执行迁移、基础数据版本与缺失项、环境策略、最近执行结果。只使用服务返回的脱敏事实，不让前端推断数据库状态。
- 主操作为“检查状态 → 生成初始化计划 → 执行初始化/补齐升级 → 查看结果”。复用现有异步 Operation、幂等键、步骤、轮询和安全取消，不用长同步 HTTP 等待。
- “检查状态”不建表、不写种子；“生成计划”只可写本次编排计划元数据，不执行目标库 DDL/DML；执行前明确服务、环境、逻辑目标、缺失项和影响摘要。
- 表结构初始化只执行受控版本迁移；基础数据按 SystemBaseline/TenantBaseline 版本补齐。已有数据不清空、不覆盖；“初始化”不等于“恢复出厂”。
- RequiredSeed 随依赖迁移执行；租户默认配置只作用于当前可信租户。演示数据仅在 Development/Test 显式允许时执行，生产拒绝；不自动创建企业登录源、部门样例或测试账号。
- 管理员密码及 SecretBootstrap 继续使用 Identity 的既有安全引导，不提供“重新初始化管理员”或固定默认密码，不把密钥放入表单、Operation、日志。
- 注册技术细节和高级策略放次级区域；操作按钮、表格、详情与表单遵循黄金页及管理页组件，不再依赖用户手填技术标识才能完成正常初始化流程。

### 4.3 固定管理员权限

- 人工管理入口和管理 API 仅允许 Identity 权威认定的有效平台管理员：当前可信租户中持有未删除、`IsSystem=true` 的内置 `SYSTEM_ADMIN` 角色，按既有直接分配/有效用户组继承规则求值。`ADMIN` 只是初始账号，不是唯一合法管理员用户名。服务端增加不可被普通权限勾选替代的门禁，不只隐藏按钮、不按客户端字段判断。
- 在管理员门禁内继续校验现有注册/计划/执行/审批/备份/取消权限和环境策略；管理员身份不是跳过备份、审批、租户隔离或目标验证的理由。
- 非管理员即便被普通角色分配了 `systemdata.service-initialization.*`、旧 V1 权限，或持有历史 `permission_nid` 声明，仍不得进入或调用管理接口。角色/用户组授权 API 也要阻止把这些特权作为普通可分配权限下放；对历史授权无损失效，不删除整个用户角色。
- V2、仍可调用的 V1、导出及管理详情统一保护，防止换路径绕过。管理员资格或会话失效后应拒绝新请求；排队后真正进入变更阶段前复核有效执行授权，不保存用户 Token 供长期重放。
- 当前 JWT 带角色快照，角色变更不推进 AuthVersion，因此只加 `RequireRole("SYSTEM_ADMIN")` 不足。复用 Identity 权威查询，在保留权限裁决中读当前管理员资格，SystemData 对保留权限不得走 `permission_nid` 直接放行路径；非初始化权限保持原缓存行为。普通角色管理者也不能经用户/用户组赋角或组成员变更自升为 SYSTEM_ADMIN，合法管理员管理管理员与最后管理员保护保持现有业务约束。
- 内部服务初始化端口仍只接受专用受信服务身份，不开放给普通登录用户，不使用“内部调用”绕过人工管理员校验。一般健康/readiness 可保留最小脱敏契约，不向普通用户暴露结构、计划和执行详情。
- 用户“不反复询问”的协作要求不取消产品在 Production/Advanced 下的强制审批、备份和验证规则。

### 4.4 版本、基础数据与自举规则

- 服务 Inspect/Verify 必须核对期望迁移 ID 的完整集合、关键表列与 RequiredSeed 事实，不能仅比较最大版本号；缺失必需事实返回 NotReady，提供脱敏缺项。
- 把 SystemData 的 migration 与必要 baseline seed 纳入同一个服务自有初始化顺序及验证。不能把 `SDM-013～016` 的占位 `SELECT 1` 或后台任务已启动当成基础数据已经就绪。
- 重复执行依据服务本地账本/版本/校验和跳过已完成项；迁移和种子都有可验证恢复点，多个执行者按目标锁串行，失败不能记作成功。已有版本不原位改写，后续修正用新增版本。
- SystemData 本身缺表/缺列时，不依赖已经报错的注册/计划 API 来修复自己：沿蓝图 33 §9 使用受控部署入口调用同一个本地 initializer，先建立最小 Schema/SystemBaseline，之后才开放管理 API/Runner。Identity 首次管理员也通过既有受控 bootstrap 完成；不增加匿名网络初始化入口。
- UnifiedHost 与独立宿主遵守相同事实验证与环境策略；Production 不因为修缺列改为启动自动迁移。Development 的自动补齐只按显式配置运行，不通过重启用户当前调试进程实施修复。
- 真正的数据库不可用、列漂移、缺少必需数据要分别报告，记录脱敏错误码/TraceId；不能吞掉 SQL 异常返回空列表，也不能把一次迁移成功当作三个页面全部恢复。

### 4.5 租户主题策略首次配置

- 未配置是独立业务状态：显示“当前未配置租户策略，使用平台默认主题”，有管理权限时提供“创建租户策略”；只读用户看到说明，无保存按钮。
- GET 不隐式创建记录；复用现有 `ThemePolicyResponse.Configured` / `ThemePolicyDto.configured`，返回 `configured=false` 而不是另建空态 DTO。区分 403、网络故障和 5xx，不能靠匹配英文错误文本或吞掉所有 `SD_VALIDATION_FAILED` 实现；未配置记录数为 0。
- 首次保存沿用现有 PUT/Upsert：首次提交 expected policy revision 为 0，编辑提交读取到的 revision；不匹配返回稳定 409，默认值不在 Allowed 集合等验证错误返回 400，不能落入通用 500。失败保留输入且不污染已保存快照。保存后重新 GET 并刷新实际主题策略；“重新读取”必须发 GET，不能只把 store 再复制一遍。
- 对齐真实主题枚举：后端当前输出 `IndustrialCyan / Light / Comfortable`，前端使用 `industrial-cyan / light / comfortable`。实施 05 §7.9 已批准后一组格式；在服务 DTO 的共同转换处统一管理 GET/PUT 与 runtime 对外格式，保持内部持久化枚举，必要时兼容旧 PascalCase 入参。未知值明确拒绝，不再让各前端分别兜底猜测，也不能只靠 Mock 已经写成前端格式证明契约正确。
- runtime 的未配置与已配置响应不能分别混用控制面 revision 与 policy revision 形成相同 ETag。验收“未配置缓存 → 首次保存 → 重新读取”必须返回变化后的策略，而非错误 304；业务编辑 revision 与缓存验证标识保持清晰职责。
- 主题默认策略可由显式 TenantBaseline 初始化补齐，但必须先查是否已有管理员策略；已存在则保留，不因补种或升级覆盖允许色板、默认模式与密度。
- 区分平台默认主题与已持久化租户基线：当前平台缺省模式为 `system`，现有租户基线为 `Light`，不能把两者无声混为一谈或在 GET 时把默认值变成租户策略。
- 不要求补齐主题配置才能显示该管理页；如果主题基线不是 RequiredSeed，缺省策略不应阻断整个服务 readiness。

## 5. 组织平台入口与术语整理

将默认“组织域平台”下混排的入口按职责整理，复用现有公开 sections 配置：

| 默认分区 | 页面 |
| --- | --- |
| 组织与人员 | 行政组织与岗位、用户任职 |
| 菜单与平台配置 | 菜单管理、功能开关、租户主题策略 |
| 服务与运维 | 服务目录、服务初始化（表结构与基础数据，仅管理员） |

- 身份与访问分区不改业务内容；不出现工厂、车间、楼层或 MasterData 假入口。
- 仅调整默认导航元数据、名称与分区，不重构 PF-01 外壳，不改现有路由/权限标识。
- 已发布自定义菜单优先，默认分区更新不能覆盖管理员的实际配置。
- 统一“组织编码/岗位编码”“所属组织”“目标页面”等中英文文案；任职表明是人员职责关系，不暗示自动授权。
- 保留各页已实施的视觉整改；除本计划明确的组织、菜单、主题首次配置、初始化和企业登录源缺列外，其他业务只回归，不扩大整改。

## 6. 文件范围与实施顺序

按此处内部顺序连续推进，不另建 PF、不逐步要求用户放行。用户已明确启动，主控已将新功能范围同步到原工作包；执行者按最新工作包边界实施，不沿用旧纯视觉限制，也不泛化扩大范围。

### 阶段 1：稳定基线与失败场景

**文件：** 本计划；读取 `docs/tasks/active/PF-02.md`、`docs/evidence/PF-02.md`、`docs/status/CURRENT.md`；读取黄金页和受影响组件。

- [ ] 接收现有纯视觉返修的稳定交接，记录最新 HEAD、dirty diff、已完成按钮/页壳增量与剩余风险；不因本计划回退这些改动。
- [x] 主控已在原 PF-02 工作包明确新增“三页错误、固定管理员初始化、组织卡片、菜单完整用例、默认入口分区”和精确跨域边界；仅此项为派遣文档完成，不代表代码完成。
- [ ] 建立缺列升级、主题首次配置、管理员授权、组织/菜单隔离夹具及下节失败用例，先复现当前缺口。已有幂等、选择竞态和权限回归继续执行，不重复建设。

### 阶段 2：先恢复三页与数据库事实检查

**修改：**

- `src/backend/src/Services/Identity/IndustrialPlatform.Identity.Infrastructure/Persistence/Entities/SsoProviderTable.cs`：对齐两处 JSON 列映射。只有现场存在另一种历史列形状时才追加 `src/backend/src/Services/Identity/IndustrialPlatform.Identity.Infrastructure/Persistence/Migrations/IdentitySchemaMigrations.cs` 的兼容迁移；不改写旧迁移。
- `src/backend/src/Services/SystemData/IndustrialPlatform.SystemData.Infrastructure/DatabaseOrchestration/Initialization/SystemDataServiceInitializer.cs`：完整待执行集合与关键表列检查、Plan/Verify 事实判断。
- `src/backend/src/Services/SystemData/IndustrialPlatform.SystemData.Infrastructure/Persistence/Migrations/ISchemaMigrationRunner.cs`、`src/backend/src/Services/SystemData/IndustrialPlatform.SystemData.Infrastructure/Persistence/Migrations/SchemaMigrationRunner.cs`：只在复用待执行查询需要时做最小接口增量；检查不创建账本。
- `src/backend/src/Services/SystemData/IndustrialPlatform.SystemData.Infrastructure/Persistence/Migrations/SystemDataSchemaMigrations.cs`：保留现有 `SDM-004-05/06`；只有账本与实表漂移需要受控修补时追加新版本。
- `src/backend/src/Services/SystemData/IndustrialPlatform.SystemData.Application/ControlPlane/ControlPlaneServices.cs`、`src/backend/src/Services/SystemData/IndustrialPlatform.SystemData.Contracts/ControlPlane/ControlPlaneContracts.cs`、`src/backend/src/Services/SystemData/IndustrialPlatform.SystemData.Api/Controllers/ControlPlaneControllers.cs`：主题未配置、首次保存版本与 ETag 契约。
- `src/frontend/src/components/systemData/ThemesAdminPage.vue`、`src/frontend/src/components/systemData/SystemDataAdminFrame.vue`、`src/frontend/src/stores/systemData/managementStore.ts`、`src/frontend/src/api/systemData/managementApi.ts`、`src/frontend/src/api/systemData/managementTypes.ts`、`src/frontend/src/api/systemData/types.ts`、`src/frontend/src/stores/systemData/runtimeStore.ts`：主题空态、保存与刷新。`src/frontend/src/systemData/runtime/navigation.ts` 继续按已批准小写枚举消费，回归即可；不为服务端错配另造多套前端转换器。

**测试：**

- 新增 `tests/Identity/IndustrialPlatform.Identity.Tests/Infrastructure_SsoProviderSchemaTests.cs`：执行真实迁移后调用 SsoStore，不用实体 CodeFirst 建表替代生产 DDL。
- 扩充 `tests/SystemData/IndustrialPlatform.SystemData.Tests/SystemDataServiceInitializerTests.cs`、`tests/SystemData/IndustrialPlatform.SystemData.Tests/Infrastructure_SchemaMigrationRunnerTests.cs`、`tests/SystemData/IndustrialPlatform.SystemData.Tests/Infrastructure_SystemDataOrchestrationStoreTests.cs`。
- 扩充 `tests/SystemData/IndustrialPlatform.SystemData.Tests/Application_ControlPlaneServiceTests.cs`、`tests/SystemData/IndustrialPlatform.SystemData.Tests/Api_ControlPlaneEndpointTests.cs` 及前端主题的 component/contract/runtime 现有测试。

- [ ] 在安全隔离库复现：SSO 按真实 DDL 建表后整行查询失败；SystemData 已有最大迁移记录但缺 `SDM-004-05/06`；空租户主题 GET 返回 400。记录真实提供程序，不将 SQLite 结果写成 PostgreSQL 已通过。
- [ ] 对齐 SSO 两列并完成空查询、新增/修改/重载；同时回归登录源发现接口，不实际发起企业 SSO 授权或改远端身份提供商。
- [ ] 修正初始化事实检查，补跑缺失迁移后验证注册/计划列表均能读取；账本称已执行而实列缺失时明确报 Drift/NotReady，不伪造已完成。
- [ ] 主题管理 GET 返回未配置状态；本地草稿独立于已保存 store，首次保存可用，真实枚举合法，错误/冲突保留输入，首次配置后的 runtime 不误 304。
- [ ] 对受影响源文件运行新鲜 Release build 后再测；此阶段只在隔离验证环境写入，不修用户现有库。

加入现有 `SystemDataServiceInitializerTests` 的失败断言示例（使用该类已有字段与 CreateContext；本轮未执行）：

```csharp
[Fact]
public async Task Latest_record_does_not_hide_missing_migrations()
{
    _dbContext.SqlSugar.CodeFirst.InitTables<SchemaMigrationRecord>(); // 仅建立隔离测试账本
    var latest = SystemDataSchemaMigrations.All[^1];
    await _dbContext.SqlSugar.Insertable(new SchemaMigrationRecord
    {
        MigrationId = latest.Id,
        Description = latest.Description,
        AppliedOn = DateTimeOffset.UtcNow,
    }).ExecuteCommandAsync();
    var context = CreateContext(latest.Id);
    var state = await _initializer.InspectAsync(context, CancellationToken.None);
    var plan = await _initializer.PlanAsync(context, state, CancellationToken.None);
    Assert.False(state.Ready);
    Assert.True(plan.RequiresApply);
}
```

另外必须使用“执行除两条增补外的全部真实旧迁移”的升级夹具，才能验证补齐前后实列、数据保留和重复执行；上述单个账本断言不能替代升级测试。

### 阶段 3：固定管理员初始化闭环

**修改范围：**

- 既有 `src/backend/src/Services/SystemData/IndustrialPlatform.SystemData.Api/Controllers/ServiceInitializationController.cs`、`src/backend/src/Services/SystemData/IndustrialPlatform.SystemData.Api/Controllers/DatabaseOrchestrationController.cs`、`src/backend/src/Services/SystemData/IndustrialPlatform.SystemData.Api/Authorization/SystemDataPermissionPolicies.cs`、`src/backend/src/Services/SystemData/IndustrialPlatform.SystemData.Api/Authorization/SystemDataPermissionAuthorizationHandler.cs`：V1/V2 同源管理员保护，阻断旧 permission claim 旁路。
- `src/backend/src/Services/Identity/IndustrialPlatform.Identity.Application/Authorization/PermissionEvaluator.cs`、`src/backend/src/Services/Identity/IndustrialPlatform.Identity.Application/Authorization/IAuthorizationDataStore.cs`、`src/backend/src/Services/Identity/IndustrialPlatform.Identity.Application/Authorization/AuthorizationSnapshot.cs`、`src/backend/src/Services/Identity/IndustrialPlatform.Identity.Infrastructure/Authentication/AuthorizationDataStore.cs`：仅补保留初始化权限的当前管理员资格判定与所需最小查询；非初始化授权语义不变。
- `src/backend/src/Services/Identity/IndustrialPlatform.Identity.Application/Management/RoleManagementService.cs`、`src/backend/src/Services/Identity/IndustrialPlatform.Identity.Application/Management/UserManagementService.cs`、`src/backend/src/Services/Identity/IndustrialPlatform.Identity.Application/UserGroups/UserGroupService.cs`：阻断普通赋予保留权限/系统管理员身份的绕行，保留合法管理和最后管理员约束；现有登录/当前用户的有效权限响应按同一保留规则过滤，不只在菜单硬编码隐藏。
- `src/backend/src/Services/SystemData/IndustrialPlatform.SystemData.Infrastructure/Reliability/SystemDataBaselineSeedRunner.cs` 与 SystemData 自有 initializer：复用安全、幂等且不覆盖管理员数据的 seed 用例；必要时提取现有 seed 执行逻辑供显式调用，不重复实现第二套。
- `src/backend/src/Services/SystemData/IndustrialPlatform.SystemData.Application/DatabaseOrchestration/DatabasePlanService.cs`、`src/backend/src/Services/SystemData/IndustrialPlatform.SystemData.Application/DatabaseOrchestration/DatabaseOperationService.cs`、`src/backend/src/Services/SystemData/IndustrialPlatform.SystemData.Application/DatabaseOrchestration/Runner/DatabaseOperationRunner.cs` 及现有 initialization contracts：仅补事实摘要、固定授权检查与执行/验证闭环。
- `src/backend/src/Hosts/IndustrialPlatform.UnifiedHost/ModuleMigrationCoordinatorHostedService.cs`；必要时涉及 Identity/SystemData 已有独立启动包装器，仅统一既有 initializer 的只读检查/受控执行及环境政策。保留 Development 显式自动初始化和首次部署入口，不无条件关闭用户调试能力，不创建新 Host。
- `src/frontend/src/components/systemData/ServiceInitializationAdminPage.vue`、`src/frontend/src/pages/pc/systemData/ServiceInitializationPage.vue`、对应 `managementApi.ts`、`managementTypes.ts`、`managementStore.ts` 与 SystemData locale：呈现第 4.2 节闭环；使用返回的有效权限，不相信前端管理员勾选。
- `src/frontend/src/pages/pc/identity/IdentityRolesPage.vue` 的权限选择区、对应 Identity locale：只标明系统保留权限不可勾选，保留当前黄金风格与其他角色操作。不改只读黄金样板 `IdentityUsersPage.vue` 的布局或按钮。

**接口原则：** 原 `IServiceInitializer.InspectAsync/PlanAsync/ApplyAsync/VerifyAsync` 与 V2 异步 Operation 是执行端口；必要只添加非敏感缺项/版本摘要，服务仍拥有自身 Migration/Seed。优先让现有 Identity 权限裁决对保留权限执行管理员硬门禁，使 UnifiedHost 进程内和独立 HTTP 适配器得到同一裁决，不建立第二份管理员名单。

**测试：** `tests/SystemData/IndustrialPlatform.SystemData.Tests/Authorization_SystemDataPermissionAuthorizationHandlerTests.cs`、`tests/Identity/IndustrialPlatform.Identity.Tests/Application_PermissionEvaluatorTests.cs`、`tests/Identity/IndustrialPlatform.Identity.Tests/Application_RoleManagementServiceTests.cs`、`tests/Identity/IndustrialPlatform.Identity.Tests/Application_UserManagementServiceTests.cs`、`tests/Identity/IndustrialPlatform.Identity.Tests/Application_UserGroupServiceTests.cs`、`tests/UnifiedHost/IndustrialPlatform.UnifiedHost.Tests/UnifiedHostInitializationOrderTests.cs`、`tests/UnifiedHost/IndustrialPlatform.UnifiedHost.Tests/InProcessSystemDataPermissionEvaluatorTests.cs`；对应初始化 API/seed/页面测试。新增隔离 E2E：`src/frontend/tests/e2e/systemdata-initialization-admin.spec.ts`。

- [ ] 先补非管理员持有初始化普通权限仍拒绝、历史 Token/claim 旁路拒绝、撤销管理员资格后拒绝、普通用户/用户组赋角不可自升权的测试；合法管理员仍满足原授权与业务门禁。
- [ ] 管理初始化入口仅管理员可见/可调用；前端所有权提示为系统固定，普通角色授权不能勾选授予；服务身份仅使用已有受信内部通道。
- [ ] 串通“检查 → 计划 → 显式执行 → 结构与数据验证 → Operation 完成”。缺表、缺增补迁移、缺 RequiredSeed、已有管理员自定义数据分别验证。
- [ ] 覆盖幂等重试、多执行者串行、失败恢复、无效目标、Production/Advanced 审批与备份、受控首次空库自举。基线补齐不能重置主题/菜单/账号或把现有 V2 注册错误改成 legacy 执行。
- [ ] 独立验收同时检查 UI 与真实 HTTP 拒绝路径；环境不足的场景明确未验证，不用源码断言、Mock 或账本最大编号冒充验收通过。

### 阶段 4：组织卡片主从改造

**修改：** `src/frontend/src/components/systemData/OrganizationsAdminPage.vue`；必要 SystemData locale。

**新增：** `src/frontend/src/components/systemData/OrganizationMasterList.vue`、`src/frontend/tests/components/OrganizationMasterList.spec.ts`。

**复用/只读：** `src/frontend/src/components/management/AppTreeTableLayout.vue`、`src/frontend/src/components/management/AppDataTable.vue`、`src/frontend/src/components/management/AppDataTable.ts`、`src/frontend/src/components/management/AppFormDrawer.vue`、`src/frontend/src/stores/systemData/managementStore.ts` 的组织选择接口；若共享尺寸确需修复，仅作兼容布局增量并补 `src/frontend/tests/components/AppTreeTableLayout.spec.ts`。

- [ ] 用当前 OrganizationNodeDto 树和上述 select 契约实现卡片节点、搜索路径、选中/清空、键盘与窄屏布局。
- [ ] 将选择相关组织动作、岗位新增移至右侧对应上下文；结构化表单和确认逻辑沿用现有实现。
- [ ] 运行卡片组件、管理页及 store 竞态回归；以参考图布局和黄金页样式对照截图。

示例测试断言（新增测试中的目标行为，不代表本轮已运行）：

```ts
// wrapper 挂载 OrganizationMasterList，nodes 夹具包含 company-1/finance-1。
await wrapper.get('[data-testid="organization-card-finance-1"]').trigger('click')
expect(wrapper.emitted('select')).toEqual([['finance-1']])
await wrapper.setProps({ selectedNId: 'finance-1' })
await wrapper.get('[data-testid="organization-selection-clear"]').trigger('click')
expect(wrapper.emitted('select')?.at(-1)).toEqual([null])
```

### 阶段 5：菜单领域、契约与默认资源基线

**修改：**

- `src/backend/src/Services/SystemData/IndustrialPlatform.SystemData.Contracts/ControlPlane/ControlPlaneContracts.cs`
- `src/backend/src/Services/SystemData/IndustrialPlatform.SystemData.Domain/ControlPlane/ControlPlaneModels.cs`
- `src/backend/src/Services/SystemData/IndustrialPlatform.SystemData.Application/ControlPlane/ControlPlaneServices.cs`
- `src/backend/src/Services/SystemData/IndustrialPlatform.SystemData.Api/Controllers/ControlPlaneControllers.cs`
- `src/backend/src/Services/SystemData/IndustrialPlatform.SystemData.Infrastructure/Reliability/SystemDataBaselineSeedRunner.cs`
- 仅必要的 `src/backend/src/Services/SystemData/IndustrialPlatform.SystemData.Infrastructure/Persistence/SystemData/SqlControlPlaneStore.cs`；先复用现有持久化字段，只有实证存在存储缺口才追加映射/迁移，不触碰数据库编排与服务初始化业务。

**测试：** `Domain_ResourceNavigationFeatureTests.cs`、`Application_ControlPlaneServiceTests.cs`、`Api_ControlPlaneEndpointTests.cs`、`Infrastructure_ControlPlanePersistenceTests.cs`、`Infrastructure_ControlPlaneSchemaTests.cs`，均在 `tests/SystemData/IndustrialPlatform.SystemData.Tests/`。

- [ ] 为完整配置、移动、停用恢复、预期修订校验补失败用例；用非法父目录、跨租户、退休资源、无权限回执、过期修订验证失败后无写入。
- [ ] 实现用例与持久化往返，保留旧端点业务语义并统一升级版本前置条件；停用不是物理删除，恢复不换编码。
- [ ] 补齐受信任默认资源/菜单基线与导入预览；只声明实际存在的页面、已有权限、真实终端，不新增业务权限或改 Identity 数据。
- [ ] 验证当前默认入口与首次发布的完整性，未解决资源声明依赖时不能声称菜单管理闭环完成。
- [ ] 新鲜 Release build 后运行后端测试，记录成功/失败/跳过及真实环境层级。

### 阶段 6：菜单编辑、预览及运行生效

**修改：** `src/frontend/src/components/systemData/NavigationAdminPage.vue`、`src/frontend/src/api/systemData/managementTypes.ts`、`src/frontend/src/api/systemData/managementApi.ts`、`src/frontend/src/stores/systemData/managementStore.ts`、`src/frontend/src/stores/systemData/runtimeStore.ts`、`src/frontend/src/systemData/runtime/navigation.ts`；仅需要时调整 `src/frontend/src/api/systemData/types.ts` 和现有刷新协调器。

**测试：** `src/frontend/tests/components/SystemDataAdminPage.spec.ts`、`src/frontend/tests/components/SystemDataActionPermissions.spec.ts`、`src/frontend/tests/contract/systemDataManagementApi.spec.ts`、`src/frontend/tests/unit/systemDataManagementStore.spec.ts`、`src/frontend/tests/unit/systemDataRuntime.spec.ts`、`src/frontend/tests/unit/systemDataRuntimeStore.spec.ts`。

- [ ] 新建/编辑状态分离，表单按 Group/Link 显示真实可持久化字段；必要字段类型收紧，不以类型断言掩盖漏传参数。
- [ ] 实现目录树、父目录选择、排序、包含停用项及恢复；冲突保留输入并定位错误节点。
- [ ] runtime 将根 Group 映射为 NavigationGroup，第二层 Group 映射为 sections，Link 映射为 items；保留顺序、自定义名称和权限过滤，不改外壳私有状态。
- [ ] 统一草稿预览与实际消费的映射规则；发布/回滚成功后刷新当前终端 runtime，刷新失败明确区分“发布已成功/本地显示刷新失败”，不重复发布。
- [ ] 从新建至重登的完整闭环回归，不仅断言页面可达或按钮存在。

排序/目录夹具必须包含反字母顺序以暴露原缺陷：`z-menu.displayOrder=1`、`a-menu.displayOrder=2`；期望始终为 z、a。层级夹具为根目录 A → 子目录 B → 页面 C，期望 B 作为 section 可见且 C 可访问；不能接受 Group 在 mapper 中被过滤掉。

### 阶段 7：平台入口、视觉及独立验收

**修改：** `src/frontend/src/components/navigation/navigation.ts` 的默认元数据；`src/frontend/src/locales/zh-CN.ts`、`src/frontend/src/locales/en-US.ts`、`src/frontend/src/localization/systemData.ts`、`src/frontend/src/localization/types.ts` 中必要键；不重构 `src/frontend/src/layouts/PcLayout.vue` 或外壳渲染。

**新增测试：** `src/frontend/tests/e2e/systemdata-organization-cards.spec.ts`、`src/frontend/tests/e2e/systemdata-menu-management.spec.ts`，使用隔离夹具。

**扩充测试：** `src/frontend/tests/e2e/systemdata-admin.spec.ts`、`src/frontend/tests/e2e/systemdata-real.spec.ts`、`src/frontend/tests/components/shell/PlatformFunctionTree.spec.ts`。

- [ ] 应用默认分区和显示名；保留动态发布菜单优先级、旧路由、当前授权和已打开页面。
- [ ] 完成视觉矩阵及前端门禁，开发停止编辑，交原独立验收任务逐项验证；发现问题集中返修，不并行改源码。
- [ ] 验收通过后按原工作包授权更新 evidence、实施05、PF-02/总 TODO/索引/CURRENT 的对应状态；只关闭已实证的三页错误、初始化、卡片和菜单项。
- [ ] MasterData 层级只记录边界，不借此改写或实施 MasterData 方案；原真实三端/十三项初始化门禁仍按各自证据处理，不随本计划一起关闭。

## 7. 可验收结果清单

| 编号 | 场景 | 必须得到的结果 |
| --- | --- | --- |
| DB-01 | 企业登录源按真实迁移新建/旧库升级后查询与保存 | 两个 JSON 字段正确往返，空列表与发现接口无缺列异常，不丢历史数据 |
| DB-02 | 最大编号已最新，但缺 SDM-004-05/06 | Inspect 不误 Ready，Plan 包含缺项；Apply 后注册/计划查询成功，重复 Apply 无破坏 |
| DB-03 | 账本已记但实列缺失、连接目标不符 | 明确 Drift/NotReady，不自动删账本、重建或换库 |
| THEME-01 | 未配置主题、只读/可管理用户进入 | GET 成功且无写入，显示默认说明、记录数 0；仅原 manage 权限可首次保存 |
| THEME-02 | 首次保存 → 重载 → runtime，已有未配置 ETag | 枚举符合真实契约、配置实际生效、不能误 304；用户合法个人偏好保持原优先级 |
| THEME-03 | 两人首次创建/陈旧编辑/非法默认值 | 并发 409、验证 400、非 500，输入保留且已保存快照不被污染 |
| THEME-04 | 已有自定义策略但缺 seed ledger，再初始化 | 配置原样保留，重复执行仍不覆盖；真实 403/5xx 不伪装成未配置 |
| INIT-01 | 非管理员拥有普通初始化权限或旧 claim，调用 V1/V2 | 管理页不开放，管理 API 拒绝；普通赋予保留权限或自升 SYSTEM_ADMIN 均被后端阻断 |
| INIT-02 | 有效管理员、撤销管理员后的旧 Token、跨租户 | 仅当前权威资格合法者可执行；撤销/越权不靠旧角色快照继续放行 |
| INIT-03 | 检查/计划/执行/重复执行/失败恢复 | 检查不写目标库，计划不执行 DDL/DML；执行补齐真实 Schema/RequiredSeed 并验证，重试不重复或覆盖数据 |
| INIT-04 | Production 与显式 Development 自动策略 | Production 不启动自动改库，Advanced 缺审批/备份拒绝；Development 保留明确配置允许的受控路径 |
| INIT-05 | SystemData 自身未初始化、尚无管理员的首次部署 | 受控本地自举先建立最小事实，不依赖自身坏掉的管理 API；无匿名初始化后门 |
| ORG-01 | 公司/多层部门/长名称/停用节点 | 层级清楚、卡片可读，无横向裁剪或假统计 |
| ORG-02 | 点击与键盘选 B，再清空 | 高亮与右侧对象一致；清空后岗位为空且不请求空组织 API |
| ORG-03 | 快速选 A→B，A 响应后到 | B 的详情/岗位不被 A 覆盖 |
| ORG-04 | 新增部门/岗位、编辑、移动、启停 | 原权限、并发与依赖校验保留；不改制造模型或自动授予角色 |
| MENU-01 | 编辑旧节点后新建目录与子菜单 | 新编码一次生成，旧节点不被覆盖，重载仍存在 |
| MENU-02 | 移动、改目标页/图标/功能/终端、重新读取 | 字段全部一致；非法父级、资源及终端被拒绝 |
| MENU-03 | 停用、查看停用项、恢复 | 同编码可恢复；活动子项不被隐式级联 |
| MENU-04 | A→B→C，反字母序排序、自定义名称 | 管理树、预览、侧栏、语言切换与重登一致 |
| MENU-05 | 双编辑页同版本，A 保存后 B 保存/发布 | B 返回 409，输入保留，不能覆盖 A |
| MENU-06 | 首次导入默认草稿并新增一个菜单后发布 | 无隐式覆盖和默认入口丢失；差异摘要与最终快照一致 |
| MENU-07 | 保存草稿、发布、回滚 | 保存不影响侧栏；发布/回滚后当前会话更新；上一版可恢复 |
| MENU-08 | view/manage/publish/rollback 分权，目标页面无权 | UI 和 API 一致拒绝未授权动作；菜单隐藏不代替资源授权 |
| UI-01 | 1440×900、1280×720、200% 缩放 | 左侧可用、右侧不被挤坏，正文无页面级横向溢出 |
| UI-02 | 中英、亮暗、键盘、空/加载/错误/禁用 | 按钮/卡片/表单可辨识；Tab/Enter/Escape 与树方向键路径成立 |

前端门禁（实施时在 `src/frontend` 执行）：

```powershell
pnpm test:unit
pnpm typecheck
pnpm lint
pnpm build
pnpm exec playwright test tests/e2e/systemdata-organization-cards.spec.ts tests/e2e/systemdata-menu-management.spec.ts tests/e2e/systemdata-initialization-admin.spec.ts
```

变更文件另做定向 Prettier 检查，不格式化无关 WIP。运行 E2E 前确认端口与身份模式；默认 Mock 4173 与用户现有真实调试环境分开，不能把占用端口直接当成正确测试服务。

上述新 E2E 路径为计划新增，并非现有已通过用例。真实 SSO/缺列升级/初始化写入须使用单独安全隔离环境与真实 PostgreSQL 驱动验证；不得直接对当前用户库执行，不能用 Mock 浏览器结果替代这层验收。

后端门禁（实施时在仓库根执行）：

```powershell
dotnet build src/backend/IndustrialPlatform.slnx --configuration Release
dotnet test src/backend/IndustrialPlatform.slnx --configuration Release --no-build
```

源码变化或锁冲突后必须重新 build。不得停止用户进程释放锁；只清理本任务自有进程。真实写入验证仅在安全隔离数据下进行；Mock、真实 HTTP、浏览器截图、外部依赖测试分别记录，跳过不写为通过。

## 8. 交付与实施边界

- 当前状态：本计划已获用户执行授权，转交原两任务；开发、自验证与独立验收结果分别记录，不提前勾选或关闭。
- 执行顺序：先三页错误与数据库事实检查，再固定管理员初始化，随后组织卡片、菜单后端/基线、菜单页面与 runtime，最后入口分区和独立验收。各块独立验证，菜单与初始化都必须端到端交付。
- 人员分工继续为主控收束范围，原开发任务 `gpt-5.6-luna max（极高）` 实施自验证，原验收任务 `gpt-5.6-sol high（高）` 在稳定交接后独立验收。不新增重复任务，不重复要求用户批准例行测试/消息。
- 不自动提交、推送、迁移真实环境、重置账号/数据库，不启动 MasterData、Audit、File、Notification 等新业务。
- 完成标准是“三页实际恢复、管理员专用初始化可补齐结构与数据且重复执行安全、卡片主从正常、菜单配置真实生效、截图可见差异关闭”，不是“修改页面名、按钮换色、最大迁移编号最新或测试能进入页面”。
