using System.Security.Cryptography;
using System.Text;
using IndustrialPlatform.Collaboration.Application;
using IndustrialPlatform.Identity.Application.Authentication;

namespace IndustrialPlatform.Collaboration.Infrastructure;

public sealed class JwtStepUpProofVerifier : IComplianceStepUpVerifier
{
    private readonly IStepUpGrantStore _grants;

    public JwtStepUpProofVerifier(IStepUpGrantStore grants) => _grants = grants;

    public async Task EnsureValidAsync(string proof, string tenantNId, string actorUserNId, string actorSessionNId, string actorSecurityVersion, string action, string requestNId, string scopeChecksum, string requestHash, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(proof))
            throw new CollaborationException(403, "COLLAB_STEP_UP_REQUIRED", "该操作需要当前密码再次验证。");
        if (proof.Length > 512)
            throw new CollaborationException(403, "COLLAB_STEP_UP_INVALID", "重新认证证明格式无效。");
        var proofHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(proof))).ToLowerInvariant();
        var receipt = await _grants.ConsumeAsync(proofHash, tenantNId, actorUserNId, actorSessionNId, actorSecurityVersion, action, requestNId, scopeChecksum, requestHash, "collaboration", DateTimeOffset.UtcNow, cancellationToken);
        if (receipt is null)
            throw new CollaborationException(403, "COLLAB_STEP_UP_INVALID", "重新认证证明无效、已过期或已使用。");
    }
}
