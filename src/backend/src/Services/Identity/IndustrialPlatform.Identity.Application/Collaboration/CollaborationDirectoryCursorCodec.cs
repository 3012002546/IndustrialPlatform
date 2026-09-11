using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace IndustrialPlatform.Identity.Application.Collaboration;

public sealed class CollaborationDirectoryCursorCodec
{
    private readonly byte[] _key;
    private readonly TimeSpan _lifetime = TimeSpan.FromMinutes(5);

    public CollaborationDirectoryCursorCodec(IConfiguration configuration)
    {
        var configured = configuration["Identity:Collaboration:DirectoryCursorSigningKey"]
            ?? configuration["Collaboration:Cursor:SigningKey"];
        _key = string.IsNullOrWhiteSpace(configured) ? [] : SHA256.HashData(Encoding.UTF8.GetBytes(configured));
    }

    public string Encode(int page, string tenantNId, string actorUserNId, string keyword, int limit, string lastUserNId)
    {
        EnsureConfigured();
        if (page < 2 || string.IsNullOrWhiteSpace(lastUserNId))
            throw new ArgumentOutOfRangeException(nameof(page));
        var payload = JsonSerializer.Serialize(new CursorPayload(page, tenantNId, actorUserNId, keyword, limit, lastUserNId, DateTimeOffset.UtcNow.Add(_lifetime).ToUnixTimeSeconds()));
        return Base64Url(Encoding.UTF8.GetBytes(payload + "." + Sign(payload)));
    }

    public int Decode(string? cursor, string tenantNId, string actorUserNId, string keyword, int limit)
    {
        if (string.IsNullOrWhiteSpace(cursor))
            return 1;
        EnsureConfigured();
        if (cursor.Length > 4096)
            throw Invalid();
        try
        {
            var parts = Encoding.UTF8.GetString(DecodeBase64Url(cursor)).Split('.', 2, StringSplitOptions.None);
            if (parts.Length != 2
                || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(parts[1]), Encoding.UTF8.GetBytes(Sign(parts[0]))))
                throw Invalid();
            var payload = JsonSerializer.Deserialize<CursorPayload>(parts[0]);
            if (payload is null
                || payload.Page < 2
                || !string.Equals(payload.TenantNId, tenantNId, StringComparison.Ordinal)
                || !string.Equals(payload.ActorUserNId, actorUserNId, StringComparison.Ordinal)
                || !string.Equals(payload.Keyword, keyword, StringComparison.Ordinal)
                || payload.Limit != limit
                || string.IsNullOrWhiteSpace(payload.LastUserNId))
                throw Invalid();
            if (payload.ExpiresOn <= DateTimeOffset.UtcNow.ToUnixTimeSeconds())
                throw Invalid();
            return payload.Page;
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException or JsonException or OverflowException)
        {
            throw Invalid();
        }
    }

    private string Sign(string payload) => Base64Url(HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(payload)));

    private void EnsureConfigured()
    {
        if (_key.Length == 0)
            throw new InvalidOperationException("Identity collaboration directory cursor signing is not configured.");
    }

    private static ArgumentException Invalid() => new("Invalid collaboration directory cursor.");

    private static string Base64Url(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] DecodeBase64Url(string value)
    {
        var normalized = value.Replace('-', '+').Replace('_', '/');
        normalized = normalized.PadRight(normalized.Length + (4 - normalized.Length % 4) % 4, '=');
        return Convert.FromBase64String(normalized);
    }

    private sealed record CursorPayload(int Page, string TenantNId, string ActorUserNId, string Keyword, int Limit, string LastUserNId, long ExpiresOn);
}
