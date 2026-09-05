namespace IndustrialPlatform.ReferenceData.Infrastructure.Dictionary;

internal static class DictionaryMigration
{
    public const string Version = "reference-data-2.7-002";

    public static string Sql(bool postgres)
    {
        var prefix = postgres ? "reference_data." : "reference_data_";
        var id = postgres ? "uuid" : "TEXT";
        var time = postgres ? "timestamptz" : "TEXT";
        var boolean = postgres ? "boolean" : "INTEGER";
        var lifecycle = $"""
            is_frozen {boolean} NOT NULL, is_locked {boolean} NOT NULL, is_deleted {boolean} NOT NULL,
            entity_type TEXT NOT NULL, created_on {time} NOT NULL, last_updated_on {time} NOT NULL,
            optimistic_version bigint NOT NULL, concurrency_version {id} NOT NULL
            """;
        return $"""
            CREATE TABLE {prefix}dictionary_definition (
                id {id} PRIMARY KEY, tenant_nid TEXT NULL, scope_type TEXT NOT NULL,
                n_id varchar(64) NOT NULL CHECK(n_id=upper(n_id)), name varchar(200) NOT NULL, description TEXT NULL,
                revision INTEGER NOT NULL CHECK(revision>0), status TEXT NOT NULL CHECK(status IN ('Draft','Published','Superseded','Disabled')),
                published_on {time} NULL, published_by TEXT NULL, {lifecycle},
                CHECK((scope_type='Platform' AND tenant_nid IS NULL) OR (scope_type='Tenant' AND tenant_nid IS NOT NULL))
            );
            CREATE UNIQUE INDEX dictionary_scope_revision_uq ON {prefix}dictionary_definition (coalesce(tenant_nid,''),n_id,revision);
            CREATE UNIQUE INDEX dictionary_current_uq ON {prefix}dictionary_definition (coalesce(tenant_nid,''),n_id) WHERE status='Published' AND NOT is_deleted;
            CREATE TABLE {prefix}dictionary_item (
                id {id} PRIMARY KEY, dictionary_definition_id {id} NOT NULL REFERENCES {prefix}dictionary_definition(id),
                n_id varchar(64) NOT NULL CHECK(n_id=upper(n_id)), name varchar(200) NOT NULL, description TEXT NULL,
                sort INTEGER NOT NULL CHECK(sort>=0), enabled {boolean} NOT NULL, {lifecycle},
                UNIQUE(dictionary_definition_id,n_id)
            );
            """;
    }
}
