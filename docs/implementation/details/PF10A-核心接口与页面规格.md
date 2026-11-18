# PF10A 核心接口与页面规格

版本：V0.5设计稿；日期：2026-11-18。关联[实施13A](../13A-Industrial%20Platform知识中心开发实施方案.md)与[数据规格](PF10A-核心数据与一致性规格.md)。覆盖知识编辑/阅读、问题详情与全局助手；后台连接管理、资料包合并、辅助工具及全部移动端布局另行细化。下列路径为拟注册合同，不表示接口已存在；注册、权限、迁移、组件和真实样本按实施 13A §13.3 验证。

业务交互、直接入库、项目资料包与 DeepSeek 请求样例见[全局助手与资料复用](PF10A-全局助手与资料复用.md)。下文 Published 表示当前已入库的有效修订。

## 1. 通用请求、版本、权限

API根`/api/v1/knowledgehub`；浏览器复用现有HTTP客户端与认证，不能在URL传Secret或长正文。真实入口由UnifiedHost/Gateway模块别名注册，不能另造冲突路由。

- 用户/租户从已验证上下文取得；首期默认项目由服务端解析，响应给出projectNId，浏览器不能通过替换项目参数越权。
- 更新或删除既有根资源时输入`optimisticVersion:string`与`concurrencyVersion:GUID`，必须与当前版本同时匹配；沿现有Studio VersionCommand语义。V0.1的expectedVersion统一由这两个字段替代，不再并存第三套令牌。
- 有副作用的POST/PUT/DELETE还携`operationKey:string(1..64)`；DELETE通过请求头`Idempotency-Key`，其余在body；DELETE版本头固定为`X-Optimistic-Version`和`X-Concurrency-Version`。消息用 clientMessageId；页面动作按 actionId 去重执行，各阶段回执按 §4.4 的 (actionId,status) 去重；取消/澄清按§4专用合同，不要求根资源版本。同key同参数返回原结果，不同参数409；待完成202/Running不谎称成功。
- JSON body只接收声明字段，拒绝未知属性；所有字符串长度/数组/正文限额见数据规格。输入trim只用于可见标题/标签/原因，不改主体标识/正文内容。
- 正常响应沿平台标准包裹，本文展示其业务data；列表data=`{items:[],page:1,pageSize:20,total:"0"}`，page≥1、pageSize=1..100，默认20。排序只能白名单字段，追加NId作稳定同值排序；total是string。
- 401未认证、403缺动作权限、404不存在或对象不在当前范围、409版本/幂等冲突、422字段/状态非法、429预算、503依赖不可用。业务错误code/path/traceId进入现有错误包装，不能新造与平台冲突的根包裹。

权限记号：KR/KM=`knowledgehub.knowledge.read/manage`；IR/IC/IM=`knowledgehub.issue.read/create/manage`；AU=`knowledgehub.assistant.use`；SM=`knowledgehub.settings.manage`。它们是现有平台权限目录中的动作，不是新角色体系。所有表中权限要求还叠加当前租户、项目、对象关系及状态；返回allowedActions供UI展示，服务端执行时重新判断。

| 操作对象 | 规则 |
| --- | --- |
| 已发布知识 | KR；编辑草稿/历史需KM |
| 本人问题草稿 | IC可创建/读写/删除/提交本人草稿；IM可管理项目草稿；IR不能读他人草稿 |
| 已提交问题 | IR或IM可读项目记录，IC保留本人创建记录的读取权；本人提交者/指定补充人可补充；指定验证人可验证；受理/分派/方案编辑/代验/取消/重开需IM |
| 知识转换 | KM及按本表可读问题、有效方案。mode=Collect 收录，mode=Draft 暂存 |
| 助手 | AU＋具体业务能力权限；会话/执行/结果只归当前本人；管理员配置权限不能阅读别人的会话 |

## 2. 知识接口

<a id="kh-dto"></a>

### 2.1 DTO

`KnowledgeContentInput`：

| 字段 | 类型/要求 | 页面来源 |
| --- | --- | --- |
| title | string，1..200 | 标题 |
| summary | string，0..1000，默认空 | 摘要，可选 |
| contentFormat | Markdown/RichText | 编辑方式，已有内容转换不得丢失格式 |
| content | string，UTF-8≤1MiB | 正文，草稿允许空；RichText机读schema待编辑器冻结 |
| categoryNId | string或null | 当前项目分类 |
| tags | string[]，≤10，每项1..32 | 自由标签输入、去重，无标签管理审批 |
| applicability | `{schemaVersion:1,mode:Unspecified|All|Listed,versions:string[]}` | 默认未注明，可直接入库；可选通用或指定系统版本，版本候选来自项目配置 |
| attachments | `{attachmentNId:string,description:string,sortOrder:number}[]` | 只能引用当前草稿已准备附件，≤20；不可传他人的FileNId绑定 |

`KnowledgeListItem`：`nId,title,summary,category:{nId,name}|null,tags,applicability,owner:{userNId,displayName},status,publishedRevisionNo?,hasDraftChanges,indexState,updatedOn,allowedActions`。普通列表元数据取发布修订；维护列表明确标明“草稿”且仅授权用户可见。

有 q 时每项可附 matches，含 matchedBy（Title/FileName/Metadata/Content）、quote?、revisionNId、locator?。元数据命中只打开资料，Content 命中可定位原始块。解析覆盖度和不可提取原因随状态展示，见[主线规格](PF10A-资料解析检索与引用验收.md)。

`KnowledgeDetail`在ListItem基础增加`revisionNId,revisionNo,contentFormat,content,attachments[],source?,optimisticVersion,concurrencyVersion`。attachment包含业务NId、文件名、类型、长度string、说明、当前availability（Ready/Blocked/Missing/BindingFailed）、safeReason，不返回永久下载地址。source仅在当前用户有来源权限时返回可导航信息；无权限不暴露问题标题/描述。

<a id="kh-apis"></a>

### 2.2 动作表

| 方法/路径 | 输入及权限 | 成功data |
| --- | --- | --- |
| GET /knowledge | q≤200、categoryNId?、tag?、systemVersion?、view=published/manage、status?、分页/排序；KR或KM | ListItem页；published只返回有效发布；manage限定维护权限 |
| GET /knowledge/{nid} | mode=published/draft/history，history需revisionNId；按§1 | Detail；draft缺失404 DRAFT_NOT_FOUND，不通过GET隐式创建 |
| GET /knowledge/{nid}/revisions | KM；分页 | revisionNId/no、title、状态、适用版本、发布人/时间；不默认把所有正文送浏览器 |
| POST /knowledge/imports | KM；operationKey、items[]，每项 clientItemId、fileName、contentType、byteLength（十进制串）、sha256、choice=New/Revision/Skip；Revision 另带目标 documentNId 及双版本。默认 New | 202 `{importNId,items:[{clientItemId,itemNId,state}]}`；按数据 §2.8 预留附件标识 |
| POST /knowledge/imports/{nid}/items/{itemNId}/prepare | KM、当前项目批次发起人、operationKey，使用该项元数据 | 返回 uploadSessionNId、expiresAt，前端走 File 上传通道传字节。同一有效会话可续传 |
| POST /knowledge/imports/{nid}/items/{itemNId}/commit | 同上、operationKey、上传完成时 File 返回的 fileNId；服务端复核该项的会话、长度、摘要和主体 | 202 受理。扫描待确认保持 WaitingForScan，明确拒绝为 Failed；后台复核可读后自动收录。已完成项重复提交可由原批次读取原目标 |
| GET /knowledge/imports/{nid} | KM、同项目批次发起人 | 200 `{importNId,items:[{itemNId,clientItemId,state,safeErrorCode,documentNId,revisionNId,indexState}]}`；未产生的字段为 null。临时失败可用原 itemNId 和新的请求 operationKey 重试该阶段，入库按项去重 |
| POST /knowledge | ContentInput＋operationKey；KM | 201 `{documentNId,revisionNId,optimisticVersion,concurrencyVersion}`，Draft |
| POST /knowledge/{nid}/draft | 双版本、operationKey、sourceRevisionNId?；KM | 从当前发布或指定历史复制；已有活动草稿返回409 ACTIVE_DRAFT_EXISTS及该草稿入口 |
| PUT /knowledge/{nid}/draft | ContentInput＋双版本＋operationKey；KM | 更新后的Detail，发布内容不改变 |
| DELETE /knowledge/{nid}/draft | 双版本头与Idempotency-Key；KM | 200 `{discarded:true}`；无发布版本时连同文档软删；已发布文档只弃草稿 |
| POST /knowledge/{nid}/collect | revisionNId、双版本、releaseNote?≤1000、operationKey；KM | `{revisionNId,revisionNo,status:Published,indexState:Pending,...双版本}`；适用于手写稿收录，上传资料由后台自动完成生效 |
| POST /knowledge/{nid}/disable | 双版本、reason 1..1000、operationKey；KM | Disabled＋双版本；立即禁止普通阅读/检索 |
| POST /knowledge/{nid}/reactivate | 双版本、revisionNId=当前发布指针、reason、operationKey；KM | Published＋Pending；选择其他历史版本须复制草稿再发布 |
| POST /knowledge/{nid}/index-retries | revisionNId、双版本、operationKey；KM，必须当前发布版 | 202 `{taskNId,generation,state:Pending}` |
| GET /knowledge/categories | view=read/manage；KR/KM | 当前项目目录，无权限过滤含义 |
| POST/PUT /knowledge/categories[/{nid}] | name≤64、parentNId?、sortOrder、双版本（更新）、operationKey；KM | 分类NId＋双版本，环/深度冲突422 |

正文发布前清理/拒绝危险HTML、外链资源和不可解析结构；按所选编辑器规定明确报字段错误，不静默删除有业务含义的正文。文件不Ready、仅图片无文字说明等不能作为有可检索正文的证明，发布可保留仅附件文档但索引状态显示不可提取，助手不编造答案。

发布响应示例：

```json
{
  "revisionNId":"rev_example_2",
  "revisionNo":"2",
  "status":"Published",
  "indexState":"Pending",
  "optimisticVersion":"7",
  "concurrencyVersion":"d3888a6a-08cf-4450-8dc4-603da24b7838"
}
```

页面反馈“已入库，正在解析”，不是“AI已可引用”。若返回时就绪状态已更新，仍以任务/文档最新响应显示。

<a id="kh-file"></a>

### 2.3 文件入口与引用定位

普通首次上传走 §2.2 的批次和逐项 prepare/commit，开始时无需已有文档。给已有文档或问题追加附件时采用以下合同。

附件上传使用File公开上传流程；KnowledgeHub附件准备接口校验父文档/问题权限，分配目标attachmentNId并返回公开上传会话。前端完成上传后调用attachment commit，服务端核对来源主体、hash、扫描及归属；具体File桥接DTO在待实现适配规格冻结。不能直接接受任意fileNId作为已授权附件。

`POST /attachments/{attachmentNId}/read-ticket`：请求`purpose=Preview|Download|Extract`，人类调用仅Preview/Download；Extract只供受信后台身份且保留发起主体范围。返回短期文件票据/现有读取入口，由File控制有效期；每次业务授权复核，票据不是永久绕过范围的下载地址。

知识引用格式：`{documentNId,revisionNId,locator:{source:Body|Attachment,attachmentNId?,kind:Paragraph|PdfPage|SheetRange,index?,page?,sheet?,range?},quote:string<=2000,applicability}`。source=Attachment时attachmentNId必填且属于该修订，页码/段落/单元格坐标均相对该附件；Body禁止attachmentNId。附件引用逐次检查对应File权限及可用状态，不能只凭文档权限读取。打开引用核验当前发布指针与状态；历史修订只向KM开放历史模式，普通用户看到“引用版本已更新/停用”，可选择打开当前版，不能把新版内容伪装成旧引用。

生成答复时以临时 evidenceId 关联上述引用。由服务端校验并解析成实际链接，DOCX 段落与表行使用原修订的有序阅读块，PDF 页和工作表范围按原件定位。

## 3. 问题接口与动作

### 3.1 DTO

`IssueDraftInput`：title 1..200、description UTF-8≤64KiB、severity=Low|Normal|High|Critical（默认Normal）、systemVersion:string≤64|null、attachments:业务附件NId[]≤20。创建/编辑不能传state、assignee、verified状态；这些用明确动作接口。

`IssueDetail`：`nId,title,description,severity,systemVersion,state,createdBy,submittedBy?,assignee?,responder?,verifier?,rootCause,solution,solutionVersion,verifiedSolutionVersion?,submittedOn?,closedOn?,attachments[],optimisticVersion,concurrencyVersion,allowedActions[]`。人员显示名称来自目录公开接口，不把名称当外键；用户失效时保留身份与历史名称展示、要求重新分派。

`IssueActivity`：`nId,sequence,kind,actor,occurredOn,fromState?,toState?,body,reason?,solutionVersion?,attachments[]`；列表分页按sequence升序，首次详情加载最新20条，向上加载旧记录，不能因新活动滚动打断用户阅读。

### 3.2 接口表

| 方法/路径 | 输入/权限 | 成功data |
| --- | --- | --- |
| GET /issues | q≤200、view=mine/drafts/assigned/verify/all、state?、severity?、systemVersion?、分页；IC限本人创建记录（含提交后）；项目范围IR或IM | 问题摘要页，草稿不会泄露给仅IR者 |
| GET /issues/{nid} | 按§1可读条件 | Detail |
| POST /issues | DraftInput＋operationKey；IC | 201 Detail（Draft），包括AI创建 |
| PUT /issues/{nid}/draft | DraftInput＋双版本＋operationKey；本人IC或IM | Detail；非Draft返回409 STATE_CONFLICT |
| DELETE /issues/{nid}/draft | 双版本＋幂等头；本人IC或IM | 软删草稿，不删File保全引用 |
| POST /issues/{nid}/transitions | action＋双版本＋operationKey＋下表动作字段 | Detail＋最新activityNId |
| PUT /issues/{nid}/solution | rootCause≤16KiB、solution≤64KiB、双版本、operationKey；IM且InProgress | 更新solutionVersion；内容未变则幂等返回 |
| POST /issues/{nid}/activities | body 1..64KiB、attachmentNIds[]、双版本、operationKey；当前允许补充者 | 新activity＋双版本；与provideInfo不同，不默默切状态 |
| GET /issues/{nid}/activities | beforeSequence?、pageSize≤100；可读 | 活动页 |
| POST /issues/{nid}/knowledge-drafts | mode=Collect/Draft、solutionVersion:string、attachmentNIds[]≤20、双版本、operationKey；KM且来源问题可读 | 202 `{conversionNId,state,targetDocumentNId?}`；不回报已发布 |
| GET /knowledge-conversions/{nid} | 来源问题可读且KM | 状态、目标草稿、缺附件错误；完成可打开草稿 |
| POST /knowledge-conversions/{nid}/retry | operationKey等于原操作，KM且来源问题可读 | 202继续同一转换；参数改变需新转换并提示已有草稿 |

### 3.3 固定迁移矩阵

所有动作都检查双版本；非法状态409，动作字段错误422。以下字段之外均拒绝，避免用通用patch任意改状态。

| action | 起点→终点 | 必需字段/执行者 |
| --- | --- | --- |
| submit | Draft→Open | 现象说明≥10非空白字符；本人IC或IM；冻结submittedBy/On，附件可选 |
| assign | Open/InProgress→原状态 | assigneeUserNId；IM；在项目有IM权限且目录有效的人员 |
| accept | Open→InProgress | assigneeUserNId（默认本人）；IM；不可选无处理权限者 |
| requestInfo | InProgress→WaitingForInfo | responderUserNId、reason；IM，指定人员在项目可读该问题 |
| provideInfo | WaitingForInfo→InProgress | body；提交人或指定补充人且可读，或IM |
| requestVerification | InProgress→PendingVerification | solutionVersion=当前非空方案版本；verifierUserNId?默认提交人；IM |
| reject | PendingVerification→InProgress | reason；指定验证人且可读，或IM；保留方案 |
| close | PendingVerification→Closed | solutionVersion=送验版本；指定验证人且可读；IM代验另填reason |
| reopen | Closed→InProgress | reason、assigneeUserNId（原处理人失效时必填）；IM |
| cancel | Open/InProgress/WaitingForInfo→Cancelled | reason；IM；取消不能转换为“已解决” |

Draft可直接删除；PendingVerification要先reject再cancel，避免绕过验证事实；Cancelled首期无恢复入口，必要时人工新建并引用原问题。InProgress可用评论补充信息，但只有requestInfo/provideInfo才改变等待状态。

<a id="kh-conversion"></a>

### 3.4 转知识映射与反馈

转知识对话框预填标题/现象/根因/方案/适用版本，允许选择附件；编辑后的知识正文在目标草稿中完成，不在转换请求内开放任意隐式改写。源方案未明确版本时新草稿标“适用范围待确认”，可直接收录，未知适用性随引用显示。转换不复制问题的私密处理评论，仅复制明确方案快照及用户选择的附件。

重复点击同一次按钮使用同一operationKey；浏览器刷新可从问题“关联知识”恢复转换状态。失败显示“草稿已创建，2份附件尚未绑定”而非笼统失败；用户可重试绑定或在草稿移除缺失项后保存。转知识不影响问题关闭。

## 4. 全局助手接口与有限能力

### 4.1 会话/执行请求

| 方法/路径 | 输入 | 结果 |
| --- | --- | --- |
| GET/POST /conversations | 读取本人会话；创建title?＋operationKey；AU | 会话列表/201 conversationNId和当前项目 |
| DELETE /conversations/{nid} | 双版本/幂等头；AU且本人 | 隐藏会话、取消尚未执行步骤；不撤销业务写入 |
| GET /conversations/{nid}/messages | afterSequence?、pageSize≤100 | 本人可读消息，重新校验来源后可返回Redacted占位 |
| POST /conversations/{nid}/messages | clientMessageId、text≤64KiB、taskNId?、pageContext? | 202 `{messageNId,executionNId,state:Queued}` |
| GET /executions/{nid} | AU且本人，领取动作时携带 X-Page-Context-Id | 执行状态、步骤、结果及安全错误，仅向匹配发起实例返回有效 pendingPageAction（§4.4） |
| POST /executions/{nid}/cancel | operationKey；AU且本人 | 202 cancelRequested；执行完成后以最终状态为准 |
| POST /executions/{nid}/clarifications | promptNId、answer≤8KiB、clientMessageId | 202原执行重新排队并递增contextVersion；原prompt只接受一次，旧写操作不重放；过期410、已被不同回答消费409 |
| GET /results/{nid} | AU＋来源当前权限、本人、未到期 | 结果或410 RESULT_EXPIRED；无权404 |
| POST /results/{nid}/exports | format=Xlsx、operationKey | 202任务标识；默认导出固定快照，失效410，不自动重查 |

取消按当前状态幂等记录 cancelRequested，已经完成则返回实际状态。消息按 sequence 补拉，前端动作通过执行状态读取。宿主可用 SignalR 提醒前端刷新，断线恢复仍读取相同的权威状态，具体机制见 §4.4。

### 4.2 前端pageContext

```json
{
  "schemaVersion":1,
  "pageContextId":"tab_opaque_id",
  "version":"4",
  "pageId":"knowledgehub.issue.list",
  "filters":{"state":"Open","severity":"High"},
  "selectedRecordNIds":["issue_example"],
  "visibleResultNId":null
}
```

字段严格白名单；version为页面实例单调递增字符串；selectedRecordNIds≤20。传业务标识不传整张列表或隐藏列，页面离开/选择清空更新版本。context只能辅助理解，后端按PageCapability声明重新验证筛选与对象权限。未来MES页面未登记时明确“不支持带条件导航”，不能静默忽略过滤器。

任务state_json：`{schemaVersion:1,kind,projectNId,systemVersion?,filters,selectedObjectRefs:[],timeRange?:{from,until,timeZoneId,resolvedAt,originalText},resultNIds:[],corrections:[]}`。filters按kind/schema白名单解析；corrections最多20条当前有效明确纠正，超限压缩但结构化最终条件不丢失。摘要为`{schemaVersion:1,text,throughSequence}`，sourceRefs单列，撤权时摘要失效重建；不由摘要覆盖state_json。

### 4.3 首批登记能力与扩展契约

表内为首批能力实例。后续模块按相同契约登记动作，项目可为同一动作绑定多个业务资源。注册字段、处理器职责、模型函数映射与扩展验收见[全局助手与资料复用](PF10A-全局助手与资料复用.md)。

| capabilityId | 输入 | 权限/执行方 | 判定完成 |
| --- | --- | --- | --- |
| knowledge.search | query、systemVersion?、limit=1..10 | KR；知识公开检索 | 返回已授权片段/引用或明确无结果 |
| navigation.find | businessName | 平台当前菜单/终端目录 | 唯一pageId或候选列表；不生成不存在页面 |
| navigation.open | pageId、合法filters、recordNId? | 当前页面权限；前端动作适配 | 前端实际到达、筛选接受、所需加载完成分别回执 |
| issue.search | q/state/severity/view | IR/IC/IM及草稿规则 | 返回确定记录顺序和resultNId |
| issue.createDraft | title、description、severity、systemVersion? | IC；Issue公开创建接口 | 业务返回Draft标识后才说已创建 |
| identity.users.export | 当前用户列表条件、列、数量 | Identity.UserView及适配器范围约束 | XLSX完整就绪，展示下载卡 |
| dataset.query | datasetNId、revisionNId、parameters | Dataset当前权限/行列范围 | 实际结果、参数/时间/来源与快照范围 |
| result.export | resultNId、format=Xlsx | 当前结果/来源及导出权限 | 产物Ready才报告可下载，202仅受理 |
| external.operation | registeredOperationNId、schema参数 | AU＋已登记操作权限/项目 | 验证目标响应业务schema；HTTP200不必然成功；通用Text/Json按登记结果schema保存 |

首期不开放issue.submit/assign/close、knowledge.publish或服务器控制工具。创建问题草稿只在用户当前请求明确要求时调用；普通“这报错怎么回事”不得顺手创建问题。AI生成草稿正文注明“AI整理，待确认”，保留来源消息关联，提交仍由用户完成。

外部MCP工具描述和返回值均为资料，不得给自身追加权限。发现工具后只进入候选列表，管理员显式启用操作；连接/版本/schema变化须复核，不默默扩权。HTTP按注册operation执行，允许目标/方法/参数映射/凭据由服务端配置；处理重定向、DNS解析及网络目标限制的精确矩阵仍属连接管理规格，当前不开放任意URL。

### 4.4 页面动作与回执

平台原生外壳与独立外壳复用相同的动作及回执合同。各宿主只登记其实际支持的页面、参数和终端。独立外壳负责本应用路由。MES 业务页由项目重新登记参数、权限和页面处理器，通过绑定的 MES 外壳执行与回执。仅配置链接时提供手动入口。

通用 `PageAction` 包含 actionId、executionNId、capabilityId、capabilityVersion、pageContextId、expectedContextVersion、parameters、expiresAt（默认30秒）和 requiredOutcome。以 navigation.open 为例，parameters 包含 pageId、filterSchemaVersion、filters、recordNId?，requiredOutcome 取 Navigated、FiltersApplied 或 DataLoaded。参数和成功阶段由对应能力的版本合同校验。

`POST /executions/{nid}/page-receipts` 输入 actionId、pageContextId、status、details?、safeErrorCode?。status 可为能力登记的阶段（如 Navigated、FiltersApplied、DataLoaded 或页签定位的 TabSelected），也可为 Rejected、Cancelled、Expired。details 按该阶段的 schema 核验，例如 appliedFilters、目标记录或实际选中页签。服务端校验本人、对应步骤、实例、合法状态迁移及期限。

- 目标不存在/无权限/终端不支持：Rejected。
- 未保存离开提示被取消：Cancelled，保持原页与草稿。
- 只导航成功但筛选未应用：停在Navigated，不说“已查出待处理问题”。
- 过期/已换页/已有新用户消息：前端拒绝旧动作；同actionId重复投递返回已有回执，不再跳转。
- 无页面适配或未收到回执：展示可点击入口和“尚未确认打开”，不得伪造完成。

#### 后端发动作，发起请求的标签页执行

前端动作归平台外壳统一接收。发送消息取得 executionNId 后，外壳跟踪这次执行，直到完成、取消或过期。首期建议把待执行动作放入现有设计的执行状态响应，在执行活跃期间短轮询即可工作。若宿主提供助手实时通道，可发送“执行状态已变化”提示，让同一个客户端立即拉取。两种方式共用一次读取、一次执行和同一回执。关闭助手面板仍由外壳接收，退出登录即停止。现有 SystemData 通知按租户和用户分组，会通知该用户的多个连接，其前端已采用 SignalR 提醒后刷新、重连后补拉的方式，可借鉴这个实现。助手动作数据归 KnowledgeHub 所有。[前端通知实现](../../../src/frontend/src/api/systemData/notificationHub.ts) · [后端通知分组](../../../src/backend/src/Services/SystemData/IndustrialPlatform.SystemData.Api/Notifications/NotificationHub.cs)

以“打开工单列表，只看未完工”为例：

1. 发送消息时，外壳附上当前页面实例 pageContextId 和版本。首期每个标签页加载外壳时在内存生成新实例 ID，SignalR 重连保持它，刷新或复制标签页生成新值。服务端将请求绑定到当前认证用户、项目、会话、实例和消息。
2. 后端从可见菜单解析业务名称，确认目标已启用且允许筛选后，在 Page 类型执行步骤里持久化动作。actionId 复用该步骤的稳定 NId，input_json 保存动作与 schema 版本。步骤为 Running 时等待前端回执。事务完成后可发轻量刷新提示。SignalR 连接 ID 只用于传输，鉴权与过期判断在读取及回执时执行。
3. 拟定 GET /executions/{nid} 传入发起实例的 X-Page-Context-Id。所有者可查看历史状态，pendingPageAction 仅向匹配原实例、仍有效且未完成的请求返回。客户端只跟踪本标签页自己发起的执行。旧标签页、后台不可见、过期或新版消息已取代的动作保持原页面，展示可手动打开的入口。恢复可见后核对待办，已失效的动作不跳转。
4. 外壳按 actionId 合并重复提醒，并在执行前复查实例、源页面版本、最新消息及期限。按已注册 pageId 解析命名路由，走已有菜单权限、终端限制和离开保护。到达后由目标页面适配器校验和应用筛选，再由实际数据请求回报加载结果。动作自身引发的换页属于该 actionId，后续回执比对目标页和本次查询标识；用户另行切页则停止后续步骤。[运行时导航](../../../src/frontend/src/systemData/runtime/navigation.ts) · [路由守卫](../../../src/frontend/src/router/guards.ts)
5. 每个阶段通过 page-receipts 回传。后端按 actionId 接受单调推进的状态，重复回执返回已有状态，已完成后忽略迟到回执。只要求打开时，实际到达目标页即可结束。要求带条件显示数据时，核对筛选和对应加载结果后结束。中途失败保留已完成阶段。前端缓存待确认回执到当前实例结束，网络恢复先补报；整页刷新后仅恢复状态或提供手动入口。超时未收到凭据显示“打开结果未确认”。工作线程等待回执期间可释放，收到回执或期限到达后根据持久步骤恢复。

同一 (actionId,status) 的已接受 details 和 safeErrorCode 取规范化指纹。校验本人、执行及页面实例后，已接受的同阶段同内容返回当前状态，同阶段异内容返回 409。迟到的旧阶段保留最高进度；已终态不再触发导航。验收同一 actionId 依次回报 Navigated、FiltersApplied、DataLoaded，然后重放前两阶段，页面仍只执行一次。

#### 新增行为的接入位置

| 要增加的能力 | 开发者需要登记或实现 | 可以复用的部分 |
| --- | --- | --- |
| 打开一个新页面 | 页面模块登记稳定 pageId、已注册路由名、业务别名、必需参数、终端与权限。仅需打开的页面沿用通用导航处理器 | 名称匹配、发送动作、路由守卫、到达回执 |
| 已有页面支持“打开并筛选” | 页面声明允许的筛选字段与版本，适配器将参数交给现有筛选和查询入口，回报实际采用的条件及加载结果 | 同一导航能力和表格查询逻辑 |
| 增加一种前端交互，例如定位到当前记录的“附件”页签 | 业务模块声明独立 capabilityId 和版本、参数、前置条件及完成判据；在外壳动作注册表绑定类型化处理函数，由相应页面确认目标记录及选中页签。客户端声明支持的能力版本，缺失时返回 UnsupportedCapability | 复用 Page 类型步骤、鉴权、标签页绑定、动作领取和回执。给 PageAction 增加 capabilityId、capabilityVersion、parameters；导航字段作为 navigation.open 的参数，requiredOutcome 由各能力登记 |
| 增加后台行为，例如受控数据查询 | 业务模块接入已有应用服务或受控数据源，返回可呈现结果；需要改变页面时另发 Page 步骤 | 会话、业务权限、幂等与结果卡 |

新增行为验收包含单标签正常执行、双标签同时登录、重复提醒、断线补报、用户取消离开和客户端版本缺少处理器。多标签用例要求仅发起页变化。若不支持筛选，可提供用户手动打开页面的入口，并清楚标明条件尚未应用。

<a id="kh-history"></a>

### 4.5 引用与历史可见性

每条Assistant/Tool消息、摘要和结果保存sourceRefs。读取历史、送入模型和导出前检查当前来源；任一来源失权或被停用，无法证明安全拆分时整段生成内容显示Redacted占位，不只隐藏链接。用户本人原始输入仍属于本人消息，但不会自动当作继续读取已撤权资料的授权。历史引用更新提示“依据版本已变更”，不无提示重写旧答案。

## 5. 三条页面流程与字段映射

<a id="kh-page"></a>

### P01 知识工作台与编辑

路由：`/pc/knowledgehub`（阅读入口）、`/pc/knowledgehub/knowledge`（维护列表）、`/pc/knowledgehub/knowledge/:nid`（阅读）、`.../:nid/edit`（编辑）。菜单一组“知识中心”，默认进入阅读搜索；维护按钮按KM显示，不新增审批菜单。

```text
┌ 知识中心                         [新建知识] [AI助手] ┐
│ 搜索标题/错误码/现象  分类  适用版本  标签   [查询]    │
├ 分类（可收起） ┬ 搜索结果：标题 / 摘要 / 适用版本      ┤
│ 实施          │ 更新时间 / 发布修订 / 负责人          │
│ 配置          │ 无结果：调整条件 · 登记问题           │
│ 故障          │ 有限结果提示、分页，不把空结果当故障   │
└───────────────┴──────────────────────────────────────┘

┌ 返回知识列表  标题/草稿状态             [暂存] [收录] ┐
│ 标题 *                                             │
├ 正文编辑主区（约70%） ┬ 属性（可收起）                 ┤
│ 标题/列表/代码/表格   │ 分类、标签、适用版本、摘要      │
│ 附件及提取状态        │ 来源问题（只读、有权时）        │
└──────────────────────┴───────────────────────────────┘
```

| 字段/控件 | API字段 | 校验/行为 |
| --- | --- | --- |
| 标题、摘要、正文 | title/summary/content | 按§2长度；保存草稿正文可空，发布执行有效内容校验 |
| 分类 | categoryNId | 单选项目目录，可未分类；不是权限范围 |
| 标签 | tags | 自由输入/已有提示，≤10，不强制先建标签 |
| 适用版本 | applicability | 默认未注明，可选通用或指定版本 |
| 附件 | attachments | 上传/扫描/绑定/Ready分别显示；失败单项重试或移除，不用整页loading |
| 保存草稿 | PUT draft | 同步busy防重；409保留输入并显示重新读取/比较 |
| 收录手写资料 | POST collect | 存在未保存内容时先保存成功再使用返回的新双版本发布；无额外审批步骤 |
| 历史/回滚 | revisions＋POST draft | 预览历史，复制为新草稿；已有草稿提示先打开/舍弃，不覆盖 |

默认不自动保存服务器，避免边输入边产生版本；离开保留原平台未保存提示。手写文档首次添加附件时分配草稿标识。直接上传资料使用批量入库入口。发布动作不自动丢弃编辑缓存；以返回版本刷新。阅读页不显示未发布摘要/标题，包含来源定位、附件状态、历史更新提示。

错误反馈：附件不Ready定位到附件行；索引失败在阅读/维护页显示安全原因与重试；DeepSeek不可用不影响阅读。纠错入口使用“登记问题”预填知识引用和现象，由用户保存草稿，首期不另建纠错工单体系。

### P02 问题列表与详情

路由：`/pc/knowledgehub/issues`、`/pc/knowledgehub/issues/:nid`。列表复用AppQueryPanel/AppDataTable，默认不多选；我的草稿/我提交/我负责/待我验证/全部是过滤视图，不创建多套页面。columns：NId短显示、标题、状态、严重度、适用版本、处理人、更新时间、行内查看。

```text
┌ 返回问题列表  标题 · 状态                 当前可执行动作 ┐
├ 现象/附件（主区）              ┬ 基本信息                ┤
│ 描述、图片、来源知识/助手       │ 严重度、系统版本        │
│ 根因与解决方法（可保存）        │ 提交人、处理人、验证人  │
├ 处理时间线                    │ 时间与关联知识草稿      │
│ 评论/补充/状态/方案版本         │                         │
│ [补充说明............] [发送]  │                         │
└────────────────────────────────┴─────────────────────────┘
```

操作区域仅呈现allowedActions：草稿保存/提交/删除；Open受理/分派/取消；InProgress保存方案/请求补充/送验/取消；WaitingForInfo补充完成；PendingVerification通过/退回/转知识；Closed重开/转知识。IM代验使用原因字段，不增加单独审批页。分派/送验/退回等结构表单复用AppFormDrawer，保留共享弹窗位置偏好。

方案编辑与普通评论分开：评论不会覆盖rootCause/solution，送验前先保存方案并使用其solutionVersion。收到409时展示当前状态/处理人变化，禁用旧动作直到重读；保留未提交文字。评论失败保留输入，成功后清空；上传完成不等于评论已保存。

转知识时预览内容和所选附件，一次确认收录。绑定完成自动切换有效修订并索引，绑定失败时保留可恢复草稿，也可选择仅暂存。

<a id="kh-assistant-page"></a>

### P03 全局助手与执行结果

入口：平台顶部AI按钮；PC右侧面板可展开为独立工作区`/pc/knowledgehub/assistant`。关闭面板只隐藏UI，不取消正在执行的操作；停止按钮明确发送cancel。模型未配置时显示设置入口（仅SM）或联系配置人员提示，普通搜索/知识页面仍可使用。

```text
┌ 当前项目  当前会话           [历史] [展开] [关闭] ┐
│ 你：这个接口报错怎么处理？                       │
│ AI：依据《接口故障处理》… [引用1] [打开原文]      │
│ 条件：系统版本v2   [展开查看]                    │
│ 你：整理成问题草稿                              │
│ 执行：创建草稿 → 已创建 [打开编辑]              │
│ 输入内容.......................... [发送/停止] │
└─────────────────────────────────────────────────┘
```

文字、引用、工具进度、条件卡和结果卡按固定类型渲染，不执行模型HTML/JS。工具卡可展开操作名称/安全参数/时间/状态，凭据不展示。Read结果可继续筛选；Write卡显示目标NId与实际状态；Unknown显示“结果待核对”，只提供状态刷新，不提供无条件“再执行”。

消息可排队，前一结果归前一消息；新消息取消旧的未发页面导航，避免晚到跳页。用户“按刚才条件再查”产生新结果，“导出刚才那张表”指原快照；过期卡只提供用户主动“重新查询”。进入问题页后选择对象可供后续指代，离开后清除当前页面选择，原任务结果仍可引用。

## 6. 菜单、终端与公共组件

现有IdentityUsersPage是查询/表格/动作布局依据；不把知识阅读/编辑/聊天硬塞成CRUD抽屉。P01列表使用AppQueryPanel/AppDataTable，P02结构动作使用AppFormDrawer，权限使用PermissionGate并由服务端复核。

菜单种子拟新增一组及知识、问题、助手、设置子项；路由名与capability pageId由统一登记映射，不让模型直接构造URL。已有库用新的幂等seed版本，不能仅改旧种子期待自动出现。read/manage分离的菜单访问矩阵逐页验证；默认角色授权仍由平台策略决定，按现有角色授予维护权。

PC完整管理；Mobile/PDA首期搜索/阅读/问题草稿/本人补充及助手，复杂知识编辑与批量导入显示明确PC入口，不伪装可操作。移动端收到仅PC页面动作返回UnsupportedTerminal并给替代只读入口。两语言、亮暗主题、键盘焦点、缩放与窄屏保持重要动作可达；复杂页高保真预览尚待用户查看，不以本文结构线框宣称视觉已验收。

<a id="kh-accept"></a>

## 7. 事件与端到端验收

拟定事件`KnowledgeHub.KnowledgePublished.v1`、`KnowledgeHub.KnowledgeDisabled.v1`、`KnowledgeHub.IssueTransitioned.v1`：统一`eventNId,tenantNId,projectNId,aggregateNId,aggregateVersion:string,occurredOn,actorUserNId,traceId`；Published附revisionNId/revisionNo/indexGeneration，Disabled附revisionNId/reasonCode，Issue附fromState/toState/activityNId/assigneeNId?/verifierNId?。不广播全文/附件Secret；消费者按当前权限读取，不靠事件载荷长期复制内容。

索引任务通知与站内消息投递都由持久意图驱动；同宿主可公开应用调用，异步可靠性不等于强制内部RabbitMQ。事件与实际Outbox注册的精确映射是D06待核验。

| 用例 | Given / When | Then（用户可见证据） |
| --- | --- | --- |
| A01 | 仅有查看权限，尝试上传资料 | 可阅读，上传接口返回403 |
| A02 | 维护人上传未填适用版本的手册 | 自动入库，可打开原件，索引完成后可问答，引用标明版本未注明 |
| A03 | 他人先保存，当前页旧双版本保存 | 409不覆盖，文字留在编辑区 |
| A04 | v1/v2方案两文档并存，用户问v1 | 引用v1有效文档；新修订不错误替换另一适用方案 |
| A05 | IC无IM，助手创建问题草稿 | 创建成功且可编辑/提交本人草稿；无法分派或处理他人问题 |
| A06 | 方案已送验后再编辑解决方法 | 409，先退回；验证引用固定solutionVersion |
| A07 | 转知识响应丢失后重复点击 | 同conversion/目标草稿；没有重复文件引用或重复知识 |
| A08 | 附件绑定失败 | 页面准确显示成功草稿和缺失附件；可重试，未Ready不发布 |
| A09 | 未保存编辑页收到导航，用户取消离开 | 原页/输入保留，助手显示取消而非已打开 |
| A10 | 导航成功但过滤字段不被目标页支持 | Rejected/部分结果，不声称条件已生效 |
| A11 | 新消息到达后旧导航执行/响应晚到 | 不跳走；旧结果留原消息，不覆盖新条件 |
| A12 | 写请求超时或进程重启 | Unknown待核对；原operationKey保留，不自动重复写 |
| A13 | 权限撤回/知识停用后恢复历史会话 | 相关生成段落/摘要/快照被限制，引用不能读原资料 |
| A14 | 原结果过期后请求导出 | 410与“已过期”；只有用户选择重查才产生新结果 |
| A15 | DeepSeek断网、无ServerMonitor/VPN/旧系统 | 知识与问题基础链可用；助手明确模型不可用 |
| A16 | 仅IR用户打开他人草稿URL；仅IC用户提交本人问题 | 他人草稿404无标题泄漏；本人问题提交后仍可读并在被指定时验证，不扩权到他人问题 |
| A17 | 关闭实时提醒，同一用户在两个标签页登录，在甲发起带筛选导航 | 仅甲通过执行状态领取，重复提醒只跳转一次，条件生效且查询完成后才报告所求结果 |
| A18 | 给已有能力登记第二个数据源或页面，再注册一种独立前端动作 | 新对象沿用原处理器与权限，新动作按登记参数、权限和成功条件执行并返回实际回执 |
| A19 | 从甲项目选资料导出，在已有本地修改的乙项目导入两次 | 预览区分新增、相同和冲突；重复项跳过，冲突由用户选择，保留本地修订。导入文件可读，建索引后可引用 |
| A20 | 维护人上传真实手册，另一位可读用户按现场报错查找并提问 | 原件可打开，答案引用定位到相应段落或页码，未注明版本时显式标出，证据不足时说明缺口 |
| A21 | 在平台内经菜单登录，并在无主平台服务的空环境按独立清单安装 | 两边分别完成上传、原文搜索、DeepSeek 配置后的带引用问答、问题草稿及本标签页导航。独立入口用本地身份验证查看/维护边界 |
| A22 | 独立实例重启，未配置外部 MES 菜单、数据源及平台用户目录 | 原有资料与审计可读，未完成任务可恢复。请求外部工单或目录导出时提示接入状态，内置页面动作仍由独立外壳按实际结果回执 |
| A23 | 从原生实例导资料包到独立实例，再从独立实例回传，含同源修订冲突 | 目标项目重新建本地文件引用并索引，重复导入跳过，冲突可预览，原文件可独立打开 |

验收必须保留页面、接口和真实来源三层证据；模拟工具、来源/模型未配置、未运行Provider测试分别记录，不以合同自检声称接入完成。
