using System.Security.Cryptography;
using System.Text;

namespace IndustrialPlatform.Collaboration.EmbeddedHost.Services;

public static class EmbeddedSessionToken
{
    public static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
}
