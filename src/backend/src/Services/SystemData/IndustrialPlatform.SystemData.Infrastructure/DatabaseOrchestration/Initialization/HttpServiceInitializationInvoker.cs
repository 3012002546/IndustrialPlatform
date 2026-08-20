using System.Net.Http.Json;
using System.Text.Json;
using IndustrialPlatform.Application.Abstractions.Initialization;
using IndustrialPlatform.SystemData.Application.DatabaseOrchestration.Initialization;
using Microsoft.Extensions.Configuration;

namespace IndustrialPlatform.SystemData.Infrastructure.DatabaseOrchestration.Initialization;

/// <summary>分布式部署中的受信 HTTP 初始化适配器。</summary>
public sealed class HttpServiceInitializationInvoker : IServiceInitializationInvoker
{
    private const string HeaderName = "X-Industrial-Initialization-Key";
    private const string ConfigurationKey = "InternalInitialization:Key";
    private const string EnvironmentVariableName = "INDUSTRIAL_PLATFORM_INITIALIZATION_KEY";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;

    public HttpServiceInitializationInvoker(HttpClient httpClient, IConfiguration configuration)
    {
        _httpClient = httpClient;
        _configuration = configuration;
    }

    public Task<ServiceInitializationState> InspectAsync(ServiceInitializationContext context, CancellationToken cancellationToken) =>
        SendAsync<ServiceInitializationState>(context, "inspect", new RequestBody(context), cancellationToken);

    public Task<ServiceInitializationPlan> PlanAsync(
        ServiceInitializationContext context,
        ServiceInitializationState inspection,
        CancellationToken cancellationToken) =>
        SendAsync<ServiceInitializationPlan>(context, "plan", new RequestBody(context, inspection, null), cancellationToken);

    public Task<ServiceInitializationState> ApplyAsync(
        ServiceInitializationContext context,
        ServiceInitializationPlan plan,
        CancellationToken cancellationToken) =>
        SendAsync<ServiceInitializationState>(context, "apply", new RequestBody(context, null, plan), cancellationToken);

    public Task<ServiceInitializationState> VerifyAsync(ServiceInitializationContext context, CancellationToken cancellationToken) =>
        SendAsync<ServiceInitializationState>(context, "verify", new RequestBody(context), cancellationToken);

    private async Task<T> SendAsync<T>(
        ServiceInitializationContext context,
        string operation,
        RequestBody body,
        CancellationToken cancellationToken)
    {
        var baseUrl = _configuration[$"InternalInitialization:ServiceUrls:{context.ServiceKey}"];
        var key = _configuration[ConfigurationKey]
            ?? Environment.GetEnvironmentVariable(EnvironmentVariableName);
        if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(key))
        {
            throw new InvalidOperationException("内部初始化服务地址或鉴权输入未配置。");
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri(new Uri(baseUrl, UriKind.Absolute), $"api/v1/internal/initialization/{context.ServiceKey}/{operation}"));
        request.Headers.TryAddWithoutValidation(HeaderName, key);
        request.Content = JsonContent.Create(body, options: JsonOptions);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
        return result ?? throw new InvalidOperationException("内部初始化端点返回空结果。");
    }

    private sealed record RequestBody(
        string TenantNId,
        string OperationNId,
        string DesiredVersion,
        ServiceInitializationPolicy Policy,
        string TraceId,
        ServiceInitializationState? Inspection,
        ServiceInitializationPlan? Plan)
    {
        public RequestBody(ServiceInitializationContext context, ServiceInitializationState? inspection = null, ServiceInitializationPlan? plan = null)
            : this(context.TenantNId, context.OperationNId, context.DesiredVersion, context.Policy, context.TraceId, inspection, plan)
        {
        }
    }
}
