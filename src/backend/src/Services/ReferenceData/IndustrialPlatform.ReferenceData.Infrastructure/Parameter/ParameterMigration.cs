namespace IndustrialPlatform.ReferenceData.Infrastructure.Parameter;

internal static class ParameterMigration
{
    public const string Version = "reference-data-2.7-003";
    public static string Sql(bool postgres)
    {
        var prefix = postgres ? "reference_data." : "reference_data_";
        var id = postgres ? "uuid" : "TEXT";
        var time = postgres ? "timestamptz" : "TEXT";
        var boolean = postgres ? "boolean" : "INTEGER";
        var json = postgres ? "jsonb" : "TEXT";
        var lifecycle = $"""
            is_frozen {boolean} NOT NULL, is_locked {boolean} NOT NULL, is_deleted {boolean} NOT NULL,
            entity_type TEXT NOT NULL, created_on {time} NOT NULL, last_updated_on {time} NOT NULL,
            optimistic_version bigint NOT NULL, concurrency_version {id} NOT NULL
            """;
        return $"""
            CREATE TABLE {prefix}parameter_app_domain (
                id {id} PRIMARY KEY, tenant_nid TEXT NULL, scope_type TEXT NOT NULL,
                n_id varchar(64) NOT NULL CHECK(n_id=upper(n_id)), name varchar(200) NOT NULL, description TEXT NULL,
                revision bigint NOT NULL CHECK(revision>0), status TEXT NOT NULL CHECK(status IN ('Active','Disabled')), {lifecycle},
                CHECK((scope_type='Platform' AND tenant_nid IS NULL) OR (scope_type='Tenant' AND tenant_nid IS NOT NULL))
            );
            CREATE UNIQUE INDEX parameter_platform_nid_uq ON {prefix}parameter_app_domain(n_id) WHERE tenant_nid IS NULL;
            CREATE UNIQUE INDEX parameter_tenant_nid_uq ON {prefix}parameter_app_domain(tenant_nid,n_id) WHERE tenant_nid IS NOT NULL;
            CREATE TABLE {prefix}parameter_key (
                id {id} PRIMARY KEY, configuration_app_domain_id {id} NOT NULL REFERENCES {prefix}parameter_app_domain(id),
                n_id varchar(64) NOT NULL CHECK(n_id=upper(n_id)), name varchar(200) NOT NULL, description TEXT NULL,
                data_type TEXT NOT NULL CHECK(data_type IN ('String','Integer','Decimal','Boolean','Date','DateTime','Enum','Json','Reference')),
                value_mode TEXT NOT NULL CHECK(value_mode IN ('Single','Multi')), value_json {json} NULL, default_value_json {json} NULL,
                is_mandatory {boolean} NOT NULL, is_read_only {boolean} NOT NULL, has_had_value {boolean} NOT NULL,
                dictionary_nid varchar(64) NULL, reference_target varchar(128) NULL,
                status TEXT NOT NULL CHECK(status IN ('Active','Disabled')), sort INTEGER NOT NULL CHECK(sort>=0), {lifecycle},
                CHECK(value_mode='Single' OR (value_json IS NULL AND default_value_json IS NULL)),
                UNIQUE(configuration_app_domain_id,n_id)
            );
            CREATE TABLE {prefix}parameter_multi_value (
                id {id} PRIMARY KEY, configuration_key_id {id} NOT NULL REFERENCES {prefix}parameter_key(id),
                n_id varchar(64) NOT NULL CHECK(n_id=upper(n_id)), name varchar(200) NULL,
                value_json {json} NOT NULL, canonical_value_hash varchar(64) NOT NULL,
                sort INTEGER NOT NULL CHECK(sort>=0), is_default {boolean} NOT NULL, enabled {boolean} NOT NULL,
                {lifecycle}, CHECK(NOT is_default OR enabled), UNIQUE(configuration_key_id,n_id)
            );
            CREATE UNIQUE INDEX parameter_enabled_value_uq ON {prefix}parameter_multi_value(configuration_key_id,canonical_value_hash) WHERE enabled;
            CREATE TABLE {prefix}parameter_history (
                id {id} PRIMARY KEY, app_domain_id {id} NOT NULL REFERENCES {prefix}parameter_app_domain(id),
                key_id {id} NULL, object_type TEXT NOT NULL, object_id {id} NOT NULL, full_nid TEXT NOT NULL,
                change_type TEXT NOT NULL, change_reason varchar(500) NOT NULL, before_summary TEXT NOT NULL, after_summary TEXT NOT NULL,
                revision bigint NOT NULL, user_nid TEXT NOT NULL, trace_id TEXT NOT NULL, created_on {time} NOT NULL
            );
            CREATE INDEX parameter_history_path_ix ON {prefix}parameter_history(app_domain_id,key_id,revision);
            """;
    }
}
