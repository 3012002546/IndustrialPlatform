using System.Text.Json;
using IndustrialPlatform.ReferenceData.Contracts.Events;
using SqlSugar;

namespace IndustrialPlatform.ReferenceData.Infrastructure.Outbox;

public static class ReferenceDataOutboxWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static Task InsertAsync(ISqlSugarClient database, string moduleKey,
        ReferenceDataIntegrationEvent @event, CancellationToken cancellationToken)
    {
        var postgres = database.CurrentConnectionConfig.DbType == DbType.PostgreSQL;
        var payload = JsonSerializer.Serialize(@event, @event.GetType(), JsonOptions);
        if (postgres)
            return database.Ado.ExecuteCommandAsync("""
                INSERT INTO reference_data.outbox_message
                    (event_id,module_key,event_name,aggregate_id,revision,payload,status,attempt_count,created_time)
                VALUES
                    (@EventId,@ModuleKey,@EventName,@AggregateId,@Revision,CAST(@Payload AS jsonb),'Pending',0,@CreatedTime)
                """,
                new SugarParameter("@EventId", @event.EventId),
                new SugarParameter("@ModuleKey", moduleKey),
                new SugarParameter("@EventName", @event.RoutingKey),
                new SugarParameter("@AggregateId", @event.AggregateId),
                new SugarParameter("@Revision", @event.Revision),
                new SugarParameter("@Payload", payload),
                new SugarParameter("@CreatedTime", @event.CreatedTime.ToUniversalTime()));

        var row = new ReferenceDataOutboxRow
        {
            EventId = @event.EventId,
            ModuleKey = moduleKey,
            EventName = @event.RoutingKey,
            AggregateId = @event.AggregateId,
            Revision = @event.Revision,
            Payload = payload,
            Status = "Pending",
            AttemptCount = 0,
            CreatedTime = @event.CreatedTime.ToLocalTime(),
        };
        return database.Insertable(row).AS("reference_data_outbox_message")
            .ExecuteCommandAsync(cancellationToken);
    }
}
