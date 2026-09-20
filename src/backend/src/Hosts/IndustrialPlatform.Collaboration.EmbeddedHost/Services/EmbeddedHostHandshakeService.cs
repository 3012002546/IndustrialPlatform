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

namespace IndustrialPlatform.Collaboration.EmbeddedHost.Services;

/// <summary>统一校验来源、签名、有效期、重放及浏览器绑定，并创建或续期持久化会话。</summary>
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
        var requestedAccountNId = EmbeddedAccountQuery.Read(context);
        var origin = RequireAllowedOrigin(context.Request.Headers.Origin.ToString());
        var challenge = await _store.GetChallengeAsync(RequireNonce(nonce), cancellationToken) ?? throw InvalidHandshake("嵌入握手挑战不存在或已过期。");
        if (challenge.ExpiresOn <= DateTimeOffset.UtcNow || !string.Equals(challenge.ParentOrigin, origin, StringComparison.Ordinal))
            throw InvalidHandshake("嵌入握手挑战不存在或已过期。");
        var source = await _sourceResolver.ResolveAsync(context, requestedAccountNId, cancellationToken) ?? throw new EmbeddedHandshakeException(401, "EMBEDDED_SOURCE_AUTH_REQUIRED", "宿主认证会话不可用。");
        if (requestedAccountNId is not null && !string.Equals(source.AccountNId, requestedAccountNId, StringComparison.Ordinal))
            throw new EmbeddedHandshakeException(403, "EMBEDDED_ACCOUNT_MISMATCH", "account 与当前 MES 登录用户不匹配。");
        return _assertions.Issue(source, challenge.Nonce, DateTimeOffset.UtcNow, _options.AssertionLifetime);
    }

    public async Task<EmbeddedHandshakeSession> CompleteAsync(string assertion, HttpContext context, CancellationToken cancellationToken)
    {
        var requestedAccountNId = EmbeddedAccountQuery.Read(context);
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
        if (!string.Equals(identity.AccountNId, verified.AccountNId, StringComparison.Ordinal))
            throw InvalidHandshake("嵌入身份映射与 account 不一致。");
        EmbeddedAccountQuery.EnsureMatches(identity, requestedAccountNId);

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
        var requestedAccountNId = EmbeddedAccountQuery.Read(context);
        var token = ReadSessionToken(context, _options.CookieName);
        var browserBinding = ReadBrowserBinding(context, _options.BrowserBindingCookieName);
        var current = await _store.GetSessionAsync(EmbeddedSessionToken.Hash(token), EmbeddedSessionBinding.Hash(browserBinding), cancellationToken)
            ?? throw new EmbeddedHandshakeException(401, "EMBEDDED_SESSION_INVALID", "嵌入会话已失效。");
        EmbeddedAccountQuery.EnsureMatches(current.Identity, requestedAccountNId);
        await _store.RevokeSessionAsync(EmbeddedSessionToken.Hash(token), EmbeddedSessionBinding.Hash(browserBinding), DateTimeOffset.UtcNow, cancellationToken);
        context.Response.Cookies.Delete(_options.CookieName, new CookieOptions { Path = "/", Secure = true, HttpOnly = true, SameSite = SameSiteMode.None });
    }

    public async Task<EmbeddedHandshakeSession> KeepAliveAsync(HttpContext context, CancellationToken cancellationToken)
    {
        var requestedAccountNId = EmbeddedAccountQuery.Read(context);
        var token = ReadSessionToken(context, _options.CookieName);
        var browserBinding = ReadBrowserBinding(context, _options.BrowserBindingCookieName);
        var now = DateTimeOffset.UtcNow;
        var current = await _store.GetSessionAsync(EmbeddedSessionToken.Hash(token), EmbeddedSessionBinding.Hash(browserBinding), cancellationToken)
            ?? throw new EmbeddedHandshakeException(401, "EMBEDDED_SESSION_INVALID", "嵌入会话已失效。");
        EmbeddedAccountQuery.EnsureMatches(current.Identity, requestedAccountNId);
        var source = await _sourceResolver.ResolveAsync(context, current.Identity.AccountNId, cancellationToken);
        if (!IsCurrentSource(current.Identity, source) || !await _mapper.IsCurrentAsync(current, cancellationToken))
        {
            await _store.RevokeSessionAsync(current.SessionTokenHash, current.BrowserBindingHash, now, cancellationToken);
            throw new EmbeddedHandshakeException(401, "EMBEDDED_SESSION_STALE", "嵌入会话主体或安全版本已变化。");
        }
        var refreshed = await _store.TouchSessionAsync(
            current.SessionTokenHash,
            current.BrowserBindingHash,
            now,
            now.Add(_options.SessionLifetime),
            cancellationToken)
            ?? throw new EmbeddedHandshakeException(401, "EMBEDDED_SESSION_INVALID", "嵌入会话已失效。");
        AppendCookie(context, _options.CookieName, token, refreshed.ExpiresOn);
        return new EmbeddedHandshakeSession(refreshed.Identity, token, refreshed.ExpiresOn, refreshed.Epoch);
    }

    public async Task<EmbeddedStoredSession?> GetSessionAsync(string token, string browserBinding, CancellationToken cancellationToken) => await _store.GetSessionAsync(EmbeddedSessionToken.Hash(token), EmbeddedSessionBinding.Hash(browserBinding), cancellationToken);

    public static bool TryReadSessionCredential(HttpContext context, string cookieName, string bindingCookieName, out string token, out string binding)
    {
        var headerToken = context.Request.Headers["X-Embedded-Session"].ToString();
        var headerBinding = context.Request.Headers["X-Embedded-Binding"].ToString();
        if (!string.IsNullOrWhiteSpace(headerToken) || !string.IsNullOrWhiteSpace(headerBinding))
        {
            token = headerToken;
            binding = headerBinding;
            return !string.IsNullOrWhiteSpace(token) && !string.IsNullOrWhiteSpace(binding);
        }

        var accessToken = context.Request.Query["access_token"].ToString();
        if (accessToken.StartsWith("embedded-session:", StringComparison.Ordinal))
        {
            var parts = accessToken.Split(':', 3, StringSplitOptions.None);
            token = parts.Length == 3 ? parts[1] : string.Empty;
            binding = parts.Length == 3 ? parts[2] : string.Empty;
            return !string.IsNullOrWhiteSpace(token) && !string.IsNullOrWhiteSpace(binding);
        }

        token = context.Request.Cookies[cookieName] ?? string.Empty;
        binding = context.Request.Cookies[bindingCookieName] ?? string.Empty;
        return !string.IsNullOrWhiteSpace(token) && !string.IsNullOrWhiteSpace(binding);
    }

    public static string ReadSessionToken(HttpContext context, string cookieName = "industrial_embedded_session")
    {
        if (TryReadSessionCredential(context, cookieName, "industrial_embedded_binding", out var token, out _))
            return token;
        throw new EmbeddedHandshakeException(401, "EMBEDDED_SESSION_INVALID", "嵌入会话不可用。");
    }

    public static string ReadBrowserBinding(HttpContext context, string cookieName = "industrial_embedded_binding")
    {
        if (TryReadSessionCredential(context, "industrial_embedded_session", cookieName, out _, out var binding))
            return binding;
        throw new EmbeddedHandshakeException(401, "EMBEDDED_SESSION_INVALID", "嵌入浏览器绑定不可用。");
    }

    private static string RequireNonce(string value) => string.IsNullOrWhiteSpace(value) || value.Length > 256 ? throw InvalidHandshake("嵌入挑战标识无效。") : value.Trim();

    private string RequireAllowedOrigin(string? parentOrigin)
    {
        if (!Uri.TryCreate(parentOrigin, UriKind.Absolute, out var origin)
            || origin.Scheme is not ("https" or "http")
            || origin.PathAndQuery is not ("" or "/")
            || origin.Fragment.Length != 0
            || !_options.IsAllowedOrigin(origin.GetLeftPart(UriPartial.Authority)))
            throw new EmbeddedHandshakeException(403, "EMBEDDED_ORIGIN_FORBIDDEN", "嵌入父页面来源不在允许列表中。");
        return origin.GetLeftPart(UriPartial.Authority);
    }

    private static EmbeddedHandshakeException InvalidHandshake(string message, Exception? inner = null) => new(403, "EMBEDDED_HANDSHAKE_INVALID", message);

    private static bool IsCurrentSource(EmbeddedIdentity identity, EmbeddedSourcePrincipal? source) =>
        source is not null
        && string.Equals(source.SourceNId, identity.SourceNId, StringComparison.Ordinal)
        && string.Equals(source.ExternalTenantNId, identity.ExternalTenantNId, StringComparison.Ordinal)
        && string.Equals(source.ExternalSubject, identity.ExternalSubject, StringComparison.Ordinal)
        && string.Equals(source.SessionNId, identity.SessionNId, StringComparison.Ordinal)
        && string.Equals(source.SecurityVersion, identity.SecurityVersion, StringComparison.Ordinal)
        && (source.AccountNId is null || string.Equals(source.AccountNId, identity.AccountNId, StringComparison.Ordinal));

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
