using IndustrialPlatform.Infrastructure.Database;
using IndustrialPlatform.Security;
using IndustrialPlatform.Identity.Infrastructure.Persistence.Entities;

namespace IndustrialPlatform.Identity.Infrastructure.Security;

/// <summary>
/// Persistent one-time nonce consumer for trusted service assertions. A duplicate is a
/// replay; database failures are allowed to fail closed by the caller.
/// </summary>
public sealed class TrustedServiceCallNonceStore : ITrustedServiceCallNonceStore
{
    private readonly SqlSugarDbContext _dbContext;

    public TrustedServiceCallNonceStore(SqlSugarDbContext dbContext) => _dbContext = dbContext;

    public async Task<bool> TryRegisterAsync(
        string issuer,
        string nonce,
        DateTimeOffset expiresOn,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(issuer) || string.IsNullOrWhiteSpace(nonce))
        {
            return false;
        }

        var now = DateTimeOffset.UtcNow;
        var cleanupBefore = now.AddSeconds(-5);
        var sugar = _dbContext.SqlSugar;
        sugar.Ado.BeginTran();
        try
        {
            await sugar.Deleteable<TrustedServiceCallNonceTable>()
                .Where(row => row.ExpiresOn <= cleanupBefore)
                .ExecuteCommandAsync(cancellationToken);
            await sugar.Insertable(new TrustedServiceCallNonceTable
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
            {
                return false;
            }

            throw new InvalidOperationException("PF05 trusted service call nonce store is unavailable.", exception);
        }
    }

    private static bool IsDuplicate(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current.Message.Contains("unique", StringComparison.OrdinalIgnoreCase)
                || current.Message.Contains("duplicate", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
