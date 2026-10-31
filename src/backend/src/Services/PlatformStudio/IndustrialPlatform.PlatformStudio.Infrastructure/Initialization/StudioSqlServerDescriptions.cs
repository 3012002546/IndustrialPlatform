using IndustrialPlatform.PlatformStudio.Infrastructure.Persistence;
using IndustrialPlatform.PlatformStudio.Domain;
using SqlSugar;

namespace IndustrialPlatform.PlatformStudio.Infrastructure.Initialization;

/// <summary>首次Studio迁移的中文字段字典；不改动任何已应用的SystemData脚本。</summary>
public static class StudioSqlServerDescriptions
{
    public static StudioSchema.Definition MigrationLedger { get; } = new("studio_schema_migrations", "migration_id:key128,description:json64,checksum:sha?,applied_on:time", "framework");
    public static StudioSchema.Definition SeedLedger { get; } = new("studio_seed_ledger", StudioSchema.SeedColumns, "baseline");
    private static readonly Dictionary<string, string> Tables = new(StringComparer.Ordinal)
    {
        ["studio_workbook_retired_file"] = "工作副本替换后的临时文件回收队列；历史版本与冲突恢复文件不入队。",
        ["studio_dashboard_screen"] = "看板匿名大屏链接；固定修订、参数及发布者安全版本，可到期和撤销，不保存明文密钥。",
        ["studio_source"] = "受控数据来源；只保存服务端登记键和秘密引用，不保存明文凭据。",
        ["studio_resource"] = "共享设计资源及草稿；发布指向由发布事务和双版本并发控制维护。",
        ["studio_revision"] = "不可变发布修订；固定配置、依赖闭包和来源合同，禁止更新及删除。",
        ["studio_grant"] = "资源用户或角色授权；与当前可信功能权限和数据策略取交集。",
        ["studio_ledger_record"] = "独立台账业务记录；按稳定字段标识保存完整值，不创建客户动态物理表。",
        ["studio_ledger_value"] = "台账类型索引；与记录值同事务更新，用于过滤、排序及唯一约束。",
        ["studio_operation_receipt"] = "业务幂等回执；Pending或Unknown禁止自动重复副作用，按业务端结果对账。",
        ["studio_audit_outbox"] = "可靠审计投递箱；与本地业务事务同写，不替代统一审计事实。",
        ["studio_report_run"] = "报告正式生成或设计预览运行；按受理世代和状态版本防止旧执行覆盖。",
        ["studio_report_artifact"] = "不可变正式报告产物；文件扫描及引用确认后登记，预览产物不在此归档。",
        ["studio_workbook"] = "工作簿元数据和当前内容版本指向；内容保存在受控文件引用中。",
        ["studio_workbook_version"] = "不可变工作簿内容版本；保存采用基版比较及幂等键，不复用上传WriterEpoch。",
        ["studio_workbook_session"] = "Office工作簿读写会话及单写锁；每次读写重验原主体身份依据。",
        ["studio_auth_request"] = "外部身份登录请求；记录state、nonce及PKCE证明，单次消费且有期限。",
        ["studio_auth_session"] = "可信Studio身份会话；来源句柄加密保存，撤权和授权版本必须重新核验。",
        ["studio_local_admin"] = "独立部署最小本地管理员；强制改密、失败锁定及授权版本受服务端控制。",
        ["studio_schema_migrations"] = "Studio统一迁移账本；固定迁移标识、说明和校验摘要，未知或漂移步骤拒绝应用。",
        ["studio_seed_ledger"] = "Studio种子账本；按种子标识、版本和摘要核验基线，不覆盖租户自定义配置。",
    };
    private static readonly Dictionary<string, string> Columns = new(StringComparer.Ordinal)
    {
        ["never_expires"] = "无限期标志；为真时忽略原到期时间，仍重验发布者及资源授权，可随时撤销。",
        ["follow_published"] = "跟随发布标志；为真时按当前已发布修订运行，为假时固定创建修订；草稿不生效。",
        ["revision_n_id"] = "链接固定的已发布修订业务标识。",
        ["token_hash"] = "256位随机大屏密钥的SHA256摘要，不保存明文密钥。",
        ["id"]="物理主键GUID，由服务生成，不供用户编辑。", ["n_id"]="公开业务或技术记录标识；租户内唯一，软删除后不可复用。", ["tenant_n_id"]="可信租户标识；仅从已验证身份上下文取得。",
        ["is_frozen"]="冻结标志；冻结后拒绝普通修改。", ["is_locked"]="锁定标志；锁定后拒绝普通修改。", ["is_deleted"]="软删除标志；删除不复用编号或级联删除历史。", ["entity_type"]="平台实体类型标识。",
        ["created_on"]="创建UTC时间，由服务端时钟填入。", ["last_updated_on"]="实际修改UTC时间，幂等无变化时不推进。", ["optimistic_version"]="乐观并发版本；实际修改递增，JSON使用十进制字符串。", ["concurrency_version"]="并发GUID令牌；实际修改时重新生成。",
        ["name"]="中文显示名称。", ["description"]="用途或迁移说明。", ["provider"]="创建时固定的来源或身份适配器类型。", ["endpoint_key"]="服务器登记连接或受信API目标键，不含明文地址与凭据。", ["endpoint_configuration_hash"]="已核验实际目标及非秘密语义配置的SHA256指纹。",
        ["credential_ref"]="服务端秘密库引用；公开DTO不得回传此值。", ["catalog_key"]="受控数据库目录键；数据库来源必填。", ["allowed_objects_json"]="允许的表、视图、过程或登记操作名单JSON，最大64KiB，禁止全库通配。", ["is_enabled"]="显式启用标志；来源须当前配置通过只读连通核验。", ["configuration_version"]="非秘密语义配置版本，从1递增；凭据轮换不推进。",
        ["timeout_seconds"]="取数超时秒数，范围1至120，执行采用来源与数据集较小限额。", ["max_rows"]="来源读取行数上限，范围1至100000，嵌套集合计入预算。", ["max_bytes"]="响应字节预算，最大52428800，JSON使用十进制字符串。",
        ["kind"]="创建后固定的资源种类；只接受代码白名单。", ["folder"]="展示分组路径，不参与权限判定。", ["document_schema_version"]="配置文档结构版本，首版为1，升级须显式迁移。", ["draft_json"]="当前规范化设计草稿JSON，最大2048KiB，保存时校验结构。", ["draft_base_revision_id"]="草稿起始发布修订主键，用于冲突比较，须属于本资源。", ["published_revision_id"]="当前发布修订主键，仅发布或回退事务可修改。", ["revision_sequence"]="已分配修订号序列，发布事务递增且不可复用。", ["owner_user_n_id"]="当前负责人可信用户标识，转交须审计。",
        ["resource_id"]="所属资源物理主键，同事务核验租户一致。", ["revision_no"]="资源内部递增修订号；资源与修订号联合唯一。", ["document_json"]="该修订完整规范化配置JSON，最大2048KiB，不可变。", ["dependencies_json"]="固定资源修订及内容摘要的去重排序依赖闭包JSON，最大512KiB。", ["source_pins_json"]="固定来源标识及配置版本JSON，最大64KiB，配置改变后拒绝静默运行。", ["content_hash"]="规范化文档、依赖和来源版本的SHA256摘要。", ["published_by"]="执行发布的可信用户标识。", ["release_note"]="本次发布说明，最大1000字符。",
        ["subject_kind"]="授权主体种类，仅User或Role。", ["subject_n_id"]="当前可信身份源的用户或角色标识。", ["capability"]="资源能力，仅View、Design、Publish、Run或Export。", ["revoked_on"]="资源授权撤销UTC时间；重新授权可显式清空。", ["granted_by"]="执行授权的可信用户标识。",
        ["model_resource_id"]="所属LedgerModel资源主键，不接受其他资源种类。", ["schema_revision_id"]="记录写入采用的台账模型修订主键。", ["values_json"]="按稳定字段标识保存的完整业务值JSON，最大256KiB，是记录值真源。", ["created_by"]="创建记录的可信用户标识。", ["last_updated_by"]="最近修改记录的可信用户标识。",
        ["record_id"]="所属台账记录主键，与字段标识组成主键。", ["field_id"]="模型稳定字段标识，最多64字符，重命名不改变标识。", ["value_kind"]="类型索引值种类；仅Text、Number、Boolean或Time且只有对应值列非空。", ["text_value"]="可查询文本索引值，最多512字符。", ["number_scaled"]="数值按六位小数缩放后的Int64，禁止转REAL比较或四舍五入。", ["bool_value"]="布尔类型索引值。", ["time_value"]="UTC时间类型索引值；业务Date按其合同规范化。", ["unique_key"]="类型及规范化值SHA256唯一键；非空时受模型与字段联合唯一约束。",
        ["executor_key"]="页面动作受理时固定的代码执行器键，非页面动作为空。", ["executor_version"]="页面动作受理时固定的执行器合同版本。",
        ["operation_key"]="调用方业务幂等键，同租户和操作种类内唯一。", ["actor_user_n_id"]="已验证的业务操作用户标识。", ["operation_kind"]="服务端登记的业务操作种类。", ["resource_n_id"]="操作所属公开资源或业务对象标识。", ["request_hash"]="规范化请求SHA256，相同幂等键不同摘要拒绝。", ["status"]="该记录的受控生命周期状态，取值由表级CHECK及状态机约束。", ["response_json"]="幂等操作已确认响应JSON，最大4096KiB；包含发布文档或台账DTO，不保存密码和来源秘密。", ["external_reference"]="实际外部业务引用，不含访问秘密。", ["error_code"]="稳定错误码，不保存连接串或秘密。", ["completed_on"]="最终结果确认UTC时间；Unknown不能据此自动释放或重放。",
        ["module_key"]="生成审计事实的Studio模块键。", ["audit_event_n_id"]="跨投递重试保持不变的审计事件标识。", ["payload_json"]="审计白名单负载JSON，最大64KiB，不存凭据及业务行全文。", ["attempt_count"]="审计投递尝试次数，从0递增。", ["next_attempt_on"]="下次允许重试的UTC时间。", ["sent_on"]="统一审计接收确认UTC时间。", ["last_error_code"]="最近投递失败稳定错误码。",
        ["execution_kind"]="报告用途，仅Production正式生成或Preview设计预览。", ["template_revision_id"]="正式报告固定模板修订主键；Preview必须为空。", ["preview_snapshot_json"]="设计预览不可变配置和依赖快照JSON，最大8192KiB，不含秘密。", ["preview_expires_on"]="预览受理时固定为创建后30分钟；不表示worker已经停止。", ["preview_manifest_json"]="预览全部页面确认后的文件引用清单JSON，最大2048KiB。", ["requested_by"]="可信报告发起用户标识。", ["parameters_json"]="已验证运行参数JSON，最大64KiB，不接受客户端覆盖租户或权限。", ["access_snapshot_json"]="策略版本、工厂范围及原身份来源依据快照JSON，最大64KiB，运行仍须重验撤权。", ["format"]="受控产物格式；报告为Pdf、Html或Xlsx，预览仅Html。", ["state_version"]="运行状态CAS版本，JSON使用十进制字符串。", ["scheduler_run_n_id"]="Scheduler受理运行公开标识，不建立跨服务外键。", ["execution_epoch"]="本地执行世代，用于拒绝旧worker覆盖当前运行。", ["started_on"]="已确认开始执行UTC时间。", ["source_manifest_json"]="来源版本、查询摘要、取数时点及资源文件引用清单JSON，最大512KiB。", ["progress_json"]="阶段、块计数及警告JSON，最大32KiB，不作为成功依据。", ["error_location"]="错误规则、带或字段路径，最多512字符，不含秘密。", ["trace_id"]="跨服务追踪标识。", ["retry_of_run_id"]="手工已知失败重试的原运行主键，不得指向自身。", ["retry_mode"]="手工重试模式，仅RenderSnapshot或NewData，与原运行引用同时为空或同时存在。",
        ["run_id"]="所属正式报告运行主键。", ["purpose"]="正式产物用途，仅Output、DataSnapshot或ResourceManifest。", ["file_n_id"]="受控File不可变内容公开标识。", ["file_reference_n_id"]="经业务归属和授权校验的文件引用标识。", ["sha256"]="不可变文件内容SHA256摘要。", ["content_length"]="不可变文件字节长度，JSON使用十进制字符串。", ["page_count"]="已完成版式的实际页数。", ["engine_manifest_json"]="渲染引擎、浏览器及字体精确版本与摘要JSON，最大64KiB。", ["retention_until"]="业务保留截止UTC时间，不绕过法律保全。",
        ["source_artifact_n_id"]="创建工作簿所依据的正式报告产物公开标识。", ["current_version_id"]="当前工作簿内容版本主键，保存采用基版CAS更新。", ["version_sequence"]="工作簿已分配内容版本序列，递增且不复用。", ["is_archived"]="工作簿已归档标志，不物理覆盖历史内容。", ["workbook_id"]="所属工作簿主键。", ["content_version"]="工作簿内容版本，JSON使用十进制字符串，不等于上传WriterEpoch。", ["base_version_id"]="编辑或保存采用的基准内容版本主键。", ["saved_by"]="确认保存的可信用户标识。", ["save_operation_key"]="工作簿保存幂等键，同一工作簿内唯一。",
        ["user_n_id"]="读写会话绑定的可信用户标识。", ["provider_key"]="代码登记的Office提供方键。", ["mode"]="工作簿会话模式，仅Edit或View。", ["access_token_hash"]="Office短期访问令牌SHA256，只保存摘要，不保存或记录明文令牌。", ["expires_on"]="请求或会话到期UTC时间。", ["last_seen_on"]="最近已验证活动UTC时间。", ["lock_hash"]="工作簿单写锁摘要；非空时同一工作簿仅允许一个锁。", ["lock_value_ciphertext"]="WOPI锁原值的受保护密文，用于协议冲突响应，不公开给业务浏览器。", ["lock_expires_on"]="单写锁到期UTC时间，续租须验证锁持有者。", ["last_save_operation_key"]="该编辑会话最近保存业务幂等键。", ["identity_evidence_json"]="服务端创建的原身份来源依据JSON，最大8KiB，每次Office读写重验。",
        ["state_hash"]="外部登录state摘要，单次消费且绑定登录请求。", ["issuer_key"]="可信身份发行方登记键。", ["nonce_hash"]="外部登录nonce摘要，用于请求关联及防重放。", ["verifier_cipher"]="PKCE验证器受保护密文，禁止进入日志。", ["return_path"]="已核验的站内返回路径，禁止任意外部重定向。", ["consumed_on"]="登录请求成功消费UTC时间。", ["session_hash"]="Studio会话票据摘要，不保存明文票据。", ["external_subject_id"]="已验证的MES原主体标识，不用于自动继承同名本地管理员。", ["external_tenant_id"]="已验证的外部租户标识，须经服务端租户映射。", ["context_handle_cipher"]="MES原来源会话句柄的受保护密文，重新授权时使用。", ["principal_json"]="已验证Studio主体及权限投影JSON，最大32KiB，不接受客户端自行构造。", ["auth_version"]="可信主体授权版本，撤权或重置后改变。", ["policy_version"]="授权策略版本，修改后旧策略缓存失效。", ["context_checked_on"]="最近向原身份源成功核验UTC时间，不用组装响应时间冒充。",
        ["login_name"]="最小本地管理员登录名，按登记比较规则唯一。", ["password_hash"]="经受控口令算法派生的摘要，不保存或回传明文密码。", ["enabled"]="最小本地管理员是否启用。", ["must_change_password"]="是否必须先完成强制改密才能使用业务能力。", ["failed_count"]="连续登录失败次数，用于锁定阈值。", ["locked_until"]="本地管理员登录锁定截止UTC时间。",
        ["migration_id"]="固定发行迁移标识，未知或未来标识拒绝降级接管。", ["checksum"]="固定迁移或种子规范化内容SHA256，补账前必须核验物理结构。", ["applied_on"]="受控迁移或种子应用成功UTC时间。", ["seed_n_id"]="代码登记的种子集合公开标识。", ["seed_version"]="种子集合版本字符串。", ["seed_class"]="种子类别，仅系统、租户基线或环境样例类别。", ["scope"]="种子适用范围，系统基线与租户范围分开。", ["system_data_operation_n_id"]="受理该初始化的SystemData操作公开标识，可为空。",
    };

    public static string TableDescription(string table) => Tables[table];
    public static string ColumnDescription(string column) => Columns[column];
    public static IReadOnlyList<string> Statements(StudioSchema.Definition table, string schema = "dbo")
    {
        _ = StudioSchema.Quote(schema, DbType.SqlServer);
        var statements = new List<string> { AddProperty(table.Name, null, TableDescription(table.Name), schema) };
        statements.AddRange(table.Columns.Split(',').Select(column => column.Split(':')[0]).Select(column => AddProperty(table.Name, column, ColumnDescription(column), schema)));
        return statements;
    }
    internal static string AddProperty(string table, string? column, string description, string schema)
    {
        _ = StudioSchema.Quote(table, DbType.SqlServer);
        if (column is not null) _ = StudioSchema.Quote(column, DbType.SqlServer);
        static string Literal(string value) => "N'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
        return "EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=" + Literal(description) + ",@level0type=N'SCHEMA',@level0name=" + Literal(schema) + ",@level1type=N'TABLE',@level1name=" + Literal(table) + (column is null ? "" : ",@level2type=N'COLUMN',@level2name=" + Literal(column));
    }

    public static async Task VerifyAsync(StudioDatabase database, StudioSchema.Definition table, CancellationToken cancellationToken)
    {
        var properties = await database.Client.Ado.SqlQueryAsync<PropertyRow>("SELECT COALESCE(c.name,'') AS ColumnName,CAST(p.value AS nvarchar(max)) AS Description FROM sys.extended_properties p JOIN sys.tables t ON t.object_id=p.major_id JOIN sys.schemas s ON s.schema_id=t.schema_id LEFT JOIN sys.columns c ON c.object_id=p.major_id AND c.column_id=p.minor_id WHERE p.class=1 AND p.name='MS_Description' AND s.name=@schema AND t.name=@table", new { schema = database.Schema, table = table.Name }, cancellationToken);
        if (!properties.Any(property => property.ColumnName.Length == 0 && property.Description == TableDescription(table.Name)) || table.Columns.Split(',').Select(column => column.Split(':')[0]).Any(column => !properties.Any(property => property.ColumnName == column && property.Description == ColumnDescription(column)))) throw new StudioException("STUDIO_SCHEMA_DRIFT", "Studio中文表列说明缺失或漂移。", 409);
    }
    public sealed class PropertyRow { public string ColumnName { get; set; } = ""; public string Description { get; set; } = ""; }
}
