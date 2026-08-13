using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Text;
using System.Xml;
using IndustrialPlatform.Identity.Application.Sso;
using IndustrialPlatform.Identity.Domain.Sso;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;

namespace IndustrialPlatform.Identity.Infrastructure.Sso;

/// <summary>
/// SAML 2.0 适配器(§26.2):SP 基于 IdP 元数据解析 SSO 端点与签名证书,
/// 回调校验签名(GetIdElement 覆写的 SignedXml)、断言时间窗/Audience/Recipient,
/// 提取 NameID 与邮箱/名称属性。v1 联邦注销暂不实现(返回空,由服务本地注销)。
/// </summary>
public sealed class Saml2ExternalIdentityProvider : IExternalIdentityProvider
{
    private const string SamlAssertionNs = "urn:oasis:names:tc:SAML:2.0:assertion";
    private const string DsXmlNs = "http://www.w3.org/2000/09/xmldsig#";

    private static readonly Action<ILogger, Exception?> MetadataFetchFailed =
        LoggerMessage.Define(
            LogLevel.Error,
            new EventId(1, nameof(MetadataFetchFailed)),
            "SAML 元数据获取/解析失败,Provider 不可用(fail-closed)。");

    private static readonly Action<ILogger, Exception?> SignatureValidationFailed =
        LoggerMessage.Define(
            LogLevel.Error,
            new EventId(2, nameof(SignatureValidationFailed)),
            "SAML 断言签名校验失败,回调校验失败。");

    private readonly ConcurrentDictionary<string, SamlMetadata> _metadataCache = new(StringComparer.Ordinal);
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<Saml2ExternalIdentityProvider> _logger;

    /// <summary>初始化 SAML 适配器。</summary>
    public Saml2ExternalIdentityProvider(
        IHttpClientFactory httpClientFactory,
        ILogger<Saml2ExternalIdentityProvider> logger)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentNullException.ThrowIfNull(logger);
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <inheritdoc/>
    public SsoProtocol Protocol => SsoProtocol.Saml2;

    /// <inheritdoc/>
    public async Task<ExternalAuthorizeResult> BuildAuthorizeUriAsync(ExternalAuthorizeContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var metadata = await GetMetadataAsync(context.Provider, cancellationToken);
        var ssoUrl = metadata.SingleSignOnServiceUrl ?? throw new SsoProviderUnavailableException();
        var uri = QueryHelpers.AddQueryString(ssoUrl, new Dictionary<string, string?>
        {
            ["RelayState"] = context.State,
        });
        return new ExternalAuthorizeResult(uri);
    }

    /// <inheritdoc/>
    public async Task<ExternalLoginResult> HandleCallbackAsync(ExternalCallbackContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        // 1) RelayState 防 CSRF:必须与一次性消费取回的一致。
        if (!context.Parameters.TryGetValue("RelayState", out var relayState) || !string.Equals(relayState, context.ExpectedState, StringComparison.Ordinal))
        {
            throw new SsoCallbackValidationException();
        }

        // 2) SAMLResponse(HTTP-POST 绑定:base64 → XML,不解压)。
        if (!context.Parameters.TryGetValue("SAMLResponse", out var rawResponse) || string.IsNullOrWhiteSpace(rawResponse))
        {
            throw new SsoCallbackValidationException();
        }

        XmlDocument document;
        try
        {
            var xml = Encoding.UTF8.GetString(Convert.FromBase64String(rawResponse));
            document = new XmlDocument { PreserveWhitespace = true };
            document.LoadXml(xml);
        }
        catch (Exception ex) when (ex is FormatException or XmlException or ArgumentNullException)
        {
            SignatureValidationFailed(_logger, ex);
            throw new SsoCallbackValidationException();
        }

        // 3) 签名校验:用元数据签名证书公钥验签(GetIdElement 按 ID 定位引用)。
        var metadata = await GetMetadataAsync(context.Provider, cancellationToken);
        if (metadata.SigningCertificate is null || !VerifySignature(document, metadata.SigningCertificate))
        {
            SignatureValidationFailed(_logger, null);
            throw new SsoCallbackValidationException();
        }

        // 4) 断言校验:时间窗 / Audience / Recipient,提取主体。
        var assertion = FindChild(document.DocumentElement!, "Assertion", SamlAssertionNs);
        if (assertion is null)
        {
            throw new SsoCallbackValidationException();
        }

        if (!ValidateConditions(assertion, DateTimeOffset.UtcNow)
            || !ValidateAudience(assertion, context.Provider.ClientIdOrEntityId)
            || !ValidateRecipient(assertion, context.RedirectUri))
        {
            throw new SsoCallbackValidationException();
        }

        var subject = FindChild(assertion, "Subject", SamlAssertionNs);
        var nameId = subject is null ? null : FindChild(subject, "NameID", SamlAssertionNs);
        var externalSubject = nameId?.InnerText?.Trim();
        if (string.IsNullOrWhiteSpace(externalSubject))
        {
            throw new SsoCallbackValidationException();
        }

        return new ExternalLoginResult(
            externalSubject!,
            FirstNonEmpty(GetAttribute(assertion, "displayName"), GetAttribute(assertion, "urn:oid:2.16.840.1.113730.3.1.241")),
            FirstNonEmpty(GetAttribute(assertion, "mail"), GetAttribute(assertion, "urn:oid:0.9.2342.19200300.100.1.3")));
    }

    /// <inheritdoc/>
    /// <remarks>v1 仅本地注销,联邦 SLO 待后续版本实现(无法保证完整 SLO 请求)。</remarks>
    public Task<ExternalLogoutResult> BuildLogoutUriAsync(ExternalLogoutContext context, CancellationToken cancellationToken)
        => Task.FromResult(new ExternalLogoutResult(null));

    /// <inheritdoc/>
    public async Task<ExternalConnectionTestResult> TestConnectionAsync(ExternalConnectionTestContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        try
        {
            var metadata = await GetMetadataAsync(context.Provider, cancellationToken, forceRefresh: true);
            if (metadata.SingleSignOnServiceUrl is null)
            {
                return new ExternalConnectionTestResult(false, "元数据缺少 SingleSignOnService 端点。");
            }

            if (metadata.SigningCertificate is null)
            {
                return new ExternalConnectionTestResult(false, "元数据缺少签名证书。");
            }

            return new ExternalConnectionTestResult(true, "元数据解析成功,SSO 端点与签名证书可用。");
        }
        catch (SsoException)
        {
            return new ExternalConnectionTestResult(false, "元数据获取/解析失败,请检查元数据地址与网络连通性。");
        }
    }

    private async Task<SamlMetadata> GetMetadataAsync(
        IdentitySsoProvider provider,
        CancellationToken cancellationToken,
        bool forceRefresh = false)
    {
        var url = provider.AuthorityOrMetadataUrl;
        if (!forceRefresh && _metadataCache.TryGetValue(url, out var cached))
        {
            return cached;
        }

        try
        {
            using var client = _httpClientFactory.CreateClient();
            var xml = await client.GetStringAsync(url, cancellationToken);
            var document = new XmlDocument { PreserveWhitespace = true };
            document.LoadXml(xml);
            var metadata = ParseMetadata(document);
            if (string.IsNullOrWhiteSpace(metadata.SingleSignOnServiceUrl))
            {
                throw new SsoCallbackValidationException();
            }

            _metadataCache[url] = metadata;
            return metadata;
        }
        catch (SsoException)
        {
            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            MetadataFetchFailed(_logger, ex);
            throw new SsoProviderUnavailableException();
        }
    }

    private static SamlMetadata ParseMetadata(XmlDocument document)
    {
        var entity = FindChild(document.DocumentElement!, "IDPSSODescriptor", "urn:oasis:names:tc:SAML:2.0:metadata");
        var ssoUrl = FindChild(entity, "SingleSignOnService", "urn:oasis:names:tc:SAML:2.0:metadata")?.GetAttribute("Location");
        if (string.IsNullOrWhiteSpace(ssoUrl))
        {
            return new SamlMetadata(null, null);
        }

        X509Certificate2? certificate = null;
        if (entity is not null)
        {
            foreach (XmlNode node in entity.ChildNodes)
            {
                if (node is not XmlElement descriptor
                    || descriptor.LocalName != "KeyDescriptor"
                    || descriptor.NamespaceURI != "urn:oasis:names:tc:SAML:2.0:metadata"
                    || !string.Equals(descriptor.GetAttribute("use"), "signing", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var keyInfo = FindChild(descriptor, "KeyInfo", DsXmlNs);
                var x509Data = FindChild(keyInfo, "X509Data", DsXmlNs);
                var certificateNode = FindChild(x509Data, "X509Certificate", DsXmlNs);
                var encoded = certificateNode?.InnerText?.Trim();
                if (string.IsNullOrWhiteSpace(encoded))
                {
                    continue;
                }

                try
                {
                    certificate = X509CertificateLoader.LoadCertificate(Convert.FromBase64String(encoded));
                    break;
                }
                catch (CryptographicException)
                {
                    continue;
                }
            }
        }

        return new SamlMetadata(ssoUrl, certificate);
    }

    /// <summary>校验签名:按 ID 属性定位被引用元素的标准 SAML GetIdElement 覆写方案。</summary>
    private bool VerifySignature(XmlDocument document, X509Certificate2 certificate)
    {
        var signature = FindDescendant(document.DocumentElement!, "Signature", DsXmlNs);
        if (signature is null)
        {
            return false;
        }

        try
        {
            var rsa = certificate.GetRSAPublicKey();
            if (rsa is null)
            {
                return false;
            }

            var signedXml = new SamlSignedXml(document)
            {
                SigningKey = rsa,
            };
            signedXml.LoadXml(signature);
            return signedXml.CheckSignature();
        }
        catch (Exception ex) when (ex is CryptographicException or XmlException or InvalidOperationException)
        {
            SignatureValidationFailed(_logger, ex);
            return false;
        }
    }

    /// <summary>校验 Conditions 时间窗(允许 ±5 分钟时钟偏差)。</summary>
    private static bool ValidateConditions(XmlElement assertion, DateTimeOffset now)
    {
        var conditions = FindChild(assertion, "Conditions", SamlAssertionNs);
        if (conditions is null)
        {
            return true;
        }

        const int skewSeconds = 300;
        var notBefore = conditions.GetAttribute("NotBefore");
        var notOnOrAfter = conditions.GetAttribute("NotOnOrAfter");
        if (!string.IsNullOrEmpty(notBefore)
            && DateTimeOffset.TryParse(notBefore, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var notBeforeUtc)
            && now.AddSeconds(skewSeconds) < notBeforeUtc)
        {
            return false;
        }

        if (!string.IsNullOrEmpty(notOnOrAfter)
            && DateTimeOffset.TryParse(notOnOrAfter, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var notOnOrAfterUtc)
            && now.AddSeconds(-skewSeconds) > notOnOrAfterUtc)
        {
            return false;
        }

        return true;
    }

    private static bool ValidateAudience(XmlElement assertion, string entityId)
    {
        var conditions = FindChild(assertion, "Conditions", SamlAssertionNs);
        var restriction = conditions is null ? null : FindChild(conditions, "AudienceRestriction", SamlAssertionNs);
        var audience = restriction is null ? null : FindChild(restriction, "Audience", SamlAssertionNs);
        return audience is not null && string.Equals(audience.InnerText.Trim(), entityId, StringComparison.Ordinal);
    }

    private static bool ValidateRecipient(XmlElement assertion, string expectedRecipient)
    {
        var subject = FindChild(assertion, "Subject", SamlAssertionNs);
        var confirmation = subject is null ? null : FindChild(subject, "SubjectConfirmation", SamlAssertionNs);
        var data = confirmation is null ? null : FindChild(confirmation, "SubjectConfirmationData", SamlAssertionNs);
        var recipient = data?.GetAttribute("Recipient");
        return recipient is not null && string.Equals(recipient, expectedRecipient, StringComparison.Ordinal);
    }

    /// <summary>读取指定名称的 SAML 属性值(兼容 OID 别名)。</summary>
    private static string? GetAttribute(XmlElement assertion, string name)
    {
        var statement = FindChild(assertion, "AttributeStatement", SamlAssertionNs);
        if (statement is null)
        {
            return null;
        }

        foreach (XmlNode node in statement.ChildNodes)
        {
            if (node is not XmlElement attribute
                || attribute.LocalName != "Attribute"
                || attribute.NamespaceURI != SamlAssertionNs
                || !string.Equals(attribute.GetAttribute("Name"), name, StringComparison.Ordinal))
            {
                continue;
            }

            foreach (XmlNode valueNode in attribute.ChildNodes)
            {
                if (valueNode is XmlElement valueElement && valueElement.LocalName == "AttributeValue")
                {
                    var text = valueElement.InnerText.Trim();
                    if (!string.IsNullOrEmpty(text))
                    {
                        return text;
                    }
                }
            }
        }

        return null;
    }

    /// <summary>按本地名+命名空间查找直接子元素(规避前缀差异)。</summary>
    private static XmlElement? FindChild(XmlElement? parent, string localName, string namespaceUri)
    {
        if (parent is null)
        {
            return null;
        }

        foreach (XmlNode child in parent.ChildNodes)
        {
            if (child is XmlElement element && element.LocalName == localName && element.NamespaceURI == namespaceUri)
            {
                return element;
            }
        }

        return null;
    }

    /// <summary>按本地名+命名空间递归查找后代元素(签名可能位于 Assertion 内)。</summary>
    private static XmlElement? FindDescendant(XmlElement root, string localName, string namespaceUri)
    {
        foreach (XmlNode child in root.ChildNodes)
        {
            if (child is not XmlElement element)
            {
                continue;
            }

            if (element.LocalName == localName && element.NamespaceURI == namespaceUri)
            {
                return element;
            }

            var nested = FindDescendant(element, localName, namespaceUri);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }

    private static string? FirstNonEmpty(string? first, string? second)
        => !string.IsNullOrWhiteSpace(first) ? first : second;

    /// <summary>SignedXml 子类:SAML 签名引用元素按 ID 属性定位(标准 GetIdElement 覆写)。</summary>
    private sealed class SamlSignedXml : SignedXml
    {
        public SamlSignedXml(XmlDocument document)
            : base(document)
        {
        }

        public override XmlElement? GetIdElement(XmlDocument? document, string? id)
        {
            if (document is null || string.IsNullOrEmpty(id))
            {
                return null;
            }

            var nodes = document.GetElementsByTagName("*");
            foreach (XmlNode node in nodes)
            {
                if (node is XmlElement element && string.Equals(element.GetAttribute("ID"), id, StringComparison.Ordinal))
                {
                    return element;
                }
            }

            return null;
        }
    }

    /// <summary>SAML 元数据投影(只保留 SSO 端点与签名证书)。</summary>
    private sealed record SamlMetadata(string? SingleSignOnServiceUrl, X509Certificate2? SigningCertificate);
}
