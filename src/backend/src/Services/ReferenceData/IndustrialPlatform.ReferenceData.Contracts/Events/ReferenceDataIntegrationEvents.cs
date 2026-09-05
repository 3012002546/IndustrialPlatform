using System.Text.Json.Serialization;
using IndustrialPlatform.EventBus.Events;

namespace IndustrialPlatform.ReferenceData.Contracts.Events;

public abstract class ReferenceDataIntegrationEvent : IntegrationEvent
{
    public const int Version = 1;
    public int EventVersion { get; } = Version;
    public string? TenantNId { get; }
    public string ScopeType { get; }
    public string? ScopeId { get; }
    public Guid AggregateId { get; }
    public string SubjectNId { get; }
    public long Revision { get; }

    [JsonIgnore]
    public abstract string RoutingKey { get; }

    [JsonIgnore]
    public override string EventType => RoutingKey;

    protected ReferenceDataIntegrationEvent(string? tenantNId, string scopeType, string? scopeId,
        Guid aggregateId, string subjectNId, long revision)
    {
        TenantNId = tenantNId;
        ScopeType = scopeType;
        ScopeId = scopeId;
        AggregateId = aggregateId;
        SubjectNId = subjectNId;
        Revision = revision;
    }
}

public sealed class ReferenceDictionaryPublishedV1(
    string? tenantNId, string scopeType, string? scopeId, Guid aggregateId, string subjectNId, long revision)
    : ReferenceDataIntegrationEvent(tenantNId, scopeType, scopeId, aggregateId, subjectNId, revision)
{
    [JsonIgnore]
    public override string RoutingKey => "industrial.reference-data.dictionary.published.v1";
}

public sealed class ReferenceConfigurationChangedV1 : ReferenceDataIntegrationEvent
{
    [JsonIgnore]
    public override string RoutingKey => "industrial.reference-data.configuration.changed.v1";
    public string AppDomainNId { get; }
    public string? KeyNId { get; }
    public string? ValueMode { get; }
    public string ChangeType { get; }

    public ReferenceConfigurationChangedV1(string? tenantNId, string scopeType, string? scopeId,
        Guid aggregateId, string subjectNId, long revision, string appDomainNId, string? keyNId,
        string? valueMode, string changeType)
        : base(tenantNId, scopeType, scopeId, aggregateId, subjectNId, revision)
    {
        AppDomainNId = appDomainNId;
        KeyNId = keyNId;
        ValueMode = valueMode;
        ChangeType = changeType;
    }
}

public sealed class ReferenceDynamicConfigurationPublishedV1(
    string? tenantNId, string scopeType, string? scopeId, Guid aggregateId, string subjectNId, long revision)
    : ReferenceDataIntegrationEvent(tenantNId, scopeType, scopeId, aggregateId, subjectNId, revision)
{
    [JsonIgnore]
    public override string RoutingKey => "industrial.reference-data.dynamic-configuration.published.v1";
}

public sealed class ReferenceMetadataPublishedV1(
    string? tenantNId, string scopeType, string? scopeId, Guid aggregateId, string subjectNId, long revision)
    : ReferenceDataIntegrationEvent(tenantNId, scopeType, scopeId, aggregateId, subjectNId, revision)
{
    [JsonIgnore]
    public override string RoutingKey => "industrial.reference-data.metadata.published.v1";
}

public sealed class ReferenceCodingRulePublishedV1(
    string? tenantNId, string scopeType, string? scopeId, Guid aggregateId, string subjectNId, long revision)
    : ReferenceDataIntegrationEvent(tenantNId, scopeType, scopeId, aggregateId, subjectNId, revision)
{
    [JsonIgnore]
    public override string RoutingKey => "industrial.reference-data.coding-rule.published.v1";
}

public sealed class ReferenceStateMachineChangedV1 : ReferenceDataIntegrationEvent
{
    [JsonIgnore]
    public override string RoutingKey => "industrial.reference-data.state-machine.changed.v1";
    public string ChangeType { get; }
    public long OptimisticVersion { get; }

    public ReferenceStateMachineChangedV1(string? tenantNId, string scopeType, string? scopeId,
        Guid aggregateId, string subjectNId, long revision, string changeType, long optimisticVersion)
        : base(tenantNId, scopeType, scopeId, aggregateId, subjectNId, revision)
    {
        ChangeType = changeType;
        OptimisticVersion = optimisticVersion;
    }
}

public sealed class ReferenceUnitDimensionChangedV1 : ReferenceDataIntegrationEvent
{
    [JsonIgnore]
    public override string RoutingKey => "industrial.reference-data.unit-of-measure.changed.v1";
    public string ChangeType { get; }
    public long OptimisticVersion { get; }

    public ReferenceUnitDimensionChangedV1(string? tenantNId, string scopeType, string? scopeId,
        Guid aggregateId, string subjectNId, long revision, string changeType, long optimisticVersion)
        : base(tenantNId, scopeType, scopeId, aggregateId, subjectNId, revision)
    {
        ChangeType = changeType;
        OptimisticVersion = optimisticVersion;
    }
}
