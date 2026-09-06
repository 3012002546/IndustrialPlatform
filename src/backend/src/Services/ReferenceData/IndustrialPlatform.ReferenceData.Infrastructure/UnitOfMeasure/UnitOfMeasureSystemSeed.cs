using System.Security.Cryptography;
using System.Text;

namespace IndustrialPlatform.ReferenceData.Infrastructure.UnitOfMeasure;

/// <summary>
/// The trusted, additive platform UOM seed. It is replayed by the service initializer
/// and recorded separately from the schema migration so an existing database can heal
/// missing defaults without changing tenant-owned definitions.
/// </summary>
public static class UnitOfMeasureSystemSeed
{
    public const string SeedKey = "reference-data.unit-of-measure.system";
    public const string SeedVersion = "1";
    public static readonly string Checksum = Convert.ToHexStringLower(SHA256.HashData(
        Encoding.UTF8.GetBytes($"{SeedKey}|{SeedVersion}|System|reference-data-unit-of-measure")));

    public static string Sql(bool postgres)
    {
        var prefix = postgres ? "reference_data." : "reference_data_";
        var trueValue = postgres ? "true" : "1";
        var falseValue = postgres ? "false" : "0";
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

    public static IReadOnlyList<string> DimensionNIds => ["MASS", "LENGTH", "TIME", "VOLUME", "COUNT", "ABSOLUTE_TEMPERATURE"];
    public static IReadOnlyList<string> UnitNIds => ["KG", "G", "M", "MM", "S", "MIN", "L", "ML", "PIECE", "K", "DEGC", "DEGF"];

    private sealed record SeedDimension(string Id, string NId, string Name, string Kind, string Base, SeedUnit[] Units);
    private sealed record SeedUnit(string Id, string NId, string Name, string Symbol, string Factor, string Offset, int Places, int Sort);
}
