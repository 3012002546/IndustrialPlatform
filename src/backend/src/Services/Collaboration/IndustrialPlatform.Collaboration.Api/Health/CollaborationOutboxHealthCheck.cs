using IndustrialPlatform.Collaboration.Application;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace IndustrialPlatform.Collaboration.Api.Health;

/// <summary>Checks the Collaboration outbox table instead of reporting registration only.</summary>
public sealed class CollaborationOutboxHealthCheck : IHealthCheck
{
    private readonly ICollaborationRepository _repository;

    public CollaborationOutboxHealthCheck(ICollaborationRepository repository) =>
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var health = await _repository.GetOutboxHealthAsync(DateTimeOffset.UtcNow, cancellationToken);
            if (health is not null && (health.DeadLetterCount > 0 || health.PendingCount > 1000))
                return HealthCheckResult.Degraded(
                    $"Collaboration outbox alert:pending={health.PendingCount},dead-letter={health.DeadLetterCount}.");
            var pending = health?.PendingCount ?? (await _repository.ListPendingOutboxAsync(DateTimeOffset.UtcNow, 1, cancellationToken)).Count;
            return HealthCheckResult.Healthy($"Collaboration schema and outbox are reachable; pending={pending}.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy(
                $"Collaboration schema/outbox is unavailable:{exception.GetType().Name}.");
        }
    }
}
