using System.Security.Cryptography;
using System.Text;

namespace IndustrialPlatform.Collaboration.EmbeddedHost.Services;

public static class EmbeddedSessionBinding
{
    public static string Hash(string binding) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(binding))).ToLowerInvariant();
}
