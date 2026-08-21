using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IndustrialPlatform.SystemData.Application.ControlPlane;

public static class NavigationSnapshotChecksum
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public static string Compute(IEnumerable<PublishedNavigationNode> nodes)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        var canonical = nodes.OrderBy(x => x.NodeNId, StringComparer.Ordinal).ToArray();
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(canonical, JsonOptions))));
    }
}
