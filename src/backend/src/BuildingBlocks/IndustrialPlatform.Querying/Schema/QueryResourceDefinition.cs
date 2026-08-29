namespace IndustrialPlatform.Querying.Schema;

public sealed class QueryResourceDefinition
{
    public QueryResourceDefinition(
        string name,
        IReadOnlyDictionary<string, QueryFieldDefinition> fields,
        string? tieBreaker = null)
    {
        Name = name;
        Fields = new Dictionary<string, QueryFieldDefinition>(fields, StringComparer.Ordinal);
        TieBreaker = tieBreaker;
    }

    public string Name { get; }

    public IReadOnlyDictionary<string, QueryFieldDefinition> Fields { get; }

    public string? TieBreaker { get; }

    public bool TryGetField(string name, out QueryFieldDefinition definition)
        => Fields.TryGetValue(name, out definition!);
}
