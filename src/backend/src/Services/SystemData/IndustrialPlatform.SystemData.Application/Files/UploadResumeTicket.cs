using System.Security.Cryptography;
using System.Text;

namespace IndustrialPlatform.SystemData.Application.Files;

public interface IUploadResumeTicketService
{
    string Issue(string tenantNId, string sessionNId, string userNId, int writerEpoch, long offset, DateTimeOffset expiresOn);
    bool Validate(string ticket, string tenantNId, string sessionNId, string userNId, int writerEpoch, long offset, DateTimeOffset now);
}

/// <summary>
/// 短时 HMAC 断点票据。票据只证明服务端已确认的租户、会话、用户、epoch 和 offset，
/// 不替代普通登录鉴权，也不把完整文件 hash 暴露给传输层。
/// </summary>
public sealed class HmacUploadResumeTicketService : IUploadResumeTicketService
{
    private static readonly byte[] Secret = SHA256.HashData(Encoding.UTF8.GetBytes("IndustrialPlatform.PF04.dev-resume-ticket"));

    public string Issue(string tenantNId, string sessionNId, string userNId, int writerEpoch, long offset, DateTimeOffset expiresOn)
    {
        var payload = $"{tenantNId}\n{sessionNId}\n{userNId}\n{writerEpoch}\n{offset}\n{expiresOn.ToUnixTimeSeconds()}";
        var encoded = Base64Url(Encoding.UTF8.GetBytes(payload));
        return $"{encoded}.{Base64Url(Sign(encoded))}";
    }

    public bool Validate(string ticket, string tenantNId, string sessionNId, string userNId, int writerEpoch, long offset, DateTimeOffset now)
    {
        try
        {
            var parts = ticket.Split('.', 2);
            if (parts.Length != 2) return false;
            var payload = Encoding.UTF8.GetString(Convert.FromBase64String(Pad(parts[0]))).Split('\n');
            if (payload.Length != 6
                || !string.Equals(payload[0], tenantNId, StringComparison.Ordinal)
                || !string.Equals(payload[1], sessionNId, StringComparison.Ordinal)
                || !string.Equals(payload[2], userNId, StringComparison.Ordinal)
                || !int.TryParse(payload[3], out var ticketEpoch)
                || !long.TryParse(payload[4], out var ticketOffset)
                || !long.TryParse(payload[5], out var expires)) return false;
            var expected = Sign(parts[0]);
            var actual = Convert.FromBase64String(Pad(parts[1]));
            return CryptographicOperations.FixedTimeEquals(expected, actual)
                && ticketEpoch == writerEpoch
                && ticketOffset == offset
                && now.ToUnixTimeSeconds() <= expires;
        }
        catch (FormatException) { return false; }
    }

    private static byte[] Sign(string value) => HMACSHA256.HashData(Secret, Encoding.UTF8.GetBytes(value));
    private static string Base64Url(byte[] value) => Convert.ToBase64String(value).Replace('+', '-').Replace('/', '_').TrimEnd('=');
    private static string Pad(string value) => value.Replace('-', '+').Replace('_', '/') + new string('=', (4 - value.Length % 4) % 4);
}
