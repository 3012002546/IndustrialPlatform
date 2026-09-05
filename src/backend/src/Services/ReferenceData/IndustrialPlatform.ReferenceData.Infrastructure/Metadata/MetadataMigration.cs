namespace IndustrialPlatform.ReferenceData.Infrastructure.Metadata;

public static class MetadataMigration
{
    public const string Version = "reference-data-2.7-006";

    public static string Sql(bool postgres)
    {
        var prefix = postgres ? "reference_data." : "reference_data_";
        var id = postgres ? "uuid" : "TEXT";
        var time = postgres ? "timestamptz" : "TEXT";
        var boolean = postgres ? "boolean" : "INTEGER";
        var number = postgres ? "numeric(28,12)" : "TEXT";
        var length = postgres ? "char_length(pattern)" : "length(pattern)";
        var minimum = postgres ? "min_value" : "CAST(min_value AS NUMERIC)";
        var maximum = postgres ? "max_value" : "CAST(max_value AS NUMERIC)";
        var minimumShape = postgres ? string.Empty : $"CHECK(min_value IS NULL OR ({SqliteDecimalCheck("min_value")}))";
        var maximumShape = postgres ? string.Empty : $"CHECK(max_value IS NULL OR ({SqliteDecimalCheck("max_value")}))";
        var lifecycle = $"""
            is_frozen {boolean} NOT NULL, is_locked {boolean} NOT NULL, is_deleted {boolean} NOT NULL,
            entity_type TEXT NOT NULL, created_on {time} NOT NULL, last_updated_on {time} NOT NULL,
            optimistic_version bigint NOT NULL, concurrency_version {id} NOT NULL
            """;
        return $"""
            CREATE TABLE {prefix}metadata_entity_schema (
                id {id} PRIMARY KEY, tenant_nid TEXT NULL, scope_type TEXT NOT NULL,
                n_id varchar(64) NOT NULL CHECK(n_id=upper(n_id)), name varchar(200) NOT NULL, description TEXT NULL,
                revision INTEGER NOT NULL CHECK(revision>0),
                status TEXT NOT NULL CHECK(status IN ('Draft','Published','Superseded','Disabled')),
                source_revision INTEGER NULL CHECK(source_revision IS NULL OR source_revision>0),
                published_on {time} NULL, published_by TEXT NULL, {lifecycle},
                CHECK((scope_type='Platform' AND tenant_nid IS NULL) OR (scope_type='Tenant' AND tenant_nid IS NOT NULL)),
                CHECK(source_revision IS NULL OR source_revision<revision),
                CHECK((status='Draft' AND published_on IS NULL)
                    OR (status IN ('Published','Superseded') AND published_on IS NOT NULL)
                    OR status='Disabled')
            );
            CREATE UNIQUE INDEX metadata_scope_revision_uq
                ON {prefix}metadata_entity_schema (coalesce(tenant_nid,''),n_id,revision);
            CREATE UNIQUE INDEX metadata_draft_uq
                ON {prefix}metadata_entity_schema (coalesce(tenant_nid,''),n_id)
                WHERE status='Draft' AND NOT is_deleted;
            CREATE UNIQUE INDEX metadata_current_uq
                ON {prefix}metadata_entity_schema (coalesce(tenant_nid,''),n_id)
                WHERE status='Published' AND NOT is_deleted;

            CREATE TABLE {prefix}metadata_attribute_definition (
                id {id} PRIMARY KEY,
                entity_schema_id {id} NOT NULL REFERENCES {prefix}metadata_entity_schema(id),
                n_id varchar(64) NOT NULL CHECK(n_id=upper(n_id)), name varchar(200) NOT NULL,
                data_type TEXT NOT NULL CHECK(data_type IN ('String','Integer','Decimal','Boolean','Date','DateTime','Enum','Reference')),
                required {boolean} NOT NULL, is_array {boolean} NOT NULL, enabled {boolean} NOT NULL,
                sort INTEGER NOT NULL CHECK(sort>=0), default_value TEXT NULL,
                min_length INTEGER NULL CHECK(min_length IS NULL OR min_length>=0),
                max_length INTEGER NULL CHECK(max_length IS NULL OR max_length>=0),
                min_value {number} NULL {minimumShape}, max_value {number} NULL {maximumShape},
                pattern TEXT NULL CHECK(pattern IS NULL OR {length}<=256),
                dictionary_nid varchar(64) NULL, reference_target varchar(64) NULL,
                precision_value INTEGER NULL CHECK(precision_value IS NULL OR precision_value BETWEEN 1 AND 28),
                scale_value INTEGER NULL CHECK(scale_value IS NULL OR scale_value BETWEEN 0 AND 12),
                unit_dimension_nid varchar(64) NULL, default_unit_nid varchar(64) NULL,
                unit_revision INTEGER NULL CHECK(unit_revision IS NULL OR unit_revision>0),
                unit_source_scope TEXT NULL CHECK(unit_source_scope IS NULL OR unit_source_scope IN ('Platform','Tenant')),
                unit_source_tenant_nid TEXT NULL, description TEXT NULL, was_published {boolean} NOT NULL,
                {lifecycle},
                UNIQUE(entity_schema_id,n_id),
                CHECK(min_length IS NULL OR max_length IS NULL OR min_length<=max_length),
                CHECK(min_value IS NULL OR max_value IS NULL OR {minimum}<={maximum}),
                CHECK(precision_value IS NULL OR scale_value IS NULL OR scale_value<=precision_value),
                CHECK(dictionary_nid IS NULL OR dictionary_nid=upper(dictionary_nid)),
                CHECK(reference_target IS NULL OR reference_target=upper(reference_target)),
                CHECK(unit_dimension_nid IS NULL OR unit_dimension_nid=upper(unit_dimension_nid)),
                CHECK(default_unit_nid IS NULL OR default_unit_nid=upper(default_unit_nid)),
                CHECK(data_type='String' OR (min_length IS NULL AND max_length IS NULL AND pattern IS NULL)),
                CHECK(data_type IN ('Integer','Decimal') OR (min_value IS NULL AND max_value IS NULL)),
                CHECK(data_type='Decimal' OR (precision_value IS NULL AND scale_value IS NULL
                    AND unit_dimension_nid IS NULL AND default_unit_nid IS NULL AND unit_revision IS NULL
                    AND unit_source_scope IS NULL AND unit_source_tenant_nid IS NULL)),
                CHECK((data_type='Enum' AND dictionary_nid IS NOT NULL)
                    OR (data_type<>'Enum' AND dictionary_nid IS NULL)),
                CHECK((data_type='Reference' AND reference_target IS NOT NULL)
                    OR (data_type<>'Reference' AND reference_target IS NULL)),
                CHECK((unit_dimension_nid IS NULL AND default_unit_nid IS NULL AND unit_revision IS NULL
                        AND unit_source_scope IS NULL AND unit_source_tenant_nid IS NULL)
                    OR (data_type='Decimal' AND unit_dimension_nid IS NOT NULL AND unit_revision IS NOT NULL
                        AND ((unit_source_scope='Platform' AND unit_source_tenant_nid IS NULL)
                            OR (unit_source_scope='Tenant' AND unit_source_tenant_nid IS NOT NULL))))
            );
            """;
    }

    private static string SqliteDecimalCheck(string column) => $"""
        typeof({column})='text'
        AND length({column}) BETWEEN 1 AND 30
        AND {column} NOT GLOB '*[^0-9.-]*'
        AND length({column})-length(replace({column},'.','')) <= 1
        AND (instr({column},'-')=0 OR (substr({column},1,1)='-' AND instr(substr({column},2),'-')=0))
        AND {column} NOT LIKE '.%' AND {column} NOT LIKE '-.%' AND {column} NOT LIKE '%.'
        AND length(CASE WHEN instr(ltrim({column},'-'),'.')=0 THEN ltrim({column},'-')
                        ELSE substr(ltrim({column},'-'),1,instr(ltrim({column},'-'),'.')-1) END) BETWEEN 1 AND 16
        AND (instr({column},'.')=0 OR length({column})-instr({column},'.') BETWEEN 1 AND 12)
        """;
}
