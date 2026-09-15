using System.Security.Cryptography;
using System.Text;
using IndustrialPlatform.Collaboration.Application.RemoteAssistance;
using IndustrialPlatform.Collaboration.Contracts;
using Microsoft.Extensions.Options;

namespace IndustrialPlatform.Collaboration.Infrastructure;

/// <summary>Generates coturn REST credentials without exposing the configured signing secret.</summary>
public sealed class RemoteAssistanceTurnCredentialProvider : IRemoteAssistanceIceServerProvider
{
    private readonly IOptions<RemoteAssistanceOptions> _options;
    private readonly Func<DateTimeOffset> _clock;

    public RemoteAssistanceTurnCredentialProvider(IOptions<RemoteAssistanceOptions> options)
        : this(options, () => DateTimeOffset.UtcNow)
    {
    }

    public RemoteAssistanceTurnCredentialProvider(IOptions<RemoteAssistanceOptions> options, Func<DateTimeOffset> clock)
    {
        _options = options;
        _clock = clock;
    }

    public Task<IReadOnlyList<IceServerDto>> GetAsync(string tenantNId, string userNId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var options = _options.Value;
        var urls = options.IceServerUrls.Where(url => !string.IsNullOrWhiteSpace(url)).Distinct(StringComparer.Ordinal).ToArray();
        var relayOnly = options.GetNormalizedIcePolicy() == "RelayOnly";
        var turnUrls = urls.Where(IsTurnUrl).ToArray();
        if (relayOnly && (turnUrls.Length == 0 || string.IsNullOrWhiteSpace(options.TurnCredentialSecret)))
            throw ConfigurationError();
        if (turnUrls.Length > 0 && string.IsNullOrWhiteSpace(options.TurnCredentialSecret))
            throw ConfigurationError();
        if (urls.Length == 0)
            return Task.FromResult<IReadOnlyList<IceServerDto>>([]);

        var expiresAt = _clock().AddSeconds(Math.Clamp(options.TurnCredentialTtlSeconds, 30, 3600));
        var username = $"{expiresAt.ToUnixTimeSeconds()}:{tenantNId}:{userNId}";
#pragma warning disable CA5350 // coturn REST credentials require HMAC-SHA1 for interoperability.
        using var hmac = string.IsNullOrWhiteSpace(options.TurnCredentialSecret)
            ? null
            : new HMACSHA1(Encoding.UTF8.GetBytes(options.TurnCredentialSecret));
#pragma warning restore CA5350
        var servers = urls.Select(url =>
        {
            if (!IsTurnUrl(url)) return new IceServerDto { Urls = [url] };
            var credential = Convert.ToBase64String(hmac!.ComputeHash(Encoding.UTF8.GetBytes(username)));
            return new IceServerDto { Urls = [url], Username = username, Credential = credential };
        }).ToArray();
        return Task.FromResult<IReadOnlyList<IceServerDto>>(servers);
    }

    private static bool IsTurnUrl(string url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out var uri)
            && (string.Equals(uri.Scheme, "turn", StringComparison.OrdinalIgnoreCase)
                || string.Equals(uri.Scheme, "turns", StringComparison.OrdinalIgnoreCase));
    }

    private static RemoteAssistanceException ConfigurationError() =>
        new("MEDIA_ICE_CONFIGURATION", "collaboration.media.iceConfiguration");
}
