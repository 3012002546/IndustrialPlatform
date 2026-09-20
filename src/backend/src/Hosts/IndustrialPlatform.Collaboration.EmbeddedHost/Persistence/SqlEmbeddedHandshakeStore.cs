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

namespace IndustrialPlatform.Collaboration.EmbeddedHost.Persistence;

/// <summary>数据库持久化握手挑战、断言使用记录、浏览器版本和会话，避免进程重启丢失安全状态。</summary>
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
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new EmbeddedPersistenceException("嵌入会话持久化不可用。", exception);
        }
    }

    public async Task<EmbeddedStoredSession?> TouchSessionAsync(string sessionTokenHash, string browserBindingHash, DateTimeOffset now, DateTimeOffset expiresOn, CancellationToken cancellationToken)
    {
        await EnsureSchemaAsync(cancellationToken);
        try
        {
            var affected = await dbContext.SqlSugar.Updateable<EmbeddedSessionTable>()
                .SetColumns(item => new EmbeddedSessionTable { ExpiresOn = expiresOn })
                .Where(item => item.SessionTokenHash == sessionTokenHash
                    && item.BrowserBindingHash == browserBindingHash
                    && item.RevokedOn == null
                    && item.ExpiresOn > now)
                .ExecuteCommandAsync(cancellationToken);
            if (affected != 1)
                return null;
            return await GetSessionAsync(sessionTokenHash, browserBindingHash, cancellationToken);
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
                AccountNId = identity.AccountNId,
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
    private static EmbeddedStoredSession ToRecord(EmbeddedSessionTable item) => new(item.SessionTokenHash, item.BrowserBindingHash, new EmbeddedIdentity(item.TenantNId, item.UserNId, item.SessionNId, item.SecurityVersion) { AccountNId = item.AccountNId, SourceNId = item.SourceNId, ExternalTenantNId = item.ExternalTenantNId, ExternalSubject = item.ExternalSubject, DisplayName = item.DisplayName }, item.Epoch, Utc(item.ExpiresOn), item.RevokedOn is null ? null : Utc(item.RevokedOn.Value));
    private static DateTimeOffset Utc(DateTimeOffset value) => new(value.DateTime, TimeSpan.Zero);
}
