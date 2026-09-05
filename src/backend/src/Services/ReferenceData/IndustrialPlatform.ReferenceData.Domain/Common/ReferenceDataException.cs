using IndustrialPlatform.SharedKernel.Exceptions;

namespace IndustrialPlatform.ReferenceData.Domain.Common;

public sealed class ReferenceDataException(string errorCode, int status = 400, string? field = null) : DomainException(errorCode)
{
    public string ErrorCode { get; } = errorCode;
    public int Status { get; } = status;
    public string? Field { get; } = field;
}

public enum ReferenceScopeType { Platform, Tenant, Factory }
public enum PublicationStatus { Draft, Published, Superseded, Disabled }

public static partial class ReferenceValidation
{
    [System.Text.RegularExpressions.GeneratedRegex("^[A-Za-z][A-Za-z0-9_.-]{1,63}$")]
    private static partial System.Text.RegularExpressions.Regex DefinitionPattern();

    [System.Text.RegularExpressions.GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9_.-]{0,63}$")]
    private static partial System.Text.RegularExpressions.Regex ItemPattern();

    public static string NId(string value, bool item = false)
    {
        if (string.IsNullOrWhiteSpace(value) || !(item ? ItemPattern() : DefinitionPattern()).IsMatch(value))
            throw new ReferenceDataException("REF-VALIDATION-FAILED", field: "nId");
        return value.ToUpperInvariant();
    }

    public static string Name(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 200)
            throw new ReferenceDataException("REF-VALIDATION-FAILED", field: "name");
        return value.Trim();
    }

    public static string? Description(string? value)
    {
        if (value?.Length > 2000) throw new ReferenceDataException("REF-VALIDATION-FAILED", field: "description");
        return value?.Trim();
    }

    public static void Scope(ReferenceScopeType scope, string? tenantNId, string? scopeNId)
    {
        if (scope == ReferenceScopeType.Factory) throw new ReferenceDataException("REF-SCOPE-FACTORY-NOT-READY", 409);
        if (!Enum.IsDefined(scope) || scopeNId is not null
            || (scope == ReferenceScopeType.Platform ? tenantNId is not null : string.IsNullOrWhiteSpace(tenantNId)))
            throw new ReferenceDataException("REF-SCOPE-INVALID");
    }
}
