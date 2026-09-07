# PF10B 标签平台数据字典、接口与页面规格

版本V1.0；2026-09-07；对应实施13B。派遣状态待派遣；就绪度待客户/设备/独立适配核验。


## 1. 类型、命名与基线

这是派遣输入的设计规格，不是已建表证据。服从[派遣前规则](../STANDARD-派遣前详细设计与页面验收.md)和[实施模板](../TEMPLATE-开发实施方案.md)。表中 `?` 表示允许 NULL；未标 `?` 均 NOT NULL。没有默认值的字段必须由经过校验的用例赋值，不由数据库猜测业务默认。

| 类型代号 | .NET / PostgreSQL / SQLite / JSON | 约束 |
| --- | --- | --- |
| id | Guid / uuid / TEXT / 不对外 | 内部主外键；应用生成；SQLite 存规范 GUID |
| key | string / varchar(128) / TEXT / string | 1～128，身份源标识保持原值；SQLite CHECK 长度 |
| nid | string / varchar(64) / TEXT / string | 本服务生成 Guid 的 N 格式32字符；不采用数据库 Id 对外 |
| str(n) | string / varchar(n) / TEXT / string | SQLite 同样限制最大 n 字符；空串是否允许见字段 |
| text | string / text / TEXT / string | 长度、敏感性在字段规则中声明 |
| i32 | int / integer / INTEGER / number | 范围逐字段指定 |
| i64 | long / bigint / INTEGER / 十进制 string | 序号/版本/字节数不经 JS number；JSON 样例 `"42"` |
| time | DateTimeOffset / timestamptz / TEXT / string | UTC RFC3339；SQLite 固定 UTC 格式比较，业务时间注入可控时钟 |
| bool | bool / boolean / INTEGER / boolean | SQLite CHECK 0/1 |
| hash | string / varchar(64) / TEXT / string | SHA-256 小写 hex，64字符；哈希不代替授权 |
| json | 已声明 DTO / jsonb / TEXT / object | schemaVersion=1，禁止任意无 schema 扩展；≤256KiB，单独注明者例外 |
| dec(p,s) | decimal / numeric(p,s) / TEXT / 十进制 string | SQLite 保存规范十进制文本，应用精度校验，不走 REAL |

逻辑字段转换为同名 snake_case：NId→n_id、TenantNId→tenant_n_id、CreatedOn→created_on。下面每行给出精确物理名。跨服务引用只是 string，不建外键；本服务自行签发的 NId 不要求外部 Identity/File 改其命名或大小写。若前置实际标识超出128，先改规格和测试再派遣，不静默截断。

**E 基组：** 领域实体表统一含下列字段，逐表不重复展开；技术表显式列字段，不套软删除/双版本。

| 逻辑字段 / 物理列 | 类型 | 默认与规则 |
| --- | --- | --- |
| Id / id | id | PK，Guid.NewGuid，无数据库业务默认 |
| IsFrozen / is_frozen | bool | false；普通修改保护 |
| IsLocked / is_locked | bool | false；锁定保护 |
| IsDeleted / is_deleted | bool | false；不等于消息撤回/打印取消 |
| EntityType / entity_type | text | 完整领域类型名，服务生成，不开放编辑 |
| CreatedOn / created_on | time | 创建时 UTC，应用赋值 |
| LastUpdatedOn / last_updated_on | time | 初始与 CreatedOn 相同；实际改变才更新 |
| OptimisticVersion / optimistic_version | i64 | 初始0，实际改变+1；API用字符串 |
| ConcurrencyVersion / concurrency_version | id | 初始非空，实际改变重新生成；API若暴露为GUID字符串 |

**B 基组：** 本文全部领域实体另含 `NId/n_id:nid` 和 `TenantNId/tenant_n_id:key`，必须 UQ(tenant_n_id,n_id)，包括已软删除行。子实体继承本地父租户，用例校验同租户，不能接受客户端提供租户。表的 E/B 组加逐表字段就是完整字段集合。可变命令使用 expectedOptimisticVersion:string + expectedConcurrencyVersion:GUID，两者匹配；幂等已完成语义先返回原结果，避免重试变409。

技术表以具体 PK/UQ 为准；领域 FK 使用普通父 Id、ON DELETE RESTRICT，不创建软删除影子列。关键冗余 NId 只读映射、同事务赋值并验证，不提供单独修改入口。所有租户查询先带 tenant_n_id，后台任务逐租户取受限上下文。状态值保存在 varchar 列并设 CHECK；未知值失败关闭，不以整数枚举顺序作持久协议。

## 2. 标签平台表与字段

Schema=`label`，SQLite前缀=`label_`。Template拥有template_*；DataPreparation拥有data_*；PrintJob拥有print_*；设备注册与执行能力通过公开端口访问device_*，不允许业务模块直接读设备Repository。一个Label服务级初始化单元，跨模块仅NId引用，不建跨模块FK。

### 2.1 template_category

领域实体，完整字段=E+B基组+下表。

| 逻辑字段 / 物理列 | 类型 | 默认、约束与用途 |
| --- | --- | --- |
| Name / `name` | str(200) | 本地化名称主阅读字段，必填 |
| Description / `description` | str(500)? | 可选 |
| CategoryKind / `category_kind` | str(24) | Material / Container / Equipment / Custom |
| Sort / `sort` | i32 | 默认0，>=0 |
| Status / `status` | str(16) | Active / Disabled，默认Active |

IX(tenant_n_id,status,sort,n_id)。三种默认类别由TenantBaseline创建，版本化且不覆盖管理员名称。停用不影响历史快照，阻止新模板采用。

### 2.2 template_definition

领域实体，完整字段=E+B基组+下表。

| 逻辑字段 / 物理列 | 类型 | 默认、约束与用途 |
| --- | --- | --- |
| CategoryNId / `category_n_id` | nid | 同模块类别稳定引用，应用核验有效 |
| Name / `name` | str(200) | 必填 |
| Description / `description` | str(500)? | 可选 |
| CurrentPublishedRevision / `current_published_revision` | i64? | 未发布NULL |
| Status / `status` | str(16) | Active / Disabled |

IX(tenant_n_id,category_n_id,status,n_id)。草稿和发布版本在revision表；发布同时推进根指针。删除只能停用逻辑入口，不清已被Prepared引用的版本。

### 2.3 template_revision

领域实体，完整字段=E+B基组+下表。

| 逻辑字段 / 物理列 | 类型 | 默认、约束与用途 |
| --- | --- | --- |
| TemplateId / `template_id` | id | FK template_definition.id |
| Revision / `revision` | i64 | >=1，每模板严格递增，不复用 |
| State / `state` | str(16) | Draft / Published / Retired |
| WidthMm / `width_mm` | dec(9,3) | >0且<=1000，规范单位mm |
| HeightMm / `height_mm` | dec(9,3) | >0且<=1000 |
| RotationDegrees / `rotation_degrees` | i32 | 0 / 90 / 180 / 270 |
| RenderFormat / `render_format` | str(16) | Pdf / Raster / Zpl |
| BackgroundFileNId / `background_file_n_id` | key? | 固定PDF/图片底稿，公开文件引用 |
| BackgroundPage / `background_page` | i32? | PDF页码从1；无底稿NULL |
| DataContractRevisionNId / `data_contract_revision_n_id` | nid | 跨DataPreparation稳定引用 |
| SchemaJson / `schema_json` | json | schemaVersion+elements数组，见§3，<=1MiB |
| SchemaHash / `schema_hash` | hash | 规范schema+尺寸/格式/底稿hash/契约版本摘要 |
| PublishedOn / `published_on` | time? | 发布时UTC |
| PublishedByUserNId / `published_by_user_n_id` | key? | 发布主体 |

UQ(template_id,revision)；IX(template_id,state)。发布后字段不可变，编辑创建新Draft；Retired只禁止新准备，历史继续可重放。背景/字体/条码引擎版本纳入渲染证据，不依赖客户端临时文件。

### 2.4 data_contract_revision

领域实体，完整字段=E+B基组+下表。

| 逻辑字段 / 物理列 | 类型 | 默认、约束与用途 |
| --- | --- | --- |
| ContractNId / `contract_n_id` | nid | 同一逻辑契约稳定身份；版本行NId独立 |
| Name / `name` | str(200) | 名称 |
| Revision / `revision` | i64 | >=1 |
| State / `state` | str(16) | Draft / Published / Retired |
| FieldsJson / `fields_json` | json | schemaVersion+fields，见§3，不超过200字段 |
| Checksum / `checksum` | hash | 规范字段规则摘要 |
| PublishedOn / `published_on` | time? | 发布时填写 |

UQ(tenant_n_id,contract_n_id,revision)、IX(tenant_n_id,state,name)。业务字段定义是版本快照，不是任意SQL列；发布后不可改类型/默认/保密级别。

### 2.5 data_binding_revision

领域实体，完整字段=E+B基组+下表。

| 逻辑字段 / 物理列 | 类型 | 默认、约束与用途 |
| --- | --- | --- |
| BindingNId / `binding_n_id` | nid | 逻辑绑定身份 |
| Name / `name` | str(200) | 名称 |
| Revision / `revision` | i64 | >=1 |
| ContractRevisionNId / `contract_revision_n_id` | nid | 指定已发布契约版本 |
| SourceSystemNId / `source_system_n_id` | key | 登记来源，不接受任意URL/SQL |
| CustomerNId / `customer_n_id` | key? | 无客户上下文可空；Confidential模式必填 |
| ConfidentialityMode / `confidentiality_mode` | str(24) | Standard / Confidential |
| State / `state` | str(16) | Draft / Published / Retired |
| MappingsJson / `mappings_json` | json | schemaVersion+字段映射+客户物料映射版本，见§3 |
| Checksum / `checksum` | hash | 含来源/客户/契约/规则版本 |
| PublishedOn / `published_on` | time? | 发布后赋值 |

UQ(tenant_n_id,binding_n_id,revision)。同一来源/客户/契约用于新准备的已发布版本唯一；显式指定旧版本也要通过当前授权。Confidential缺映射直接阻断，不回退企业内部名称。

### 2.6 data_prepared_job

技术记录，不继承 E/B；完整字段如下。

| 逻辑字段 / 物理列 | 类型 | 默认、约束与用途 |
| --- | --- | --- |
| Id / `id` | id | PK |
| NId / `n_id` | nid | UQ(tenant_n_id,n_id) |
| TenantNId / `tenant_n_id` | key | 可信租户 |
| RequestedByUserNId / `requested_by_user_n_id` | key | 操作人/调用主体 |
| RequestNId / `request_n_id` | nid | 准备幂等 |
| RequestHash / `request_hash` | hash | 来源/业务引用/版本/输入/客户/明细身份摘要 |
| SourceSystemNId / `source_system_n_id` | key | 来源系统 |
| BusinessRef / `business_ref` | str(200) | 业务引用，不作为隐式全局唯一键 |
| SourceVersion / `source_version` | str(128)? | 来源支持时必填；无版本的限制登记 |
| CustomerNId / `customer_n_id` | key? | 保密模式必填 |
| TemplateRevisionNId / `template_revision_n_id` | nid | 跨模板版本无FK |
| ContractRevisionNId / `contract_revision_n_id` | nid | 固定契约版本 |
| BindingRevisionNId / `binding_revision_n_id` | nid | 固定映射版本 |
| InputHash / `input_hash` | hash | 原始受权输入规范摘要 |
| SnapshotHash / `snapshot_hash` | hash | 全部PreparedItem按itemKey排序摘要 |
| ArtifactHash / `artifact_hash` | hash | 整个产物清单摘要 |
| CreatedOn / `created_on` | time | 服务UTC |
| ExpiresOn / `expires_on` | time | 创建后15分钟；只限制新提交，不影响已正式任务历史 |
| State / `state` | str(16) | Preparing / Ready / Failed / Expired |
| ErrorCode / `error_code` | str(96)? | 失败字段路径可另安全返回，不带保密原文 |

技术快照；UQ(tenant_n_id,requested_by_user_n_id,request_n_id)，IX(state,expires_on)。Ready后输入/版本/明细/产物不可变。过期同键查询仍返回原过期状态，新准备必须新requestNId。未正式使用的准备包默认保留24小时，正式引用跟随打印历史365天或项目保全，不能因ExpiresOn已到直接删。

### 2.7 data_prepared_item

技术记录，不继承 E/B；完整字段如下。

| 逻辑字段 / 物理列 | 类型 | 默认、约束与用途 |
| --- | --- | --- |
| Id / `id` | id | PK |
| PreparedJobId / `prepared_job_id` | id | FK data_prepared_job.id |
| ItemKey / `item_key` | str(128) | 来自业务的稳定明细身份，不等于份数 |
| BusinessItemRef / `business_item_ref` | str(200) | 容器/设备/物料等本条引用 |
| SnapshotJson / `snapshot_json` | json | 按已发布契约校验后的最小打印字段；保密数据仅保存允许字段 |
| SnapshotHash / `snapshot_hash` | hash | 该条规范化快照 |
| ArtifactFileNId / `artifact_file_n_id` | key | 固定渲染产物引用；不是URL |
| ArtifactHash / `artifact_hash` | hash | 该产物二进制SHA256 |
| RenderEngineVersion / `render_engine_version` | str(128) | 引擎/字体/条码配置版本指纹 |

UQ(prepared_job_id,item_key)。无领域生命周期：追加后不可改，随Prepared引用生命周期清理。打印与预览使用同一ArtifactHash；若现场需按打印机DPI重渲染，必须准备阶段选择目标并生成新Prepared，不能提交时悄悄替换。

### 2.8 print_job

领域实体，完整字段=E+B基组+下表。

| 逻辑字段 / 物理列 | 类型 | 默认、约束与用途 |
| --- | --- | --- |
| PreparedJobNId / `prepared_job_n_id` | nid | 跨DataPreparation公开快照引用 |
| PreparedSnapshotHash / `prepared_snapshot_hash` | hash | 防提交时偷换准备内容 |
| RequestedByUserNId / `requested_by_user_n_id` | key | 调用主体 |
| RequestNId / `request_n_id` | nid | 提交幂等 |
| RequestHash / `request_hash` | hash | 准备hash+明细copies+printer+模式 |
| State / `state` | str(24) | Queued / Running / PartiallySucceeded / Succeeded / Failed / Cancelled / OutcomeUnknown |
| CreatedBySourceSystemNId / `created_by_source_system_n_id` | key | 来源系统 |
| OriginalJobNId / `original_job_n_id` | nid? | 仅原快照重打时指向原Job |
| ReprintReason / `reprint_reason` | str(500)? | 重打必填 |
| CompletedOn / `completed_on` | time? | 全明细终结时赋值 |

UQ(tenant_n_id,requested_by_user_n_id,request_n_id)、IX(tenant_n_id,state,created_on,n_id)。Job汇总来自Item结果，不能全收到Submitted就Succeeded；取消只取消未提交明细，已提交或未知项不得假装取消。

### 2.9 print_item

领域实体，完整字段=E+B基组+下表。

| 逻辑字段 / 物理列 | 类型 | 默认、约束与用途 |
| --- | --- | --- |
| PrintJobId / `print_job_id` | id | FK print_job.id |
| PreparedItemKey / `prepared_item_key` | str(128) | 固定prepared明细身份 |
| BusinessItemRef / `business_item_ref` | str(200) | 历史展示用业务引用 |
| PrinterNId / `printer_n_id` | nid | 跨设备模块稳定引用 |
| ExecutorNId / `executor_n_id` | nid | 提交时授权目标；禁止失败后自动换节点 |
| Copies / `copies` | i32 | 1～100；不同业务容器必须分Item |
| State / `state` | str(24) | Queued / Claimed / Submitting / Submitted / Confirmed / Failed / Cancelled / OutcomeUnknown |
| CurrentEpoch / `current_epoch` | i64 | 初始0，领取时+1 |
| CurrentExecutionNId / `current_execution_n_id` | nid? | 初始NULL，当前执行身份 |
| OriginalItemNId / `original_item_n_id` | nid? | 重打关联原条目 |
| ArtifactHash / `artifact_hash` | hash | 与准备固定产物一致 |
| ReceiptLevel / `receipt_level` | str(24) | None / AgentAccepted / SpoolerAccepted / DeviceConfirmed / OperatorVerified |
| LastErrorCode / `last_error_code` | str(96)? | 稳定错误 |

UQ(print_job_id,prepared_item_key)；IX(state,executor_n_id,created_on)；每个Item最多一个活动epoch。缺设备确认时状态Submitted不能伪称Confirmed；Job的OutcomeUnknown优先于全部普通成功。部分成功逐明细显示，不隐去失败。

### 2.10 print_attempt

技术记录，不继承 E/B；完整字段如下。

| 逻辑字段 / 物理列 | 类型 | 默认、约束与用途 |
| --- | --- | --- |
| Id / `id` | id | PK |
| TenantNId / `tenant_n_id` | key | 可信租户 |
| NId / `n_id` | nid | 追加事实身份 |
| PrintItemId / `print_item_id` | id | FK print_item.id |
| ExecutionNId / `execution_n_id` | nid | 本次执行身份 |
| Epoch / `epoch` | i64 | 当前执行版本 |
| ExecutorNId / `executor_n_id` | nid | 受权执行节点 |
| EventKind / `event_kind` | str(24) | Claimed / SubmitIntent / Submitted / Receipt / Unknown / OperatorResolution |
| ReceiptNId / `receipt_n_id` | nid? | 回执事件身份，设备端重试复用 |
| ReceiptHash / `receipt_hash` | hash? | 回执规范payload摘要 |
| OccurredOn / `occurred_on` | time | 服务接收时间 |
| DeviceOccurredOn / `device_occurred_on` | time? | 设备提供且可选，不用于租约权威 |
| Outcome / `outcome` | str(24) | Pending / Submitted / Confirmed / Failed / OutcomeUnknown |
| ConfirmationSource / `confirmation_source` | str(24) | None / Agent / Spooler / Device / Operator |
| ErrorCode / `error_code` | str(96)? | 安全错误 |
| ActorUserNId / `actor_user_n_id` | key? | 人工核实时必填 |
| Reason / `reason` | str(500)? | 人工核实/重打决策必填 |

追加事实，不UPDATE旧回执；UQ(tenant_n_id,executor_n_id,receipt_n_id) WHERE receipt_n_id IS NOT NULL；同ReceiptNId异hash409。IX(print_item_id,epoch,occurred_on)。设备I/O不在DB事务，SubmitIntent落盘之后崩溃且无法证明是否提交→OutcomeUnknown，禁止后台自动重发。

### 2.11 device_printer

领域实体，完整字段=E+B基组+下表。

| 逻辑字段 / 物理列 | 类型 | 默认、约束与用途 |
| --- | --- | --- |
| Name / `name` | str(200) | 人可读设备名称 |
| Model / `model` | str(128) | 实测型号 |
| ConnectionKind / `connection_kind` | str(24) | WindowsSpooler / Tcp / Usb / Ble / ClassicSpp |
| Protocol / `protocol` | str(32) | WindowsDriver / Zpl / Tspl / EscPos / VendorSdk；仅实测组合可启用 |
| Dpi / `dpi` | i32 | 203 / 300 / 600；其他型号须先扩规格 |
| SupportedFormats / `supported_formats` | json | {schemaVersion:1,formats:[Pdf或Raster或Zpl]} |
| ReceiptCapability / `receipt_capability` | str(24) | SpoolerAccepted / DeviceConfirmed / OperatorVerified |
| ConnectionProfileRef / `connection_profile_ref` | str(128) | 节点本地受控配置引用，不下发任意地址/命令 |
| Status / `status` | str(16) | Active / Disabled |

IX(tenant_n_id,status,name)。型号/协议/连接方式三者不能混为品牌；菜单工具“打印表格”与真实标签作业是不同权限/入口。

### 2.12 device_executor

领域实体，完整字段=E+B基组+下表。

| 逻辑字段 / 物理列 | 类型 | 默认、约束与用途 |
| --- | --- | --- |
| Name / `name` | str(200) | 工位/终端名称 |
| ExecutorKind / `executor_kind` | str(24) | WindowsAgent / AndroidPda |
| PublicKeyThumbprint / `public_key_thumbprint` | hash | 注册公钥指纹，秘密保存在节点安全存储 |
| CredentialVersion / `credential_version` | i64 | 初始1，轮换/重装递增 |
| State / `state` | str(24) | PendingEnrollment / Active / Paused / Revoked |
| LastSeenOn / `last_seen_on` | time? | 最近认证心跳 |
| AgentVersion / `agent_version` | str(64) | 实际运行版本 |
| ProtocolVersion / `protocol_version` | i32 | 固定1 |

UQ(tenant_n_id,public_key_thumbprint)；IX(state,last_seen_on)。心跳默认10s，30s无心跳显示离线；离线不允许自动转移已Submitting任务。重装不复制旧私钥，不以同名工位自动继承。

### 2.13 device_executor_binding

领域实体，完整字段=E+B基组+下表。

| 逻辑字段 / 物理列 | 类型 | 默认、约束与用途 |
| --- | --- | --- |
| ExecutorId / `executor_id` | id | FK device_executor.id |
| PrinterNId / `printer_n_id` | nid | 同设备模块受控引用 |
| AllowedOperation / `allowed_operation` | str(24) | Print，首版不开放通用OS命令 |
| CapabilitySnapshot / `capability_snapshot` | json | {schemaVersion:1,protocol,formats,receiptLevel,verifiedModel,verifiedOn} |
| Status / `status` | str(16) | Active / Disabled |

UQ(executor_id,printer_n_id)；领取前逐节点/打印机/租户验证，节点不能凭知道PrinterNId领取其他工位任务。

### 2.14 服务技术设施和Agent本地账本

Label服务schema_migrations/seed_ledger/outbox_message采用[PF05细化规格](PF05-数据接口与页面规格.md)§2.10的明确字段和类型，表名置于label Schema、ModuleKey填实际模块；复用当前服务自有初始化/事务模式，不引用ReferenceData具体表或机械按模块建立空账本。预定migration `label-1.0-001`设施、002模板/数据、003打印/设备；seed `label.baseline/permissions/navigation/default-categories`首版1.0.0，checksum从实际产物计算。总控派遣前核对当前版本/独立装配注册，禁止隐式EnsureCreated。

准备/提交/领取/回执本地事实与Outbox同事务；独立模式可以本地Dispatcher，不依赖消息中间件才算可用。Job命令按PrintJob→按Id排序PrintItem→Attempt锁定，节点领取以原子条件更新获取唯一epoch；网络/设备I/O在提交后执行，人工核实和迟到回执必须比较epoch。

Windows/PDA执行端使用私有SQLite `execution_ledger`（技术表）：`execution_n_id TEXT PK, item_n_id TEXT NOT NULL, epoch INTEGER NOT NULL, artifact_hash TEXT NOT NULL, printer_n_id TEXT NOT NULL, state TEXT NOT NULL CHECK(Claimed,SubmitIntent,Submitted,Confirmed,Failed,OutcomeUnknown), created_on TEXT NOT NULL, updated_on TEXT NOT NULL, receipt_n_id TEXT NULL, receipt_hash TEXT NULL, error_code TEXT NULL`；UQ(item_n_id,epoch)。epoch≥1、hash长度64。持久SubmitIntent后才发设备字节；账本恢复后先与服务端对账再接新任务。不得删除旧账本“修复卡住”。终结账本至少保留365天或服务端确认归档后按批准策略清理；未知项永不自动清。

节点领取租约30s、心跳10s，仅用于“尚未提交”接管；进入SubmitIntent后租约过期不是可重试证据。旧epoch回执保存为迟到事实并对账，不能覆盖当前epoch结果，尤其不能触发第二次物理打印。服务端重装/备份回滚先暂停派发、reconcile双方执行身份后恢复。

## 3. JSON字段规则与业务样例

`FieldsJson={schemaVersion:1,fields:[{key:string<=64,labelKey:string<=128,dataType:'String'|'Integer'|'Decimal'|'Boolean'|'Date'|'Instant',required:boolean,maxLength?:int,precision?:int,scale?:int,unitNId?:string,authority:'Source'|'Derived'|'Manual',manualOverrideAllowed:boolean,sensitivity:'Public'|'Internal'|'CustomerOnly',defaultValue?:typedValue}]}`。字段key唯一，Decimal精度≤28且scale≤8，Date为yyyy-MM-dd不转换时区，Instant为RFC3339，Integer为Int64十进制字符串。没有明确允许，不得人工覆盖Source字段。

`MappingsJson={schemaVersion:1,sourceAdapterKey:string,customerMappingRevisionNId?:string,fields:[{targetKey,sourcePath?:string,constant?:typedValue,transform:'Identity'|'FormatDate'|'FormatDecimal'|'CustomerMaterialMap',format?:string}]}`。sourcePath只允许注册DTO属性路径；sourcePath/constant互斥；模板不能执行JS/SQL/任意网络请求。CustomerMaterialMap要求匹配当前客户+内部稳定物料标识，缺失阻断，禁止用内部name兜底。

`SchemaJson={schemaVersion:1,elements:[{elementNId:string32,kind:'Text'|'Barcode'|'QrCode'|'Image'|'Line',xMm:decimal-string,yMm:decimal-string,widthMm:decimal-string,heightMm:decimal-string,fieldKey?:string,staticText?:string,rotation:0|90|180|270,fontRef?:string,barcodeFormat?:string}]}`。最多500元素；位置与尺寸须在画布内；fieldKey必须存在且权限允许；条码格式/字符集由渲染adapter验证，不将用户内容直接拼ZPL。底稿hash/字体/编码版本纳入SchemaHash，预览与打印同产物。

首个样例：同一物料两只容器 C001/C002 → 两个PreparedItem，分别有containerNId、customerMaterialName、lot、quantity:decimal-string、unitNId和printedOn；Copies=2表示每个明细两份，不能把C001/C002压成一个明细两份。保密样例：没有客户物料名→400 `LABEL_CUSTOMER_MAPPING_REQUIRED`，预览/日志/产物均不得含内部物料名。

## 4. API与状态契约

公开`/label/api/v1`、内部`/api/v1`；ApiResult信封和TraceId沿平台。配置列表pageIndex=1/pageSize=30，最大100，name/state按白名单排序；打印历史默认近7天、最大查询31天，可分页查询365天历史的不同窗口；不开放全量无限扫描。表格query descriptor走共享解析器，不能直接拼SQL。

| 编号 / Method 相对路径 | Request字段 | Response及权限 |
| --- | --- | --- |
| A10B-01 GET/POST templates | 列表query / {categoryNId,name,description?} | TemplateSummary或新templateNId；template.read/manage |
| A10B-02 POST templates/{nId}/revisions | {baseRevision?:string,widthMm:string,heightMm:string,renderFormat,backgroundFileNId?,dataContractRevisionNId,schemaJson,双版本} | {revisionNId,revision:string,state,双版本}；template.manage；编辑Draft另PUT同revision路径 |
| A10B-03 POST templates/{nId}/revisions/{revision}/publish | {schemaHash,requestNId,双版本} | Published版本；template.publish；变化/过期版本409 |
| A10B-04 GET/POST data-contracts或bindings | 配置查询 / §2.4或2.5业务字段（不接生命周期） | 当前版本摘要；data.manage；修订/发布分别`/{nId}/revisions`与`/{nId}/publish`，带checksum/双版本 |
| A10B-05 POST prepared-jobs | {requestNId,sourceSystemNId,businessRef,sourceVersion?,customerNId?,templateRevisionNId,bindingRevisionNId,items:[{itemKey,businessItemRef,values:typedObject}]} | 202 {preparedJobNId,state}，完成GET取Ready；prepare权限；最多100明细/总body1MiB |
| A10B-06 GET prepared-jobs/{nId}及/{nId}/preview | 无body；preview指定itemKey | {state,snapshotHash,artifactHash,expiresOn,items:[{itemKey,safeFields,artifactHash}]} / 受权二进制；不再次取业务源 |
| A10B-07 POST print-jobs | {requestNId,preparedJobNId,snapshotHash,items:[{itemKey,copies:number,printerNId,executorNId}]} | 202 {jobNId,state,items:[{itemNId,state}]}；submit；到期410、hash冲突409 |
| A10B-08 GET print-jobs及/{nId} | 受控query / 无body | 历史/逐明细/attempt/确认来源/原重打关联；history.read |
| A10B-09 POST print-jobs/{nId}/cancel | {requestNId,reason,双版本} | 逐条取消/不可取消结果；print.cancel；不能取消已物理提交结果 |
| A10B-10 POST print-jobs/{nId}/reprints | {requestNId,itemNIds:string[],reason:string<=500,copies:number,printerNId,executorNId} | 新Job/Item指向原快照/原明细，202；print.reprint；未知先核实，不自动重打 |
| A10B-11 POST print-items/{nId}/resolutions | {requestNId,outcome:'Confirmed'或'NotPrinted'或'Unknown',reason,expectedEpoch:string,双版本} | 追加人工核实，保持来源Operator；print.resolve；NotPrinted不自动新建打印 |
| A10B-12 GET/POST printers或executors | 列表query / §2.11/12可写字段，公钥注册走受控配对 | 设备能力/状态；device.manage；PUT配置双版本，秘密不回显 |
| A10B-13 POST executions/claim | {requestNId,executorNId,credentialVersion:string,supportedBindingNIds:string[]} | {executionNId,itemNId,epoch:string,artifactHash,artifactAccess,printerProfileRef,copies,leaseExpiresOn}或204；节点身份不等于普通登录 |
| A10B-14 POST executions/{nId}/receipts | {receiptNId,epoch:string,eventKind,outcome,confirmationSource,errorCode?,deviceOccurredOn?} | {accepted:boolean,currentState}；认证节点/归属/epoch；同键异载荷409 |
| A10B-15 POST executions/{nId}/heartbeat | {epoch:string,executorNId} | {leaseExpiresOn,state}；每10s，过期按§2.14恢复规则，不自动重发物理操作 |

错误稳定码：LABEL_VALIDATION_FAILED(400)、LABEL_CUSTOMER_MAPPING_REQUIRED(400)、LABEL_NOT_FOUND(404)、LABEL_VERSION_CONFLICT(409)、LABEL_IDEMPOTENCY_CONFLICT(409)、LABEL_SOURCE_CHANGED(409)、LABEL_PREPARED_EXPIRED(410)、LABEL_DEVICE_UNAVAILABLE(503)。OutcomeUnknown是200查询中的业务状态，不作为5xx诱导HTTP自动重试。权限前缀统一`label.`；公开预览/产物每次校验租户/客户/用途，短期访问并不取消服务端权威。

```json
{"requestNId":"fcdb71383b62403b896b97aa3a652006","preparedJobNId":"caaad64b409847fcb614e729e586ee36","snapshotHash":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","items":[{"itemKey":"C001","copies":2,"printerNId":"73933a8024094c789bcc901eb7c1b021","executorNId":"09b93b17950c496a8897c8d4c67ba734"},{"itemKey":"C002","copies":2,"printerNId":"73933a8024094c789bcc901eb7c1b021","executorNId":"09b93b17950c496a8897c8d4c67ba734"}]}
```

示例身份仅为文档fixture；实际NId按§1生成。相同request再次提交返回同Job；改变C002份数仍复用request则409。重打原标签始终原Prepared产物；“重新生成”调用A10B-05新request读取最新业务/规则，再走预览确认。

事件envelope：`eventNId:string,tenantNId:string,eventType:Label.PrintJobPrepared.v1或Submitted.v1或ItemStatusChanged.v1或ReprintRequested.v1,schemaVersion:1,aggregateNId:string,aggregateVersion:string,occurredOn:RFC3339,traceId:string,data:{jobNId?,itemNId?,state,confirmationSource?}`；不带标签正文/客户秘密。相同Event幂等，旧aggregateVersion不覆盖新状态；缺口通过GET历史重读。

## 5. 页面线框图、字段与加载归属

### 5.1 W10B-01 分类/模板与设计器

```text
┌ 标签模板                                               新建模板 ┐
├ 分类/快速查找 ┬ 选中模板名称 / 状态 / 当前版本        编辑 更多 ┤
│ compact列表   │ Tab：版本 / 绑定字段 / 预览 / 发布历史          │
│ Material      │ 版本表：版本/格式/尺寸/状态/发布时间/更多        │
│ Container     │                                     新建修订   │
│ Equipment     │ 右侧局部loading/error，左侧不卸载               │
└───────────────┴───────────────────────────────────────────────┘
设计器独立路由：
┌ 模板/草稿版本 / 保存状态                    预览 保存 发布     ┐
├ 元素工具 ┬ 标签画布（尺寸/底稿/位置） ┬ 选中元素属性/字段绑定   ┤
│ 紧凑工具 │ 主空间，缩放适配           │ Text/Barcode的必要属性  │
└──────────┴───────────────────────────┴────────────────────────┘
```

路由`/pc/label/templates`、`/pc/label/templates/:templateNId/revisions/:revision/design`；模板列表模板选择single/compact，右侧版本表none/full；分类可折叠筛选，不再另加第三套完整表。设计器只有工作工具/画布/属性，不套三张CRUD卡片。表单name1～200，尺寸(0,1000]mm保留3小数、rotation枚举；字段只选已发布契约的允许key。A10B-01～04，预览使用固定render结果，未保存变更先保存或明确丢弃；发布前显示版本/hash/影响。

### 5.2 W10B-02 数据契约与客户绑定

```text
┌ 数据配置                              新建契约 / 新建绑定 ┐
├ Tab：契约 | 来源绑定 | 客户映射                          ┤
│ 名称/来源/客户/状态查询                     查询 重置    │
│ 全宽表：名称/版本/权威来源/保密级别/状态/更多            │
└─────────────────────────────────────────────────────────┘
编辑抽屉：基本信息 → 字段表(key/type/必填/精度/权威/人工权限)
          → 绑定与样例验证 → 错误字段路径 → 保存 / 发布
```

`/pc/label/data-contracts`、`/pc/label/bindings`共用此结构；selection=none/full，data.manage。字段key不可重复、默认值符合类型、Decimal精度/scale、CustomerOnly字段无映射禁止发布/准备；错误按字段路径定位，不显示客户秘密值。没有公开手写SQL/任意URL输入框。

### 5.3 W10B-03 打印作业与W10B-04 PDA

```text
PC /pc/label/print
┌ 打印标签：1业务输入 → 2固定预览 → 3提交结果             ┐
│ 来源/业务引用/模板版本                    准备标签     │
├ 业务明细表（itemKey/数量/单位/份数） ┬ 当前明细固定预览 ┤
│ 单选当前预览，不代表只打印这一条     │ 版本/校验/有效期 │
│ 逐行份数1～100，不同容器单独一行     │ 不重新取数       │
├ 打印机 / 工位 / 能力 / 总份数                确认提交  ┤
└ 状态逐明细：排队/已提交/已确认/未知，未知→核实入口      ┘
PDA /pda/label/print
┌ 返回  扫描/选择业务 → 明细 → 预览确认 → 目标 → 结果     ┐
│ 每步一屏，当前输入/目标和结果保留，48px按钮             │
│ 本机蓝牙能力不支持：显示原因/选择授权固定工位           │
└ 无法确认是否出纸：核实，不能默认“重试打印”             ┘
```

业务输入变更即作废当前Prepared引用并提示重新准备，不能保留旧hash继续提交。准备/提交各自guard；409/410保留业务选择，提供重新准备；“未知”不显示默认自动重试。详情选择single/compact但提交集合由明确作业明细决定，不把预览选中行当批量打印范围。A10B-05～07/11，print.prepare/submit/resolve独立权限。

### 5.4 W10B-05 历史与W10B-06 设备

```text
历史 /pc/label/history
┌ 查询日期/业务引用/状态                         查询 重置 ┐
│ Job表：时间/业务/状态/份数/更多（none/full）             │
└ 查看详情→AppFormDrawer：Job身份/版本/原任务关联          ┘
  Tab：Item明细 / 执行记录；按条看确认来源；核实/原快照重打

设备 /pc/label/printers、/pc/label/executors
┌ 名称/状态/类型                                 新增   ┐
│ 全宽表：名称/型号/协议/连接/节点/已验能力/状态/更多     │
└ 配置抽屉：受控配置引用 / 绑定节点 / 样本验证结果        ┘
```

历史禁通用“再次提交”按钮；重打原标签与最新数据重新生成分开、原因必填。记录Submitted确认来源，不统一绿色“打印成功”。设备普通CRUD选择none/full，多个配置集用Tab/独立路由，不做三栏小卡片。Mobile`/mobile/label/history`只查看授权历史/逐条结果；复制W10B-05为单屏列表→详情，无原生能力不展示生产蓝牙打印按钮。

全部路由菜单放“标签管理”对应子项，命名路由、API注册、权限、版本化菜单种子与真实登录用户验证完整。英文、主题/密度、200%缩放/窄屏、未选/loading/empty/error/409/无权限、主从快速切换与保存重复点击按共同规则验收。大规模画布/大表边界需用200字段/500元素/100明细的本规格上限样例检查。

## 6. 内部任务与前置输入

| TASK | 精确输入与顺序 | 验收断言 |
| --- | --- | --- |
| PF10B-001 | §3样例+G10B-1/2/3，客户字段/保密/设备/身份适配核验先完成，回写最终映射后派生产 | 缺字段、客户映射/设备回执不明均显式阻塞，不交给开发猜 |
| PF10B-002 | §1/2.14，独立Host/初始化/身份文件审计公开端口 | 不启动全平台可认证并真实持久审计；平台切换只替适配；真实私有配置不出库 |
| PF10B-003 | §2.4～2.7/§3/§4 A04～06 | 字段type/precision/权威/保密校验；两容器不同Item；同键异输入409；源变化不偷换快照 |
| PF10B-004 | §2.1～2.3/§3/§5 W01，模板版本/底稿/渲染 | 发布不可改；预览hash等于实际打印产物；条码转义/背景坐标/字体固定 |
| PF10B-005 | §2.8～2.10/§4 A07～11/13～15，提交→领取→回执 | 并发只一个epoch；未确认不Succeeded；未知不自动重试；部分成功逐条可见 |
| PF10B-006 | §2.11～2.14，Windows服务+用户态助手+私有ledger | 真实工位同执行路径；SubmitIntent后崩溃Unknown；Electron复用同Agent |
| PF10B-007 | 同一DTO接PDA原生adapter，明确BLE与SPP | 真机出纸、断连/权限/重复请求、跨工位不得越权 |
| PF10B-008 | §5全部线框+§4 API，按模板/数据/作业/历史/设备纵向接页 | 真实菜单、客户保密负例、copies/Item区分、主从选择、错误保留输入 |
| PF10B-009 | §2.14恢复/§3版本，备份回滚/重装/签名/受控试点 | 暂停→对账→恢复；旧路径停调度后才回切，无双路打印 |
| PF10B-010 | 主文L1～L8+本规格反例全链 | 独立/平台/外部与真实设备证据分开；Mock不替实际出纸 |

| Gate | 责任与当前缺口 | 派遣前退出条件 |
| --- | --- | --- |
| G10B-1 | 总控+业务方：首个客户业务DTO、名称保密/客户映射版本、撤单/源版本信号 | 成功/缺映射/订单失效样例与适配端口定稿，不以无客户fixture冒充已接入 |
| G10B-2 | 总控+设备核验执行者：打印机型号/固件/协议、Windows驱动/PDA SDK、回执层级 | 固定adapter配置字段/许可/版本、真实打印与未知结果恢复证据；不宣称全品牌 |
| G10B-3 | 总控：独立身份/文件/审计适配实际选用组件与存储字段、初始化/Outbox映射 | 复用成熟组件并给确切类型/配置/迁移，不创建第二套未设计账号/存储表；此门未闭合不能派002生产 |

这些未知项影响产品适配，不能通过拍脑袋补字段关闭。以上核心标签数据、接口、线框和验收已具体化；本轮未运行设备核验或启动开发。
