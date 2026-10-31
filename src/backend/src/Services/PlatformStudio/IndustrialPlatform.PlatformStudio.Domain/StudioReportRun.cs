namespace IndustrialPlatform.PlatformStudio.Domain;

/// <summary>一次报告或冻结草稿预览；状态版本和世代共同拒绝迟到执行结果，Unknown永不按TTL释放。</summary>
public sealed class StudioReportRun : StudioTechnicalRecord
{
    /// <summary>受理时固定Production或Preview，禁止将预览提升为正式归档。</summary>
    public string ExecutionKind { get; set; } = "Production";
    /// <summary>正式运行固定的发布修订主键；预览必须为空。</summary>
    public Guid? TemplateRevisionId { get; set; }
    /// <summary>预览受理时冻结的根文档及依赖闭包；不随草稿修改，最大8MiB。</summary>
    public string? PreviewSnapshotJson { get; set; }
    /// <summary>兼容STU-004非空约束的历史列；新预览写MaxValue占位，执行、读取和回收不依据此列失效，对外expiresOn始终为空。</summary>
    public DateTimeOffset? PreviewExpiresOn { get; set; }
    /// <summary>页面全部上传、扫描及引用确认后的预览清单；失败和未完成时为空。</summary>
    public string? PreviewManifestJson { get; set; }
    /// <summary>本租户整次业务生成防重键；受理重放不创建新运行。</summary>
    public string OperationKey { get; set; } = "";
    /// <summary>可信身份中的原发起人；不接受请求参数冒充。</summary>
    public string RequestedBy { get; set; } = "";
    /// <summary>受理时的已校验查询参数；与模板快照共同固定。</summary>
    public string ParametersJson { get; set; } = "{}";
    /// <summary>原工厂范围/权限依据及来源会话标识；不保存Cookie或登录令牌，不对浏览器回传。</summary>
    public string AccessSnapshotJson { get; set; } = "{}";
    /// <summary>输出Pdf/Html/Xlsx；Preview固定Html。</summary>
    public string Format { get; set; } = "Html";
    /// <summary>从PendingDispatch推进的业务运行状态；Unknown需有证据核对，不按时间清除。</summary>
    public string Status { get; set; } = "PendingDispatch";
    /// <summary>每次状态CAS递增；HTTP以十进制字符串表达。</summary>
    public long StateVersion { get; set; }
    /// <summary>公开调度受理返回的运行NId；不建跨库外键。</summary>
    public string? SchedulerRunNId { get; set; }
    /// <summary>真实worker领取时递增，与状态版本共同拒绝旧执行回写。</summary>
    public long ExecutionEpoch { get; set; }
    /// <summary>真实执行领取时间UTC，排队不写开始时间。</summary>
    public DateTimeOffset? StartedOn { get; set; }
    /// <summary>确认终态或登记Unknown的时间UTC，不是活动槽释放依据。</summary>
    public DateTimeOffset? CompletedOn { get; set; }
    /// <summary>实际取数时点/查询及资源版本依据，采集完成后固定；前置阶段为空。</summary>
    public string? SourceManifestJson { get; set; }
    /// <summary>执行阶段及进度诊断，不作为生成成功证明。</summary>
    public string ProgressJson { get; set; } = "{}";
    /// <summary>可对外呈现的稳定错误码，不保存堆栈或秘密。</summary>
    public string? ErrorCode { get; set; }
    /// <summary>错误对应规则/带/字段路径，最多512字符。</summary>
    public string? ErrorLocation { get; set; }
    /// <summary>受理链的追踪标识，不参与业务幂等hash。</summary>
    public string TraceId { get; set; } = "";
    /// <summary>明确失败后的手工重试来源；Unknown不可直接重跑。</summary>
    public Guid? RetryOfRunId { get; set; }
    /// <summary>RenderSnapshot或NewData，与重试来源成对保存。</summary>
    public string? RetryMode { get; set; }
}
