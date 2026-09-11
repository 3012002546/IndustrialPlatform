using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using IndustrialPlatform.Collaboration.Application;
using IndustrialPlatform.Security;
using IndustrialPlatform.SystemData.Application.Auditing;
using IndustrialPlatform.SystemData.Contracts.Auditing;

namespace IndustrialPlatform.Collaboration.Infrastructure;

public sealed class SystemDataAuditPort : ICollaborationAuditPort
{
    private readonly IAuditService _audit;

    public SystemDataAuditPort(IAuditService audit) => _audit = audit;

    public async Task WriteAsync(string tenantNId, TrustedCollaborationCall? serviceCall, string actorUserNId, string action, string objectType, string objectNId, object payload, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(payload);
        if (json.Length > 8192)
            throw new CollaborationException(400, "COLLAB_AUDIT_PAYLOAD_TOO_LARGE", "审计载荷超过允许大小。");
        var eventSeed = $"{tenantNId}\n{actorUserNId}\n{action}\n{objectType}\n{objectNId}\n{json}";
        var eventNId = "AUD-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(eventSeed))).ToLowerInvariant();
        await _audit.IngestAsync(tenantNId, new AuditFactIngestRequest
        {
            ProducerServiceKey = "collaboration",
            AuditEventNId = eventNId,
            OccurredOn = DateTimeOffset.UtcNow,
            ActorUserNId = actorUserNId,
            Action = action,
            ObjectType = objectType,
            ObjectNId = objectNId,
            PayloadJson = json,
            Severity = "Info",
        }, cancellationToken);
    }
}
