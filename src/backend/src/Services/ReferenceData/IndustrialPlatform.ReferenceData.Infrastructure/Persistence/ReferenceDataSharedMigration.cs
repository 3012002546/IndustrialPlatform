namespace IndustrialPlatform.ReferenceData.Infrastructure.Persistence;

public static class ReferenceDataSharedMigration
{
    public const string Version = "reference-data-2.7-009";

    public static string Sql(bool postgres)
    {
        var prefix = postgres ? "reference_data." : "reference_data_";
        var id = postgres ? "uuid" : "TEXT";
        var time = postgres ? "timestamptz" : "TEXT";
        var payload = postgres ? "jsonb" : "TEXT CHECK(json_valid(payload))";
        return $"""
            CREATE TABLE {prefix}outbox_message (
                event_id {id} PRIMARY KEY,
                module_key varchar(32) NOT NULL CHECK(module_key IN
                    ('dictionary','parameter','dynamic-property','metadata','coding-rule','state-machine','unit-of-measure')),
                event_name varchar(96) NOT NULL,
                aggregate_id {id} NOT NULL,
                revision bigint NOT NULL CHECK(revision>0),
                payload {payload} NOT NULL,
                status varchar(16) NOT NULL CHECK(status IN ('Pending','Published','Failed')),
                attempt_count INTEGER NOT NULL CHECK(attempt_count BETWEEN 0 AND 10),
                next_attempt_on {time} NULL,
                last_error varchar(500) NULL,
                created_time {time} NOT NULL,
                published_on {time} NULL,
                CHECK((status='Published' AND published_on IS NOT NULL)
                    OR (status IN ('Pending','Failed') AND published_on IS NULL)),
                CHECK(status<>'Failed' OR (attempt_count=10 AND next_attempt_on IS NULL))
            );
            CREATE INDEX reference_data_outbox_pending_ix
                ON {prefix}outbox_message(status,next_attempt_on,created_time);
            CREATE INDEX reference_data_outbox_module_ix
                ON {prefix}outbox_message(module_key,status);
            """;
    }
}
