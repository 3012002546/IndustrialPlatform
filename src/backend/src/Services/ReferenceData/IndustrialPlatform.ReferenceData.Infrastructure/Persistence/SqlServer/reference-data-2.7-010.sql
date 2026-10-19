-- SQL Server 2019/2022；新增 Provider 的独立脚本，应用后只能追加迁移。
-- reference-data-2.7-010
WITH ranked_drafts AS (
    SELECT id,
           ROW_NUMBER() OVER (
               PARTITION BY coalesce(tenant_nid,N''),n_id
               ORDER BY revision DESC,id DESC
           ) AS draft_rank
    FROM dbo.reference_data_dictionary_definition
    WHERE status=N'Draft' AND is_deleted=0
)
UPDATE dbo.reference_data_dictionary_definition
   SET status=N'Disabled',last_updated_on=CURRENT_TIMESTAMP
 WHERE id IN (SELECT id FROM ranked_drafts WHERE draft_rank>1);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.reference_data_dictionary_definition') AND name=N'dictionary_draft_uq')
CREATE UNIQUE INDEX dictionary_draft_uq ON dbo.reference_data_dictionary_definition (tenant_scope,n_id)
    WHERE status=N'Draft' AND is_deleted=0;
