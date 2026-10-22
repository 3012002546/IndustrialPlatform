namespace IndustrialPlatform.SystemData.Contracts.Auditing;

public sealed record AuditFactIngestRequest
{
    public string? ProducerServiceKey { get; init; }
    public string? AuditEventNId { get; init; }
    public DateTimeOffset? OccurredOn { get; init; }
    public string? ActorUserNId { get; init; }
    public string? Action { get; init; }
    public string? ObjectType { get; init; }
    public string? ObjectNId { get; init; }
    public string? PayloadJson { get; init; }
    public string? TraceId { get; init; }
    public string? Severity { get; init; }
    public string? SourceIp { get; init; }
    public string? UserAgent { get; init; }
}

public sealed record AuditFactV1
{
    public string TenantNId { get; init; } = string.Empty;
    public string ProducerServiceKey { get; init; } = string.Empty;
    public string AuditEventNId { get; init; } = string.Empty;
    public DateTimeOffset OccurredOn { get; init; }
    public DateTimeOffset ReceivedOn { get; init; }
    public string? ActorUserNId { get; init; }
    public string Action { get; init; } = string.Empty;
    public string ObjectType { get; init; } = string.Empty;
    public string? ObjectNId { get; init; }
    public string PayloadJson { get; init; } = "{}";
    public string TraceId { get; init; } = string.Empty;
    public string Severity { get; init; } = "Info";
}

public sealed record AuditFactPageV1
{
    public IReadOnlyList<AuditFactV1> Items { get; init; } = [];
    public int Page { get; init; }
    public int PageSize { get; init; }
    public long Total { get; init; }
}

public sealed record AuditLifecycleRequest
{
    public string? State { get; init; }
    public DateTimeOffset? RetentionUntil { get; init; }
    public bool? LegalHold { get; init; }
    public string? LegalHoldReason { get; init; }
}
