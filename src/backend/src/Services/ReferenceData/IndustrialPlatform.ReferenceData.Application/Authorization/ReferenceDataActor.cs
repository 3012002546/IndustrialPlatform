namespace IndustrialPlatform.ReferenceData.Application.Authorization;

/// <summary>Verified identity adapted at the API boundary; never read from a request body.</summary>
public sealed record ReferenceDataActor(string TenantNId, string UserNId, bool CanManagePlatform, string TraceId)
{
    public ReferenceDataPermissionDecision? ParameterDisableDecision { get; init; }
    public ReferenceDataPermissionDecision? DynamicPropertyDisableDecision { get; init; }
}
