namespace IndustrialPlatform.Web.Querying;

public sealed record ODataQueryValidationSettings(
    int MaxTop = IndustrialPlatform.Querying.Validation.QueryLimits.MaxTop,
    int MaxFilterNodes = IndustrialPlatform.Querying.Validation.QueryLimits.MaxFilterNodes,
    int MaxFunctionDepth = IndustrialPlatform.Querying.Validation.QueryLimits.MaxFunctionDepth,
    int MaxSortFields = IndustrialPlatform.Querying.Validation.QueryLimits.MaxSortFields);
