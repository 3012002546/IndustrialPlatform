# PF10A 核心数据与一致性规格

版本：V0.4设计稿；日期：2026-11-18。配套[实施13A](../13A-Industrial%20Platform知识中心开发实施方案.md)与[接口页面规格](PF10A-核心接口与页面规格.md)。本轮细化知识、问题、会话/执行三条主线；表、字段、默认值与预算是主控设计，不表示已建库或完成Provider验证。模型配置、外部工具连接、资料包合并、专用搜索索引及辅助工具的完整物理字典另列待细化，不能据此宣称整包D02已闭合。

普通上传在 File 就绪后直接建立有效修订，Published 是内部状态。可选草稿用于手写和未完成的操作。[业务入口与资料复用](PF10A-全局助手与资料复用.md)

## 1. 公共类型与数据库边界

沿平台Entity及双版本约定，单一KnowledgeHub初始化单元；业务表逻辑前缀`knowledgehub_`。PG/SQL Server所属schema与初始化注册在工程细化时固定，SQLite用同名前缀；不因知识/问题/助手分三个物理库。下文表名、列名均snake_case，C#为PascalCase，DTO为camelCase。`?`允许null；其余必填，无特殊说明均无数据库默认值，由应用显式赋值。

| 代号 | .NET / JSON | PostgreSQL / SQLite / SQL Server | 约束 |
| --- | --- | --- | --- |
| id | Guid / GUID字符串 | uuid / TEXT / uniqueidentifier | 主键；API不暴露内部Id |
| nid | string / string | varchar(64) / TEXT / nvarchar(64) | 本服务稳定业务NId，服务端生成，1～64，不复用 |
| externalnid | string / string | varchar(128) / TEXT / nvarchar(128) | 已有File等公开业务标识，沿其契约保留；不得按本服务nid长度截断 |
| identity | string / string | varchar(256) / TEXT / nvarchar(256) | 可信租户/用户标识，1～256；原样保存，不按名称合并或截断 |
| key(n) | string / string | varchar(n) / TEXT / nvarchar(n) | Ordinal比较；SQL Server标识使用BIN2；枚举有CHECK |
| text(n) | string / string | varchar(n) / TEXT / nvarchar(n) | UI文本；输入限制按.NET UTF-16长度计算，SQLite/PG物理长度检查不得比应用更严格 |
| body(k) | string / string | text / TEXT / nvarchar(max) | UTF-8不超过k KiB，应用验证 |
| json(k) | 结构DTO，落库string / object或array | text / TEXT / nvarchar(max) | UTF-8上限k KiB；固定schema，拒绝未声明字段 |
| i32 | int / number | integer / INTEGER / int | 明确上下界 |
| i64 | long / 十进制string | bigint / INTEGER / bigint | 非负，禁止前端Number中转 |
| flag | bool / boolean | boolean / INTEGER / bit | SQLite CHECK 0/1 |
| time | DateTimeOffset / RFC3339 UTC | timestamptz / TEXT / datetimeoffset(7) | 写入统一UTC微秒精度；SQLite固定六位小数Z，验证排序及往返 |
| hash | string / string | varchar(64) / TEXT / varchar(64) | 小写SHA-256十六进制 |

**E领域公共列**：`id:id PK`、`n_id:nid`、`tenant_n_id:identity`、`is_frozen/is_locked/is_deleted:flag=false`、`entity_type:text(512)`、`created_on/last_updated_on:time`、`optimistic_version:i64=0`、`concurrency_version:id=新GUID`、`created_by/updated_by:identity`。创建两时间相同；实际变化同时推进两个版本和更新时间；无变化幂等不推进。普通写操作检查锁定/冻结/删除。UQ(tenant_n_id,n_id)，软删除保留唯一性。

**T技术记录公共列**：`id:id PK`、`n_id:nid`、`tenant_n_id:identity`、`created_on:time`，UQ(tenant_n_id,n_id)。可变技术记录另显式列出状态/并发/更新时间；不滥用Entity冻结生命周期。

除project表外，项目内聚合根加`project_n_id:nid`；项目归属经ProjectWorkspace公开契约核对，不建跨模块FK。子表通过所属聚合取得项目，不再复制可独立修改的项目字段。FK只在同模块，ON DELETE RESTRICT，写入检查租户一致；跨模块只保存公开NId。索引名称`ix_kh_*`/`uq_kh_*`不超过63 ASCII字节。

本服务不接受请求体tenant/user冒充身份；超过标识契约长度时返回明确身份映射错误，在适配前解决，禁止截断或哈希替换。上线前核对实际身份发行者长度；该核验仍属于D06。

## 2. 项目与知识

### 2.1 knowledgehub_project（ProjectWorkspace，E）

一行是当前部署中的项目范围，首期每租户一个默认项目，不创建跨项目分享流程。

| 字段 | 类型/默认 | 说明与写入时机 |
| --- | --- | --- |
| name | text(128) | 项目显示名，初始化及设置页维护 |
| is_default | flag=true | 初始化指定；首期不提供切换/新增项目界面 |
| system_versions_json | json(8)/[] | 数组最多50个唯一版本名，每项1～64；Ordinal精确标识，不自动推断SemVer兼容 |
| time_zone_id | key(128)? / null | 有项目时区时初始化；缺失仍可建项目、上传和搜索。相对时间查询先补问并固化范围，不能借用服务器时区 |
| is_enabled | flag=true | 禁用项目立即阻止新业务操作 |

默认项目初始化幂等，同租户创建默认项目使用初始化锁；数据库过滤唯一索引覆盖`tenant_n_id WHERE is_default=true AND is_deleted=false`，三Provider必须验证。服务启动不依赖项目设置页先被用户打开。

<a id="kh-document"></a>

### 2.2 knowledgehub_knowledge_document（KnowledgeBase，E＋project_n_id）

一行代表稳定知识身份；标题、内容和适用版本在修订中，不让未发布标题泄露到普通查询。

| 字段 | 类型/默认 | 说明 |
| --- | --- | --- |
| owner_user_n_id | identity | 默认创建人；有manage权限可转交，记录审计 |
| published_revision_id | id? / null | FK revision，唯一有效发布指针，必须属于本文档 |
| draft_revision_id | id? / null | FK revision，唯一活动草稿，必须属于本文档 |
| revision_sequence | i64/0 | 分配修订序号；每次新草稿在文档CAS事务递增，废弃序号不复用 |
| is_enabled | flag/false | 首次发布设true；停用设false，不抹掉历史指针 |
| disabled_on | time?/null | 最近停用时间；重发后清空 |
| disabled_reason | text(1000)?/null | 停用原因必填，审计保留历次原因 |

状态不重复落库：无发布指针=Draft；有指针且enabled=Published；有指针且未enabled=Disabled。hasDraftChanges由草稿内容hash与发布修订hash比较。修订创建/保存/删除、发布/停用、目录/标签变化均通过文档根CAS；不在API暴露子表的独立并发令牌。

索引：(tenant_n_id,project_n_id,is_deleted,is_enabled,last_updated_on,id)。普通查询只join发布指针，管理查询可另join草稿；没有草稿时“编辑”先通过显式新草稿接口复制发布内容。

### 2.3 knowledgehub_knowledge_revision（KnowledgeBase，E）

一行是一份内容修订，草稿可编辑，发布后内容不可变；仅管理人员可读历史，助手仅读当前有效指针。

| 字段 | 类型/默认 | 说明 |
| --- | --- | --- |
| document_id | id FK document | 所属知识，创建后不变 |
| revision_no | i64 | 创建草稿时分配，1起；内容修订号不是系统版本 |
| state | key(16)/Draft | Draft/Published；发布设Published并Freeze；不提供解冻已发布内容入口 |
| title | text(200) | 不得空白；草稿可保存空正文但标题必填 |
| summary | text(1000)/空串 | 用户摘要；不存在时不自动让模型补写 |
| content_format | key(16)/Markdown | Markdown/RichText；富文本结构及编辑器版本仍待D06冻结 |
| content | body(1024)/空串 | 规范化正文；无可读附件时发布不得为空 |
| category_id | id? FK category | 可为空，表示未分类；改变草稿不移动已发布内容 |
| applicability_json | json(8) | `{schemaVersion:1,mode:Unspecified|All|Listed,versions:string[]}`；默认Unspecified且数组空，有效修订也可保留未注明，明确版本检索不得当作通用。All时数组空，Listed时1～50个项目登记版本 |
| content_hash | hash | 正文、标题、摘要、格式、分类、适用范围、排序后的标签与附件内容快照的规范化hash；附件包含文件hash/文件名/说明/排序，不包含每次复制变化的引用NId、动态扫描/绑定状态或票据 |
| published_on | time?/null | 发布事务写入；Draft时null |
| published_by | identity?/null | 发布操作者；不要求异人审批 |
| release_note | text(1000)?/null | 发布时可选修改说明 |
| source_kind | key(16)/Manual | Manual/Issue/Import/Rollback |
| source_n_id | nid?/null | 来源问题/导入项/历史修订公开标识，经模块公开契约消费 |
| source_version | key(64)?/null | 来源操作快照版本，来源为Issue时是对应解决方案版本 |

UQ(tenant_n_id,document_id,revision_no)；UQ活动Draft(document_id WHERE state=Draft AND is_deleted=false)与文档指针双重核验。发布修订禁止正文、附件、标签变更；回滚复制历史内容产生新Draft。不同系统版本需并存方案时用不同文档，避免旧系统方案随修订被替换。

### 2.4 knowledgehub_knowledge_category（KnowledgeBase，E＋project_n_id）

`name:text(64)`、`parent_id:id? FK self`、`normalized_parent_key:key(64)`（根为固定`root`，其他为父NId）、`sort_order:i32=0`（0～100000）。UQ(tenant,project,normalized_parent_key,name)在SQL Server的nvarchar最大键长为896字节；软删仍保留同名身份，恢复不新建重名项。深度最多5，禁止环；分类仅组织内容不授予权限。被任何保留修订引用时禁止删除，提供停用/重命名；停用分类禁止新草稿选择，历史仍显示其名称。另加`is_enabled:flag=true`。

### 2.5 knowledgehub_knowledge_revision_tag（KnowledgeBase，T）

`revision_id:id FK revision`、`name:text(32)`。每修订最多10项，去首尾空白后Ordinal去重；UQ(tenant,revision_id,name)，索引(tenant,name,revision_id)支持标签查询。无独立“标签管理/权限”产品。发布后不可变，草稿替换标签在文档事务内完成。

<a id="kh-attachment"></a>

### 2.6 knowledgehub_knowledge_attachment（KnowledgeBase，T）

`revision_id:id FK revision`、`file_reference_n_id:externalnid`、`file_n_id:externalnid?`、`file_name:text(260)`、`content_type:text(128)`、`byte_length:i64`、`expected_sha256:hash?`、`sha256:hash?`（提交后核验值）、`upload_session_n_id:externalnid?`、`description:text(500)=空串`、`sort_order:i32`（0～19）、`reference_state:key(16)=Preparing`（Preparing/Ready/Failed/Released）、`failure_code:key(64)?`、`last_updated_on:time`。

一行是一条**本修订独立持有**的File引用，最多20条；UQ(tenant,revision_id,file_reference_n_id)。跨修订/问题转知识需新引用，原文件可复用，但不共享可释放的业务引用。Preparing/Failed允许file_n_id与提交后sha256为空；准备阶段保留File返回的上传会话/引用，不虚构FileNId。Commit后核验长度与预期hash，Ready必须有file_n_id和实际sha256；转换绑定的上传会话可空。只有Ready且当前File允许读取的附件可以发布/提取/下载；显示缓存文件名不代表拥有读取权限。发布后附件业务内容不可变；File撤销/扫描状态动态变化由读取门禁体现，不改写历史内容。

<a id="kh-index-job"></a>

### 2.7 knowledgehub_knowledge_index_task（KnowledgeBase，T）

`document_id:id FK`、`revision_id:id FK`、`generation:i64`、`engine_profile_version:key(64)`、`state:key(16)`（Pending/Processing/Ready/Failed/Obsolete）、`attempt:i32=0`、`max_attempts:i32=3`、`lease_token:id?`、`lease_until:time?`、`next_attempt_on:time?`、`last_error_code:key(64)?`、`safe_error:text(1000)?`、`last_updated_on:time`。UQ(tenant,revision_id,generation)，索引(state,next_attempt_on,lease_until)。代次递增由文档根串行分配，另加文档`index_generation:i64=0`。

索引worker每次开始/提交校验当前指针和enabled；旧代次只标Obsolete，不发布过期索引。自动重试最多3次，5秒/30秒退避，格式不支持/权限失效不可自动重试。人工重试创建新代次；切换Embedding/切块策略创建新profile代次，旧向量不混用。关键词索引按[FTS5 与精确键方案](../13A-Industrial%20Platform知识中心开发实施方案.md#chinese)装配，向量索引另行选型，不把本任务表当作全文检索实现。

片段所属位置、规范化文本和原文的区分、分来源提取结果与原子就绪规则见[资料解析检索与引用验收](PF10A-资料解析检索与引用验收.md)。提取失败或超限的任务不暴露暂存代次，部分覆盖按来源记录。

<a id="kh-import"></a>

### 2.8 普通文件导入批次与逐项记录（KnowledgeBase）

`knowledgehub_import_batch`（T）存 `project_n_id:nid`、`actor_user_n_id:identity`、`operation_key:key(64)`、`input_hash:hash`。批次、所有文件项与 §5.1 的创建幂等凭据在同一事务保存。进度按项汇总，索引状态读目标修订的当前代次。操作限本项目仍有维护权的批次发起人。

`knowledgehub_import_item`（T，可变）存 `batch_id:id FK`、`client_item_id:key(64)`、`attachment_n_id:nid`（预留）、`file_name:text(260)`、`content_type:text(128)`、`byte_length:i64`、`expected_sha256:hash`、`choice:key(16)`（New/Revision/Skip）、`target_document_n_id:nid?`、`target_optimistic_version:i64?`、`target_concurrency_version:id?`（Revision 时三项必填）、`state:key(24)`（AwaitingUpload/WaitingForScan/Collected/Skipped/Failed）、`upload_session_n_id/file_reference_n_id/file_n_id:externalnid?`、`document_n_id/revision_n_id:nid?`、`safe_error_code:key(64)?`、`last_updated_on:time`。UQ(batch_id,client_item_id)，UQ(tenant_n_id,attachment_n_id)。Skip 直接结束；等扫描或临时失败保留原项。批量与文件预算见 §6。

普通导入用预留 attachmentNId 作为 File 稳定归属。引用 Active 且可读后，在本服务同一事务检查发起人权限与目标双版本，以项状态 CAS 创建修订及附件、切换有效指针、登记索引意图并标记 Collected。附件沿用预留 NId 和受控引用。File 已提交而业务事务中断时先核对原引用再续办，重复提交只产生一个有效修订。放弃且未绑定的引用由清理任务核实后释放。ZIP 来源比较和目标合并选择由 009 扩展。

## 3. 问题与转换

### 3.1 knowledgehub_issue（IssueTracking，E＋project_n_id）

| 字段 | 类型/默认 | 说明 |
| --- | --- | --- |
| title | text(200) | 必填 |
| description | body(64)/空串 | 草稿可空；提交至少10个非空白字符说明现象，附件可选 |
| severity | key(16)/Normal | Low/Normal/High/Critical；不自动执行升级或SLA |
| system_version | key(64)?/null | 项目已登记版本，可空代表未指定，不等于通用 |
| state | key(32)/Draft | Draft/Open/InProgress/WaitingForInfo/PendingVerification/Closed/Cancelled |
| assignee_user_n_id | identity?/null | 处理人；受理/分派时设值 |
| responder_user_n_id | identity?/null | 等待补充的指定人员；离开等待状态清空 |
| verifier_user_n_id | identity?/null | 验证人；送验默认提交人，可由manage指定 |
| submitted_by | identity?/null | 首次提交人，不随转交改变 |
| submitted_on | time?/null | 首次提交时间 |
| root_cause | body(16)/空串 | 根因可未确定，不能用AI猜测冒充事实 |
| solution | body(64)/空串 | 送验必填；在InProgress由处理人或manage编辑 |
| solution_version | i64/0 | 根因/解决方法每次实质变化递增；已送验时冻结该版本 |
| verified_solution_version | i64?/null | 关闭时记录验证的确切解决方案版本，重开清空 |
| closed_on | time?/null | 关闭时写入，重开清空；旧时间在活动记录保留 |
| last_state_reason | text(1000)?/null | 最近退回/取消/重开原因 |

草稿和已提交记录共用NId，不另造业务编号生成器。IC允许读取本人创建的问题（包括提交后），IR/IM允许读取项目已提交问题；补充/验证仍检查对象关系和状态，IC不扩权到他人问题。UI显示短NId，可复制完整NId；短显示不作为查询标识。本人草稿删除仅软删；已提交问题取消，不删除活动历史。索引(tenant,project,state,last_updated_on,id)、(tenant,project,assignee,state)、(tenant,project,verifier,state)、(tenant,project,created_by,state)。

### 3.2 knowledgehub_issue_activity（IssueTracking，T）

一行是不可变处理事实：`issue_id:id FK`、`sequence:i64`、`kind:key(32)`、`actor_user_n_id:identity`、`from_state/to_state:key(32)?`、`body:body(64)=空串`、`reason:text(1000)?`、`assignee/responder/verifier_user_n_id:identity?`、`solution_version:i64?`、`solution_snapshot_json:json(96)?`（仅送验/关闭/转换时保存根因/方案/版本）、`operation_key:key(64)`。枚举kind：Created/Edited/Submitted/Assigned/Accepted/InfoRequested/InfoAdded/SolutionSaved/VerificationRequested/Rejected/Closed/Reopened/Cancelled/KnowledgeConverted。

issue另加`activity_sequence:i64=0`，每次业务事务CAS后分配；UQ(tenant,issue_id,sequence)，操作幂等不新增活动。活动不得从模型编造；写入人来自可信主体。UI活动页只展示当前可读问题的历史。

### 3.3 knowledgehub_issue_attachment（IssueTracking，T）

`issue_id:id FK`、`activity_id:id? FK activity`、其余文件字段与§2.6相同（不含revision_id）；每问题最多20条，活动可关联既有问题附件，不复制所有权。草稿/处理期间可移除附件并记录活动；送验/关闭快照引用的附件保留，不因界面删除物理释放；保全按File契约处理。

### 3.4 knowledgehub_issue_knowledge_conversion（IssueTracking，T，可变）

`project_n_id:nid`、`issue_id:id FK`、`solution_version:i64`、`actor_user_n_id:identity`、`operation_key:key(64)`、`input_hash:hash`、`mode:key(8)`（Collect/Draft）、`snapshot_json:json(192)`、`target_document_n_id/target_revision_n_id:nid?`、`state:key(24)`（Pending/Creating/BindingFiles/Completed/Failed）、`failure_code:key(64)?`、`lease_token:id?`、`lease_until:time?`、`last_updated_on:time`。

UQ(tenant,project,operation_key)；索引(tenant,issue_id,solution_version)。允许人工对同一方案明确再次转换，但UI提示已有目标，默认打开原草稿；自动重试只重用原key。转换需issue.read与knowledge.manage且方案非空，状态PendingVerification/Closed；转换快照固定后不受问题后来编辑影响。人工预览确认收录后，附件全部绑定即自动切换有效修订；仅暂存或绑定未完成时保留Draft；source_n_id及source_version保存来源。

转换分三步：Issue保存转换意图/冻结输入→Knowledge公开CreateDraftFromIssue幂等建草稿（以转换NId为源操作键）→独立绑定选定附件并回填目标。任一步失败可按转换NId恢复；草稿与File引用可核对，不跨模块事务写库，不删除已成功目标来假装回滚。附件全部Ready后依mode生效或暂存，完成目标核对后才Completed，失败草稿保留并在编辑页提示缺附件。

## 4. 会话与执行

### 4.1 knowledgehub_assistant_conversation（Assistant，E＋project_n_id）

`owner_user_n_id:identity`、`title:text(120)`（首条文本截取，不自动调用模型命名）、`message_sequence:i64=0`、`latest_user_message_n_id:nid?`、`last_activity_on:time`、`summary_json:json(32)?`（结构见接口规格）、`summary_through_sequence:i64=0`、`summary_source_refs_json:json(32)=[]`。首期会话仅本人可读，无公开共享会话或管理员阅读全部会话入口。后台诊断使用脱敏执行元数据。

索引(tenant,project,owner,last_activity_on,id)。软删会话立即隐藏并阻止新步骤；实际副作用按执行记录留存，删除不撤销业务草稿。

### 4.2 knowledgehub_assistant_message（Assistant，T）

`conversation_id:id FK`、`sequence:i64`、`role:key(16)`（User/Assistant/Tool）、`client_message_id:nid?`、`execution_n_id:nid?`、`text:body(64)`、`parts_json:json(128)=[]`、`source_refs_json:json(64)=[]`、`state:key(16)`（Pending/Streaming/Completed/Failed/Redacted）、`last_updated_on:time`。UQ(tenant,conversation_id,sequence)；用户消息filtered UQ(tenant,conversation_id,client_message_id)。请求体最大64KiB，工具原始输出不能无限进入消息。

用户消息持久化后分配执行，同一clientMessageId与规范化内容hash重放返回同一消息/执行，不重新生成；同键异内容409。模型流式输出断线后从持久消息/执行恢复，不仅存在连接内存。

### 4.3 knowledgehub_assistant_task_context（Assistant，E）

`conversation_id:id FK`、`task_kind:key(24)`（Knowledge/Issue/Dataset/Navigation）、`state_json:json(32)`、`source_refs_json:json(32)=[]`、`last_execution_n_id:nid?`。一任务保存明确条件/用户纠正/时间范围/结果引用；当前taskNId由消息部分引用，可恢复旧任务，不要求复杂任务树UI。每次变更使用CAS，摘要不能改写确定条件。

### 4.4 knowledgehub_assistant_execution（Assistant，T，可变）

`conversation_id:id FK`、`user_message_id:id FK message`、`task_n_id:nid?`、`owner_user_n_id:identity`、`state:key(24)`（Queued/Running/WaitingForInput/Succeeded/Failed/Cancelled/Unknown）、`context_version:i64`、`page_context_id:nid?`、`page_context_version:i64?`、`deadline_on:time?`、`active_elapsed_ms:i32=0`（0～120000，服务端持久累计；仅活动执行计时，排队/等待澄清不计；每次恢复使用剩余预算设置deadline）、`tool_call_count:i32=0`、`waiting_prompt_json:json(16)?`（promptNId、question、allowedAnswerSchema、sourceRefs、createdOn、expiresOn、answeredByMessageNId?）、`cancel_requested:flag=false`、`lease_token:id?`、`lease_until:time?`、`safe_error_code:key(64)?`、`last_updated_on/completed_on:time?`。UQ(tenant,user_message_id)。

澄清问题在进入WaitingForInput前持久化，默认24小时到期；回答同时校验promptNId、当前状态和当前权限，clientMessageId幂等，不能借回答重放旧写步骤。WaitingForInput释放worker lease，不占120秒活动执行预算；无未核对写步骤时，同会话新任务可运行，原任务回答恢复后重新排队并CAS更新上下文。Unknown写步骤未核对前阻止该会话继续执行写动作，允许状态查询。追问到期记Failed/CLARIFICATION_EXPIRED，保留消息。

每会话活动执行串行，消息可排队，UI显示排队；不会同时修改同一任务状态。新用户消息到来后，旧执行结果仍落原消息，但旧执行未发出的页面跳转取消（检查latest_user_message_n_id）；旧外部写入继续核对结果，不能仅因新消息标Cancelled。不同会话独立运行，数据库lease控制多实例唯一执行；worker崩溃后已发写请求进入Unknown先核对。

### 4.5 knowledgehub_assistant_step（Assistant，T，可变）

`execution_id:id FK`、`ordinal:i32`（1～6）、`capability_id:key(128)`、`capability_version:key(32)`、`kind:key(16)`（Read/Write/Page）、`input_json:json(32)`、`input_hash:hash`、`operation_key:key(64)`、`state:key(24)`（Pending/Running/Succeeded/Failed/Cancelled/Unknown）、`result_n_id:nid?`、`external_operation_id:key(256)?`、`started_on/completed_on:time?`、`receipt_json:json(16)?`、`safe_error_code:key(64)?`。UQ(tenant,execution_id,ordinal)，写步骤operationKey不可重新分配；参数不存Token/Secret原文。

Page 步骤的 receipt_json 保存能力所需阶段、已接受阶段的详情指纹、最高进度和终态，按步骤行原值 CAS 更新；每个合法阶段可分别回执。字段及测试见接口 §4.4。

### 4.6 knowledgehub_assistant_result（Assistant，T）

`execution_id:id FK`、`step_n_id:nid?`、`owner_user_n_id:identity`、`project_n_id:nid`、`kind:key(24)`（Knowledge/Issue/Table/Navigation/Export/Text/Json）、`source_refs_json:json(64)`、`conditions_json:json(32)`、`observed_on:time`、`expires_on:time?`、`row_order_json:json(64)?`、`inline_data_json:json(256)?`、`snapshot_reference_n_id:externalnid?`、`byte_length:i64`、`sha256:hash?`。Text/Json仅接受已登记工具的输出schema；Text以`{text}`存储且≤64KiB，Json≤256KiB，超限按快照预算存File或明确拒绝。两者仍保存工具操作身份/来源引用并检查当前权限，不作为HTML执行。大结果入File，不把业务数据无限塞数据库。来源校验可用时方可读取；仅知道resultNId不能授权。

原结果导出固定快照；只保存查询条件时按钮叫“重新查询并导出”。TTL过期不偷偷重查。打开详情仍通过业务模块当前权限，不用历史快照冒充当前业务对象。

## 5. 幂等、事务与文件适配

<a id="kh-idempotency"></a>

### 5.1 knowledgehub_operation（服务级T，可变）

`actor_user_n_id:identity`、`project_n_id:nid`、`operation_name:key(64)`、`operation_key:key(64)`、`input_hash:hash`、`state:key(16)`（Running/Completed/Failed/Unknown）、`result_n_id:nid?`、`response_json:json(16)?`、`lease_token:id?`、`lease_until:time?`、`expires_on:time`、`last_updated_on:time`。UQ(tenant,project,actor,operation_name,operation_key)可能超SQL Server900字节，**用下列固定长度scope_hash唯一索引**：追加`scope_hash:hash`，hash输入为上述五字段的UTF-8规范JSON数组，UQ(scope_hash)，命中后逐字段比对原值，碰撞报错不当作重放。

Completed/Failed完整回包保留至少30天，随后仅清空response_json，保留同一行的scope原字段/hash、input_hash、最终状态及result_n_id作为最小操作凭据，不删除唯一键。expires_on表示回包清理时间，不能当作操作重执行期限。创建知识/问题也必须在同库事务内写入该凭据；后续同键仅返回当前有权目标或410 OPERATION_RESULT_EXPIRED，绝不重新创建。Unknown/Running不得按TTL清理；转换源标识、消息clientMessageId也保留业务关联，不能只靠业务NId声称防重。重放先重新授权，再返回已存在的当前可读结果；不能用缓存响应绕过撤权。

### 5.2 事务边界

| 操作 | 本服务事务 | 事务外及恢复 |
| --- | --- | --- |
| 保存草稿 | 文档CAS＋草稿内容/标签＋审计意图＋幂等结果 | 已准备File引用绑定失败显式显示，不能假装保存了可读附件 |
| 发布 | 检查当前File状态、文档双版本；冻结修订、切换指针、索引代次/事件、审计意图、完成幂等 | 异步索引；读取仍实时检查File/修订，File瞬时撤权不靠发布时快照放行 |
| 问题迁移 | 问题CAS、方案/状态、活动、审计/通知意图、幂等结果 | 通知失败仅重试通知，不重做状态迁移 |
| 转知识 | 源转换意图；目标Knowledge自身幂等事务分别提交 | 按§3.4持久状态核对，不跨模块直写 |
| 助手写动作 | 先落执行/步骤/稳定operationKey，再调用业务公开接口 | 超时Unknown，通过key/目标标识查询；不无条件重试POST |

沿服务级Outbox/可靠审计机制，具体适配和表结构沿平台已有约定核验后复用，不为每模块造新Outbox。租约、状态更新使用CAS；重复事件消费按eventNId及aggregate version丢弃已应用版本，不倒退当前状态。

### 5.3 File/Audit/Notification已核对与缺口

- `RuntimeSupport.Files.IOwnedFileService`已有Prepare/Commit/Read/GetReadTicket/Release/SetRetention等操作，`TrustedServiceContext`必须由服务端构造；不能从浏览器绑定Actor/OwnerService。 现有实现限定 platform-studio。002 按受信服务和归属类型成对登记，普通上传登记 knowledgehub + KnowledgeAttachment，读取、释放均复查原归属。上传 purpose 按生产者选择，保留 Studio 原值。File 及作业入口对租户、发起人限制 128 字符，较长身份须在接入时验证稳定映射或明确拒绝该能力。
- SchedulerOnce 当前固定 Studio 服务、报表 handler、runNId 参数、作业名称与去重键。002 按受信生产者登记 handler 及载荷校验，从持久任务标识解析实际归属。新服务的受理键与作业编号均纳入生产者范围，旧 Studio 键与查询路径保持兼容。验收同键重试、跨服务读取或取消被拒及关闭 Studio 后仍可索引。未知先查询原受理记录，业务层负责索引代次与重试。
- 扫描复核在 File 所有者适配内进行。现有 CommitAsync 对未 Clean 或 Restricted 均可能返回 Quarantined。给受信调用者返回该归属引用对应的安全扫描结论，区分待扫描、明确阻断与可提交；只有最后一种再按原发起人提交到 Active。扫描或回调中断按原项核对，未知期间原件保持不可读。
- 当前通用FileBindingRequest仍带ConversationNId/MessageNId等聊天字段，不能塞知识NId冒充聊天。需补/确认`IFileReferenceCopy`公开适配：输入源引用、已验证读取主体、目标业务归属、稳定operationKey；返回新的受控引用/文件标识/当前状态。优先复用文件对象并新建业务引用，若公开能力暂不可用，保持附件待绑定并阻止发布，不跨仓储复制。该端口是**待实现契约**，不是现有接口。
- AuditFactIngestRequest已有producer/event/actor/action/object/payload/trace；发布、转换、状态迁移记录对象与版本，默认不把全文、完整工具结果或Secret写日志。使用本服务持久审计意图后幂等投递，不能把远程审计写成与业务同库事务。
- Notification的SystemMessageRequest与SendSystemMessageAsync已存在；投递只携安全标题/目标路由/对象引用，点击重新鉴权。不能拿管理员actor替代业务发生者，具体生产者授权适配须核对后固定。

<a id="kh-budgets"></a>

## 6. 保留、清理与容量（首期默认设计）

| 数据 | 默认 | 处理 |
| --- | --- | --- |
| 知识发布修订、问题及处理记录 | 不自动到期 | 备份/保全；后续明确归档政策再清理，不实现静默历史删除 |
| 会话 | 最近活动起90天 | UI可手动删除；隐藏并取消未执行步骤，7天回收正文；执行审计另保留 |
| 助手查询结果/快照 | 24小时 | 到期结果卡保留条件/来源/截止时间并标过期；不得原样导出 |
| 工具执行元数据 | 180天 | 不保留Secret/无界原始结果；Unknown未解决不按TTL清理 |
| 幂等完整回包 | 完成后至少30天 | 之后清理回包，最小操作凭据与唯一键不自动到期；未完成不可清理 |
| 每文件/每批 | 20MiB/50项，总计200MiB | 超限上传前提示，后端再次检查；这些是待真实样本核验的初始值 |
| 提取内容/解析时间 | 每文件4MiB文本/60秒 | 超限明确失败，不把截断内容标全量成功 |
| 查询/导出快照 | 10000行、10MiB取较小 | 超限要求缩小条件；不无提示丢行；可导出结果不等于全部源数据 |

会话过期不删除已创建问题；法律保全/所属File保留优先于清理。调整配置只影响后续到期计算规则，敏感历史删除须专门流程，不在设计任务中执行。上述预算以部署配置暴露，配置范围/升级兼容在D06补齐。

## 7. 必须验证的反例

1. 两人编辑同一草稿：第二次旧双版本写409，保留本地文本；不能覆盖第一人。
2. 发布A后立刻发布B，A索引晚到：A不得重新成为当前结果；B失败时人工阅读B、问答显示待索引，不暗中引用A。
3. File于发布后撤权：原文中的附件下载、检索片段和历史答案按当前权限隐藏；正文未撤权部分仅在可证明不依赖该附件时保留。
4. 转换创建已成功但响应丢失：重试查询到同一目标，附件按各自稳定key绑定；不新建第二份知识。
5. 送验后有人尝试改方案：拒绝，先退回InProgress；验证始终针对被送验的solution_version。
6. 模型提出createIssueDraft但用户无issue.create：拒绝，不借assistant.use越权。
7. 浏览器新消息/换页后旧导航回执：仍记录原步骤，不覆盖当前页面/任务；前端未保存提示取消时不报告导航成功。
8. 服务重启、写请求结果不明：记录Unknown并核对；读步骤可重试，写步骤不换key重发。

完整schema迁移、索引名称、JSON Schema机读件、真实Provider/文件/模型验证仍待D06；本稿不生成数据库或执行迁移。
