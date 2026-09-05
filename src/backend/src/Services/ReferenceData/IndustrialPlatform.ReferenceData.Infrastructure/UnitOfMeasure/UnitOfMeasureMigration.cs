namespace IndustrialPlatform.ReferenceData.Infrastructure.UnitOfMeasure;

public static class UnitOfMeasureMigration
{
    public const string Version = "reference-data-2.7-005";

    public static string Sql(bool postgres)
    {
        var prefix = postgres ? "reference_data." : "reference_data_";
        var id = postgres ? "uuid" : "TEXT";
        var time = postgres ? "timestamptz" : "TEXT";
        var boolean = postgres ? "boolean" : "INTEGER";
        var falseValue = postgres ? "false" : "0";
        var trueValue = postgres ? "true" : "1";
        var number = postgres ? "numeric(28,12)" : "TEXT";
        var factorCheck = postgres
            ? "CHECK(factor_to_base>0)"
            : $"CHECK({SqliteDecimalCheck("factor_to_base")} AND CAST(factor_to_base AS NUMERIC)>0)";
        var offsetCheck = postgres ? string.Empty : $"CHECK({SqliteDecimalCheck("offset_to_base")})";
        var lifecycle = $"""
            is_frozen {boolean} NOT NULL, is_locked {boolean} NOT NULL, is_deleted {boolean} NOT NULL,
            entity_type TEXT NOT NULL, created_on {time} NOT NULL, last_updated_on {time} NOT NULL,
            optimistic_version bigint NOT NULL, concurrency_version {id} NOT NULL
            """;
        return $"""
            CREATE TABLE {prefix}unit_of_measure_dimension (
                id {id} PRIMARY KEY, tenant_nid TEXT NULL, scope_type TEXT NOT NULL,
                n_id varchar(64) NOT NULL CHECK(n_id=upper(n_id)), name varchar(200) NOT NULL, description TEXT NULL,
                revision INTEGER NOT NULL CHECK(revision>0), status TEXT NOT NULL CHECK(status IN ('Draft','Published','Superseded','Disabled')),
                source_revision INTEGER NULL CHECK(source_revision IS NULL OR source_revision>0),
                published_on {time} NULL, published_by TEXT NULL, is_system_defined {boolean} NOT NULL,
                conversion_kind TEXT NOT NULL CHECK(conversion_kind IN ('Ratio','AbsoluteTemperature')),
                base_unit_nid varchar(64) NOT NULL CHECK(base_unit_nid=upper(base_unit_nid)), {lifecycle},
                CHECK((scope_type='Platform' AND tenant_nid IS NULL) OR (scope_type='Tenant' AND tenant_nid IS NOT NULL)),
                CHECK((status='Draft' AND published_on IS NULL) OR status='Disabled' OR
                      (status IN ('Published','Superseded') AND published_on IS NOT NULL)),
                CHECK(is_system_defined={falseValue} OR (scope_type='Platform' AND status IN ('Published','Superseded','Disabled')))
            );
            CREATE UNIQUE INDEX uom_platform_revision_uq ON {prefix}unit_of_measure_dimension(n_id,revision)
                WHERE tenant_nid IS NULL AND NOT is_deleted;
            CREATE UNIQUE INDEX uom_tenant_revision_uq ON {prefix}unit_of_measure_dimension(tenant_nid,n_id,revision)
                WHERE tenant_nid IS NOT NULL AND NOT is_deleted;
            CREATE UNIQUE INDEX uom_platform_draft_uq ON {prefix}unit_of_measure_dimension(n_id)
                WHERE tenant_nid IS NULL AND status='Draft' AND NOT is_deleted;
            CREATE UNIQUE INDEX uom_tenant_draft_uq ON {prefix}unit_of_measure_dimension(tenant_nid,n_id)
                WHERE tenant_nid IS NOT NULL AND status='Draft' AND NOT is_deleted;
            CREATE UNIQUE INDEX uom_platform_current_uq ON {prefix}unit_of_measure_dimension(n_id)
                WHERE tenant_nid IS NULL AND status='Published' AND NOT is_deleted;
            CREATE UNIQUE INDEX uom_tenant_current_uq ON {prefix}unit_of_measure_dimension(tenant_nid,n_id)
                WHERE tenant_nid IS NOT NULL AND status='Published' AND NOT is_deleted;
            CREATE TABLE {prefix}unit_of_measure_unit (
                id {id} PRIMARY KEY, unit_dimension_id {id} NOT NULL REFERENCES {prefix}unit_of_measure_dimension(id),
                n_id varchar(64) NOT NULL CHECK(n_id=upper(n_id)), name varchar(200) NOT NULL, symbol varchar(64) NOT NULL,
                factor_to_base {number} NOT NULL {factorCheck}, offset_to_base {number} NOT NULL {offsetCheck},
                decimal_places INTEGER NOT NULL CHECK(decimal_places BETWEEN 0 AND 12),
                rounding_mode TEXT NOT NULL CHECK(rounding_mode IN ('ToEven','AwayFromZero')),
                enabled {boolean} NOT NULL, sort INTEGER NOT NULL CHECK(sort>=0), {lifecycle},
                UNIQUE(unit_dimension_id,n_id)
            );
            {Seed(prefix, trueValue, falseValue)}
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

    private static string Seed(string prefix, string trueValue, string falseValue)
    {
        const string rootType = "IndustrialPlatform.ReferenceData.Domain.UnitOfMeasure.UnitDimension";
        const string unitType = "IndustrialPlatform.ReferenceData.Domain.UnitOfMeasure.UnitDefinition";
        const string timestamp = "2026-09-05T00:00:00+00:00";
        var dimensions = new[]
        {
            new SeedDimension("10000000-0000-0000-0000-000000000001", "MASS", "Mass", "Ratio", "KG",
                [new("11000000-0000-0000-0000-000000000001", "KG", "Kilogram", "kg", "1", "0", 3, 0),
                 new("11000000-0000-0000-0000-000000000002", "G", "Gram", "g", "0.001", "0", 3, 1)]),
            new SeedDimension("10000000-0000-0000-0000-000000000002", "LENGTH", "Length", "Ratio", "M",
                [new("12000000-0000-0000-0000-000000000001", "M", "Metre", "m", "1", "0", 3, 0),
                 new("12000000-0000-0000-0000-000000000002", "MM", "Millimetre", "mm", "0.001", "0", 3, 1)]),
            new SeedDimension("10000000-0000-0000-0000-000000000003", "TIME", "Time", "Ratio", "S",
                [new("13000000-0000-0000-0000-000000000001", "S", "Second", "s", "1", "0", 3, 0),
                 new("13000000-0000-0000-0000-000000000002", "MIN", "Minute", "min", "60", "0", 3, 1)]),
            new SeedDimension("10000000-0000-0000-0000-000000000004", "VOLUME", "Volume", "Ratio", "L",
                [new("14000000-0000-0000-0000-000000000001", "L", "Litre", "L", "1", "0", 3, 0),
                 new("14000000-0000-0000-0000-000000000002", "ML", "Millilitre", "mL", "0.001", "0", 3, 1)]),
            new SeedDimension("10000000-0000-0000-0000-000000000005", "COUNT", "Count", "Ratio", "PIECE",
                [new("15000000-0000-0000-0000-000000000001", "PIECE", "Piece", "piece", "1", "0", 0, 0)]),
            new SeedDimension("10000000-0000-0000-0000-000000000006", "ABSOLUTE_TEMPERATURE", "Absolute temperature", "AbsoluteTemperature", "K",
                [new("16000000-0000-0000-0000-000000000001", "K", "Kelvin", "K", "1", "0", 2, 0),
                 new("16000000-0000-0000-0000-000000000002", "DEGC", "Degree Celsius", "°C", "1", "273.15", 2, 1),
                 new("16000000-0000-0000-0000-000000000003", "DEGF", "Degree Fahrenheit", "°F", "0.555555555556", "255.372222222222", 2, 2)]),
        };
        var statements = new List<string>();
        foreach (var dimension in dimensions)
        {
            statements.Add($"""
                INSERT INTO {prefix}unit_of_measure_dimension
                    (id,tenant_nid,scope_type,n_id,name,description,revision,status,source_revision,published_on,published_by,
                     is_system_defined,conversion_kind,base_unit_nid,is_frozen,is_locked,is_deleted,entity_type,created_on,last_updated_on,
                     optimistic_version,concurrency_version)
                VALUES ('{dimension.Id}',NULL,'Platform','{dimension.NId}','{dimension.Name}',NULL,1,'Published',NULL,'{timestamp}','SYSTEM',
                        {trueValue},'{dimension.Kind}','{dimension.Base}',{falseValue},{falseValue},{falseValue},'{rootType}','{timestamp}','{timestamp}',1,
                        '{dimension.Id[..^1]}f') ON CONFLICT DO NOTHING;
                """);
            foreach (var unit in dimension.Units)
            {
                statements.Add($"""
                    INSERT INTO {prefix}unit_of_measure_unit
                        (id,unit_dimension_id,n_id,name,symbol,factor_to_base,offset_to_base,decimal_places,rounding_mode,enabled,sort,
                         is_frozen,is_locked,is_deleted,entity_type,created_on,last_updated_on,optimistic_version,concurrency_version)
                    VALUES ('{unit.Id}','{dimension.Id}','{unit.NId}','{unit.Name}','{unit.Symbol}','{unit.Factor}','{unit.Offset}',{unit.Places},'ToEven',
                            {trueValue},{unit.Sort},{falseValue},{falseValue},{falseValue},'{unitType}','{timestamp}','{timestamp}',0,
                            '{unit.Id[..^1]}f') ON CONFLICT DO NOTHING;
                    """);
            }
        }
        return string.Join(Environment.NewLine, statements);
    }

    private sealed record SeedDimension(string Id, string NId, string Name, string Kind, string Base, SeedUnit[] Units);
    private sealed record SeedUnit(string Id, string NId, string Name, string Symbol, string Factor, string Offset, int Places, int Sort);
}
