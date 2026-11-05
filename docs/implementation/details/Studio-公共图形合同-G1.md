# Studio 公共图形合同（G1）

更新：2026-10-31。归属：[公共图形架构](Studio-公共图形工作台与流程复用架构.md) §4、§5、§8.1。本文是该架构的**公共接口明细唯一维护源**；TraceModel 的持久化字段/API 仍以[接口规格 §1/2/7](PF08-PF09-接口与配置合同.md)为准。G1细化后用户授权连续完成011～013，实现与实测见[开发证据](../../evidence/PF09-追溯011-013落地-20261031.md)。下文“本轮仅文档/待G2”描述G1发生时的基线，不能作为当前未实施结论；公共类型已落入graph/types.ts，新增nodeToken仅留追溯领域，API以权威接口最新版本为准。

## 1. 当前源码与最小适配位置

只读基线：2026-10-31，`develop`，HEAD `300ef995d9a66b5fe8412fd29b626a5bb53000b1`。开工时已有会话总览修改及未跟踪的 G1 开场提示，均保留。本轮不安装依赖、不运行 PoC、不操作业务数据/服务、不提交。下表路径相对仓库根目录；不是新文件已创建的声明。

| 已核对位置 | 当前能力/耦合 | 最小接入建议与阶段 |
| --- | --- | --- |
| `src/frontend/src/components/studio/shared/model.ts`：`createHistory<T>` | 泛型 clone 快照、默认100步、相等提交不入栈、reset/undo/redo；没有编辑事务、保存基线或图模型 | 复用算法；G2 验证一次领域事务只 commit 一次。不要将图转成 LayoutNode 来复用 |
| 同目录 `StudioEditorShell.vue` | 工具栏/structure/properties/workspace 插槽、侧栏、属性选择、apply、撤销；`freeCanvas` 能跳过 CSS canvas，但 root/flat/focused、watch、快捷键、fit/pan 仍由壳持有 | `freeCanvas` 本身不等于图支持。G2 验证外部受控的 history/selection/viewport 窄接口；图模式不遍历 `layout.children`，不触发壳自动 fit/pan/历史监听。默认树模式原行为不变 |
| `src/frontend/src/pages/pc/studio/report/ReportDesignerPage.vue` | 已通过 shell.apply 修改克隆文档；已有 busy、保存比较、预览和返回链 | 参考操作归属；不复制报表持久化/预览生命周期，不把现有页面每一处异步细节当作图合同已实现 |
| `src/frontend/src/api/studio.ts`、`src/frontend/src/workspace/returnFromWorkspace.ts` | 资源与双版本、设计读取/保存/校验/发布、工作台返回基础 | T1 复用；领域运行请求由页面控制器发起。离开确认后导航成功才关原标签 |
| `src/backend/src/Services/PlatformStudio/IndustrialPlatform.PlatformStudio.Application/Publishing/StudioPublishing.cs` | RequireKind 只准 Dataset/LedgerModel/LowCodePage/Dashboard/Playlist/Report/ReportBundle；草稿CAS、修订/依赖/sourcePins/审计已有 | T1 增加 TraceModel 发布与依赖校验消费，不能只改白名单 |
| `…/IndustrialPlatform.PlatformStudio.Domain/StudioDocuments.cs` | schemaVersion=1/2MiB 基础检查及各已交付 kind 校验；没有 TraceModel 专有校验分支 | T1 补对象/关系/绑定/布局/limits 校验；当前泛型通过不能证明追溯配置合法 |
| `…/IndustrialPlatform.PlatformStudio.Domain/StudioPrincipal.cs`、`StudioDeploymentProfile.cs`；`…/IndustrialPlatform.PlatformStudio.Infrastructure/Persistence/StudioSchema.cs` | TraceModel→Traceability→studio.trace 映射、模块名及 resource kind CHECK 已登记 | 保留既有登记；后续核对实际权限种子、菜单、能力与API，避免重复表/迁移 |
| `…/IndustrialPlatform.PlatformStudio.Application/StudioAuthorization.cs`、`StudioPorts.cs` | 功能权限∩资源grant、租户一致性；IStudioIdentity/Authorization/SourceExecutor/ResourceStore/Files 等端口 | T1 经应用服务消费，数据范围还须来源执行层校验；公共画布没有 store/SQL/权限判断实现 |
| `…/IndustrialPlatform.PlatformStudio.Infrastructure/Integration/Embedded/StudioFileAdapters.cs` | TemplateAsset/TemplatePackage 已有 Traceability 权限前缀 | 复用文件门面，不据此称追溯 node-link 已实现 |
| `src/frontend/package.json`、`pnpm-lock.yaml`；Studio 前后端源码搜索 | 没有 `@antv/x6` 引用；未发现 TraceModel 编辑器/查询服务实现 | X6 版本、插件及交互/释放全部待 G2；011～014 未完成 |

建议新增落点沿主文 §7.2：`components/studio/graph/` 放本文公共类型、工作台/X6适配；`components/studio/trace/` 放 TraceModel 适配/属性；`pages/pc/studio/trace/` 放请求与页面生命周期。G2 先在隔离目录验证这些边界，生产夹具只在 PoC 内；不建生产空壳工程、不新增通用事实表、不拆 npm 包。

## 2. 类型、身份和兼容总则

下面 TypeScript 是拟定接口，不是已发布 SDK/HTTP DTO。所有接口字段默认必填；`?` 表示可省略；仅显式 `| null` 接受 null。数组可空但不可 null，禁止 undefined 数组成员；字符串非空（显示文本可空），数值必须有限。`Readonly` 表示调用者不可原地修改；边界输入仍须运行时检查。

| 项目 | 合同 |
| --- | --- |
| `graphContractVersion` | 字面量1；只描述前端投影/交互。与领域 document.schemaVersion=1、Studio documentSchemaVersion、资源CAS、revisionNo 不混用 |
| NId/版本 | 资源/修订 NId 沿128长度限制；revisionNo/optimisticVersion 等保持十进制字符串，concurrencyVersion 为GUID。不得转 Number。本文 generation/responseVersion 也是十进制字符串；仅本页递增，用 BigInt 或等价无损计数 |
| 稳定身份 | 定义 node/edge ID 来自领域对象/关系 ID；改显示名、移动不换ID。port ID 在节点内唯一，重排端口不改ID；删后重建是新定义ID，撤销恢复原ID |
| 投影 ID | 节点/边 ID 各在当前图内唯一；选择使用 kind+id，不靠标题、数组下标或端点对。X6 cell ID 由适配对 `[kind,id]` 可逆编码，不能简单拼接引起冲突 |
| 运行身份 | 服务端对象key/证据key当作不透明字符串保留，前端不另算业务hash。运行node.id不是objectTypeId；生产instanceId不是definitionNodeId |
| schema兼容 | G2双方只接受公共版本1；未知版本显示不兼容并禁止编辑/保存，不尝试X6.fromJSON猜测。可选展示字段的同版本增补可忽略；新增必填/身份/语义变化升级公共版本并给转换器与回归 |
| 领域兼容 | TraceModel仍schemaVersion=1；本轮仅细化既有layout端口空值/几何校验，见接口§7.1。无新表、运行API字段或新schemaVersion。旧合法对象/关系ID不重编 |
| 保留未知数据 | 领域适配从原document克隆，只修改所负责字段，不能从投影反推整份document而丢parameters/绑定/扩展。未知schema禁止保存；同schema未知字段保留，未知节点kind可只读占位并给诊断，不允许隐式删除 |
| 信任边界 | 公共层只消费受权后的最小显示投影和不透明引用，不含SQL、凭据、startKey/cursor、可执行HTML/URL。label按文本渲染；style/icon只能取代码登记项 |

```ts
type Id = string
type Seq = string // /^(0|[1-9][0-9]*)$/，不按字符串字典序比较
type Mode = 'design' | 'preview' | 'run'
type Target =
  | { kind: 'node'; id: Id }
  | { kind: 'edge'; id: Id }
  | { kind: 'port'; nodeId: Id; id: Id }
type DefinitionRef =
  | { kind: 'draft'; resourceNId: Id; documentStamp: Seq }
  | { kind: 'published'; resourceNId: Id; revisionNId: Id }
interface GraphScope {
  contextKey: Id // 页面控制器生成的不透明作用域键；换租户/主体/资源/模式即更换
  definition: DefinitionRef
}
interface Point { x: number; y: number }
interface Box extends Point { width: number; height: number }
interface PortView {
  id: Id; role: 'in' | 'out' | 'both'
  side: 'left' | 'right' | 'top' | 'bottom'; order: number
  label?: string
}
interface NodeView {
  id: Id; kind: string; label: string; ports: readonly PortView[]
  definitionNodeId?: Id // 定义图=id；运行图可多对一
  detailRef?: Id // 仅领域控制器能解析；不是URL/访问凭证
  tone?: 'neutral' | 'info' | 'success' | 'warning' | 'danger'
}
interface Endpoint { nodeId: Id; portId: Id }
interface EdgeView {
  id: Id; kind: string; label: string
  source: Endpoint | null; target: Endpoint | null
  definitionEdgeId?: Id; detailRef?: Id
  tone?: 'neutral' | 'info' | 'success' | 'warning' | 'danger'
}
interface LayoutView {
  nodes: readonly ({ id: Id } & Box)[]
  edges: readonly { id: Id; vertices: readonly Point[] }[]
}
interface Diagnostic {
  id: Id; code: string; severity: 'error' | 'warning' | 'info'; message: string
  target?: Target; path?: string // path保留领域验证器原路径；不从消息猜ID
}
interface GraphView {
  graphContractVersion: 1; scope: GraphScope; kind: 'definition' | 'runtime'
  nodes: readonly NodeView[]; edges: readonly EdgeView[]; layout: LayoutView
  diagnostics: readonly Diagnostic[]
}
```

字段规则：

| 字段组 | 默认、限制和错误语义 |
| --- | --- |
| scope | 无默认；contextKey不证明授权，只用于隔离显示。definition必须精确匹配本次加载；draft.documentStamp是当前本地文档代次，不冒充发布修订；可编辑设计必须使用draft作用域，published定义只读 |
| nodes/edges | 空图合法；重复ID为结构错误，拒绝整次加载/提交并保留原图；未知kind输出占位及错误诊断。kind为适配器本地登记键，公共层不解析Genealogy/Control等含义 |
| ports | 每节点端口唯一；order非负整数，相同order按id稳定排序。G2不做端口数据类型推导；方向匹配仅为基础约束，最终连接规则归领域 |
| source/target | 完整Endpoint须指向存在节点及端口；null只供定义草稿未连接状态，必须有定位诊断、禁止发布；运行事实边不得悬空。加载阶段缺端点与null区分，前者是结构错误 |
| definitionNodeId/definitionEdgeId | 定义图必填且等于id；运行图按领域有映射时必填。引用的定义属于scope.definition；Run-only用户只接收允许显示的引用，不因此读取设计JSON |
| detailRef/tone | 缺detailRef禁用详情；缺tone=neutral。tone只是视觉提示，不表示任务权威状态，不作为执行/授权条件 |
| layout | 必须为每个可见node/edge提供一条布局，不重复、不含悬挂ID；领域缺省布局由适配器计算后补齐投影。x/y及折点范围±1,000,000，宽高1～10,000逻辑px；越界/NaN拒绝，不静默夹断持久值 |
| diagnostics | 空数组表示当前已做校验无诊断，不代表服务器发布校验已通过。target不存在则保留为文档级诊断，不跳到同名对象；path与稳定目标同时存在时先定位target再打开字段 |

定义图不能直接作为运行事实图：追溯定义节点是对象类型，查询可投影成许多对象；生产图可沿定义拓扑显示实例覆盖。两者都通过GraphView显示，公共层不需要分辨批次或任务。

## 3. 领域扩展、编辑与诊断接口

```ts
type Outcome<T> = { ok: true; value: T; diagnostics: readonly Diagnostic[] }
  | { ok: false; code: string; diagnostics: readonly Diagnostic[] }
interface ConnectionIntent {
  edgeId?: Id // 有值=重连，无值=新连接
  source: Endpoint | null; target: Endpoint | null; edgeKind: string
}
type GraphIntent =
  | { type: 'addNode'; catalogKey: string; at: Point }
  | { type: 'remove'; targets: readonly Target[] }
  | { type: 'connect'; connection: ConnectionIntent }
  | { type: 'layout'; nodes: LayoutView['nodes']; edges: LayoutView['edges'] }
interface CatalogEntry {
  key: string; label: string; group: string
  enabled: boolean; disabledReason?: string
}
interface EditContext {
  scope: GraphScope; expectedDocumentStamp: Seq
}
interface PreparedEdit<D> {
  document: D; selection: readonly Target[] // 提交后的建议选择
}
interface DomainGraphAdapter<D, PropertyCommand> {
  readonly key: string; readonly graphContractVersion: 1
  catalog(document: Readonly<D>): readonly CatalogEntry[]
  project(document: Readonly<D>, scope: GraphScope): Outcome<GraphView>
  checkConnection(document: Readonly<D>, intent: ConnectionIntent): readonly Diagnostic[]
  edit(document: Readonly<D>, command: GraphIntent | PropertyCommand,
    context: EditContext): Outcome<PreparedEdit<D>>
  validate(document: Readonly<D>, scope: GraphScope): readonly Diagnostic[]
}
```

`D`是TraceModel或生产夹具自身的类型；`PropertyCommand`由各适配器定义，例如`{type:'trace.renameObject',objectTypeId,name}`，不在公共包枚举追溯实体或任意JSON patch。上层控制器持有适配器、document、历史及服务；公共X6适配只发GraphIntent/selection/viewport事件。`project/edit/validate/checkConnection`均是同步纯函数，不查库、不发HTTP，不直接修改传入对象。

| 操作 | 输入、归属、结果与失败 |
| --- | --- |
| 节点目录 | 领域返回已登记CatalogEntry；空=不能新增。enabled=false须disabledReason。拖入后edit分配一次稳定ID并建立合法草稿；不完整绑定转error诊断，可保存不可发布 |
| 属性面板 | 由宿主按target挂载领域Vue面板，不在公共层定义万能表单schema。面板读当前草稿，提交强类型PropertyCommand；取消/验证失败保留输入，不先改原对象。辅助目录异步加载单独显示错误 |
| 新连接 | 拖线时基础存在性/方向检查+checkConnection；拒绝则原定义不变。允许环/自环/同端点多边的最终规则由领域决定；没有全局DAG/唯一端点对规则。落到空白仅打开临时配置，取消即移除临时线，不创建事实 |
| 重连/断开 | 保留edgeId；有效重连同时更新领域两端与布局并重验映射。显式断开可形成端口null的无效草稿，保留查询配置与错误诊断；其后修复同一关系，不删除证据历史 |
| 完整校验 | validate检查整个领域定义，输出与当前documentStamp绑定的诊断；只修改显示状态，不入历史。服务器validate/publish仍独立检查Schema、引用、依赖/来源版本与权限 |
| 诊断定位 | graph target使用稳定ID；接口path由领域映射，不把数组下标长期当ID。错误/警告支持定位后展开对应属性组；无可见目标显示文档级列表。已删除对象的旧诊断撤去并全量重算 |
| 领域试查/业务操作 | 按钮放领域面板或运行工具区，经页面控制器调用现有受权服务；busy/取消与请求代次独立。公共图只显示结果，不能从连线自动启动查询或推进任务 |

错误级诊断不全等于不可存草稿：ID冲突、非法类型/坐标等结构错误拒绝提交；未就绪映射、空端口等合法结构的语义错误允许成为草稿，禁止发布。服务端错误码保留原值与path；本文本地错误用`GRAPH_STRUCTURE_INVALID / GRAPH_CONTEXT_STALE / GRAPH_READ_ONLY / GRAPH_VERSION_UNSUPPORTED / GRAPH_DISPOSED`，不伪装HTTP成功或私增后端错误协议。

## 4. 原子事务、历史、保存与视口

### 4.1 单一文档与历史

权威编辑值是领域完整document（其中包含layout）；GraphView与X6 cells均为可重建派生值。推荐复用`createHistory<D>`算法，由图页面的编辑控制器唯一持有；壳工具栏/快捷键通过窄接口委托它。X6 History不启用为第二条历史。既有树编辑器继续使用壳自己的历史，不把两条栈同步作为方案。

事务顺序：捕获before与documentStamp→在克隆值执行一个或一组领域命令→检查结构→生成投影/诊断→准备阶段全部成功后一次替换document并commit→documentStamp递增→渲染。提交前失败保留before、选择及历史，临时X6操作恢复before投影；提交后渲染异常显示画布错误并允许从当前document重建，不丢草稿、不清历史或重复提交。渲染事件回流必须携带内部重放标记并忽略，不能二次commit。documentStamp在撤销/恢复时也递增，不能重用旧请求代次。

| 用户动作 | 原子内容 | 历史/脏状态 |
| --- | --- | --- |
| 拖动/尺寸/折点 | pointerdown捕获；move只展示预览；pointerup提交最终几何；Escape/pointercancel回退 | 一次；无净变化零次；不按每像素提交 |
| 属性+图形复合 | 例如改对象宽高同时更新style与layout覆盖，或重连同时改两端映射 | 一次；面板Apply/失焦结束一次编辑，不逐按键保存 |
| 多选删除节点 | 同时删除所选对象、所有入/出/自环关系、相关layout；领域检查并移除起点类型引用等直接依赖，留下必要的缺起点诊断 | 一次；撤销恢复完整对象/关系/布局。外部资源引用不可静默修改，服务器发布再查 |
| 删除边 | 删关系定义及布局；对已发布修订/运行事实没有删除作用 | 一次；undo原ID恢复 |
| 自动布局 | 用户在设计态主动应用最终坐标；布局失败无提交 | 一次；打开图时生成缺省显示布局不自动写草稿 |
| 选择/多选、开关面板、诊断定位 | UI状态；选择不存在目标时清除并提示，不选择同名项 | 零次，不脏 |
| 缩放/平移/fit；运行临时拖动 | 页面viewState；不在TraceModel持久化viewport | 零次，不脏 |
| 运行刷新/试查结果/诊断刷新 | 替换独立运行/诊断状态 | 零次，不脏，不清空编辑历史 |
| undo/redo | 回到历史document，重建投影并重算诊断；选择过滤为仍存在目标 | 各改变当前文档；dirty与保存基线比较，撤回已保存内容可恢复不脏 |

不把诊断/运行结果/选择放进document。若需撤销后恢复原选择，只能作为历史条目的UI附属信息，不让单纯选择产生条目。默认100步沿现有history；大图快照成本留给GX-08实测，不在G1另造patch事件存储。

### 4.2 保存、发布和并发

每次save捕获`{contextKey,documentStamp,document,optimisticVersion,concurrencyVersion}`，同步防重入。PUT沿既有双版本CAS；保存只处理捕获快照。成功仅更新同一资源的CAS与已保存基线，不覆盖期间继续编辑的document；只有当前document等于被接受快照才清dirty。服务器返回规范化内容时，当前仍是同一编辑代次才可整体应用，否则保留更新草稿并明确待再次保存。

失败保留草稿、历史、面板输入与dirty。409保留本地/本次提交/服务端版本差异，提供重新读取并比较入口，不自动覆盖或重新PUT；用户选择采用服务器内容才reset历史。网络结果不明先受权读取草稿比较版本/内容，不在不确定时推进保存基线。401/403/资源不可见保留本地未保存编辑的恢复状态但禁用保存，撤去不可再见运行数据；不自动导出敏感恢复副本。

保存/校验成功回包与错误都检查contextKey；已切资源的回包不改变新页busy、版本或诊断。发布先保证目标草稿已保存且无错误，按VersionCommand+dependencyPlanHash及既有幂等键合同处理；依赖/来源变化409重新校验。发布生成不可变修订，不能把旧运行切到新修订；保存/发布本身不作为编辑撤销条目。

### 4.3 单一视口

```ts
interface ViewportState { zoom: number; x: number; y: number }
interface ViewportControl {
  get(): ViewportState
  set(value: ViewportState): Outcome<ViewportState>
  fit(): Outcome<ViewportState>
  focus(target: Target): Outcome<ViewportState>
}
```

x/y为逻辑原点在容器CSS像素中的平移，屏幕位置=`logical*zoom+translation`；zoom范围0.05～3，初始1，首次可见加载fit按范围限制。超范围显式set拒绝，工具滚轮在边界停止；fit若小于最小比例仍保留可平移区域，不裁掉数据冒充完整。容器宽高0时推迟fit；用户手动移动后resize只调整视口容器，不强制跳回fit。G2可据实测调整范围，但须回写本表。

X6是图模式下唯一执行视口变换的控制者，壳显示读数/工具按钮通过ViewportControl委托。外层不再CSS scale、不再scroll平移叠加X6平移、不调用树形logicalDelta处理图坐标。快捷键只在当前活动画布生效，输入框/IME/contenteditable由输入自己处理撤销；删除只处理定义目标，run/preview不删除数据。隐藏标签和卸载的监听撤销见§6。

确定的是单一权威与用户可见结果；X6事件顺序、批处理/重放标记、拖拽取消、侧栏尺寸变化与fit的内部接法尚未验证，由G2提供证据，不写成现成插件能力。

## 5. 运行快照与实例详情

公共v1只接收**完整替换快照**。追溯HTTP expand仍沿既有增量，领域控制器串行合并后生成完整GraphView；公共层不暴露任意增量协议，不要求消息总线/订阅服务器。

```ts
interface RuntimeStamp {
  scope: GraphScope
  runKey: Id // 追溯本页查询上下文；生产夹具流程实例ID
  generation: Seq // 起点/条件/模型/实例变化即递增
  responseVersion: Seq // 同代接受的完整投影序号，首次1
  requestId: Id // 追溯保留服务器requestId，夹具显式提供
}
interface Observation {
  sourceKey: Id; observedOn: string | null; receivedOn: string | null
  quality: 'known' | 'unknown'; reason?: string
}
interface RuntimeInstance {
  id: Id; definitionNodeId: Id; iteration: Seq; attempt: Seq
  stateKey: string; stateLabel: string; tone: NonNullable<NodeView['tone']>
  startedOn: string | null; endedOn: string | null; detailRef: Id
}
interface InstanceOverlay {
  nodeId: Id; instanceIds: readonly Id[]; label: string
  tone: NonNullable<NodeView['tone']> // 由领域依据全部实例计算
}
interface RuntimeSnapshot {
  graphContractVersion: 1; stamp: RuntimeStamp; graph: GraphView
  receivedOn: string; observedOn: string | null
  completeness: 'complete' | 'partial' | 'unknown'
  reasons: readonly string[]; observations: readonly Observation[]
  instances: readonly RuntimeInstance[]; overlays: readonly InstanceOverlay[]
}
interface DetailSelection {
  stamp: RuntimeStamp; selectionGeneration: Seq
  target: Target; detailRef: Id; instanceId?: Id
}
```

| 字段/行为 | 必填、默认和错误 |
| --- | --- |
| stamp/graph.scope | 必须相等且属于当前context/definition/runKey/generation；不匹配无副作用丢弃。responseVersion必须递增，重复/较小丢弃；本协议是全量替换，因此较大序号无需补增量 |
| receivedOn/observedOn | ISO8601带时区；前者为本页接收时间，后者为来源真实观察时间，未知用null。不得用接收/绘制时间补来源时间 |
| completeness/reasons | 无默认；partial/unknown必须有原因；complete也不承诺跨来源事务一致。trace.status只映射Complete→complete、Partial→partial；Unknown源质量由observations表达，不给TraceResult虚增Unknown枚举 |
| observations | 已知来源逐项提供；服务器未给时间则null+quality=unknown+reason；不可伪造来源共同时间。空数组须在reasons解释无观察信息（纯夹具可注明fixture） |
| instances/overlays | 追溯为空数组，实际对象是graph.nodes而非生产任务实例。生产所有id唯一，iteration/attempt从1开始；同一节点多迭代/尝试保留独立id；overlay不能引用缺失实例或不同定义节点 |
| 运行状态 | stateKey/label由领域提供，公共层不枚举Running/Completed/Partial之间转换，不计算任务进度。没有overlay按neutral显示，不能默认完成；汇总必须能进入全部实例列表 |
| 详情 | 公共层只发DetailSelection，宿主持有对应受权查询或夹具。返回内容须同时匹配stamp（含responseVersion）和selectionGeneration/target/instanceId；晚到成功与失败一律丢弃 |

TraceResult 没有服务器单调responseVersion。**本地responseVersion只表示接受顺序，不能当来源快照版本或跨页面续查凭证。** requestId也只作请求追踪，不按字符串大小判断新旧。G2生产夹具有显式服务模拟版本；将来若真实生产服务提供事件序列/版本，须由其领域接口先定义再适配，不能用本地序号证明后台推进正确。

追溯合并规则：同一generation最多一个query/expand在途；捕获当前baseResponseVersion；回包时先检查模型修订/上下文/base仍一致再合并。按稳定node.key/edge.key去重，端点相同且key不同全保留；同key内容发生冲突则保留既有值、给Partial冲突诊断并要求重查，不擅自最后写入覆盖。先合节点再验边引用；结构异常拒绝整包，不吞边。只有收到有效回包才推进responseVersion；一次expand Complete不抹掉此前仍未解除的Partial原因。cursor仅由领域控制器保存，过期重新选择/查询，不送X6或日志。

证据详情首版展示已受权TraceResult.edges.evidence中的recordKey/时间/数量/单位与关系/两端，选择按edge.key定位。需要补充资料时沿已有node-detail/node-link且使用实际节点及linkId重新授权；不从recordKey拼URL、不虚构edge-detail端点。若后续业务要求证据自身的独立附件权限/API，须在T1修改权威合同后实现，G2不假称已有。

## 6. 页面控制器与生命周期

```ts
interface EditAccess { readOnly: boolean; reason?: string }
interface GraphEvents {
  onIntent(intent: GraphIntent): void
  onSelection(targets: readonly Target[]): void
  onViewport(value: ViewportState): void
}
interface GraphSession {
  load(view: GraphView, mode: Mode, access: EditAccess): Outcome<void>
  select(targets: readonly Target[]): Outcome<void>
  setRuntime(snapshot: RuntimeSnapshot): Outcome<void>
  setMode(mode: Mode): Outcome<void>
  setAccess(access: EditAccess): Outcome<void>
  readonly viewport: ViewportControl
  dispose(): void
}
declare function createGraphSession(container: HTMLElement, events: GraphEvents): GraphSession
```

load先校验完整输入再原子替换，失败不留半张图；不同scope加载前旧受权内容必须先撤去，失败显示新上下文错误，不能回显旧租户图。select空数组清选择，定义设计态可多选，preview/run首版单选（多项输入拒绝）；setRuntime仅preview/run可用且不可改定义。setMode离开design前由宿主处理dirty/未提交输入，取消则不切模式；方法本身无保存副作用。dispose可重复调用，之后除dispose外返回GRAPH_DISPOSED；订阅释放由创建者持有，无全局图实例。

EditAccess无默认，来自宿主能力/锁定状态；有效可编辑条件为design+draft+readOnly=false，其他情况拒绝编辑意图并返回GRAPH_READ_ONLY（事件通道不发命令）。只读仍允许选择/详情/视口及运行本次布局；design只读不写布局。reason可省略，省略时使用通用只读说明。setAccess变只读即取消未提交拖拽并复原，不自动保存；API仍须重新授权。onIntent只表示用户请求，领域控制器成功提交后才load新投影；程序load/select/setRuntime重放不重复发意图/选择事件。模式或资源切换的新scope必须由宿主load，不靠setMode变更修订身份。createGraphSession只创建视图，不加载业务数据，dispose解除对events及container的引用。

| 时点 | 请求与数据显示 | 图/UI资源 |
| --- | --- | --- |
| 初次设计加载 | 草稿与辅助目录分开；草稿就绪即投影，辅助失败可重试；历史reset一次，不把初始加载记作编辑 | 容器挂载可见后创建一个图，fit一次；属性按选中挂载 |
| 发起新查询/更改起点条件 | 先推进generation、取消旧请求、清旧选择/详情/结果，再受权查询；空结果明确empty，失败明确error | 图可显示加载态，但不能把旧结果当新查询；失败不回退演示数据 |
| 原查询继续展开 | 串行受理；保留现有图，边界节点局部busy；失败保留本次已有结果和失败提示 | 不重置用户视口；新增布局仅安置新节点，主动全图重排须用户操作 |
| 快速选A→B | selectionGeneration递增、取消A详情/链接请求；B局部loading，清A内容 | 图不整体闪屏；A迟到错误不能覆盖B |
| 切修订/模型/实例/租户/登录主体 | 更换contextKey与generation，取消所有查询/详情/链接，清票据、运行数据、诊断和模态URL；模型重新受权加载 | 跨上下文不保留运行选择/缓存；设计草稿离开保护不跨租户复用 |
| 取消查询 | abort尽力终止传输，推进generation使旧回包失效；已有展开结果可保留但标“已取消/非完整”，提供重新查询 | 取消不宣称服务器工作已停止；新根查询未取得结果则为空的取消态 |
| 撤权/401/403/不可见404 | 清不可再见结果/详情/票据并停止请求，当前页明确诊断；恢复需重新授权 | 图上的旧敏感label也移除；禁用按钮不替代服务端校验 |
| 标签隐藏/KeepAlive deactivated | 停轮询、取消请求并失效代次，撤销临时文件URL/订阅；保留仅本标签设计草稿与视口状态 | dispose图与ResizeObserver/DOM/键盘/Pointer监听；再激活重建图并受权重取运行数据，不把隐藏当永久卸载 |
| 标签关闭/路由卸载/注销 | 导航确认通过后取消所有在途请求，丢弃本页缓存、票据、详情及本地对象URL；不依赖beforeunload完成服务器事务 | dispose图、事件、观察器、定时器、领域订阅；无双重订阅/全局强引用；返回沿returnFromWorkspace |

v1无后台自动刷新要求，追溯由查询/展开触发；G2用手动注入运行全量快照。若生产监视后续采用轮询/推送，由领域控制器注册和释放；公共组件不为“运行”自动创建timer。文件URL到期按node-link重授权，文件扫描/ImportAndScan临时引用清理由既有服务器所有者合同处理；浏览器dispose不等于服务器已释放。

## 7. TraceModel 逐项适配与保存边界

| 权威字段 | 直接复用或投影 | 不进入公共层/兼容处理 |
| --- | --- | --- |
| schemaVersion、parameters、scope、start | 原样留在document，起点面板拥有配置 | 不用graphContractVersion替换；可信租户/工厂仍由服务器验证。startKey只存页面控制器 |
| objects[].id/name | 定义NodeView.id/definitionNodeId/label；kind固定登记为trace.object | identity/grain/summary/detail是领域配置，公共图只接收已解析文本，不持有Binding或Dataset |
| objects[].style | 登记icon/color映射为显示皮肤，宽高供无layout覆盖时回退 | 不向X6开放任意HTML/CSS。布局尺寸优先于style缺省，几何规则见接口§7.1 |
| relations[].id/name/fromTypeId/toTypeId | 定义EdgeView.id/definitionEdgeId/label，连接端点从类型ID与layout端口生成 | kind/semantic只作领域登记与图例；forward/reverse、identity/evidence/label绑定留在关系面板与服务端 |
| layout.nodes | objectTypeId↔投影node.id，位置和尺寸逐项映射 | 保持既有数组结构，不序列化X6 cells。缺省行仅补投影；拖动/主动布局才写对应行 |
| layout.edges | relationId↔投影edge.id，sourcePort/targetPort/vertices | 规范端口out/in；显式null是断开草稿，未知字符串报错。定义关系两端保留，见接口§7.1；运行事实不从这些连线产生 |
| limits | 现有12/1000/3000/30预算及允许请求限制原样执行 | 不映射成“画布可以偷偷裁掉”的限制；不向通用图硬编码追溯查询预算 |
| 资源、RevisionDto、CAS | 通过现有Studio生命周期及DefinitionRef关联 | graph不保存resource grant/sourcePins/连接秘密；发布依赖环检查不等于禁止领域事实环 |
| TraceResult.nodes | id=key，definitionNodeId=objectTypeId，摘要生成label；detailRef为本页映射表键 | 完整identity由领域控制器保存；两厂同编码仍两节点，NodeView不能重算key |
| TraceResult.edges | id=key，definitionEdgeId=relationId，from/to保持真实箭头；运行端口由适配固定out/in | evidence保存在领域详情映射，不能按from/to压成一边。反向查询只改变查找方式 |
| status/partialReasons/observedOn/sourceObservations | 映射RuntimeSnapshot完整性/诊断/观察列表 | 不映射为任务Completed/Failed；Unknown质量不丢失，不能用刷新时间冒充来源时间 |
| cursor/requestId | 控制器持有cursor；requestId供RuntimeStamp追踪 | 不修改API添加generation/responseVersion，两个本地序号由控制器生成 |

设计端每个trace.object派生端口`in`与`out`，左/右，order=0；多个关系可共用同一端口。端口是适配呈现，不是数据库对象、关系查询参数或物料事实。合法同类型自环使用同节点out→in；Genealogy和Context均可有多重关系，但影响范围算法只选相应Genealogy规则。

加载未知/缺失layout的回退仅显示：按objects原顺序每行4列，x=80+240*(i%4)、y=80+160*floor(i/4)，宽高取style有效值否则180×80；无边布局为out/in及空折点。再次投影同文档得到同结果，不能每次随机布局。运行实际对象另由G2布局策略计算；不把设计对象类型坐标强塞给每个批次。

本轮确有合同细化只有接口§7.1的端口/几何默认、null与校验；不是新增TraceModel字段或服务。页面§6同步“一次操作一次撤销”：单独移动只撤销布局，复合改端点/属性操作原子撤销，避免历史文字被误读成两条历史。其他身份、凭证、预算、生命周期不变。

## 8. 三种页面模式与操作归属

```text
资源返回 | 名称/修订/模式/保存状态 | [撤销 恢复 校验] [预览] [保存 发布]
目录/结构或起点 | 同一图形工作台 + 视口工具 | 领域属性或证据/实例详情
                诊断/Partial原因/来源时间（可收起并重新展开）
```

| 区域 | design | preview | run |
| --- | --- | --- | --- |
| 顶部 | 返回、草稿、撤销/恢复、校验、预览、保存/发布；Design/Publish分别受权 | 明示“草稿快照预览”、快照代次、返回设计、试查/取消；无保存发布 | 发布修订与查询/实例，领域查询/取消/刷新；无编辑保存发布 |
| 左侧 | 对象目录/结构/起点配置；添加能力按catalog | 冻结配置的试查输入/样本选择；不能修改定义 | 授权起点搜索/选择、方向/深度/关系过滤；生产夹具为实例选择 |
| 中央 | 允许定义布局、连线、选择；工具栏委托唯一视口 | 只读定义或试查投影，允许选择/缩放/本次视图拖动 | 只读事实图/定义覆盖，多实例角标；本次移动不持久化 |
| 右侧 | 当前节点/关系属性、正反映射试查 | 冻结属性只读、模拟/真实试查标记、证据详情 | 节点详情/所选证据边/实例列表；领域受权动作独立入口 |
| 诊断 | 字段/关系定位，允许保存的错误仍阻断发布 | 缺参数/失败/不完整/来源观察时间 | Partial边界/未知质量/权限与详情失败；不伪装空集合 |

预览捕获documentStamp与克隆document，后续编辑不改变已打开预览；重新预览产生新context。preview-start凭证的Preview作用域不能用于正式query。Run-only用户不得为了画图调用Design权限的revision详情接口；追溯运行只使用专用运行结果与其可显示摘要。生产运行的安全显示定义属于未来生产服务合同，G2由夹具供应，不能据此扩大Studio修订读取权限。

复用已有外壳插槽、主题/语言与返回链；新增仅图模式的控制接口，不改变看板/报表/低代码画布模型。侧栏重新展开入口始终可见；1080p及页面规格规定三视口/中英/明暗/键盘均交G2/T1按适用验证。

## 9. 固定纸面夹具一：追溯

夹具名`trace-multiparent-v1`，全部为自建模拟数据，不是服务器签名凭证或客户数据。以下TS生成完整TraceModel草稿，Binding采用接口§3.3；Dataset修订为G2夹具登记键，正式环境须经现有发布/授权。

```ts
const row = (...path: string[]) => ({ from: 'Row', path, nullPolicy: 'Error' })
const node = (...path: string[]) => ({ from: 'Node', path, nullPolicy: 'Error' })
const constant = (value: string) => ({ from: 'Constant', value, nullPolicy: 'Error' })
const targetIdentity = {
  sourceSystem: row('sourceSystem'), factory: row('factory'), businessKey: row('targetKey'),
}
const traversal = (reverse: boolean) => ({
  datasetRevisionNId: reverse ? 'fixture-movement-reverse-r1' : 'fixture-movement-forward-r1',
  inputMap: [
    { target: reverse ? 'toId' : 'fromId', source: node('identity', 'businessKey') },
    { target: 'factory', source: node('identity', 'factory') },
  ],
  result: { collectionPath: ['rows'], targetObjectTypeId: 'lot', targetIdentity, summaryMap: [] },
})
const traceDocument = {
  schemaVersion: 1, parameters: [], scope: { factoryIds: ['F1', 'F2'], industryTags: ['fixture'] },
  start: {
    objectTypeIds: ['lot'], datasetRevisionNId: 'fixture-lots-r1', collectionPath: ['rows'],
    parameterMap: [], searchFields: [],
    columns: [{ fieldId: 'code', label: '批号', width: 160, visible: true }],
    rowKey: row('rowId'), objectType: constant('lot'),
    identityMap: { sourceSystem: row('sourceSystem'), factory: row('factory'), businessKey: row('businessKey') },
  },
  objects: [{
    id: 'lot', name: '批次',
    identity: { sourceSystem: row('sourceSystem'), factory: row('factory'), businessKey: row('businessKey'), grain: 'Batch' },
    summary: [{ label: '批号', value: row('code') }],
    detail: { datasetRevisionNId: 'fixture-lot-detail-r1',
      parameterMap: [{ target: 'id', source: node('identity', 'businessKey') }, { target: 'factory', source: node('identity', 'factory') }],
      fields: [{ label: '批号', value: row('code'), format: 'Text' }], links: [] },
    style: { color: '#5279a8', iconKey: 'box', width: 180, height: 80 },
  }],
  relations: [{
    id: 'consume', name: '消耗/返工事实', fromTypeId: 'lot', toTypeId: 'lot',
    kind: 'Genealogy', semantic: 'Consume', forward: traversal(false), reverse: traversal(true),
    identity: row('movementId'), evidence: { recordKey: row('movementId'), occurredOn: row('occurredOn'), quantity: row('quantity'), unit: row('unit') },
    label: row('movementId'),
  }],
  layout: { nodes: [{ objectTypeId: 'lot', x: 80, y: 80, width: 180, height: 80 }],
    edges: [{ relationId: 'consume', sourcePort: 'out', targetPort: 'in', vertices: [{ x: 300, y: 40 }] }] },
  limits: { maxDepth: 12, maxNodes: 1000, maxEdges: 3000, timeoutSeconds: 30 },
}
```

固定Dataset与结果映射：`fixture-lots-r1`返回`{rows:[{rowId,sourceSystem,factory,businessKey,code}],total:"4"}`；detail按可信factory+id仅返回对应行；forward按fromId，reverse按toId过滤下表movement，输出相同movementId/occurredOn/quantity/unit，targetKey分别为to/from的businessKey。来源策略再限制可信租户/工厂；前端传factory不能扩大权限。所有时间为`2026-10-31T00:00:00Z`，unit=`kg`，quantity为下表十进制字符串。

| 运行key（仅夹具不透明别名） | 完整身份（tenant/source/factory/type/businessKey） | code | 图位置x/y |
| --- | --- | --- | --- |
| nA | tenant-fixture/MES-A/F1/lot/A | LOT-001 | 80/80 |
| nB | tenant-fixture/MES-A/F1/lot/B | LOT-002 | 80/240 |
| nC | tenant-fixture/MES-A/F1/lot/C | LOT-003 | 360/160 |
| nA2 | tenant-fixture/MES-A/F2/lot/A | LOT-001 | 80/400 |

| 边key | relationId | from→to | evidence.recordKey | quantity |
| --- | --- | --- | --- | --- |
| e1 | consume | nA→nC | M1 | "2.0" |
| e2 | consume | nA→nC | M2 | "3.0" |
| e3 | consume | nB→nC | M3 | "4.0" |
| e4 | consume | nC→nA | M4 | "1.0" |

双起点nA/nA2、Both、深度12的固定回包含4节点4边；nA2独立存在不合并。每个运行节点definitionNodeId=`lot`，port为in/out、尺寸180×80；每条边definitionEdgeId=`consume`，source=from.out、target=to.in，布局vertices空，实际多重边与环的路由可由X6适配计算，不能改变ID。`scope={contextKey:'trace-page-1',definition:{kind:'published',resourceNId:'fixture-trace',revisionNId:'fixture-trace-r1'}}`，runKey=`query-1`，generation=`1`，responseVersion=`1`，requestId=`fixture-q1`。TraceResult.status=Complete，partialReasons=[]，观察来源MES-A为上表时刻；RuntimeSnapshot.instances/overlays为空。

预期结果（纸面推演，非已运行测试）：

1. 保存/重开仍为一个lot定义节点和一个consume自环关系，不能保存成4个对象类型。布局/绑定/limits不丢；端口不会生成额外持久对象。
2. 选e1/e2分别显示M1/2.0和M2/3.0；选择nA2详情必须取F2+A。上游查nC仍显示nA→nC箭头；重复expand同key不增加边，e4真实环仍存在，重复展开由服务算法停止。
3. 把consume.targetPort置null，领域生成定位到edge.consume/path=`layout.edges[0].targetPort`的error；可保存草稿、发布阻断；一次undo恢复in和原投影。拖动lot只有一次布局历史。
4. 删除lot同时清consume、相关layout及start.objectTypeIds中的lot，产生缺起点配置诊断；一次undo完整恢复。不会删除运行M1～M4，也不改已发布r1。
5. 将夹具回包改Partial、原因`fixture:source-timeout`、MES-A观察quality=unknown，显示Partial与未知来源，不显示生产Failed或Complete；取消后迟到同requestId回包也因generation失效而丢弃。
6. 凭证过期/撤权由夹具明确模拟拒绝，页面清不可见内容；纸面状态不证明实际签名或服务器授权通过。文件/外链使用B28/B45独立夹具，不将无附件的此例当作扫描/CSP证据。

## 10. 固定纸面夹具二：生产多实例

夹具名`production-parallel-v1`。这是PoC自己的类型化领域数据，不是承诺将来ProcessDefinition的数据库/API schema；属性面板只能改标签/布局与夹具允许的类型，运行状态只读注入。公共组件仍只用§2～6。

```json
{
  "schemaVersion": 1,
  "id": "fixture-process",
  "revisionNId": "fixture-process-r1",
  "activities": [
    {"id":"issue","kind":"task","name":"领料"},
    {"id":"weigh","kind":"task","name":"称量"},
    {"id":"split","kind":"parallelSplit","name":"并行开始"},
    {"id":"process","kind":"task","name":"加工"},
    {"id":"inspect","kind":"task","name":"检验"},
    {"id":"join","kind":"parallelJoin","name":"汇合"},
    {"id":"release","kind":"task","name":"放行"}
  ],
  "transitions": [
    {"id":"c1","from":"issue","to":"weigh","kind":"control"},
    {"id":"c2","from":"weigh","to":"split","kind":"control"},
    {"id":"c3","from":"split","to":"process","kind":"control"},
    {"id":"c4","from":"split","to":"inspect","kind":"control"},
    {"id":"c5","from":"process","to":"join","kind":"control"},
    {"id":"c6","from":"inspect","to":"join","kind":"control"},
    {"id":"c7","from":"join","to":"release","kind":"control"}
  ],
  "layout": {
    "nodes": [
      {"id":"issue","x":0,"y":120,"width":160,"height":72},
      {"id":"weigh","x":220,"y":120,"width":160,"height":72},
      {"id":"split","x":440,"y":120,"width":80,"height":72},
      {"id":"process","x":600,"y":40,"width":160,"height":72},
      {"id":"inspect","x":600,"y":240,"width":160,"height":72},
      {"id":"join","x":840,"y":120,"width":80,"height":72},
      {"id":"release","x":1000,"y":120,"width":160,"height":72}
    ],
    "edges": []
  }
}
```

生产适配：activities→NodeView（id/definitionNodeId同activity.id，kind=`production.`+kind）；transitions→EdgeView（id/definitionEdgeId同transition.id），固定in/out端口，source=from.out、target=to.in；空edges布局逐条投影为vertices=[]。连接只允许out→in；本夹具拒绝重复control端点对，允许split两出/join两入；这些规则写在生产适配，不能影响追溯多重边。禁止把control线误称数据绑定，也不在此定义BPMN Token执行。

运行`scope.contextKey=production-page-1`，definition为published/fixture-process/fixture-process-r1，runKey=`process-001`，generation=`1`。以下时间统一当日UTC：

| 实例id / definitionNodeId | iteration / attempt | 快照1（responseVersion="1"） | 快照2（responseVersion="2"） | startedOn / endedOn（快照2） |
| --- | --- | --- | --- | --- |
| i-issue / issue | 1 / 1 | Completed | Completed | 00:00 / 00:01 |
| i-weigh / weigh | 1 / 1 | Completed | Completed | 00:01 / 00:02 |
| i-split / split | 1 / 1 | Completed | Completed | 00:02 / 00:02 |
| i-process / process | 1 / 1 | Completed | Completed | 00:02 / 00:06 |
| i-inspect-1 / inspect | 1 / 1 | Failed | Failed | 00:02 / 00:04 |
| i-inspect-2 / inspect | 2 / 1 | Running | Completed | 00:04 / 00:07 |
| i-join / join | 1 / 1 | Waiting | Completed | 00:02 / 00:07 |
| i-release / release | 1 / 1 | Waiting | Completed | 00:07 / 00:08 |

时间写入RuntimeInstance时补齐`2026-10-31T…:00Z`；快照1中Running/Waiting的endedOn=null，尚未开始的i-release.startedOn=null。stateLabel为中文状态，Completed=success、Failed=danger、Running=info、Waiting=neutral；detailRef=`detail:`+实例id。快照1 observedOn/receivedOn=`2026-10-31T00:06:00Z`，快照2为`00:08:00Z`；来源`fixture-production`的观察时间同该快照并标明“模拟状态”，completeness=complete只指夹具数据齐全，不是全部任务成功；requestId依次fixture-p1/p2。

每节点overlay.instanceIds列其全部实例；普通单实例label为stateLabel/tone同实例。inspect的快照1汇总“2次：1失败、1运行”/warning；快照2“2次：1失败、1完成”/warning，详情仍列两次。不以最近一次成功涂掉第一次失败，也不把两次业务迭代写成attempt=2。若未来增加同迭代重试，应新增实例id、保持iteration而增加attempt；本例不模拟执行器重试。

预期结果：

1. 同一个工作台切到此适配，显示7节点7控制边，无追溯实体依赖。选择inspect显示两实例，选择i-inspect-1始终看到Failed历史；刷新快照2不生成编辑历史。
2. 改inspect名称+移动坐标可组合成一次事务；一次undo恢复两者；保存重开不包含instances/overlays。运行态拖动只改viewState。
3. 在快照1选i-inspect-1后切i-inspect-2，旧详情迟到必须丢弃；快照2替换后旧responseVersion=1详情也丢弃并重取当前选择。
4. 新定义r2不能套在process-001的r1运行实例上；错修订回包拒绝，提示重新加载匹配定义。非法连接由生产适配报错，不改变trace适配的多边/环行为。
5. join/release显示变化全部来自夹具；图层没有“推进到放行”的业务命令，纸面与G2都不能据此验收真实并行汇合或库存/设备动作。

## 11. G2 直接输入、核验及完成边界

下一会话可直接按本文§2～10实现**隔离PoC：一个公共工作台、一个X6适配、两个领域适配与固定夹具**。先核对最新HEAD/WIP与隔离目录，不修改用户调试数据；本轮没有创建PoC。G2发现合同不能实现时先回写本明细及受影响权威规格，不能另留一份竞争schema。

| 已有验收映射 | G2核验与须留证据 | 产品/后续阶段界限 |
| --- | --- | --- |
| GX-01/02，A02，B25/B29 | 两夹具同组件；保存重开；复合编辑单次撤销；delete级联；pointercancel回退；输入IME/Ctrl+Z；诊断定位 | G2夹具序列化不代替T1真实资源CAS/发布 |
| GX-03，A12，B26/B27 | 4节点4边全部可选，e1/e2分离、自环/多父保留；切生产适配不复制工作台 | 另需T1批次链+序列号装配链、真实有界BFS |
| GX-04/05 | 两次检验与两快照；错修订/旧查询/旧选择成功与失败；取消/切租户/卸载/KeepAlive | 真实持久化生产实例与事件顺序留GX-10 |
| GX-06，B24/B28/B41/B45，D03/D06 | 模拟凭证到期/撤权/只读/文件嵌入拒绝，记录mock边界；run不读Design修订 | T1实测签名5分钟、消费重授权、扫描/CSP；不以PoC伪凭证PASS关闭安全门禁 |
| GX-07，B29 | G2若试验壳窄适配，证明树模式不受影响；单一视口缩放后连线/折点/属性联动；三视口中英明暗及侧栏 | 若PoC没加载真实壳，标未测，接产品前补真实消费者回归 |
| GX-08，页面规格§9 | 固定4核/8GiB基准，1000节点/3000边完整图，固定夹具5次，记录P50/P95、峰值内存、布局/渲染/交互分段、重开/刷新释放 | 夹具无取数则取数耗时=不适用，不能报0证明查询性能；T1另测30秒预算及Partial |
| GX-09，B44，D01/D09/D10 | 依赖树不引Report/Office/生产；隔离构建与导入方向检查 | 原生/独立实际菜单、专用API及单模块装配由T1/T2适用验收，不关闭C5全组合 |
| GX-10 | 只列未来输入：定义/实例表、等待/汇合/重试/返工/设备副作用与恢复 | G2不实现、不计通过 |

候选锁包清单沿主文§7.1/page§8：记录实际包name/version/integrity/sourceCommit、发行包hash、许可文件/NOTICE及传递依赖、测试证据。X6精确版本本轮未选；G2查官方发行物和实际插件peer兼容，不能把历史MIT描述当整包准入。逐项判断是否需要独立插件：选择/框选、键盘、端口连接/折点、节点Vue渲染、布局；只取实测需要项，History不成为权威栈，剪贴板/小地图等非本轮必要项不顺手引入。

释放核验记录：打开/切夹具/注入快照/隐藏恢复/关闭循环，监听器、DOM、ResizeObserver、timer、请求、图实例与堆快照；标明浏览器/构建/版本、循环次数、GC条件及初始/稳定/峰值，不以一次“dispose已调用”称无泄漏。性能须同时记录全部1000/3000已加载且可选择的数量，失败/超时/Partial保留，禁止隐藏节点降低数据量冒充通过。

三类状态清单：

- **已确定（G1）**：定义/布局/运行分层；公共v1类型与本地版本；领域强类型编辑/连接/校验；一次事务一条历史；X6唯一图视口；TraceModel逐字段映射；端口空值草稿；三模式页面、运行详情归属/释放；两份纸面固定夹具及原GX/A/B/D映射。
- **待PoC（G2）**：实际版本/插件/许可物料；壳外部控制窄接口及X6事件抑制；拖拽取消/键盘焦点/连线/循环和多重边路由；布局算法、节点渲染方式、100步快照成本；1000/3000性能与释放；实际两个消费者复用证据。不得标已安装、通过或生产准入。
- **后续生产**：Process/Task/Step定义与实例持久化、任务授权/动作、等待/汇合、并发及恢复、设备副作用、BPMN互操作范围、在途迁移和业务审计。Trace T1/T2仍按011～014与本身准入连续推进，不能由G1完成自动启动。

## 12. 本轮核对记录

文档推演覆盖：trace同端点M1/M2分别选择、多父M3、真实环M4、跨厂同编码；生产两次检验与快照切换；共同保存/重开、复合撤销、诊断定位和详情归属。合同中没有公共图层业务查询或生产推进入口。权威同步范围仅接口§7.1与页面§6的歧义收口；不变更报告预览、临时查询表格或Workbook生命周期。

完成验证只限文档：用仓库现有TypeScript编译器在内存中检查6段TS示例（strict/noEmit，0错误），执行TraceModel示例检查绑定/预算/自环结构，解析生产JSON并核对7节点7边及并行分支/汇合；检查本轮7份文档273处本地文件链接均存在，`git diff --check`通过。Python启动别名不可用后改用现有Node完成链接检查，未安装工具。未运行X6、浏览器、后端/前端产品构建或业务测试。G2的真实结果回写主文§10及适用证据，不能把本文的预期结果改写为实测通过。
