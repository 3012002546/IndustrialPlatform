using Microsoft.AspNetCore.Http;
using System.Text;

namespace IndustrialPlatform.Web.Middleware;

/// <summary>Builds a request path suitable for logs without copying credential-like query values.</summary>
public static class RequestLogPathRedactor
{
    private const string Redacted = "[REDACTED]";

    public static string Redact(PathString path, IQueryCollection query)
    {
        var result = new StringBuilder(path.Value ?? string.Empty);
        var first = true;
        foreach (var pair in query)
        {
            foreach (var value in pair.Value)
            {
                result.Append(first ? '?' : '&');
                first = false;
                result.Append(Uri.EscapeDataString(pair.Key));
                result.Append('=');
                result.Append(Uri.EscapeDataString(IsSensitiveKey(pair.Key) ? Redacted : value ?? string.Empty));
            }
        }

        return result.ToString();
    }

    private static bool IsSensitiveKey(string key)
    {
        var normalized = key.Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .ToLowerInvariant();
        return normalized.Contains("token", StringComparison.Ordinal)
            || normalized.Contains("secret", StringComparison.Ordinal)
            || normalized.Contains("password", StringComparison.Ordinal)
            || normalized.Contains("apikey", StringComparison.Ordinal)
            || normalized.Contains("authorization", StringComparison.Ordinal)
            || normalized is "code" or "key";
    }
}
