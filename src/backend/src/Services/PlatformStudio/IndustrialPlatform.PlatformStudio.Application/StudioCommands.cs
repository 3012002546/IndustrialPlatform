using System.Text.Json;
using System.Text.Json.Nodes;
using IndustrialPlatform.PlatformStudio.Domain;

namespace IndustrialPlatform.PlatformStudio.Application;

/// <summary>本地事务操作的持久幂等收口；外部副作用必须使用动作专用的Pending/Unknown流程。</summary>
public sealed class StudioCommands(IStudioResourceStore store, TimeProvider clock)
{
    public static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web);
    public async Task<T> ExecuteAsync<T>(StudioPrincipal principal, string kind, string resourceNId, string key, object request, Func<Task<T>> action, CancellationToken cancellationToken)
    {
        ValidateKey(key);
        var requestHash = StudioJson.Hash(StudioJson.Canonicalize(JsonSerializer.SerializeToNode(request, JsonOptions)));
        return await store.TransactionAsync(async () =>
        {
            var receipt = await store.FindReceiptAsync(principal.TenantNId, kind, key, cancellationToken);
            if (receipt is not null)
            {
                if (receipt.ActorUserNId != principal.SubjectNId || receipt.RequestHash != requestHash || receipt.ResourceNId != resourceNId) throw new StudioException("STUDIO_IDEMPOTENCY_CONFLICT", "幂等键已用于不同请求。", 409);
                if (receipt.Status == "Succeeded" && receipt.ResponseJson is not null) return JsonSerializer.Deserialize<T>(receipt.ResponseJson, JsonOptions)!;
                throw new StudioException("STUDIO_OPERATION_UNKNOWN", "该操作尚未确认完成，不能重复执行。", 409);
            }
            receipt = new() { NId = "receipt-" + Guid.NewGuid().ToString("N"), TenantNId = principal.TenantNId, CreatedOn = clock.GetUtcNow(), OperationKey = key, OperationKind = kind, ResourceNId = resourceNId, ActorUserNId = principal.SubjectNId, RequestHash = requestHash };
            await store.InsertReceiptAsync(receipt, cancellationToken);
            var result = await action();
            receipt.Status = "Succeeded";
            receipt.CompletedOn = clock.GetUtcNow();
            receipt.ResponseJson = JsonSerializer.Serialize(result, JsonOptions);
            if (System.Text.Encoding.UTF8.GetByteCount(receipt.ResponseJson) > 4 * 1024 * 1024) throw new StudioException("STUDIO_LIMIT", "幂等响应超过4MiB预算。", 429);
            await store.UpdateReceiptAsync(receipt, cancellationToken);
            return result;
        }, cancellationToken);
    }
    public static void ValidateKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > 128 || key.Any(character => character is < (char)33 or > (char)126)) throw new StudioException("STUDIO_VALIDATION", "写操作必须提供1～128位非空ASCII Idempotency-Key。", path: "Idempotency-Key");
    }
}
