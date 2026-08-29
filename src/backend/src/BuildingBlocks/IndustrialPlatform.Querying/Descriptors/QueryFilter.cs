namespace IndustrialPlatform.Querying.Descriptors;

public sealed record QueryFilter(string Field, QueryOperator Operator, object? Value);
