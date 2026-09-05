namespace IndustrialPlatform.ReferenceData.Infrastructure.DynamicProperty;

internal static class DynamicConfigurationMigration
{
    public const string Version = "reference-data-2.7-004";
    public static string Sql(bool postgres)
    {
        var prefix = postgres ? "reference_data." : "reference_data_";
        var id = postgres ? "uuid" : "TEXT";
        var time = postgres ? "timestamptz" : "TEXT";
        var boolean = postgres ? "boolean" : "INTEGER";
        var json = postgres ? "jsonb" : "TEXT";
        var number = postgres ? "numeric(28,10)" : "TEXT";
        var bounds = postgres ? "numeric" : "TEXT";
        var date = postgres ? "date" : "TEXT";
        var lifecycle = $"""
            is_frozen {boolean} NOT NULL, is_locked {boolean} NOT NULL, is_deleted {boolean} NOT NULL,
            entity_type TEXT NOT NULL, created_on {time} NOT NULL, last_updated_on {time} NOT NULL,
            optimistic_version bigint NOT NULL CHECK(optimistic_version>=0), concurrency_version {id} NOT NULL
            """;
        var columns = new[] { "string_value", "integer_value", "decimal_value", "boolean_value", "date_value", "date_time_value", "json_value", "reference_value" };
        var types = new[] { "value_type IN ('String','Enum')", "value_type='Integer'", "value_type='Decimal'", "value_type='Boolean'",
            "value_type='Date'", "value_type='DateTime'", "value_type='Json'", "value_type='Reference'" };
        var check = string.Join(" OR ", columns.Select((column, index) => "(" + types[index] + " AND " +
            string.Join(" AND ", columns.Select(other => other + (other == column ? " IS NOT NULL" : " IS NULL"))) + ")"));
        return $"""
            CREATE TABLE {prefix}dynamic_property_definition (
                id {id} PRIMARY KEY, n_id varchar(64) NOT NULL CHECK(n_id=upper(n_id)), name varchar(200) NOT NULL,
                description TEXT NULL, scope_type TEXT NOT NULL, tenant_nid TEXT NULL, revision INTEGER NOT NULL CHECK(revision>0),
                status TEXT NOT NULL CHECK(status IN ('Draft','Published','Superseded','Disabled')),
                published_on {time} NULL, published_by TEXT NULL, {lifecycle},
                CHECK((scope_type='Platform' AND tenant_nid IS NULL) OR (scope_type='Tenant' AND tenant_nid IS NOT NULL))
            );
            CREATE UNIQUE INDEX dynamic_platform_revision_uq ON {prefix}dynamic_property_definition(n_id,revision) WHERE tenant_nid IS NULL;
            CREATE UNIQUE INDEX dynamic_tenant_revision_uq ON {prefix}dynamic_property_definition(tenant_nid,n_id,revision) WHERE tenant_nid IS NOT NULL;
            CREATE UNIQUE INDEX dynamic_platform_draft_uq ON {prefix}dynamic_property_definition(n_id) WHERE tenant_nid IS NULL AND status='Draft';
            CREATE UNIQUE INDEX dynamic_tenant_draft_uq ON {prefix}dynamic_property_definition(tenant_nid,n_id) WHERE tenant_nid IS NOT NULL AND status='Draft';
            CREATE UNIQUE INDEX dynamic_platform_published_uq ON {prefix}dynamic_property_definition(n_id) WHERE tenant_nid IS NULL AND status='Published';
            CREATE UNIQUE INDEX dynamic_tenant_published_uq ON {prefix}dynamic_property_definition(tenant_nid,n_id) WHERE tenant_nid IS NOT NULL AND status='Published';
            CREATE TABLE {prefix}dynamic_property_field (
                id {id} PRIMARY KEY, definition_id {id} NOT NULL REFERENCES {prefix}dynamic_property_definition(id),
                n_id varchar(64) NOT NULL CHECK(n_id=upper(n_id)), name varchar(200) NOT NULL,
                data_type TEXT NOT NULL CHECK(data_type IN ('String','Integer','Decimal','Boolean','Date','DateTime','Enum','Json','Reference')),
                required {boolean} NOT NULL, enabled {boolean} NOT NULL, sort INTEGER NOT NULL CHECK(sort>=0),
                default_value_json {json} NULL, min_length INTEGER NULL, max_length INTEGER NULL,
                min_value {bounds} NULL, max_value {bounds} NULL, scale INTEGER NULL CHECK(scale BETWEEN 0 AND 10),
                pattern varchar(256) NULL, dictionary_nid varchar(64) NULL, reference_target varchar(128) NULL, description TEXT NULL,
                has_had_value {boolean} NOT NULL, was_published {boolean} NOT NULL, {lifecycle},
                UNIQUE(definition_id,n_id), UNIQUE(id,definition_id)
            );
            CREATE TABLE {prefix}dynamic_property_record (
                id {id} PRIMARY KEY, definition_id {id} NOT NULL REFERENCES {prefix}dynamic_property_definition(id),
                n_id varchar(64) NOT NULL CHECK(n_id=upper(n_id)), name varchar(200) NULL, category varchar(100) NULL,
                sort INTEGER NOT NULL CHECK(sort>=0), enabled {boolean} NOT NULL, {lifecycle},
                UNIQUE(definition_id,n_id), UNIQUE(id,definition_id)
            );
            CREATE INDEX dynamic_record_page_ix ON {prefix}dynamic_property_record(definition_id,sort,n_id);
            CREATE TABLE {prefix}dynamic_property_value (
                id {id} PRIMARY KEY, definition_id {id} NOT NULL REFERENCES {prefix}dynamic_property_definition(id),
                record_id {id} NOT NULL REFERENCES {prefix}dynamic_property_record(id),
                field_id {id} NOT NULL REFERENCES {prefix}dynamic_property_field(id), value_type TEXT NOT NULL,
                string_value TEXT NULL, integer_value bigint NULL, decimal_value {number} NULL, boolean_value {boolean} NULL,
                date_value {date} NULL, date_time_value {time} NULL, json_value {json} NULL, reference_value varchar(128) NULL,
                {lifecycle}, UNIQUE(record_id,field_id), CHECK({check}),
                FOREIGN KEY(record_id,definition_id) REFERENCES {prefix}dynamic_property_record(id,definition_id),
                FOREIGN KEY(field_id,definition_id) REFERENCES {prefix}dynamic_property_field(id,definition_id)
            );
            CREATE INDEX dynamic_value_definition_ix ON {prefix}dynamic_property_value(definition_id);
            """;
    }
}
