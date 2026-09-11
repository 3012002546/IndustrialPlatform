using System.Security.Cryptography;
using System.Text;

namespace IndustrialPlatform.Collaboration.Application;

public sealed class PageCursorCodec
{
    private readonly byte[] _key;
    private readonly TimeSpan _lifetime;

    public PageCursorCodec(string? signingKey, TimeSpan lifetime)
    {
        _key = string.IsNullOrWhiteSpace(signingKey) ? [] : SHA256.HashData(Encoding.UTF8.GetBytes(signingKey));
        _lifetime = lifetime == TimeSpan.Zero ? throw new ArgumentOutOfRangeException(nameof(lifetime)) : lifetime;
    }

    public string Encode(int page, string binding)
    {
        EnsureConfigured();
        ArgumentOutOfRangeException.ThrowIfLessThan(page, 2);
        var expires = DateTimeOffset.UtcNow.Add(_lifetime).ToUnixTimeSeconds();
        var payload = $"v1.{page}.{expires}.{HashBinding(binding)}";
        return Base64Url(Encoding.UTF8.GetBytes(payload + "." + Sign(payload)));
    }

    public int Decode(string? cursor, string binding)
    {
        if (string.IsNullOrWhiteSpace(cursor))
            return 1;
        EnsureConfigured();
        if (cursor.Length > 2048)
            throw Invalid();
        try
        {
            var parts = Encoding.UTF8.GetString(Base64UrlDecode(cursor)).Split('.', StringSplitOptions.None);
            if (parts.Length != 5 || parts[0] != "v1" || !int.TryParse(parts[1], out var page) || page < 2
                || !long.TryParse(parts[2], out var expires) || !CryptographicOperations.FixedTimeEquals(
                    Encoding.UTF8.GetBytes(parts[4]), Encoding.UTF8.GetBytes(Sign(string.Join('.', parts[0], parts[1], parts[2], parts[3]))))
                || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(parts[3]), Encoding.UTF8.GetBytes(HashBinding(binding))))
                throw Invalid();
            if (DateTimeOffset.FromUnixTimeSeconds(expires) <= DateTimeOffset.UtcNow)
                throw new CollaborationException(410, "COLLAB_CURSOR_EXPIRED", "分页游标已过期，请重新加载列表。");
            return page;
        }
        catch (CollaborationException)
        {
            throw;
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException or OverflowException)
        {
            throw Invalid();
        }
    }

    private string Sign(string payload) => Base64Url(HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(payload)));

    private void EnsureConfigured()
    {
        if (_key.Length == 0)
            throw new CollaborationException(503, "COLLAB_CURSOR_UNAVAILABLE", "分页游标签名服务尚未配置。");
    }

    private static string HashBinding(string binding) => Base64Url(SHA256.HashData(Encoding.UTF8.GetBytes(binding)));

    private static string Base64Url(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Base64UrlDecode(string value)
    {
        var normalized = value.Replace('-', '+').Replace('_', '/');
        normalized = normalized.PadRight(normalized.Length + (4 - normalized.Length % 4) % 4, '=');
        return Convert.FromBase64String(normalized);
    }

    private static CollaborationException Invalid() => new(400, "COLLAB_CURSOR_INVALID", "分页游标无效。");
}
