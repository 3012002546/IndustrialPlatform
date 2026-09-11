using System.Security.Cryptography;
using System.Text.Json;
using IndustrialPlatform.Security;
using IndustrialPlatform.SystemData.Api.Controllers;
using IndustrialPlatform.SystemData.Application.Auditing;
using IndustrialPlatform.SystemData.Contracts.Auditing;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;

namespace IndustrialPlatform.SystemData.Tests;

public sealed class Pf05AuditIngressTests
{
    [Fact]
    public async Task Receiver_rejects_unknown_payload_fields_before_persisting()
    {
        var (result, audit) = await InvokeAsync(new AuditFactIngestRequest
        {
            AuditEventNId = "AUD-1",
            Action = "collaboration.message.send",
            ObjectType = "message",
            ObjectNId = "M-1",
            PayloadJson = "{\"schemaVersion\":1,\"result\":\"Succeeded\",\"unknown\":true}",
        });

        Assert.Equal(422, Assert.IsType<UnprocessableEntityObjectResult>(result).StatusCode);
        Assert.False(audit.WasCalled);
    }

    [Fact]
    public async Task Receiver_rejects_untrusted_action_and_request_source_metadata()
    {
        var (badAction, badActionAudit) = await InvokeAsync(new AuditFactIngestRequest
        {
            AuditEventNId = "AUD-1",
            Action = "collaboration.anything.execute",
            ObjectType = "message",
            ObjectNId = "M-1",
            PayloadJson = "{\"schemaVersion\":1,\"result\":\"Succeeded\"}",
        });
        var (sourceMetadata, sourceMetadataAudit) = await InvokeAsync(new AuditFactIngestRequest
        {
            AuditEventNId = "AUD-2",
            Action = "collaboration.message.send",
            ObjectType = "message",
            ObjectNId = "M-1",
            SourceIp = "127.0.0.1",
            PayloadJson = "{\"schemaVersion\":1,\"result\":\"Succeeded\"}",
        });

        Assert.Equal(422, Assert.IsType<UnprocessableEntityObjectResult>(badAction).StatusCode);
        Assert.Equal(422, Assert.IsType<UnprocessableEntityObjectResult>(sourceMetadata).StatusCode);
        Assert.False(badActionAudit.WasCalled);
        Assert.False(sourceMetadataAudit.WasCalled);
    }

    private static async Task<(IActionResult Result, RecordingAuditService Audit)> InvokeAsync(AuditFactIngestRequest request)
    {
        using var rsa = RSA.Create(2048);
        var publicKeyPath = Path.GetTempFileName();
        await File.WriteAllTextAsync(publicKeyPath, rsa.ExportRSAPublicKeyPem());
        try
        {
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TrustedServiceCalls:Callers:collaboration:Issuer"] = "collaboration",
                ["TrustedServiceCalls:Callers:collaboration:KeyId"] = "pf05-test-key",
                ["TrustedServiceCalls:Callers:collaboration:PublicKeyPath"] = publicKeyPath,
                ["TrustedServiceCalls:Callers:collaboration:AllowedTenantNIds:0"] = "T-1",
                ["Collaboration:ServiceIdentity:Issuer"] = "collaboration",
                ["Collaboration:ServiceIdentity:KeyId"] = "pf05-test-key",
                ["Collaboration:ServiceIdentity:PrivateKey"] = rsa.ExportRSAPrivateKeyPem(),
            }).Build();
            using var signer = new TrustedServiceCallSigner(configuration);
            var body = JsonSerializer.SerializeToUtf8Bytes(request);
            var httpContext = new DefaultHttpContext();
            httpContext.Request.Method = HttpMethods.Post;
            httpContext.Request.Path = "/internal/pf05/audits/facts:ingest";
            httpContext.Request.ContentType = "application/json";
            httpContext.Request.ContentLength = body.Length;
            httpContext.Request.Body = new MemoryStream(body);
            httpContext.Request.Headers[TrustedServiceCallValidator.HeaderName] = signer.Sign(
                HttpMethod.Post,
                "internal/pf05/audits/facts:ingest",
                body,
                "systemdata.pf05",
                "T-1",
                "U-1",
                "SID-1",
                "7",
                "audit.ingest",
                request.AuditEventNId!);
            var audit = new RecordingAuditService();
            var controller = new CollaborationSystemDataInternalController(
                new TrustedServiceCallValidator(configuration),
                new AcceptingNonceStore(),
                audit)
            {
                ControllerContext = new ControllerContext { HttpContext = httpContext },
            };

            return (await controller.Ingest(request, CancellationToken.None), audit);
        }
        finally
        {
            File.Delete(publicKeyPath);
        }
    }

    private sealed class AcceptingNonceStore : ITrustedServiceCallNonceStore
    {
        public Task<bool> TryRegisterAsync(string issuer, string nonce, DateTimeOffset expiresOn, CancellationToken cancellationToken) => Task.FromResult(true);
    }

    private sealed class RecordingAuditService : IAuditService
    {
        public bool WasCalled { get; private set; }

        public Task<AuditFactV1> IngestAsync(string tenantNId, AuditFactIngestRequest request, CancellationToken cancellationToken)
        {
            WasCalled = true;
            return Task.FromResult(new AuditFactV1());
        }

        public Task<AuditFactPageV1> QueryAsync(string tenantNId, string actorUserNId, string? producerServiceKey, string? action, DateTimeOffset? from, DateTimeOffset? until, int page, int pageSize, CancellationToken cancellationToken) => Task.FromException<AuditFactPageV1>(new NotSupportedException());
        public Task<AuditFactV1?> GetAsync(string tenantNId, string actorUserNId, string producerServiceKey, string auditEventNId, CancellationToken cancellationToken) => Task.FromException<AuditFactV1?>(new NotSupportedException());
        public Task<string> ExportCsvAsync(string tenantNId, string actorUserNId, string? producerServiceKey, string? action, DateTimeOffset? from, DateTimeOffset? until, CancellationToken cancellationToken) => Task.FromException<string>(new NotSupportedException());
        public Task UpdateLifecycleAsync(string tenantNId, string actorUserNId, string producerServiceKey, string auditEventNId, AuditLifecycleRequest request, CancellationToken cancellationToken) => Task.FromException(new NotSupportedException());
        public Task<bool> RecoverOutboxAsync(string tenantNId, string actorUserNId, Guid eventId, CancellationToken cancellationToken) => Task.FromException<bool>(new NotSupportedException());
    }
}
