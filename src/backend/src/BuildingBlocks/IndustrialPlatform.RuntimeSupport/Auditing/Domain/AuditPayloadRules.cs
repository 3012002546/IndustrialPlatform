using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace IndustrialPlatform.SystemData.Domain.Auditing;

/// <summary>Audit payload normalization and export safety rules.</summary>
public static class AuditPayloadRules
{
    private static readonly HashSet<string> CollaborationActions = new(StringComparer.Ordinal)
    {
        "collaboration.conversation.create",
        "collaboration.conversation.hide",
        "collaboration.conversation.restore",
        "collaboration.message.send",
        "collaboration.message.retract",
        "collaboration.message.personal-hide",
        "collaboration.attachment.authorize",
        "collaboration.attachment.bind",
        "collaboration.compliance.view.original",
        "collaboration.compliance.disposition.create",
        "collaboration.compliance.legal-hold.create",
        "collaboration.compliance.legal-hold.review",
        "collaboration.compliance.legal-hold.release-request",
        "collaboration.compliance.legal-hold.release-approve",
        "collaboration.compliance.legal-hold.file-sync",
        "collaboration.compliance.export.prepare",
        "collaboration.compliance.export.approve",
        "collaboration.compliance.export.start",
        "collaboration.compliance.export.complete",
        "collaboration.compliance.export.failed",
        "collaboration.compliance.export.expired",
        "collaboration.compliance.export.approval-expired",
        "collaboration.compliance.export.download.authorize",
        "collaboration.compliance.export.download.claim",
        "collaboration.compliance.retention.update",
    };
    private static readonly HashSet<string> CollaborationPayloadFields = new(StringComparer.Ordinal)
    {
        "schemaVersion", "requestNId", "result", "conversationNId", "messageNId", "attachmentNId", "caseNId", "operationId", "scopeChecksum", "stateVersion", "count", "errorCode",
    };
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

    public static bool IsAllowedCollaborationAction(string? action) =>
        action is not null && CollaborationActions.Contains(action);

    public static bool IsValidCollaborationPayload(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload) || Encoding.UTF8.GetByteCount(payload) > 8192)
            return false;

        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("schemaVersion", out var schemaVersion)
                || schemaVersion.ValueKind != JsonValueKind.Number
                || !schemaVersion.TryGetInt32(out var version)
                || version != 1
                || !root.TryGetProperty("result", out var result)
                || result.ValueKind != JsonValueKind.String
                || result.GetString() is not ("Requested" or "Succeeded" or "Failed" or "Denied"))
                return false;

            foreach (var property in root.EnumerateObject())
            {
                if (!CollaborationPayloadFields.Contains(property.Name) || !IsValidCollaborationPayloadValue(property.Name, property.Value))
                    return false;
            }

            return true;
        }
        catch (JsonException)
        {
            return false;
        }
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
        // Delivery time is not business identity; the same event id may be retried later.
        _ = occurredOn;
        var canonical = JsonSerializer.Serialize(new
        {
            producerServiceKey,
            auditEventNId,
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

    private static bool IsValidCollaborationPayloadValue(string name, JsonElement value) =>
        name switch
        {
            "schemaVersion" => value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var version) && version == 1,
            "result" or "requestNId" or "conversationNId" or "messageNId" or "attachmentNId" or "caseNId" or "operationId" => value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString()),
            "scopeChecksum" => value.ValueKind == JsonValueKind.String && IsSha256(value.GetString()),
            "stateVersion" => value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var stateVersion) && stateVersion >= 0,
            "count" => value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var count) && count >= 0,
            "errorCode" => value.ValueKind == JsonValueKind.String && value.GetString()!.Length <= 96,
            _ => false,
        };

    private static bool IsSha256(string? value) =>
        value is { Length: 64 } && value.All(Uri.IsHexDigit);
}
