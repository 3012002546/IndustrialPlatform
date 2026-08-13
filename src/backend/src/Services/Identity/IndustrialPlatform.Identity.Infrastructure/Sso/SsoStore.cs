using IndustrialPlatform.Identity.Application.Management;
using IndustrialPlatform.Identity.Application.Sso;
using Identities = IndustrialPlatform.Identity.Domain.Identities;
using IndustrialPlatform.Identity.Domain.Sso;
using IndustrialPlatform.Identity.Domain.Users;
using IndustrialPlatform.Identity.Infrastructure.Outbox;
using IndustrialPlatform.Identity.Infrastructure.Persistence;
using IndustrialPlatform.Identity.Infrastructure.Persistence.Entities;
using IndustrialPlatform.Identity.Infrastructure.Security;
using IndustrialPlatform.Infrastructure.Database;
using IndustrialPlatform.SharedKernel.Exceptions;
using SqlSugar;

namespace IndustrialPlatform.Identity.Infrastructure.Sso;

/// <summary>
/// SSO 持久化端口实现(§26):Provider/外部账号/Client/端点/浏览器会话的查询与写入。
/// 浏览器会话句柄只以 SHA-256 哈希入库,明文仅在内存与 Cookie 流转;写操作按双版本乐观并发。
/// </summary>
public sealed class SsoStore : ISsoStore
{
    private readonly SqlSugarDbContext _dbContext;

    /// <summary>初始化 SSO 存储。</summary>
    public SsoStore(SqlSugarDbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        _dbContext = dbContext;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<IdentitySsoProvider>> ListEnabledProvidersAsync(string tenantNId, CancellationToken cancellationToken)
    {
        var rows = await _dbContext.SqlSugar.Queryable<SsoProviderTable>()
            .Where(t => t.TenantNId == tenantNId && t.Enabled && !t.IsDeleted)
            .OrderBy(t => t.CreatedOn, OrderByType.Desc)
            .ToListAsync(cancellationToken);
        return rows.Select(TableMapper.ToSsoProvider).ToList();
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<IdentitySsoProvider>> ListProvidersAsync(string tenantNId, CancellationToken cancellationToken)
    {
        var rows = await _dbContext.SqlSugar.Queryable<SsoProviderTable>()
            .Where(t => t.TenantNId == tenantNId && !t.IsDeleted)
            .OrderBy(t => t.CreatedOn, OrderByType.Desc)
            .ToListAsync(cancellationToken);
        return rows.Select(TableMapper.ToSsoProvider).ToList();
    }

    /// <inheritdoc/>
    public async Task<IdentitySsoProvider?> FindProviderByNIdAsync(string tenantNId, string providerNId, CancellationToken cancellationToken)
    {
        var row = await QueryProviderRowAsync(tenantNId, providerNId, cancellationToken);
        return row is null ? null : TableMapper.ToSsoProvider(row);
    }

    /// <inheritdoc/>
    public async Task<IdentitySsoProvider?> FindProviderByClientIdAsync(string tenantNId, string clientIdOrEntityId, CancellationToken cancellationToken)
    {
        var row = await _dbContext.SqlSugar.Queryable<SsoProviderTable>()
            .Where(t => t.TenantNId == tenantNId && t.ClientIdOrEntityId == clientIdOrEntityId && !t.IsDeleted)
            .FirstAsync(cancellationToken);
        return row is null ? null : TableMapper.ToSsoProvider(row);
    }

    /// <inheritdoc/>
    public async Task AddProviderAsync(IdentitySsoProvider provider, CancellationToken cancellationToken)
        => await _dbContext.SqlSugar.Insertable(TableMapper.ToTable(provider)).ExecuteCommandAsync(cancellationToken);

    /// <inheritdoc/>
    public Task UpdateProviderAsync(IdentitySsoProvider provider, long expectedOptimisticVersion, Guid expectedConcurrencyVersion, CancellationToken cancellationToken)
        => UpdateRowAsync(TableMapper.ToTable(provider), expectedOptimisticVersion, expectedConcurrencyVersion, cancellationToken);

    /// <inheritdoc/>
    public async Task<IdentityExternalAccount?> FindExternalAccountAsync(Guid providerId, string externalSubject, CancellationToken cancellationToken)
    {
        var row = await _dbContext.SqlSugar.Queryable<SsoExternalAccountTable>()
            .Where(t => t.SsoProviderId == providerId && t.ExternalSubject == externalSubject && !t.IsDeleted)
            .FirstAsync(cancellationToken);
        return row is null ? null : TableMapper.ToSsoExternalAccount(row);
    }

    /// <inheritdoc/>
    public async Task<IdentityExternalAccount?> FindExternalAccountByUserIdAsync(Guid providerId, Guid userId, CancellationToken cancellationToken)
    {
        var row = await _dbContext.SqlSugar.Queryable<SsoExternalAccountTable>()
            .Where(t => t.SsoProviderId == providerId && t.UserId == userId && !t.IsDeleted)
            .FirstAsync(cancellationToken);
        return row is null ? null : TableMapper.ToSsoExternalAccount(row);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<StoredSsoAccount>> ListExternalAccountsAsync(Guid providerId, string providerNId, CancellationToken cancellationToken)
    {
        // 多表 OrderBy 存在 SqlSugar 别名限制,统一取回后内存排序(与 ManagementStore 同约定)。
        var rows = await _dbContext.SqlSugar.Queryable<SsoExternalAccountTable>()
            .Where(t => t.SsoProviderId == providerId && !t.IsDeleted && !t.SsoProviderIsDeleted)
            .InnerJoin<UserTable>((a, u) => a.UserId == u.Id && !u.IsDeleted)
            .Select((a, u) => new
            {
                a.Id,
                AccountNId = a.NId,
                a.CreatedOn,
                UserNId = u.NId,
                u.LoginName,
                u.Name,
                a.ExternalName,
                a.ExternalEmail,
                a.LastLoginOn,
                a.OptimisticVersion,
                a.ConcurrencyVersion,
            })
            .ToListAsync(cancellationToken);

        return rows
            .OrderBy(r => r.CreatedOn)
            .Select(r => new StoredSsoAccount(
                r.Id,
                r.AccountNId,
                providerNId,
                r.UserNId,
                r.LoginName,
                r.Name,
                r.ExternalName,
                r.ExternalEmail,
                r.LastLoginOn,
                r.OptimisticVersion,
                r.ConcurrencyVersion))
            .ToList();
    }

    /// <inheritdoc/>
    public async Task AddExternalAccountAsync(IdentityExternalAccount account, CancellationToken cancellationToken)
        => await _dbContext.SqlSugar.Insertable(TableMapper.ToTable(account)).ExecuteCommandAsync(cancellationToken);

    /// <inheritdoc/>
    public Task UpdateExternalAccountAsync(IdentityExternalAccount account, long expectedOptimisticVersion, Guid expectedConcurrencyVersion, CancellationToken cancellationToken)
        => UpdateRowAsync(TableMapper.ToTable(account), expectedOptimisticVersion, expectedConcurrencyVersion, cancellationToken);

    /// <inheritdoc/>
    public async Task<IdentitySsoClient?> FindClientByNIdAsync(string tenantNId, string clientNId, CancellationToken cancellationToken)
    {
        var row = await QueryClientRowAsync(tenantNId, clientNId, cancellationToken);
        return row is null ? null : await LoadClientAsync(row, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<IdentitySsoClient?> FindClientByOAuthClientIdAsync(string tenantNId, string oauthClientId, CancellationToken cancellationToken)
    {
        var row = await _dbContext.SqlSugar.Queryable<SsoClientTable>()
            .Where(t => t.TenantNId == tenantNId && t.OAuthClientId == oauthClientId && !t.IsDeleted)
            .FirstAsync(cancellationToken);
        return row is null ? null : await LoadClientAsync(row, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<IdentitySsoClient>> ListClientsAsync(string tenantNId, CancellationToken cancellationToken)
    {
        var rows = await _dbContext.SqlSugar.Queryable<SsoClientTable>()
            .Where(t => t.TenantNId == tenantNId && !t.IsDeleted)
            .OrderBy(t => t.CreatedOn, OrderByType.Desc)
            .ToListAsync(cancellationToken);

        var clients = new List<IdentitySsoClient>(rows.Count);
        foreach (var row in rows)
        {
            clients.Add(await LoadClientAsync(row, cancellationToken));
        }

        return clients;
    }

    /// <inheritdoc/>
    public async Task AddClientAsync(IdentitySsoClient client, CancellationToken cancellationToken)
    {
        var sugar = _dbContext.SqlSugar;
        sugar.Ado.BeginTran();
        try
        {
            await sugar.Insertable(TableMapper.ToTable(client)).ExecuteCommandAsync(cancellationToken);
            foreach (var endpoint in client.Endpoints.Where(e => !e.IsDeleted))
            {
                await sugar.Insertable(TableMapper.ToTable(endpoint)).ExecuteCommandAsync(cancellationToken);
            }

            sugar.Ado.CommitTran();
        }
        catch
        {
            sugar.Ado.RollbackTran();
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task UpdateClientAsync(IdentitySsoClient client, long expectedOptimisticVersion, Guid expectedConcurrencyVersion, CancellationToken cancellationToken)
    {
        var sugar = _dbContext.SqlSugar;
        sugar.Ado.BeginTran();
        try
        {
            await UpdateRowAsync(TableMapper.ToTable(client), expectedOptimisticVersion, expectedConcurrencyVersion, cancellationToken);
            await SyncEndpointsAsync(sugar, client, cancellationToken);
            sugar.Ado.CommitTran();
        }
        catch
        {
            sugar.Ado.RollbackTran();
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<IdentitySsoBrowserSession?> FindBrowserSessionByHandleAsync(string sessionHandle, CancellationToken cancellationToken)
    {
        var hash = Hashing.Sha256Hex(sessionHandle);
        var row = await _dbContext.SqlSugar.Queryable<SsoBrowserSessionTable>()
            .Where(t => t.SessionHandleHash == hash && !t.IsDeleted)
            .FirstAsync(cancellationToken);
        return row is null ? null : TableMapper.ToSsoBrowserSession(row);
    }

    /// <inheritdoc/>
    public async Task<SsoBrowserSessionCreated> CreateBrowserSessionAsync(string tenantNId, string providerNId, Guid userId, bool userIsDeleted, int authVersion, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var handle = SecureTokens.Base64UrlToken(32);
        var session = IdentitySsoBrowserSession.Create(
            tenantNId,
            SecureTokens.NId("SES-"),
            providerNId,
            userId,
            userIsDeleted,
            Hashing.Sha256Hex(handle),
            authVersion,
            now);
        await _dbContext.SqlSugar.Insertable(TableMapper.ToTable(session)).ExecuteCommandAsync(cancellationToken);
        return new SsoBrowserSessionCreated(session, handle);
    }

    /// <inheritdoc/>
    public Task UpdateBrowserSessionAsync(IdentitySsoBrowserSession session, long expectedOptimisticVersion, Guid expectedConcurrencyVersion, CancellationToken cancellationToken)
        => UpdateRowAsync(TableMapper.ToTable(session), expectedOptimisticVersion, expectedConcurrencyVersion, cancellationToken);

    /// <inheritdoc/>
    public async Task AddJitUserAsync(
        User user,
        IdentityExternalAccount externalAccount,
        IReadOnlyCollection<OutboxEnvelope> outboxEvents,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(externalAccount);

        var sugar = _dbContext.SqlSugar;
        sugar.Ado.BeginTran();
        try
        {
            await sugar.Insertable(TableMapper.ToTable(user)).ExecuteCommandAsync(cancellationToken);
            foreach (var relation in user.UserRoles.Where(r => !r.IsDeleted))
            {
                await sugar.Insertable(TableMapper.ToTable(relation)).ExecuteCommandAsync(cancellationToken);
            }

            await sugar.Insertable(TableMapper.ToTable(externalAccount)).ExecuteCommandAsync(cancellationToken);
            await OutboxRows.InsertAsync(sugar, outboxEvents, cancellationToken);

            sugar.Ado.CommitTran();
        }
        catch
        {
            sugar.Ado.RollbackTran();
            throw;
        }
    }

    /// <summary>载入 Client 聚合(含活动端点,双重过滤)。</summary>
    private async Task<IdentitySsoClient> LoadClientAsync(SsoClientTable row, CancellationToken cancellationToken)
    {
        var endpointRows = await _dbContext.SqlSugar.Queryable<SsoClientEndpointTable>()
            .Where(t => t.SsoClientId == row.Id && !t.IsDeleted && !t.SsoClientIsDeleted)
            .OrderBy(t => t.Id)
            .ToListAsync(cancellationToken);
        return TableMapper.ToSsoClient(row, endpointRows.Select(TableMapper.ToSsoClientEndpoint).ToList());
    }

    /// <summary>按 NId 查询 Provider 行(含软删除过滤)。</summary>
    private async Task<SsoProviderTable?> QueryProviderRowAsync(string tenantNId, string providerNId, CancellationToken cancellationToken)
    {
        var normalized = Identities.NId.Create(providerNId).Normalized;
        return await _dbContext.SqlSugar.Queryable<SsoProviderTable>()
            .Where(t => t.TenantNId == tenantNId && t.NormalizedNId == normalized && !t.IsDeleted)
            .FirstAsync(cancellationToken);
    }

    private async Task<SsoClientTable?> QueryClientRowAsync(string tenantNId, string clientNId, CancellationToken cancellationToken)
    {
        var normalized = Identities.NId.Create(clientNId).Normalized;
        return await _dbContext.SqlSugar.Queryable<SsoClientTable>()
            .Where(t => t.TenantNId == tenantNId && t.NormalizedNId == normalized && !t.IsDeleted)
            .FirstAsync(cancellationToken);
    }

    /// <summary>按双版本原子更新任意 SSO 生命周期行(§6);影响行数非 1 抛并发异常。</summary>
    private async Task UpdateRowAsync<T>(
        T row,
        long expectedOptimisticVersion,
        Guid expectedConcurrencyVersion,
        CancellationToken cancellationToken)
        where T : class, ISsoLifecycleRow, new()
    {
        var affected = await _dbContext.SqlSugar.Updateable(row)
            .Where(t => t.Id == row.Id
                && !t.IsDeleted
                && t.OptimisticVersion == expectedOptimisticVersion
                && t.ConcurrencyVersion == expectedConcurrencyVersion)
            .ExecuteCommandAsync(cancellationToken);
        if (affected != 1)
        {
            throw new ConcurrencyException($"SSO 实体 {typeof(T).Name} 更新失败:并发版本不匹配或记录不存在。");
        }
    }

    /// <summary>Client 端点 diff:新增插入、移除软删、启停状态同步。</summary>
    private static async Task SyncEndpointsAsync(ISqlSugarClient sugar, IdentitySsoClient client, CancellationToken cancellationToken)
    {
        var activeRows = await sugar.Queryable<SsoClientEndpointTable>()
            .Where(t => t.SsoClientId == client.Id && !t.IsDeleted && !t.SsoClientIsDeleted)
            .ToListAsync(cancellationToken);
        var existingById = activeRows.ToDictionary(r => r.Id);
        var aggregateActive = client.Endpoints.Where(e => !e.IsDeleted).ToList();
        var aggregateById = aggregateActive.ToDictionary(e => e.Id);

        foreach (var endpoint in aggregateActive.Where(e => !existingById.ContainsKey(e.Id)))
        {
            await sugar.Insertable(TableMapper.ToTable(endpoint)).ExecuteCommandAsync(cancellationToken);
        }

        foreach (var row in activeRows.Where(r => !aggregateById.ContainsKey(r.Id)))
        {
            await sugar.Updateable<SsoClientEndpointTable>()
                .SetColumns(t => new SsoClientEndpointTable { IsDeleted = true })
                .Where(t => t.Id == row.Id && !t.IsDeleted)
                .ExecuteCommandAsync(cancellationToken);
        }

        foreach (var endpoint in aggregateActive.Where(e => existingById.ContainsKey(e.Id)))
        {
            var row = existingById[endpoint.Id];
            if (row.Enabled != endpoint.Enabled)
            {
                await sugar.Updateable<SsoClientEndpointTable>()
                    .SetColumns(t => new SsoClientEndpointTable { Enabled = endpoint.Enabled })
                    .Where(t => t.Id == endpoint.Id && !t.IsDeleted)
                    .ExecuteCommandAsync(cancellationToken);
            }
        }
    }
}
