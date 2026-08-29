namespace IndustrialPlatform.Querying.Validation;

public sealed record QueryValidationError(string Code, string Message, string? Parameter = null);

public sealed class QueryValidationException : ArgumentException
{
    public QueryValidationException(QueryValidationError error)
        : base(error.Message, error.Parameter)
    {
        Error = error;
    }

    public QueryValidationError Error { get; }
}
