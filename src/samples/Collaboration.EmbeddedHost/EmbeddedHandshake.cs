using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using IndustrialPlatform.Infrastructure.Database;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using SqlSugar;

namespace IndustrialPlatform.Collaboration.EmbeddedHost;

public sealed record EmbeddedHostHandshakeOptions(IReadOnlyCollection<string> AllowedParentOrigins)
{
    public string CookieName { get; init; } = "industrial_embedded_session";
    public string BrowserBindingCookieName { get; init; } = "industrial_embedded_binding";
    public string SourceSessionCookieName { get; init; } = "embedded_host_session";
    public TimeSpan ChallengeLifetime { get; init; } = TimeSpan.FromMinutes(2);
    public TimeSpan AssertionLifetime { get; init; } = TimeSpan.FromSeconds(60);
    public TimeSpan SessionLifetime { get; init; } = TimeSpan.FromSeconds(60);
}

public sealed record EmbeddedIdentity(string TenantNId, string UserNId, string SessionNId, string SecurityVersion)
{
    public string SourceNId { get; init; } = string.Empty;
    public string ExternalTenantNId { get; init; } = string.Empty;
    public string ExternalSubject { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
}

public sealed record EmbeddedSourcePrincipal(
    string SourceNId,
    string ExternalTenantNId,
    string ExternalSubject,
    string DisplayName,
    string SecurityVersion,
    string SessionNId);

public sealed record EmbeddedHandshakeChallenge(
    string Challenge,
    string Nonce,
    string ParentOrigin,
    DateTimeOffset ExpiresOn,
    [property: JsonIgnore] string BrowserBindingHash);

public sealed record EmbeddedHandshakeCompletion(string Assertion);

public sealed record EmbeddedHandshakeSession(EmbeddedIdentity Identity, string SessionToken, DateTimeOffset ExpiresOn, long Epoch);

public sealed record EmbeddedIdentityAssertion(
    string SourceNId,
    string Issuer,
    string ExternalSubject,
    string ExternalTenantNId,
    string DisplayName,
    string SecurityVersion,
    string SourceSessionNId,
    string Nonce,
    string Jti,
    DateTimeOffset ExpiresOn);

public interface IEmbeddedSubjectIdentityMapper
{
    Task<EmbeddedIdentity?> MapAsync(EmbeddedIdentityAssertion assertion, CancellationToken cancellationToken);
    Task<bool> IsCurrentAsync(EmbeddedStoredSession session, CancellationToken cancellationToken);
}

public interface IEmbeddedSourcePrincipalResolver
{
    Task<EmbeddedSourcePrincipal?> ResolveAsync(HttpContext context, CancellationToken cancellationToken);
}

public interface IEmbeddedIdentityAssertionIssuer
{
    string Issue(EmbeddedSourcePrincipal source, string nonce, DateTimeOffset issuedOn, TimeSpan lifetime);
    EmbeddedIdentityAssertion Validate(string assertion, DateTimeOffset now);
}

public interface IEmbeddedHandshakeStore
{
    Task<EmbeddedStoredChallenge?> GetChallengeAsync(string nonce, CancellationToken cancellationToken);
    Task SaveChallengeAsync(EmbeddedStoredChallenge challenge, CancellationToken cancellationToken);
    Task<EmbeddedChallengeConsumeResult> ConsumeChallengeAsync(string nonce, string browserBindingHash, string jti, DateTimeOffset now, DateTimeOffset assertionExpiresOn, CancellationToken cancellationToken);
    Task<EmbeddedStoredSession?> GetSessionAsync(string sessionTokenHash, string browserBindingHash, CancellationToken cancellationToken);
    Task<EmbeddedStoredSession> CreateSessionAsync(EmbeddedIdentity identity, string browserBindingHash, string sessionTokenHash, DateTimeOffset expiresOn, CancellationToken cancellationToken);
    Task RevokeSessionAsync(string sessionTokenHash, string browserBindingHash, DateTimeOffset revokedOn, CancellationToken cancellationToken);
}

public enum EmbeddedChallengeConsumeResult
{
    Invalid,
    Replayed,
    Consumed,
}

public sealed record EmbeddedStoredChallenge(
    string Challenge,
    string Nonce,
    string BrowserBindingHash,
    string ParentOrigin,
    DateTimeOffset ExpiresOn,
    DateTimeOffset? ConsumedOn);

public sealed record EmbeddedStoredSession(
    string SessionTokenHash,
    string BrowserBindingHash,
    EmbeddedIdentity Identity,
    long Epoch,
    DateTimeOffset ExpiresOn,
    DateTimeOffset? RevokedOn);

public sealed class EmbeddedHandshakeException(int statusCode, string code, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
    public string Code { get; } = code;
}

public sealed class EmbeddedPersistenceException(string message, Exception? innerException = null) : Exception(message, innerException);

public sealed class EmbeddedHostHandshakeService
{
    private readonly EmbeddedHostHandshakeOptions _options;
    private readonly IEmbeddedSubjectIdentityMapper _mapper;
    private readonly IEmbeddedSourcePrincipalResolver _sourceResolver;
    private readonly IEmbeddedIdentityAssertionIssuer _assertions;
    private readonly IEmbeddedHandshakeStore _store;

    public EmbeddedHostHandshakeService(EmbeddedHostHandshakeOptions options, IEmbeddedSubjectIdentityMapper mapper, IEmbeddedHandshakeStore store, IEmbeddedSourcePrincipalResolver sourceResolver, IEmbeddedIdentityAssertionIssuer assertions)
    {
        _options = options;
        _mapper = mapper;
        _store = store;
        _sourceResolver = sourceResolver;
        _assertions = assertions;
    }

    public async Task<EmbeddedHandshakeChallenge> CreateChallengeAsync(string parentOrigin, HttpContext context, CancellationToken cancellationToken)
    {
        var origin = RequireAllowedOrigin(parentOrigin);
        var now = DateTimeOffset.UtcNow;
        var browserBinding = context.Request.Cookies[_options.BrowserBindingCookieName] ?? string.Empty;
        var setBrowserBindingCookie = string.IsNullOrWhiteSpace(browserBinding);
        if (setBrowserBindingCookie)
            browserBinding = RandomToken();
        var challenge = new EmbeddedStoredChallenge(RandomToken(), RandomToken(), Hash(browserBinding), origin, now.Add(_options.ChallengeLifetime), null);
        await _store.SaveChallengeAsync(challenge, cancellationToken);
        if (setBrowserBindingCookie)
            AppendCookie(context, _options.BrowserBindingCookieName, browserBinding, challenge.ExpiresOn);
        return new EmbeddedHandshakeChallenge(challenge.Challenge, challenge.Nonce, challenge.ParentOrigin, challenge.ExpiresOn, challenge.BrowserBindingHash);
    }

    public async Task<string> IssueAssertionAsync(string nonce, HttpContext context, CancellationToken cancellationToken)
    {
        var origin = RequireAllowedOrigin(context.Request.Headers.Origin.ToString());
        var challenge = await _store.GetChallengeAsync(RequireNonce(nonce), cancellationToken) ?? throw InvalidHandshake("嵌入握手挑战不存在或已过期。");
        if (challenge.ExpiresOn <= DateTimeOffset.UtcNow || !string.Equals(challenge.ParentOrigin, origin, StringComparison.Ordinal))
            throw InvalidHandshake("嵌入握手挑战不存在或已过期。");
        var source = await _sourceResolver.ResolveAsync(context, cancellationToken) ?? throw new EmbeddedHandshakeException(401, "EMBEDDED_SOURCE_AUTH_REQUIRED", "宿主认证会话不可用。");
        return _assertions.Issue(source, challenge.Nonce, DateTimeOffset.UtcNow, _options.AssertionLifetime);
    }

    public async Task<EmbeddedHandshakeSession> CompleteAsync(string assertion, HttpContext context, CancellationToken cancellationToken)
    {
        var origin = RequireAllowedOrigin(context.Request.Headers.Origin.ToString());
        var browserBinding = context.Request.Cookies[_options.BrowserBindingCookieName];
        if (string.IsNullOrWhiteSpace(browserBinding))
            throw InvalidHandshake("嵌入浏览器绑定不可用。");

        EmbeddedIdentityAssertion verified;
        try
        {
            verified = _assertions.Validate(assertion, DateTimeOffset.UtcNow);
        }
        catch (Exception exception) when (exception is SecurityTokenException or ArgumentException or FormatException)
        {
            throw InvalidHandshake("嵌入身份断言无效。", exception);
        }

        var challenge = await _store.GetChallengeAsync(verified.Nonce, cancellationToken) ?? throw InvalidHandshake("嵌入握手挑战不存在或已过期。");
        if (challenge.ExpiresOn <= DateTimeOffset.UtcNow || !string.Equals(challenge.ParentOrigin, origin, StringComparison.Ordinal) || !FixedEquals(challenge.BrowserBindingHash, Hash(browserBinding)))
            throw InvalidHandshake("嵌入握手挑战不存在或已过期。");

        var identity = await _mapper.MapAsync(verified, cancellationToken) ?? throw new EmbeddedHandshakeException(403, "EMBEDDED_IDENTITY_NOT_MAPPED", "外部身份未映射到平台用户。");
        if (!string.Equals(identity.SourceNId, verified.SourceNId, StringComparison.Ordinal)
            || !string.Equals(identity.ExternalTenantNId, verified.ExternalTenantNId, StringComparison.Ordinal)
            || !string.Equals(identity.ExternalSubject, verified.ExternalSubject, StringComparison.Ordinal)
            || !string.Equals(identity.SessionNId, verified.SourceSessionNId, StringComparison.Ordinal)
            || !string.Equals(identity.SecurityVersion, verified.SecurityVersion, StringComparison.Ordinal))
            throw InvalidHandshake("嵌入身份映射与断言不一致。");

        var consumed = await _store.ConsumeChallengeAsync(challenge.Nonce, Hash(browserBinding), verified.Jti, DateTimeOffset.UtcNow, verified.ExpiresOn, cancellationToken);
        if (consumed == EmbeddedChallengeConsumeResult.Replayed)
            throw new EmbeddedHandshakeException(403, "EMBEDDED_ASSERTION_REPLAYED", "嵌入身份断言已被使用。");
        if (consumed != EmbeddedChallengeConsumeResult.Consumed)
            throw InvalidHandshake("嵌入握手挑战不存在或已使用。");

        var sessionToken = RandomToken();
        var expiresOn = DateTimeOffset.UtcNow.Add(_options.SessionLifetime);
        var session = await _store.CreateSessionAsync(identity, Hash(browserBinding), Hash(sessionToken), expiresOn, cancellationToken);
        AppendCookie(context, _options.CookieName, sessionToken, session.ExpiresOn);
        return new EmbeddedHandshakeSession(identity, sessionToken, session.ExpiresOn, session.Epoch);
    }

    public async Task<EmbeddedHandshakeSession> RenewAsync(string assertion, HttpContext context, CancellationToken cancellationToken)
    {
        var currentToken = ReadSessionToken(context, _options.CookieName);
        var browserBinding = ReadBrowserBinding(context, _options.BrowserBindingCookieName);
        var current = await _store.GetSessionAsync(EmbeddedSessionToken.Hash(currentToken), EmbeddedSessionBinding.Hash(browserBinding), cancellationToken) ?? throw new EmbeddedHandshakeException(401, "EMBEDDED_SESSION_INVALID", "嵌入会话已失效。");

        var origin = RequireAllowedOrigin(context.Request.Headers.Origin.ToString());
        EmbeddedIdentityAssertion verified;
        try
        {
            verified = _assertions.Validate(assertion, DateTimeOffset.UtcNow);
        }
        catch (Exception exception) when (exception is SecurityTokenException or ArgumentException or FormatException)
        {
            throw InvalidHandshake("嵌入身份断言无效。", exception);
        }

        if (!string.Equals(verified.ExternalSubject, current.Identity.ExternalSubject, StringComparison.Ordinal)
            || !string.Equals(verified.ExternalTenantNId, current.Identity.ExternalTenantNId, StringComparison.Ordinal)
            || !string.Equals(verified.SourceNId, current.Identity.SourceNId, StringComparison.Ordinal)
            || !string.Equals(verified.SourceSessionNId, current.Identity.SessionNId, StringComparison.Ordinal)
            || !string.Equals(verified.SecurityVersion, current.Identity.SecurityVersion, StringComparison.Ordinal))
            throw new EmbeddedHandshakeException(403, "EMBEDDED_SESSION_SUBJECT_CHANGED", "嵌入会话主体或安全版本已变化。");

        var challenge = await _store.GetChallengeAsync(verified.Nonce, cancellationToken) ?? throw InvalidHandshake("续期必须使用新的宿主认证挑战。");
        if (!string.Equals(challenge.ParentOrigin, origin, StringComparison.Ordinal) || !FixedEquals(challenge.BrowserBindingHash, Hash(browserBinding)))
            throw InvalidHandshake("续期挑战与当前浏览器绑定不一致。");
        var identity = await _mapper.MapAsync(verified, cancellationToken) ?? throw new EmbeddedHandshakeException(403, "EMBEDDED_IDENTITY_NOT_MAPPED", "外部身份未映射到平台用户。");
        if (!string.Equals(identity.SecurityVersion, current.Identity.SecurityVersion, StringComparison.Ordinal))
            throw new EmbeddedHandshakeException(401, "EMBEDDED_SESSION_STALE", "嵌入会话安全版本已变化。");

        var consumed = await _store.ConsumeChallengeAsync(challenge.Nonce, Hash(browserBinding), verified.Jti, DateTimeOffset.UtcNow, verified.ExpiresOn, cancellationToken);
        if (consumed == EmbeddedChallengeConsumeResult.Replayed)
            throw new EmbeddedHandshakeException(403, "EMBEDDED_ASSERTION_REPLAYED", "嵌入身份断言已被使用。");
        if (consumed != EmbeddedChallengeConsumeResult.Consumed)
            throw InvalidHandshake("续期挑战不存在或已使用。");

        var sessionToken = RandomToken();
        var expiresOn = DateTimeOffset.UtcNow.Add(_options.SessionLifetime);
        var session = await _store.CreateSessionAsync(identity, Hash(browserBinding), Hash(sessionToken), expiresOn, cancellationToken);
        AppendCookie(context, _options.CookieName, sessionToken, session.ExpiresOn);
        return new EmbeddedHandshakeSession(identity, sessionToken, session.ExpiresOn, session.Epoch);
    }

    public async Task RevokeAsync(HttpContext context, CancellationToken cancellationToken)
    {
        var token = ReadSessionToken(context, _options.CookieName);
        var browserBinding = ReadBrowserBinding(context, _options.BrowserBindingCookieName);
        await _store.RevokeSessionAsync(EmbeddedSessionToken.Hash(token), EmbeddedSessionBinding.Hash(browserBinding), DateTimeOffset.UtcNow, cancellationToken);
        context.Response.Cookies.Delete(_options.CookieName, new CookieOptions { Path = "/", Secure = true, HttpOnly = true, SameSite = SameSiteMode.None });
    }

    public async Task<EmbeddedStoredSession?> GetSessionAsync(string token, string browserBinding, CancellationToken cancellationToken) => await _store.GetSessionAsync(EmbeddedSessionToken.Hash(token), EmbeddedSessionBinding.Hash(browserBinding), cancellationToken);

    public static string ReadSessionToken(HttpContext context, string cookieName = "industrial_embedded_session")
    {
        var cookie = context.Request.Cookies[cookieName];
        if (!string.IsNullOrWhiteSpace(cookie))
            return cookie;
        throw new EmbeddedHandshakeException(401, "EMBEDDED_SESSION_INVALID", "嵌入会话不可用。");
    }

    public static string ReadBrowserBinding(HttpContext context, string cookieName = "industrial_embedded_binding") => context.Request.Cookies[cookieName] is { } cookie && !string.IsNullOrWhiteSpace(cookie)
        ? cookie
        : throw new EmbeddedHandshakeException(401, "EMBEDDED_SESSION_INVALID", "嵌入浏览器绑定不可用。");

    private static string RequireNonce(string value) => string.IsNullOrWhiteSpace(value) || value.Length > 256 ? throw InvalidHandshake("嵌入挑战标识无效。") : value.Trim();

    private string RequireAllowedOrigin(string? parentOrigin)
    {
        if (!Uri.TryCreate(parentOrigin, UriKind.Absolute, out var origin)
            || origin.Scheme is not ("https" or "http")
            || origin.PathAndQuery is not ("" or "/")
            || origin.Fragment.Length != 0
            || !_options.AllowedParentOrigins.Contains(origin.GetLeftPart(UriPartial.Authority), StringComparer.Ordinal))
            throw new EmbeddedHandshakeException(403, "EMBEDDED_ORIGIN_FORBIDDEN", "嵌入父页面来源不在允许列表中。");
        return origin.GetLeftPart(UriPartial.Authority);
    }

    private static EmbeddedHandshakeException InvalidHandshake(string message, Exception? inner = null) => new(403, "EMBEDDED_HANDSHAKE_INVALID", message);

    private static void AppendCookie(HttpContext context, string name, string value, DateTimeOffset expiresOn) => context.Response.Cookies.Append(name, value, new CookieOptions
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.None,
        IsEssential = true,
        Path = "/",
        Expires = expiresOn,
    });

    private static string RandomToken() => Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static bool FixedEquals(string left, string right) => CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(left), Encoding.UTF8.GetBytes(right));
}

public sealed class ConfigurationEmbeddedSourcePrincipalResolver(IConfiguration configuration, EmbeddedHostHandshakeOptions options) : IEmbeddedSourcePrincipalResolver
{
    public Task<EmbeddedSourcePrincipal?> ResolveAsync(HttpContext context, CancellationToken cancellationToken)
    {
        var sourceCookie = context.Request.Cookies[options.SourceSessionCookieName];
        if (!string.IsNullOrWhiteSpace(sourceCookie))
        {
            var sourceSession = configuration.GetSection($"EmbeddedCollaboration:SourceSessions:{sourceCookie}");
            var configured = IsActive(sourceSession) ? Read(sourceSession) : null;
            if (configured is not null)
                return Task.FromResult<EmbeddedSourcePrincipal?>(configured);
        }

        if (context.User.Identity?.IsAuthenticated == true && string.Equals(context.User.FindFirstValue("embedded_source_authenticated"), "true", StringComparison.Ordinal))
        {
            var sourceNId = context.User.FindFirstValue("source_n_id") ?? configuration["EmbeddedCollaboration:DefaultSourceNId"];
            var externalTenantNId = context.User.FindFirstValue("external_tenant_n_id");
            var externalSubject = context.User.FindFirstValue(JwtRegisteredClaimNames.Sub) ?? context.User.FindFirstValue(ClaimTypes.NameIdentifier);
            var sessionNId = context.User.FindFirstValue("source_session_n_id") ?? context.User.FindFirstValue("sid");
            var securityVersion = context.User.FindFirstValue("security_version");
            if (sourceNId is not null && externalTenantNId is not null && externalSubject is not null && sessionNId is not null && securityVersion is not null)
                return Task.FromResult<EmbeddedSourcePrincipal?>(new EmbeddedSourcePrincipal(sourceNId, externalTenantNId, externalSubject, context.User.Identity?.Name ?? externalSubject, securityVersion, sessionNId));
        }

        return Task.FromResult<EmbeddedSourcePrincipal?>(null);
    }

    private static EmbeddedSourcePrincipal? Read(IConfigurationSection section)
    {
        var source = section["SourceNId"];
        var tenant = section["ExternalTenantNId"];
        var subject = section["ExternalSubject"];
        var session = section["SessionNId"];
        var version = section["SecurityVersion"];
        return string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(tenant) || string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(session) || string.IsNullOrWhiteSpace(version)
            ? null
            : new EmbeddedSourcePrincipal(source, tenant, subject, section["DisplayName"] ?? subject, version, session);
    }

    private static bool IsActive(IConfigurationSection section)
    {
        var status = section["Status"];
        if (!string.IsNullOrWhiteSpace(status) && !string.Equals(status, "Active", StringComparison.OrdinalIgnoreCase))
            return false;

        var enabled = section["Enabled"];
        return string.IsNullOrWhiteSpace(enabled) || bool.TryParse(enabled, out var value) && value;
    }
}

public sealed class ConfigurationEmbeddedSubjectIdentityMapper(IConfiguration configuration) : IEmbeddedSubjectIdentityMapper
{
    public Task<EmbeddedIdentity?> MapAsync(EmbeddedIdentityAssertion assertion, CancellationToken cancellationToken)
    {
        var source = configuration.GetSection($"EmbeddedCollaboration:Sources:{assertion.SourceNId}");
        var expectedTenant = source[$"ExternalTenantMappings:{assertion.ExternalTenantNId}"];
        var subject = source.GetSection($"SubjectMappings:{assertion.ExternalSubject}");
        var configuredTenant = subject["ExternalTenantNId"];
        var configuredVersion = subject["SecurityVersion"];
        if (!IsActive(subject)
            || string.IsNullOrWhiteSpace(expectedTenant)
            || string.IsNullOrWhiteSpace(configuredTenant)
            || string.IsNullOrWhiteSpace(configuredVersion)
            || !string.Equals(configuredTenant, assertion.ExternalTenantNId, StringComparison.Ordinal)
            || !string.Equals(configuredVersion, assertion.SecurityVersion, StringComparison.Ordinal))
            return Task.FromResult<EmbeddedIdentity?>(null);

        var userNId = "ext_" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{assertion.SourceNId}\0{assertion.ExternalTenantNId}\0{assertion.ExternalSubject}"))).ToLowerInvariant();
        return Task.FromResult<EmbeddedIdentity?>(new EmbeddedIdentity(expectedTenant, userNId, assertion.SourceSessionNId, assertion.SecurityVersion)
        {
            SourceNId = assertion.SourceNId,
            ExternalTenantNId = assertion.ExternalTenantNId,
            ExternalSubject = assertion.ExternalSubject,
            DisplayName = subject["DisplayName"] ?? assertion.DisplayName,
        });
    }

    public Task<bool> IsCurrentAsync(EmbeddedStoredSession session, CancellationToken cancellationToken)
    {
        var source = configuration.GetSection($"EmbeddedCollaboration:Sources:{session.Identity.SourceNId}");
        var expectedTenant = source[$"ExternalTenantMappings:{session.Identity.ExternalTenantNId}"];
        var subject = source.GetSection($"SubjectMappings:{session.Identity.ExternalSubject}");
        var configuredTenant = subject["ExternalTenantNId"];
        var configuredVersion = subject["SecurityVersion"];
        var userNId = "ext_" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{session.Identity.SourceNId}\0{session.Identity.ExternalTenantNId}\0{session.Identity.ExternalSubject}"))).ToLowerInvariant();
        var current = IsActive(subject)
            && string.Equals(expectedTenant, session.Identity.TenantNId, StringComparison.Ordinal)
            && string.Equals(configuredTenant, session.Identity.ExternalTenantNId, StringComparison.Ordinal)
            && string.Equals(configuredVersion, session.Identity.SecurityVersion, StringComparison.Ordinal)
            && string.Equals(userNId, session.Identity.UserNId, StringComparison.Ordinal);

        var sourceSession = configuration.GetSection($"EmbeddedCollaboration:SourceSessions:{session.Identity.SessionNId}");
        if (sourceSession.Exists())
        {
            current = current
                && IsActive(sourceSession)
                && string.Equals(sourceSession["SourceNId"], session.Identity.SourceNId, StringComparison.Ordinal)
                && string.Equals(sourceSession["ExternalTenantNId"], session.Identity.ExternalTenantNId, StringComparison.Ordinal)
                && string.Equals(sourceSession["ExternalSubject"], session.Identity.ExternalSubject, StringComparison.Ordinal)
                && string.Equals(sourceSession["SecurityVersion"], session.Identity.SecurityVersion, StringComparison.Ordinal);
        }

        return Task.FromResult(current);
    }

    private static bool IsActive(IConfigurationSection section)
    {
        var status = section["Status"];
        if (!string.IsNullOrWhiteSpace(status) && !string.Equals(status, "Active", StringComparison.OrdinalIgnoreCase))
            return false;

        var enabled = section["Enabled"];
        return string.IsNullOrWhiteSpace(enabled) || bool.TryParse(enabled, out var value) && value;
    }
}

public sealed class ConfigurationEmbeddedIdentityAssertionIssuer : IEmbeddedIdentityAssertionIssuer, IDisposable
{
    private readonly IConfiguration _configuration;
    private readonly ConcurrentDictionary<string, RSA> _privateKeys = new(StringComparer.Ordinal);

    public ConfigurationEmbeddedIdentityAssertionIssuer(IConfiguration configuration) => _configuration = configuration;

    public void Dispose()
    {
        foreach (var key in _privateKeys.Values)
            key.Dispose();
        _privateKeys.Clear();
    }

    public string Issue(EmbeddedSourcePrincipal source, string nonce, DateTimeOffset issuedOn, TimeSpan lifetime)
    {
        if (lifetime <= TimeSpan.Zero || lifetime > TimeSpan.FromSeconds(60))
            throw new EmbeddedHandshakeException(503, "EMBEDDED_IDENTITY_NOT_READY", "嵌入身份断言有效期配置无效。");
        var settings = ResolveBySource(source.SourceNId);
        var privateKey = _privateKeys.GetOrAdd(settings.SourceNId, _ => LoadKey(settings, requirePrivate: true));
        var issuedUtc = issuedOn.ToUniversalTime();
        var signingKey = new RsaSecurityKey(privateKey) { KeyId = settings.KeyId };
        var token = new JwtSecurityToken(
            settings.Issuer,
            settings.Audience,
            [
                new Claim(JwtRegisteredClaimNames.Sub, source.ExternalSubject.Trim()),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
                new Claim(JwtRegisteredClaimNames.Iat, EpochTime.GetIntDate(issuedUtc.UtcDateTime).ToString(System.Globalization.CultureInfo.InvariantCulture)),
                new Claim("external_tenant_n_id", source.ExternalTenantNId.Trim()),
                new Claim("display_name", source.DisplayName.Trim()),
                new Claim("security_version", source.SecurityVersion.Trim()),
                new Claim("source_session_n_id", source.SessionNId.Trim()),
                new Claim("nonce", nonce.Trim()),
            ],
            issuedUtc.UtcDateTime,
            issuedUtc.Add(lifetime).UtcDateTime,
            new SigningCredentials(signingKey, SecurityAlgorithms.RsaSha256));
        token.Header[JwtHeaderParameterNames.Typ] = "industrial-embedded-identity+jwt";
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public EmbeddedIdentityAssertion Validate(string assertion, DateTimeOffset now)
    {
        var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
        var token = handler.ReadJwtToken(assertion);
        if (!string.Equals(token.Header.Typ, "industrial-embedded-identity+jwt", StringComparison.Ordinal) || !string.Equals(token.Header.Alg, SecurityAlgorithms.RsaSha256, StringComparison.Ordinal))
            throw new SecurityTokenException("embedded assertion header invalid");
        var settings = ResolveByIssuer(token.Issuer, token.Header.Kid);
        using var publicKey = LoadKey(settings, requirePrivate: false);
        var principal = handler.ValidateToken(assertion, new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = settings.Issuer,
            ValidateAudience = true,
            ValidAudience = settings.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new RsaSecurityKey(publicKey)
            {
                KeyId = settings.KeyId,
                CryptoProviderFactory = new CryptoProviderFactory { CacheSignatureProviders = false },
            },
            ValidateLifetime = true,
            RequireExpirationTime = true,
            RequireSignedTokens = true,
            ClockSkew = TimeSpan.FromSeconds(5),
        }, out _);
        var issuedOn = token.IssuedAt;
        var expiresOn = token.ValidTo;
        if (issuedOn > now.UtcDateTime.AddSeconds(5) || expiresOn - issuedOn > TimeSpan.FromSeconds(60) || expiresOn <= now.UtcDateTime)
            throw new SecurityTokenException("embedded assertion lifetime invalid");

        var externalSubject = Required(principal, JwtRegisteredClaimNames.Sub, 256);
        var externalTenant = Required(principal, "external_tenant_n_id", 128);
        var displayName = Required(principal, "display_name", 200);
        var securityVersion = Required(principal, "security_version", 128);
        var sourceSession = Required(principal, "source_session_n_id", 128);
        var nonce = Required(principal, "nonce", 256);
        var jti = Required(principal, JwtRegisteredClaimNames.Jti, 128);
        return new EmbeddedIdentityAssertion(settings.SourceNId, settings.Issuer, externalSubject, externalTenant, displayName, securityVersion, sourceSession, nonce, jti, new DateTimeOffset(expiresOn, TimeSpan.Zero));
    }

    private EmbeddedIdentitySourceSettings ResolveBySource(string sourceNId) => ReadSettings(sourceNId, _configuration.GetSection($"EmbeddedCollaboration:Sources:{sourceNId}"));

    private EmbeddedIdentitySourceSettings ResolveByIssuer(string? issuer, string? keyId)
    {
        foreach (var source in _configuration.GetSection("EmbeddedCollaboration:Sources").GetChildren())
        {
            var settings = ReadSettings(source.Key, source);
            if (string.Equals(settings.Issuer, issuer, StringComparison.Ordinal) && string.Equals(settings.KeyId, keyId, StringComparison.Ordinal))
                return settings;
        }
        throw new SecurityTokenException("embedded assertion issuer or kid invalid");
    }

    private static EmbeddedIdentitySourceSettings ReadSettings(string sourceNId, IConfigurationSection section)
    {
        var issuer = Required(section["Issuer"], "issuer");
        var audience = Required(section["Audience"], "audience");
        var keyId = Required(section["KeyId"], "keyId");
        return new EmbeddedIdentitySourceSettings(sourceNId, issuer, audience, keyId, section["PrivateKeyPath"], section["PrivateKey"], section["PublicKeyPath"], section["PublicKey"]);
    }

    private static RSA LoadKey(EmbeddedIdentitySourceSettings settings, bool requirePrivate)
    {
        var pem = requirePrivate
            ? !string.IsNullOrWhiteSpace(settings.PrivateKeyPath) ? File.ReadAllText(settings.PrivateKeyPath) : settings.PrivateKey
            : !string.IsNullOrWhiteSpace(settings.PublicKeyPath) ? File.ReadAllText(settings.PublicKeyPath) : settings.PublicKey;
        if (string.IsNullOrWhiteSpace(pem))
            throw new EmbeddedHandshakeException(503, "EMBEDDED_IDENTITY_NOT_READY", "嵌入身份签名密钥未配置。");
        var rsa = RSA.Create();
        try
        {
            rsa.ImportFromPem(pem);
            return rsa;
        }
        catch (Exception exception) when (exception is CryptographicException or ArgumentException or IOException)
        {
            rsa.Dispose();
            throw new EmbeddedHandshakeException(503, "EMBEDDED_IDENTITY_NOT_READY", "嵌入身份签名密钥无效。");
        }
    }

    private static string Required(IConfigurationSection section, string key) => Required(section[key], key);

    private static string Required(string? value, string field) => string.IsNullOrWhiteSpace(value) ? throw new EmbeddedHandshakeException(503, "EMBEDDED_IDENTITY_NOT_READY", $"嵌入身份配置缺少 {field}。") : value.Trim();

    private static string Required(ClaimsPrincipal principal, string claimType, int maxLength)
    {
        var value = principal.FindFirstValue(claimType);
        return string.IsNullOrWhiteSpace(value) || value.Length > maxLength ? throw new SecurityTokenException("embedded assertion claim invalid") : value;
    }

    private sealed record EmbeddedIdentitySourceSettings(string SourceNId, string Issuer, string Audience, string KeyId, string? PrivateKeyPath, string? PrivateKey, string? PublicKeyPath, string? PublicKey);
}

public sealed class SqlEmbeddedHandshakeStore(SqlSugarDbContext dbContext) : IEmbeddedHandshakeStore, IDisposable
{
    private readonly SemaphoreSlim _schemaGate = new(1, 1);
    private volatile bool _schemaReady;

    public void Dispose() => _schemaGate.Dispose();

    public async Task<EmbeddedStoredChallenge?> GetChallengeAsync(string nonce, CancellationToken cancellationToken)
    {
        await EnsureSchemaAsync(cancellationToken);
        try
        {
            var row = await dbContext.SqlSugar.Queryable<EmbeddedChallengeTable>().Where(item => item.Nonce == nonce).FirstAsync(cancellationToken);
            return row is null ? null : ToRecord(row);
        }
        catch (Exception exception)
        {
            throw new EmbeddedPersistenceException("嵌入握手持久化不可用。", exception);
        }
    }

    public async Task SaveChallengeAsync(EmbeddedStoredChallenge challenge, CancellationToken cancellationToken)
    {
        await EnsureSchemaAsync(cancellationToken);
        try
        {
            await dbContext.SqlSugar.Insertable(ToRow(challenge)).ExecuteCommandAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            throw new EmbeddedPersistenceException("嵌入握手持久化不可用。", exception);
        }
    }

    public async Task<EmbeddedChallengeConsumeResult> ConsumeChallengeAsync(string nonce, string browserBindingHash, string jti, DateTimeOffset now, DateTimeOffset assertionExpiresOn, CancellationToken cancellationToken)
    {
        await EnsureSchemaAsync(cancellationToken);
        var sql = dbContext.SqlSugar;
        try
        {
            if (await sql.Queryable<EmbeddedAssertionJtiTable>().AnyAsync(item => item.Jti == jti, cancellationToken))
                return EmbeddedChallengeConsumeResult.Replayed;
            sql.Ado.BeginTran();
            try
            {
                var affected = await sql.Updateable<EmbeddedChallengeTable>()
                    .SetColumns(item => new EmbeddedChallengeTable { ConsumedOn = now })
                    .Where(item => item.Nonce == nonce && item.BrowserBindingHash == browserBindingHash && item.ConsumedOn == null && item.ExpiresOn > now)
                    .ExecuteCommandAsync(cancellationToken);
                if (affected != 1)
                {
                    sql.Ado.RollbackTran();
                    return EmbeddedChallengeConsumeResult.Invalid;
                }
                await sql.Insertable(new EmbeddedAssertionJtiTable { Jti = jti, ExpiresOn = assertionExpiresOn.AddSeconds(5) }).ExecuteCommandAsync(cancellationToken);
                sql.Ado.CommitTran();
                return EmbeddedChallengeConsumeResult.Consumed;
            }
            catch
            {
                sql.Ado.RollbackTran();
                throw;
            }
        }
        catch (Exception exception)
        {
            try
            {
                if (await sql.Queryable<EmbeddedAssertionJtiTable>().AnyAsync(item => item.Jti == jti, cancellationToken))
                    return EmbeddedChallengeConsumeResult.Replayed;
            }
            catch (Exception readException)
            {
                throw new EmbeddedPersistenceException("嵌入握手持久化不可用。", readException);
            }
            throw new EmbeddedPersistenceException("嵌入握手持久化不可用。", exception);
        }
    }

    public async Task<EmbeddedStoredSession?> GetSessionAsync(string sessionTokenHash, string browserBindingHash, CancellationToken cancellationToken)
    {
        await EnsureSchemaAsync(cancellationToken);
        try
        {
            var row = await dbContext.SqlSugar.Queryable<EmbeddedSessionTable>().Where(item => item.SessionTokenHash == sessionTokenHash && item.BrowserBindingHash == browserBindingHash).FirstAsync(cancellationToken);
            if (row is null || row.RevokedOn is not null || Utc(row.ExpiresOn) <= DateTimeOffset.UtcNow)
                return null;
            var epoch = await dbContext.SqlSugar.Queryable<EmbeddedBrowserEpochTable>().Where(item => item.BrowserBindingHash == row.BrowserBindingHash).FirstAsync(cancellationToken);
            return epoch is null || epoch.Epoch != row.Epoch ? null : ToRecord(row);
        }
        catch (Exception exception)
        {
            throw new EmbeddedPersistenceException("嵌入会话持久化不可用。", exception);
        }
    }

    public async Task<EmbeddedStoredSession> CreateSessionAsync(EmbeddedIdentity identity, string browserBindingHash, string sessionTokenHash, DateTimeOffset expiresOn, CancellationToken cancellationToken)
    {
        await EnsureSchemaAsync(cancellationToken);
        var sql = dbContext.SqlSugar;
        sql.Ado.BeginTran();
        try
        {
            var epoch = await sql.Queryable<EmbeddedBrowserEpochTable>().Where(item => item.BrowserBindingHash == browserBindingHash).FirstAsync(cancellationToken);
            var nextEpoch = (epoch?.Epoch ?? 0) + 1;
            if (epoch is null)
                await sql.Insertable(new EmbeddedBrowserEpochTable { BrowserBindingHash = browserBindingHash, Epoch = nextEpoch }).ExecuteCommandAsync(cancellationToken);
            else
                await sql.Updateable<EmbeddedBrowserEpochTable>().SetColumns(item => new EmbeddedBrowserEpochTable { Epoch = nextEpoch }).Where(item => item.BrowserBindingHash == browserBindingHash).ExecuteCommandAsync(cancellationToken);
            await sql.Updateable<EmbeddedSessionTable>().SetColumns(item => new EmbeddedSessionTable { RevokedOn = DateTimeOffset.UtcNow }).Where(item => item.BrowserBindingHash == browserBindingHash && item.RevokedOn == null).ExecuteCommandAsync(cancellationToken);
            var row = new EmbeddedSessionTable
            {
                SessionTokenHash = sessionTokenHash,
                BrowserBindingHash = browserBindingHash,
                TenantNId = identity.TenantNId,
                UserNId = identity.UserNId,
                SessionNId = identity.SessionNId,
                SecurityVersion = identity.SecurityVersion,
                SourceNId = identity.SourceNId,
                ExternalTenantNId = identity.ExternalTenantNId,
                ExternalSubject = identity.ExternalSubject,
                DisplayName = identity.DisplayName,
                Epoch = nextEpoch,
                ExpiresOn = expiresOn,
            };
            await sql.Insertable(row).ExecuteCommandAsync(cancellationToken);
            sql.Ado.CommitTran();
            return ToRecord(row);
        }
        catch (Exception exception)
        {
            sql.Ado.RollbackTran();
            throw new EmbeddedPersistenceException("嵌入会话持久化不可用。", exception);
        }
    }

    public async Task RevokeSessionAsync(string sessionTokenHash, string browserBindingHash, DateTimeOffset revokedOn, CancellationToken cancellationToken)
    {
        await EnsureSchemaAsync(cancellationToken);
        try
        {
            await dbContext.SqlSugar.Updateable<EmbeddedSessionTable>().SetColumns(item => new EmbeddedSessionTable { RevokedOn = revokedOn }).Where(item => item.SessionTokenHash == sessionTokenHash && item.BrowserBindingHash == browserBindingHash && item.RevokedOn == null).ExecuteCommandAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            throw new EmbeddedPersistenceException("嵌入会话持久化不可用。", exception);
        }
    }

    private async Task EnsureSchemaAsync(CancellationToken cancellationToken)
    {
        if (_schemaReady)
            return;
        await _schemaGate.WaitAsync(cancellationToken);
        try
        {
            if (_schemaReady)
                return;
            try
            {
                dbContext.SqlSugar.CodeFirst.InitTables<EmbeddedChallengeTable, EmbeddedAssertionJtiTable, EmbeddedBrowserEpochTable, EmbeddedSessionTable>();
                _schemaReady = true;
            }
            catch (Exception exception)
            {
                throw new EmbeddedPersistenceException("嵌入握手持久化不可用。", exception);
            }
        }
        finally
        {
            _schemaGate.Release();
        }
    }

    private static EmbeddedChallengeTable ToRow(EmbeddedStoredChallenge item) => new()
    {
        Challenge = item.Challenge,
        Nonce = item.Nonce,
        BrowserBindingHash = item.BrowserBindingHash,
        ParentOrigin = item.ParentOrigin,
        ExpiresOn = item.ExpiresOn,
        ConsumedOn = item.ConsumedOn,
    };

    private static EmbeddedStoredChallenge ToRecord(EmbeddedChallengeTable item) => new(item.Challenge, item.Nonce, item.BrowserBindingHash, item.ParentOrigin, Utc(item.ExpiresOn), item.ConsumedOn is null ? null : Utc(item.ConsumedOn.Value));
    private static EmbeddedStoredSession ToRecord(EmbeddedSessionTable item) => new(item.SessionTokenHash, item.BrowserBindingHash, new EmbeddedIdentity(item.TenantNId, item.UserNId, item.SessionNId, item.SecurityVersion) { SourceNId = item.SourceNId, ExternalTenantNId = item.ExternalTenantNId, ExternalSubject = item.ExternalSubject, DisplayName = item.DisplayName }, item.Epoch, Utc(item.ExpiresOn), item.RevokedOn is null ? null : Utc(item.RevokedOn.Value));
    private static DateTimeOffset Utc(DateTimeOffset value) => new(value.DateTime, TimeSpan.Zero);
}

[SugarTable("collaboration_embedded_challenge")]
public sealed class EmbeddedChallengeTable
{
    [SugarColumn(ColumnName = "challenge")] public string Challenge { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "nonce", IsPrimaryKey = true)] public string Nonce { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "browser_binding_hash")] public string BrowserBindingHash { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "parent_origin")] public string ParentOrigin { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "expires_on")] public DateTimeOffset ExpiresOn { get; set; }
    [SugarColumn(ColumnName = "consumed_on", IsNullable = true)] public DateTimeOffset? ConsumedOn { get; set; }
}

[SugarTable("collaboration_embedded_assertion_jti")]
public sealed class EmbeddedAssertionJtiTable
{
    [SugarColumn(ColumnName = "jti", IsPrimaryKey = true)] public string Jti { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "expires_on")] public DateTimeOffset ExpiresOn { get; set; }
}

[SugarTable("collaboration_embedded_browser_epoch")]
public sealed class EmbeddedBrowserEpochTable
{
    [SugarColumn(ColumnName = "browser_binding_hash", IsPrimaryKey = true)] public string BrowserBindingHash { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "epoch")] public long Epoch { get; set; }
}

[SugarTable("collaboration_embedded_session")]
public sealed class EmbeddedSessionTable
{
    [SugarColumn(ColumnName = "session_token_hash", IsPrimaryKey = true)] public string SessionTokenHash { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "browser_binding_hash")] public string BrowserBindingHash { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "tenant_n_id")] public string TenantNId { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "user_n_id")] public string UserNId { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "session_n_id")] public string SessionNId { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "security_version")] public string SecurityVersion { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "source_n_id")] public string SourceNId { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "external_tenant_n_id")] public string ExternalTenantNId { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "external_subject")] public string ExternalSubject { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "display_name")] public string DisplayName { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "epoch")] public long Epoch { get; set; }
    [SugarColumn(ColumnName = "expires_on")] public DateTimeOffset ExpiresOn { get; set; }
    [SugarColumn(ColumnName = "revoked_on", IsNullable = true)] public DateTimeOffset? RevokedOn { get; set; }
}

public static class EmbeddedSessionToken
{
    public static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
}

public static class EmbeddedSessionBinding
{
    public static string Hash(string binding) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(binding))).ToLowerInvariant();
}

public static class EmbeddedSessionEpochFilter
{
    public static bool Accept(EmbeddedStoredSession currentSession, long eventEpoch, EmbeddedIdentity eventIdentity)
    {
        if (currentSession.RevokedOn is not null || currentSession.ExpiresOn <= DateTimeOffset.UtcNow || currentSession.Epoch != eventEpoch)
            return false;

        return string.Equals(currentSession.Identity.SourceNId, eventIdentity.SourceNId, StringComparison.Ordinal)
            && string.Equals(currentSession.Identity.ExternalTenantNId, eventIdentity.ExternalTenantNId, StringComparison.Ordinal)
            && string.Equals(currentSession.Identity.ExternalSubject, eventIdentity.ExternalSubject, StringComparison.Ordinal)
            && string.Equals(currentSession.Identity.TenantNId, eventIdentity.TenantNId, StringComparison.Ordinal)
            && string.Equals(currentSession.Identity.UserNId, eventIdentity.UserNId, StringComparison.Ordinal)
            && string.Equals(currentSession.Identity.SessionNId, eventIdentity.SessionNId, StringComparison.Ordinal)
            && string.Equals(currentSession.Identity.SecurityVersion, eventIdentity.SecurityVersion, StringComparison.Ordinal);
    }
}

public sealed class EmbeddedSessionAuthenticationMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, IEmbeddedHandshakeStore store, EmbeddedHostHandshakeOptions options, IEmbeddedSubjectIdentityMapper mapper)
    {
        if (context.User.Identity?.IsAuthenticated != true)
        {
            var token = TryReadToken(context, options.CookieName);
            var browserBinding = TryReadBinding(context, options.BrowserBindingCookieName);
            if (token is not null && browserBinding is not null)
            {
                try
                {
                    var session = await store.GetSessionAsync(EmbeddedSessionToken.Hash(token), EmbeddedSessionBinding.Hash(browserBinding), context.RequestAborted);
                    if (session is not null)
                    {
                        if (await mapper.IsCurrentAsync(session, context.RequestAborted))
                        {
                            var claims = new[]
                            {
                                new Claim(ClaimTypes.NameIdentifier, session.Identity.UserNId),
                                new Claim(JwtRegisteredClaimNames.Sub, session.Identity.UserNId),
                                new Claim("tenant_id", session.Identity.TenantNId),
                                new Claim("sid", session.Identity.SessionNId),
                                new Claim("ver", session.Identity.SecurityVersion),
                                new Claim("embedded_session", "true"),
                                new Claim("embedded_epoch", session.Epoch.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                            };
                            context.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "EmbeddedSession"));
                        }
                        else
                        {
                            await store.RevokeSessionAsync(session.SessionTokenHash, session.BrowserBindingHash, DateTimeOffset.UtcNow, context.RequestAborted);
                        }
                    }
                }
                catch (EmbeddedPersistenceException)
                {
                    // No persistence fallback: a failed store never authenticates a request.
                }
            }
        }
        await next(context);
    }

    private static string? TryReadToken(HttpContext context, string cookieName)
    {
        var cookie = context.Request.Cookies[cookieName];
        return string.IsNullOrWhiteSpace(cookie) ? null : cookie;
    }

    private static string? TryReadBinding(HttpContext context, string cookieName)
    {
        var cookie = context.Request.Cookies[cookieName];
        return string.IsNullOrWhiteSpace(cookie) ? null : cookie;
    }
}
