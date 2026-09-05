namespace IndustrialPlatform.ReferenceData.Infrastructure.Persistence;

/// <summary>Cross-module integrity constraints added after the initial PF-03 schema.</summary>
public static class ReferenceDataIntegrityMigration
{
    public const string Version = "reference-data-2.7-010";

    public static string Sql(bool postgres)
    {
        var definitions = postgres
            ? "reference_data.dictionary_definition"
            : "reference_data_dictionary_definition";
        return $"""
            WITH ranked_drafts AS (
                SELECT id,
                       ROW_NUMBER() OVER (
                           PARTITION BY coalesce(tenant_nid,''),n_id
                           ORDER BY revision DESC,id DESC
                       ) AS draft_rank
                FROM {definitions}
                WHERE status='Draft' AND NOT is_deleted
            )
            UPDATE {definitions}
               SET status='Disabled',last_updated_on=CURRENT_TIMESTAMP
             WHERE id IN (SELECT id FROM ranked_drafts WHERE draft_rank>1);
            CREATE UNIQUE INDEX dictionary_draft_uq
                ON {definitions} (coalesce(tenant_nid,''),n_id)
                WHERE status='Draft' AND NOT is_deleted;
            """;
    }
}
