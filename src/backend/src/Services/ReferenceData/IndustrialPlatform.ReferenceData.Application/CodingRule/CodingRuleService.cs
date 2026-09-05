using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using IndustrialPlatform.ReferenceData.Application.Authorization;
using IndustrialPlatform.ReferenceData.Application.Caching;
using IndustrialPlatform.ReferenceData.Contracts;
using IndustrialPlatform.ReferenceData.Contracts.CodingRule;
using IndustrialPlatform.ReferenceData.Domain.CodingRule;
using IndustrialPlatform.ReferenceData.Domain.Common;

namespace IndustrialPlatform.ReferenceData.Application.CodingRule;

public sealed class CodingRuleService
{
    private const long PreviewSequence = 1;
    private readonly ICodingRuleRepository repository;
    private readonly TimeProvider timeProvider;
    private readonly IReferenceDataCache cache;

    public CodingRuleService(ICodingRuleRepository repository) :
        this(repository, TimeProvider.System, NullReferenceDataCache.Instance)
    { }

    public CodingRuleService(ICodingRuleRepository repository, TimeProvider timeProvider) :
        this(repository, timeProvider, NullReferenceDataCache.Instance)
    { }

    public CodingRuleService(ICodingRuleRepository repository, IReferenceDataCache cache) :
        this(repository, TimeProvider.System, cache)
    { }

    public CodingRuleService(ICodingRuleRepository repository, TimeProvider timeProvider,
        IReferenceDataCache cache)
    {
        this.repository = repository;
        this.timeProvider = timeProvider;
        this.cache = cache;
    }

    public async Task<(IReadOnlyList<CodingRuleSummaryDto> Items, long Total)> SearchAsync(
        ReferenceDataActor actor, CodingRuleQuery query, CancellationToken cancellationToken)
    {
        ValidatePage(query.PageIndex, query.PageSize, query.Keyword);
        if (query.ScopeType is not null) _ = ParseScope(query.ScopeType);
        if (query.Status is not null
            && !Enum.GetNames<PublicationStatus>().Contains(query.Status, StringComparer.Ordinal))
            throw new ReferenceDataException("REF-VALIDATION-FAILED", field: "status");
        return await repository.SearchAsync(actor.TenantNId, query, cancellationToken);
    }

    public async Task<CodingRuleDetailDto> GetAsync(
        ReferenceDataActor actor, Guid id, CancellationToken cancellationToken) =>
        MapDetail(await LoadAsync(actor, id, cancellationToken));

    public async Task<CodingRuleDetailDto> CreateAsync(
        ReferenceDataActor actor, CreateCodingRuleRequest request, CancellationToken cancellationToken)
    {
        var scope = ParseScope(request.ScopeType);
        if (scope == ReferenceScopeType.Platform && !actor.CanManagePlatform)
            throw new ReferenceDataException("ID_PERMISSION_DENIED", 403);
        var rule = new CodingRuleDefinition(request.NId, request.Name, scope,
            scope == ReferenceScopeType.Platform ? null : actor.TenantNId, null);
        rule.Update(request.Name, request.TargetEntityNId, request.Template, ParseResetPolicy(request.ResetPolicy));
        await repository.CreateAsync(rule, null, cancellationToken);
        return MapDetail(rule);
    }

    public async Task<CodingRuleDetailDto> UpdateAsync(
        ReferenceDataActor actor, Guid id, UpdateCodingRuleRequest request, CancellationToken cancellationToken)
    {
        var rule = await LoadWritableAsync(actor, id, request.ExpectedOptimisticVersion,
            request.ExpectedConcurrencyVersion, cancellationToken);
        rule.Update(request.Name, request.TargetEntityNId, request.Template, ParseResetPolicy(request.ResetPolicy));
        await repository.SaveAsync([new(rule, request.ExpectedOptimisticVersion,
            request.ExpectedConcurrencyVersion)], cancellationToken);
        await cache.InvalidateAsync(ReferenceDataCacheKeys.CodingRules(rule.NId), CancellationToken.None);
        return MapDetail(rule);
    }

    public async Task<CodingRuleDetailDto> CloneAsync(
        ReferenceDataActor actor, Guid id, PublishOrDisableRequest request, CancellationToken cancellationToken)
    {
        ValidateReason(request.ChangeReason, required: false);
        var source = await LoadWritableAsync(actor, id, request.ExpectedOptimisticVersion,
            request.ExpectedConcurrencyVersion, cancellationToken);
        var clone = source.Clone(await repository.GetNextRevisionAsync(source, cancellationToken));
        await repository.CreateAsync(clone, new(source, request.ExpectedOptimisticVersion,
            request.ExpectedConcurrencyVersion), cancellationToken);
        return MapDetail(clone);
    }

    public async Task<CodingRuleDetailDto> PublishAsync(
        ReferenceDataActor actor, Guid id, PublishOrDisableRequest request, CancellationToken cancellationToken)
    {
        ValidateReason(request.ChangeReason, required: false);
        var rule = await LoadWritableAsync(actor, id, request.ExpectedOptimisticVersion,
            request.ExpectedConcurrencyVersion, cancellationToken);
        var changes = new List<CodingRuleChange>();
        var previous = await repository.GetPublishedInScopeAsync(rule, cancellationToken);
        if (previous is not null && previous.Id != rule.Id)
        {
            var version = previous.OptimisticVersion;
            var token = previous.ConcurrencyVersion;
            previous.Supersede();
            changes.Add(new(previous, version, token));
        }
        rule.Publish(actor.UserNId);
        changes.Add(new(rule, request.ExpectedOptimisticVersion, request.ExpectedConcurrencyVersion));
        await repository.SaveAsync(changes, cancellationToken);
        await cache.InvalidateAsync(ReferenceDataCacheKeys.CodingRules(rule.NId), CancellationToken.None);
        return MapDetail(rule);
    }

    public async Task<CodingRuleDetailDto> DisableAsync(
        ReferenceDataActor actor, Guid id, PublishOrDisableRequest request, CancellationToken cancellationToken)
    {
        ValidateReason(request.ChangeReason, required: true);
        var rule = await LoadWritableAsync(actor, id, request.ExpectedOptimisticVersion,
            request.ExpectedConcurrencyVersion, cancellationToken);
        rule.Disable();
        await repository.SaveAsync([new(rule, request.ExpectedOptimisticVersion,
            request.ExpectedConcurrencyVersion)], cancellationToken);
        await cache.InvalidateAsync(ReferenceDataCacheKeys.CodingRules(rule.NId), CancellationToken.None);
        return MapDetail(rule);
    }

    public async Task<CodePreviewDto> PreviewByIdAsync(
        ReferenceDataActor actor, Guid id, PreviewCodeRequest request,
        CancellationToken cancellationToken)
    {
        var rule = await LoadAsync(actor, id, cancellationToken);
        return Preview(rule, actor.TenantNId, request.FactoryId, timeProvider.GetUtcNow());
    }

    public async Task<CodePreviewDto> PreviewAsync(
        ReferenceDataActor actor, string nId, PreviewCodeRequest request,
        CancellationToken cancellationToken)
    {
        var rule = await LoadPublishedRevisionAsync(actor, nId, request, cancellationToken);
        return Preview(rule, actor.TenantNId, request.FactoryId, timeProvider.GetUtcNow());
    }

    public async Task<GeneratedCodeDto> GenerateAsync(
        ReferenceDataActor actor, string nId, GenerateCodeRequest request, string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        ValidateIdempotencyKey(idempotencyKey);
        var normalizedNId = NormalizeNId(nId);
        var source = ResolveSource(actor, request.SourceScope, request.SourceTenantNId);
        if (request.RuleRevision < 1) throw NotFound();
        var rule = await repository.GetRevisionAsync(actor.TenantNId, normalizedNId, source,
            request.RuleRevision, cancellationToken) ?? throw NotFound();
        var now = timeProvider.GetUtcNow();
        var context = rule.ContextValue(actor.TenantNId, request.FactoryId);
        var generated = await repository.GenerateAsync(new(rule, actor.TenantNId, request.FactoryId,
            Hash(idempotencyKey), Hash(actor.TenantNId, source, request.SourceTenantNId, normalizedNId,
                request.RuleRevision.ToString(CultureInfo.InvariantCulture), request.FactoryId),
            Hash(context), now, now.AddDays(7)), cancellationToken);
        return new(rule.NId, rule.Revision, generated.Code, generated.Sequence, generated.PeriodKey,
            generated.GeneratedOn);
    }

    private async Task<CodingRuleDefinition> LoadPublishedRevisionAsync(
        ReferenceDataActor actor, string nId, PreviewCodeRequest request,
        CancellationToken cancellationToken)
    {
        if (request.RuleRevision is not >= 1) throw NotFound();
        var source = ResolveSource(actor, request.SourceScope, request.SourceTenantNId);
        var normalizedNId = NormalizeNId(nId);
        var key = ReferenceDataCacheKeys.CodingRule(
            ReferenceDataCacheKeys.SourceTenantKey(source, request.SourceTenantNId), normalizedNId);
        var cached = await cache.GetAsync<CodingRuleState>(key, cancellationToken);
        if (cached is not null && cached.Revision == request.RuleRevision.Value
            && cached.PublishedOn is not null
            && cached.ScopeType.ToString() == source
            && string.Equals(cached.TenantNId, request.SourceTenantNId, StringComparison.Ordinal))
            return CodingRuleDefinition.Restore(cached);

        var generation = await cache.GetGenerationAsync(key.GenerationKey, cancellationToken);
        var rule = await repository.GetRevisionAsync(actor.TenantNId, normalizedNId, source,
            request.RuleRevision.Value, cancellationToken) ?? throw NotFound();
        await cache.SetIfGenerationAsync(key, State(rule), ReferenceDataCacheTtl.CodingRule,
            generation, cancellationToken);
        return rule;
    }

    private async Task<CodingRuleDefinition> LoadAsync(
        ReferenceDataActor actor, Guid id, CancellationToken cancellationToken) =>
        await repository.GetAsync(actor.TenantNId, id, cancellationToken) ?? throw NotFound();

    private async Task<CodingRuleDefinition> LoadWritableAsync(
        ReferenceDataActor actor, Guid id, long version, Guid token, CancellationToken cancellationToken)
    {
        var rule = await LoadAsync(actor, id, cancellationToken);
        if (rule.ScopeType == ReferenceScopeType.Platform && !actor.CanManagePlatform)
            throw new ReferenceDataException("ID_PERMISSION_DENIED", 403);
        rule.CheckVersion(version, token);
        return rule;
    }

    private static CodePreviewDto Preview(
        CodingRuleDefinition rule, string tenantNId, string? factoryId, DateTimeOffset at)
    {
        var rendered = rule.Render(at, tenantNId, factoryId, PreviewSequence);
        return new(rule.NId, rule.Revision, rule.ScopeType.ToString(), rule.TenantNId, rendered.Code,
            PreviewSequence, rendered.PeriodKey, at, false);
    }

    private static CodingResetPolicy ParseResetPolicy(string value)
    {
        if (!Enum.TryParse<CodingResetPolicy>(value, ignoreCase: false, out var policy)
            || !Enum.IsDefined(policy))
            throw new ReferenceDataException("REF-CODING-TEMPLATE-INVALID", 422, "resetPolicy");
        return policy;
    }

    private static ReferenceScopeType ParseScope(string value)
    {
        if (!Enum.TryParse<ReferenceScopeType>(value, ignoreCase: false, out var scope) || !Enum.IsDefined(scope))
            throw new ReferenceDataException("REF-SCOPE-INVALID");
        if (scope == ReferenceScopeType.Factory)
            throw new ReferenceDataException("REF-SCOPE-FACTORY-NOT-READY", 409);
        return scope;
    }

    private static string ResolveSource(
        ReferenceDataActor actor, string? sourceScope, string? sourceTenantNId)
    {
        if (sourceScope is null) throw new ReferenceDataException("REF-SCOPE-INVALID");
        var scope = ParseScope(sourceScope);
        if (scope == ReferenceScopeType.Platform)
        {
            if (sourceTenantNId is not null) throw new ReferenceDataException("REF-SCOPE-INVALID");
            return nameof(ReferenceScopeType.Platform);
        }
        if (string.IsNullOrWhiteSpace(sourceTenantNId))
            throw new ReferenceDataException("REF-SCOPE-INVALID");
        if (!string.Equals(actor.TenantNId, sourceTenantNId, StringComparison.Ordinal)) throw NotFound();
        return nameof(ReferenceScopeType.Tenant);
    }

    private static string NormalizeNId(string nId)
    {
        try { return ReferenceValidation.NId(nId); }
        catch (ReferenceDataException) { throw NotFound(); }
    }

    private static void ValidatePage(int pageIndex, int pageSize, string? keyword)
    {
        if (pageIndex < 1 || pageSize is < 1 or > 100 || keyword?.Length > 200)
            throw new ReferenceDataException("REF-VALIDATION-FAILED");
    }

    private static void ValidateReason(string? reason, bool required)
    {
        if ((required && string.IsNullOrWhiteSpace(reason)) || reason?.Length > 1000)
            throw new ReferenceDataException("REF-VALIDATION-FAILED", field: "changeReason");
    }

    private static void ValidateIdempotencyKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ReferenceDataException("REF-IDEMPOTENCY-REQUIRED", 400, "Idempotency-Key");
        if (key.Length > 200 || key.Any(char.IsControl))
            throw new ReferenceDataException("REF-VALIDATION-FAILED", field: "Idempotency-Key");
    }

    private static string Hash(params string?[] values)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> length = stackalloc byte[sizeof(int)];
        foreach (var value in values)
        {
            if (value is null)
            {
                BinaryPrimitives.WriteInt32BigEndian(length, -1);
                hash.AppendData(length);
                continue;
            }
            var bytes = Encoding.UTF8.GetBytes(value);
            BinaryPrimitives.WriteInt32BigEndian(length, bytes.Length);
            hash.AppendData(length);
            hash.AppendData(bytes);
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    public static CodingRuleSummaryDto ToSummary(CodingRuleDefinition rule) => new(
        rule.Id, rule.NId, rule.Name, rule.TargetEntityNId, rule.Template, rule.ResetPolicy.ToString(),
        rule.ScopeType.ToString(), rule.TenantNId, rule.Revision, rule.Status.ToString(), rule.SourceRevision,
        rule.OptimisticVersion, rule.ConcurrencyVersion, rule.LastUpdatedOn, rule.PublishedOn, rule.PublishedBy,
        rule.IsFrozen, rule.IsLocked);

    private static CodingRuleState State(CodingRuleDefinition rule) => new(
        rule.Id, rule.NId, rule.Name, rule.TargetEntityNId, rule.Template, rule.ResetPolicy, rule.ScopeType,
        rule.TenantNId, rule.Revision, rule.Status, rule.SourceRevision, rule.PublishedOn, rule.PublishedBy,
        rule.IsFrozen, rule.IsLocked, rule.IsDeleted, rule.CreatedOn, rule.LastUpdatedOn,
        rule.OptimisticVersion, rule.ConcurrencyVersion);

    private static CodingRuleDetailDto MapDetail(CodingRuleDefinition rule) => new(
        rule.Id, rule.NId, rule.Name, rule.TargetEntityNId, rule.Template, rule.ResetPolicy.ToString(),
        rule.ScopeType.ToString(), rule.TenantNId, rule.Revision, rule.Status.ToString(), rule.SourceRevision,
        rule.OptimisticVersion, rule.ConcurrencyVersion, rule.CreatedOn, rule.LastUpdatedOn, rule.PublishedOn,
        rule.PublishedBy, rule.IsFrozen, rule.IsLocked);

    private static ReferenceDataException NotFound() => new("REF-CODING-RULE-NOT-FOUND", 404);
}
