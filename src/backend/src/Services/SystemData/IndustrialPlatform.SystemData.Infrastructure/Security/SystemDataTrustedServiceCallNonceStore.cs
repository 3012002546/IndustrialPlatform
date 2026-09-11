using IndustrialPlatform.Infrastructure.Database;
using IndustrialPlatform.Security;
using SqlSugar;

namespace IndustrialPlatform.SystemData.Infrastructure.Security;

[SugarTable("system_trusted_service_call_nonce")]
internal sealed class SystemDataTrustedServiceCallNonceTable
{
    [SugarColumn(ColumnName = "issuer", IsPrimaryKey = true)] public string Issuer { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "nonce", IsPrimaryKey = true)] public string Nonce { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "expires_on")] public DateTimeOffset ExpiresOn { get; set; }
    [SugarColumn(ColumnName = "created_on")] public DateTimeOffset CreatedOn { get; set; }
}

/// <summary>SystemData-owned durable replay guard for internal PF05 calls.</summary>
public sealed class SystemDataTrustedServiceCallNonceStore : ITrustedServiceCallNonceStore
{
    private readonly SqlSugarDbContext _dbContext;

    public SystemDataTrustedServiceCallNonceStore(SqlSugarDbContext dbContext) => _dbContext = dbContext;

    public async Task<bool> TryRegisterAsync(string issuer, string nonce, DateTimeOffset expiresOn, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(issuer) || string.IsNullOrWhiteSpace(nonce))
            return false;
        var now = DateTimeOffset.UtcNow;
        var cleanupBefore = now.AddSeconds(-5);
        var sugar = _dbContext.SqlSugar;
        sugar.Ado.BeginTran();
        try
        {
            await sugar.Deleteable<SystemDataTrustedServiceCallNonceTable>()
                .Where(row => row.ExpiresOn <= cleanupBefore)
                .ExecuteCommandAsync(cancellationToken);
            await sugar.Insertable(new SystemDataTrustedServiceCallNonceTable
            {
                Issuer = issuer,
                Nonce = nonce,
                ExpiresOn = expiresOn,
                CreatedOn = now,
            }).ExecuteCommandAsync(cancellationToken);
            sugar.Ado.CommitTran();
            return true;
        }
        catch (Exception exception)
        {
            sugar.Ado.RollbackTran();
            if (IsDuplicate(exception))
                return false;
            throw new InvalidOperationException("SystemData trusted service nonce store is unavailable.", exception);
        }
    }

    private static bool IsDuplicate(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current.Message.Contains("unique", StringComparison.OrdinalIgnoreCase)
                || current.Message.Contains("duplicate", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}
