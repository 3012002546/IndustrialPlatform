using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using IndustrialPlatform.PlatformStudio.Domain;

namespace IndustrialPlatform.PlatformStudio.Application.Trace;

/// <summary>进程内短期凭据：重启即失效；签名与五分钟期限不能代替每次消费的权威授权。</summary>
public sealed class TraceCredentials(TimeProvider clock)
{
    private readonly byte[] _key = RandomNumberGenerator.GetBytes(32);
    private readonly Dictionary<string, (DateTimeOffset Expires, TraceQueryState State)> _queries = new();
    private readonly object _gate = new();
    public static string Fingerprint(StudioPrincipal p) => StudioJson.Hash(StudioJson.Canonicalize(new JsonObject
    {
        ["tenant"] = p.TenantNId, ["subject"] = p.SubjectNId, ["issuer"] = p.IssuerKey, ["auth"] = p.AuthVersion, ["policy"] = p.PolicyVersion,
        ["permissions"] = new JsonArray(p.Permissions.Order(StringComparer.Ordinal).Select(x => (JsonNode?)JsonValue.Create(x)).ToArray()),
        ["roles"] = new JsonArray(p.Roles.Order(StringComparer.Ordinal).Select(x => (JsonNode?)JsonValue.Create(x)).ToArray()),
        ["factories"] = new JsonArray(p.FactoryNIds.Order(StringComparer.Ordinal).Select(x => (JsonNode?)JsonValue.Create(x)).ToArray())
    }));
    public string Issue(StudioPrincipal p, string resource, string revision, string purpose, JsonObject data)
    {
        var payload = new JsonObject { ["fingerprint"] = Fingerprint(p), ["resource"] = resource, ["revision"] = revision, ["purpose"] = purpose, ["expires"] = clock.GetUtcNow().AddMinutes(5).ToUnixTimeSeconds(), ["data"] = data.DeepClone() };
        var bytes = Encoding.UTF8.GetBytes(payload.ToJsonString());
        return Convert.ToBase64String(bytes) + "." + Convert.ToBase64String(HMACSHA256.HashData(_key, bytes));
    }
    public JsonObject Read(StudioPrincipal p, string resource, string revision, string purpose, string? token)
    {
        try
        {
            if (string.IsNullOrEmpty(token) || token.Length > 131072) throw Invalid();
            var parts = token.Split('.'); if (parts.Length != 2) throw Invalid();
            var bytes = Convert.FromBase64String(parts[0]);
            if (!CryptographicOperations.FixedTimeEquals(HMACSHA256.HashData(_key, bytes), Convert.FromBase64String(parts[1]))) throw Invalid();
            var payload = JsonNode.Parse(bytes)!.AsObject();
            if (payload["expires"]!.GetValue<long>() <= clock.GetUtcNow().ToUnixTimeSeconds()) throw new StudioException("TRACE_CREDENTIAL_EXPIRED", "追溯凭据已过期，请重新选择起点。", 409);
            if (StudioJson.Text(payload["fingerprint"]) != Fingerprint(p) || StudioJson.Text(payload["resource"]) != resource || StudioJson.Text(payload["revision"]) != revision || StudioJson.Text(payload["purpose"]) != purpose) throw Invalid();
            return payload["data"]!.DeepClone().AsObject();
        }
        catch (Exception e) when (e is FormatException or System.Text.Json.JsonException or InvalidOperationException or NullReferenceException) { throw Invalid(); }
    }
    internal string Save(StudioPrincipal p, string resource, string revision, TraceQueryState state)
    {
        lock (_gate)
        {
            foreach (var expired in _queries.Where(x => x.Value.Expires <= clock.GetUtcNow()).Select(x => x.Key).ToArray()) _queries.Remove(expired);
            if (_queries.Count >= 32) throw new StudioException("STUDIO_LIMIT", "继续查询槽位已满，请稍后重试。", 429);
            var id = Guid.NewGuid().ToString("N");
            _queries[id] = (clock.GetUtcNow().AddMinutes(5), state);
            return Issue(p, resource, revision, "Cursor", new() { ["id"] = id, ["queryHash"] = state.QueryHash });
        }
    }
    internal TraceQueryState Resume(StudioPrincipal p, string resource, string revision, string token)
    {
        var data = Read(p, resource, revision, "Cursor", token);
        lock (_gate)
        {
            if (!_queries.Remove(StudioJson.RequiredString(data, "id"), out var entry) || entry.Expires <= clock.GetUtcNow()) throw new StudioException("TRACE_CREDENTIAL_EXPIRED", "继续状态已失效，请重新查询。", 409);
            if (entry.State.QueryHash != StudioJson.Text(data["queryHash"])) throw Invalid();
            return entry.State;
        }
    }
    private static StudioException Invalid() => new("STUDIO_FORBIDDEN", "追溯凭据无效或授权范围已改变。", 403);
}

internal sealed record TraceWork(JsonObject Node, int Depth);
internal sealed class TraceQueryState(JsonObject parameters, string direction, string[] relationIds, int depth)
{
    public JsonObject Parameters { get; } = parameters;
    public string Direction { get; } = direction;
    public string[] RelationIds { get; } = relationIds;
    public int MaxDepth { get; } = depth;
    public string QueryHash { get; } = StudioJson.Hash(parameters.ToJsonString() + direction + string.Join("|", relationIds) + depth + Guid.NewGuid().ToString("N"));
    public Queue<TraceWork> Pending { get; } = new();
    public HashSet<string> Visited { get; } = new(StringComparer.Ordinal);
    public HashSet<string> CompletedNodes { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, int> Offsets { get; } = new(StringComparer.Ordinal);
}
