namespace IndustrialPlatform.ReferenceData.Contracts;

public sealed record PublishOrDisableRequest(long ExpectedOptimisticVersion, Guid ExpectedConcurrencyVersion, string? ChangeReason);
public sealed record ReferenceSource(string SourceScope, string? SourceTenantNId, string NId, int Revision);
