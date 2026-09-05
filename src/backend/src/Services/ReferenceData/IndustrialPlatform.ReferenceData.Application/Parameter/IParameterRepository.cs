using IndustrialPlatform.ReferenceData.Contracts.Parameter;
using IndustrialPlatform.ReferenceData.Domain.Parameter;

namespace IndustrialPlatform.ReferenceData.Application.Parameter;

public sealed record ParameterQuery(int PageIndex = 1, int PageSize = 20, string? Keyword = null, string? Status = null, string? ScopeType = null,
    string? SortField = null, bool Descending = false);
public interface IParameterRepository
{
    Task<(IReadOnlyList<ConfigurationDomainSummaryDto> Items, long Total)> SearchAsync(string tenantNId, ParameterQuery query, CancellationToken cancellationToken);
    Task<ConfigurationAppDomain?> GetAsync(string tenantNId, Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<ConfigurationAppDomain>> GetActiveScopesAsync(string tenantNId, string nId, CancellationToken cancellationToken);
    Task CreateAsync(ConfigurationAppDomain domain, ConfigurationHistoryDto history, CancellationToken cancellationToken);
    Task SaveAsync(ConfigurationAppDomain domain, long expectedVersion, Guid expectedToken, ConfigurationHistoryDto history, CancellationToken cancellationToken);
    Task<(IReadOnlyList<ConfigurationHistoryDto> Items, long Total)> HistoryAsync(Guid domainId, Guid? keyId, int pageIndex, int pageSize, CancellationToken cancellationToken);
}
