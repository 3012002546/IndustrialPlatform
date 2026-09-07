using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace IndustrialPlatform.SystemData.Domain.Auditing;

/// <summary>Audit payload normalization and export safety rules.</summary>
public static class AuditPayloadRules
{
    private static readonly HashSet<string> SensitiveNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "password", "passwd", "token", "access_token", "refresh_token", "secret", "client_secret",
        "authorization", "cookie", "connectionstring", "connection_string", "privatekey", "private_key"
    };

    public static string Sanitize(string? payload, out string hash)
    {
        JsonNode? node;
        try
        {
            node = JsonNode.Parse(string.IsNullOrWhiteSpace(payload) ? "{}" : payload);
        }
        catch (JsonException exception)
        {
            throw new ArgumentException("审计 payload 必须是合法 JSON。", nameof(payload), exception);
        }

        var normalized = Normalize(node);
        var result = normalized?.ToJsonString(new JsonSerializerOptions { WriteIndented = false }) ?? "null";
        hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(result))).ToLowerInvariant();
        return result;
    }

    public static string ComputeIdempotencyHash(
        string producerServiceKey,
        string auditEventNId,
        DateTimeOffset occurredOn,
        string? actorUserNId,
        string action,
        string objectType,
        string? objectNId,
        string payloadJson,
        string? traceId,
        string severity,
        string? sourceIp,
        string? userAgent)
    {
        var canonical = JsonSerializer.Serialize(new
        {
            producerServiceKey,
            auditEventNId,
            occurredOn = occurredOn.ToUniversalTime().ToString("O"),
            actorUserNId,
            action,
            objectType,
            objectNId,
            payloadJson,
            traceId,
            severity,
            sourceIp,
            userAgent,
        });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    public static string SafeCsvCell(string? value)
    {
        var text = value ?? string.Empty;
        if (text.Length > 0 && text[0] is '=' or '+' or '-' or '@')
        {
            text = "'" + text;
        }

        return text.Contains(';') || text.Contains('"') || text.Contains('\r') || text.Contains('\n')
            ? $"\"{text.Replace("\"", "\"\"", StringComparison.Ordinal)}\""
            : text;
    }

    private static JsonNode? Normalize(JsonNode? node, string? propertyName = null)
    {
        if (propertyName is not null && SensitiveNames.Contains(propertyName))
        {
            return JsonValue.Create("[REDACTED]");
        }

        return node switch
        {
            JsonObject obj => new JsonObject(obj.OrderBy(item => item.Key, StringComparer.Ordinal)
                .ToDictionary(item => item.Key, item => Normalize(item.Value, item.Key), StringComparer.Ordinal)),
            JsonArray array => new JsonArray(array.Select(item => Normalize(item)).ToArray()),
            _ => node?.DeepClone()
        };
    }
}
