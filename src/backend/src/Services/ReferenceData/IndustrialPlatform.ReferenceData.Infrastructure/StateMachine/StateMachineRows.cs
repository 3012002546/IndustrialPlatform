using SqlSugar;

namespace IndustrialPlatform.ReferenceData.Infrastructure.StateMachine;

internal sealed class StateMachineDefinitionRow
{
    [SugarColumn(ColumnName = "id", IsPrimaryKey = true)] public Guid Id { get; set; }
    [SugarColumn(ColumnName = "tenant_nid")] public string? TenantNId { get; set; }
    [SugarColumn(ColumnName = "scope_type")] public string ScopeType { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "n_id")] public string NId { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "name")] public string Name { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "description")] public string? Description { get; set; }
    [SugarColumn(ColumnName = "revision")] public int Revision { get; set; }
    [SugarColumn(ColumnName = "status")] public string Status { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "source_revision")] public int? SourceRevision { get; set; }
    [SugarColumn(ColumnName = "published_on")] public DateTimeOffset? PublishedOn { get; set; }
    [SugarColumn(ColumnName = "published_by")] public string? PublishedBy { get; set; }
    [SugarColumn(ColumnName = "is_frozen")] public bool IsFrozen { get; set; }
    [SugarColumn(ColumnName = "is_locked")] public bool IsLocked { get; set; }
    [SugarColumn(ColumnName = "is_deleted")] public bool IsDeleted { get; set; }
    [SugarColumn(ColumnName = "entity_type")] public string EntityType { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "created_on")] public DateTimeOffset CreatedOn { get; set; }
    [SugarColumn(ColumnName = "last_updated_on")] public DateTimeOffset LastUpdatedOn { get; set; }
    [SugarColumn(ColumnName = "optimistic_version")] public long OptimisticVersion { get; set; }
    [SugarColumn(ColumnName = "concurrency_version")] public Guid ConcurrencyVersion { get; set; }
}

internal sealed class StateMachineNodeRow
{
    [SugarColumn(ColumnName = "id", IsPrimaryKey = true)] public Guid Id { get; set; }
    [SugarColumn(ColumnName = "state_machine_definition_id")] public Guid StateMachineDefinitionId { get; set; }
    [SugarColumn(ColumnName = "definition_revision")] public int DefinitionRevision { get; set; }
    [SugarColumn(ColumnName = "n_id")] public string NId { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "name")] public string Name { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "description")] public string? Description { get; set; }
    [SugarColumn(ColumnName = "is_initial")] public bool IsInitial { get; set; }
    [SugarColumn(ColumnName = "is_terminal")] public bool IsTerminal { get; set; }
    [SugarColumn(ColumnName = "outcome")] public string Outcome { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "color")] public string? Color { get; set; }
    [SugarColumn(ColumnName = "sort")] public int Sort { get; set; }
}

internal sealed class StateMachineTransitionRow
{
    [SugarColumn(ColumnName = "id", IsPrimaryKey = true)] public Guid Id { get; set; }
    [SugarColumn(ColumnName = "state_machine_definition_id")] public Guid StateMachineDefinitionId { get; set; }
    [SugarColumn(ColumnName = "definition_revision")] public int DefinitionRevision { get; set; }
    [SugarColumn(ColumnName = "from_status_nid")] public string FromStatusNId { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "action_nid")] public string ActionNId { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "action_name")] public string ActionName { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "to_status_nid")] public string ToStatusNId { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "description")] public string? Description { get; set; }
}
