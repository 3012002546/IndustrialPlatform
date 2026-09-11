using System.Security.Cryptography;
using System.Text;
using IndustrialPlatform.Identity.Application.Authentication;
using IndustrialPlatform.Identity.Contracts.Collaboration;
using IndustrialPlatform.Identity.Domain.Passwords;
using IndustrialPlatform.Identity.Domain.LoginSecurity;
using IndustrialPlatform.Identity.Infrastructure.Authentication;
using IndustrialPlatform.Security;
using IndustrialPlatform.SharedKernel.Exceptions;
using IndustrialPlatform.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;

namespace IndustrialPlatform.Identity.Api.Controllers;

[ApiController]
[Route("auth/step-up")]
[Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class CollaborationStepUpController : ControllerBase
{
    private readonly IAuthenticationStore _authentication;
    private readonly IPasswordHasher _passwords;
    private readonly IStepUpGrantStore _grants;
    private readonly ICurrentUser _current;
    private readonly IConfiguration _configuration;
    private readonly Microsoft.Extensions.Options.IOptions<AuthenticationOptions> _authenticationOptions;
    private readonly TrustedServiceCallValidator _trustedCalls;
    private readonly ITrustedServiceCallNonceStore _nonceStore;

    public CollaborationStepUpController(
        IAuthenticationStore authentication,
        IPasswordHasher passwords,
        IStepUpGrantStore grants,
        ICurrentUser current,
        IConfiguration configuration,
        Microsoft.Extensions.Options.IOptions<AuthenticationOptions> authenticationOptions,
        TrustedServiceCallValidator trustedCalls,
        ITrustedServiceCallNonceStore nonceStore)
    {
        _authentication = authentication;
        _passwords = passwords;
        _grants = grants;
        _current = current;
        _configuration = configuration;
        _authenticationOptions = authenticationOptions;
        _trustedCalls = trustedCalls;
        _nonceStore = nonceStore;
    }

    [HttpPost]
    public async Task<IActionResult> Create(CollaborationStepUpRequest request, CancellationToken cancellationToken)
    {
        var password = string.IsNullOrWhiteSpace(request.CurrentPassword) ? request.Password : request.CurrentPassword;
        var binding = request.Binding?.Trim();
        if (string.IsNullOrWhiteSpace(_current.UserNId) || string.IsNullOrWhiteSpace(_current.TenantId)
            || string.IsNullOrWhiteSpace(binding))
            return StatusCode(403, ApiResult.Fail<object?>("COLLAB_STEP_UP_REQUIRED", "该操作需要当前密码再次验证。"));

        var authenticated = await _authentication.FindByNIdAsync(_current.UserNId, cancellationToken);
        if (authenticated is null || !string.Equals(authenticated.User.TenantNId, _current.TenantId, StringComparison.Ordinal)
            || authenticated.User.IsDeleted || authenticated.User.Status != IndustrialPlatform.Identity.Domain.Users.UserStatus.Active)
            return StatusCode(403, ApiResult.Fail<object?>("COLLAB_STEP_UP_INVALID", "当前密码验证失败。"));

        var now = DateTimeOffset.UtcNow;
        try
        {
            authenticated.User.EnsureLoginAllowed(now);
        }
        catch (IndustrialPlatform.SharedKernel.Exceptions.UnauthorizedException)
        {
            return StatusCode(429, ApiResult.Fail<object?>("COLLAB_STEP_UP_RATE_LIMITED", "再认证失败次数过多，请稍后再试。"));
        }

        // Only the current, persisted system administrator assignment waives the
        // password challenge. Session version and signed command binding still apply.
        if (!authenticated.IsSystemAdmin && string.IsNullOrWhiteSpace(password))
            return StatusCode(403, ApiResult.Fail<object?>("COLLAB_STEP_UP_REQUIRED", "该操作需要当前密码再次验证。"));

        if (!authenticated.IsSystemAdmin && !_passwords.Verify(authenticated.User.PasswordHash, password!))
        {
            await RecordFailureAsync(authenticated, now, cancellationToken);
            return StatusCode(403, ApiResult.Fail<object?>("COLLAB_STEP_UP_INVALID", "当前密码验证失败。"));
        }

        var session = User.FindFirst(ClaimConstants.SessionId)?.Value ?? string.Empty;
        var authVersion = User.FindFirst(ClaimConstants.AuthVersion)?.Value ?? string.Empty;
        var currentAuthVersion = authenticated.User.AuthVersion.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (string.IsNullOrWhiteSpace(session) || !string.Equals(authVersion, currentAuthVersion, StringComparison.Ordinal))
            return StatusCode(401, ApiResult.Fail<object?>("COLLAB_STEP_UP_INVALID", "当前登录会话已失效，请重新登录。"));

        var signer = _configuration.GetSection("TrustedServiceCalls:Signers:collaboration");
        var keyId = signer["KeyId"] ?? "pf05-binding-v1";
        var issuer = signer["Issuer"] ?? "collaboration";
        var audience = signer["Audience"] ?? "identity.pf05";
        RSA? configuredPublicKey;
        try
        {
            configuredPublicKey = LoadConfiguredBindingPublicKey(signer);
        }
        catch (Exception exception) when (exception is IOException or CryptographicException or ArgumentException)
        {
            return StatusCode(503, ApiResult.Fail<object?>("COLLAB_STEP_UP_BINDING_UNAVAILABLE", "重新认证服务暂不可用，请稍后重试。"));
        }

        if (configuredPublicKey is null)
            return StatusCode(503, ApiResult.Fail<object?>("COLLAB_STEP_UP_BINDING_UNAVAILABLE", "重新认证服务暂不可用，请稍后重试。"));

        System.Security.Claims.ClaimsPrincipal principal;
        try
        {
            using (configuredPublicKey)
                principal = StepUpBinding.Validate(binding, configuredPublicKey, keyId, issuer, audience, now, out _);
        }
        catch (Exception exception) when (exception is SecurityTokenException or ArgumentException or FormatException)
        {
            return StatusCode(403, ApiResult.Fail<object?>("COLLAB_STEP_UP_INVALID", "重新认证上下文已失效，请重新发起操作。"));
        }

        var bindingTenant = principal.FindFirst("tenant_id")?.Value;
        var bindingActor = principal.FindFirst("actor_user_n_id")?.Value;
        var bindingSession = principal.FindFirst("actor_session_n_id")?.Value;
        var bindingVersion = principal.FindFirst("actor_security_version")?.Value;
        var action = principal.FindFirst("action")?.Value;
        var requestNId = principal.FindFirst("request_n_id")?.Value;
        var scopeChecksum = principal.FindFirst("scope_checksum")?.Value;
        var requestHash = principal.FindFirst("request_hash")?.Value;
        if (!string.Equals(bindingTenant, _current.TenantId, StringComparison.Ordinal)
            || !string.Equals(bindingActor, _current.UserNId, StringComparison.Ordinal)
            || !string.Equals(bindingSession, session, StringComparison.Ordinal)
            || !string.Equals(bindingVersion, currentAuthVersion, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(action) || string.IsNullOrWhiteSpace(requestNId)
            || string.IsNullOrWhiteSpace(scopeChecksum) || string.IsNullOrWhiteSpace(requestHash))
            return StatusCode(403, ApiResult.Fail<object?>("COLLAB_STEP_UP_INVALID", "重新认证上下文已失效，请重新发起操作。"));

        if (!authenticated.IsSystemAdmin)
            await RecordSuccessAsync(authenticated, now, cancellationToken);
        var expires = now.AddMinutes(2);
        var proof = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        await _grants.IssueAsync(new StepUpGrant(
            authenticated.User.TenantNId,
            authenticated.User.NId,
            session,
            authVersion,
            action,
            requestNId,
            scopeChecksum,
            requestHash,
            StepUpGrantStore.HashProof(binding),
            StepUpGrantStore.HashProof(proof),
            now,
            expires), cancellationToken);
        return Ok(ApiResult.Ok(new CollaborationStepUpResponse(proof, expires)));
    }

    [AllowAnonymous]
    [HttpPost("~/internal/pf05/identity/step-up/consume")]
    public async Task<IActionResult> Consume(CollaborationStepUpConsumeRequest request, CancellationToken cancellationToken)
    {
        var validation = await _trustedCalls.ValidateAsync(Request, "identity.pf05", "step-up.consume", _nonceStore, cancellationToken);
        if (validation.Status != TrustedServiceCallValidationStatus.Valid || validation.Call is null)
            return StatusCode(401, ApiResult.Fail<object?>("PF05_SERVICE_CALL_INVALID", "PF05 service assertion is invalid."));
        var call = validation.Call;
        if (string.IsNullOrWhiteSpace(request.Proof)
            || string.IsNullOrWhiteSpace(request.Action)
            || !string.Equals(request.RequestNId, call.RequestNId, StringComparison.Ordinal))
            return StatusCode(403, ApiResult.Fail<object?>("COLLAB_STEP_UP_INVALID", "重新认证证明绑定已失效。"));

        var consumed = await _grants.ConsumeAsync(
            StepUpGrantStore.HashProof(request.Proof),
            call.TenantNId,
            call.ActorUserNId,
            call.ActorSessionNId,
            call.ActorSecurityVersion,
            request.Action,
            request.RequestNId,
            request.ScopeChecksum,
            request.RequestHash,
            "collaboration",
            DateTimeOffset.UtcNow,
            cancellationToken);
        return consumed is null
            ? StatusCode(403, ApiResult.Fail<object?>("COLLAB_STEP_UP_INVALID", "重新认证证明无效、已过期或已使用。"))
            : Ok(ApiResult.Ok(new CollaborationStepUpConsumeResponse(consumed.ReceiptNId, consumed.ConsumedOn)));
    }

    private async Task RecordFailureAsync(AuthenticatedUser authenticated, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var expectedOptimistic = authenticated.User.OptimisticVersion;
        var expectedConcurrency = authenticated.User.ConcurrencyVersion;
        authenticated.User.RecordLoginFailure(now, new LoginAttemptPolicy(_authenticationOptions.Value.MaxLoginFailures, _authenticationOptions.Value.LockDuration));
        try
        {
            await _authentication.UpdateUserAsync(authenticated.User, expectedOptimistic, expectedConcurrency, cancellationToken);
        }
        catch (ConcurrencyException)
        {
            // A concurrent security update is fail-closed for this re-auth attempt.
            throw new SecurityStoreUnavailableException();
        }
    }

    private async Task RecordSuccessAsync(AuthenticatedUser authenticated, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (authenticated.User.FailedLoginCount == 0 && authenticated.User.LockedUntil is null)
            return;
        var expectedOptimistic = authenticated.User.OptimisticVersion;
        var expectedConcurrency = authenticated.User.ConcurrencyVersion;
        authenticated.User.RecordLoginSuccess(now);
        await _authentication.UpdateUserAsync(authenticated.User, expectedOptimistic, expectedConcurrency, cancellationToken);
    }

    private static RSA? LoadConfiguredBindingPublicKey(IConfigurationSection signer)
    {
        var publicKeyPath = signer["PublicKeyPath"];
        var publicKeyPem = signer["PublicKey"];
        var privateKeyPath = signer["PrivateKeyPath"];
        var privateKeyPem = signer["PrivateKey"];
        if (string.IsNullOrWhiteSpace(publicKeyPath) && string.IsNullOrWhiteSpace(publicKeyPem)
            && string.IsNullOrWhiteSpace(privateKeyPath) && string.IsNullOrWhiteSpace(privateKeyPem))
            return null;
        var rsa = RSA.Create();
        if (!string.IsNullOrWhiteSpace(publicKeyPath))
            rsa.ImportFromPem(System.IO.File.ReadAllText(publicKeyPath));
        else if (!string.IsNullOrWhiteSpace(publicKeyPem))
            rsa.ImportFromPem(publicKeyPem);
        else
            rsa.ImportFromPem(string.IsNullOrWhiteSpace(privateKeyPath) ? privateKeyPem! : System.IO.File.ReadAllText(privateKeyPath));
        return rsa;
    }
}
