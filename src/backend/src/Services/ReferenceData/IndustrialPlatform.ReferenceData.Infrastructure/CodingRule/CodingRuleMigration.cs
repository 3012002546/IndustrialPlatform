namespace IndustrialPlatform.ReferenceData.Infrastructure.CodingRule;

public static class CodingRuleMigration
{
    public const string Version = "reference-data-2.7-007";

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
            CREATE TABLE {prefix}coding_rule_definition (
                id {id} PRIMARY KEY, tenant_nid TEXT NULL, scope_type TEXT NOT NULL,
                n_id varchar(64) NOT NULL CHECK(n_id=upper(n_id)), name varchar(200) NOT NULL,
                target_entity_nid varchar(64) NOT NULL CHECK(target_entity_nid=upper(target_entity_nid)),
                template varchar(1024) NOT NULL CHECK(length(template) BETWEEN 1 AND 1024),
                reset_policy TEXT NOT NULL CHECK(reset_policy IN ('Never','Yearly','Monthly','Daily')),
                revision INTEGER NOT NULL CHECK(revision>0),
                status TEXT NOT NULL CHECK(status IN ('Draft','Published','Superseded','Disabled')),
                source_revision INTEGER NULL CHECK(source_revision IS NULL OR source_revision>0),
                published_on {time} NULL, published_by TEXT NULL, {lifecycle},
                CHECK((scope_type='Platform' AND tenant_nid IS NULL) OR (scope_type='Tenant' AND tenant_nid IS NOT NULL)),
                CHECK((status='Draft' AND published_on IS NULL) OR status='Disabled' OR
                      (status IN ('Published','Superseded') AND published_on IS NOT NULL)),
                CHECK((published_on IS NULL AND published_by IS NULL) OR
                      (published_on IS NOT NULL AND published_by IS NOT NULL)),
                CHECK(source_revision IS NULL OR source_revision<revision),
                UNIQUE(id,revision)
            );
            CREATE UNIQUE INDEX coding_rule_platform_revision_uq ON {prefix}coding_rule_definition(n_id,revision)
                WHERE tenant_nid IS NULL AND NOT is_deleted;
            CREATE UNIQUE INDEX coding_rule_tenant_revision_uq ON {prefix}coding_rule_definition(tenant_nid,n_id,revision)
                WHERE tenant_nid IS NOT NULL AND NOT is_deleted;
            CREATE UNIQUE INDEX coding_rule_platform_draft_uq ON {prefix}coding_rule_definition(n_id)
                WHERE tenant_nid IS NULL AND status='Draft' AND NOT is_deleted;
            CREATE UNIQUE INDEX coding_rule_tenant_draft_uq ON {prefix}coding_rule_definition(tenant_nid,n_id)
                WHERE tenant_nid IS NOT NULL AND status='Draft' AND NOT is_deleted;
            CREATE UNIQUE INDEX coding_rule_platform_current_uq ON {prefix}coding_rule_definition(n_id)
                WHERE tenant_nid IS NULL AND status='Published' AND NOT is_deleted;
            CREATE UNIQUE INDEX coding_rule_tenant_current_uq ON {prefix}coding_rule_definition(tenant_nid,n_id)
                WHERE tenant_nid IS NOT NULL AND status='Published' AND NOT is_deleted;

            CREATE TABLE {prefix}coding_rule_sequence (
                coding_rule_id {id} NOT NULL,
                rule_revision INTEGER NOT NULL CHECK(rule_revision>0),
                period_key varchar(8) NOT NULL CHECK(length(period_key) BETWEEN 1 AND 8),
                context_hash varchar(64) NOT NULL CHECK(length(context_hash)=64),
                last_value bigint NOT NULL CHECK(last_value>0),
                created_on {time} NOT NULL, last_updated_on {time} NOT NULL,
                PRIMARY KEY(coding_rule_id,rule_revision,period_key,context_hash),
                FOREIGN KEY(coding_rule_id,rule_revision)
                    REFERENCES {prefix}coding_rule_definition(id,revision)
            );

            CREATE TABLE {prefix}coding_rule_idempotency_record (
                id {id} PRIMARY KEY, tenant_nid TEXT NOT NULL, rule_nid varchar(64) NOT NULL CHECK(rule_nid=upper(rule_nid)),
                idempotency_key_hash varchar(64) NOT NULL CHECK(length(idempotency_key_hash)=64),
                request_hash varchar(64) NOT NULL CHECK(length(request_hash)=64),
                coding_rule_id {id} NOT NULL,
                rule_revision INTEGER NOT NULL CHECK(rule_revision>0),
                source_scope TEXT NOT NULL CHECK(source_scope IN ('Platform','Tenant')),
                source_tenant_nid TEXT NULL,
                code varchar(128) NOT NULL CHECK(length(code) BETWEEN 1 AND 128),
                sequence_value bigint NOT NULL CHECK(sequence_value>0),
                period_key varchar(8) NOT NULL CHECK(length(period_key) BETWEEN 1 AND 8),
                generated_on {time} NOT NULL, expires_on {time} NOT NULL,
                CHECK((source_scope='Platform' AND source_tenant_nid IS NULL) OR
                      (source_scope='Tenant' AND source_tenant_nid IS NOT NULL)),
                CHECK(expires_on>generated_on),
                UNIQUE(tenant_nid,rule_nid,idempotency_key_hash),
                FOREIGN KEY(coding_rule_id,rule_revision)
                    REFERENCES {prefix}coding_rule_definition(id,revision)
            );
            CREATE INDEX coding_rule_idempotency_expiry_ix ON {prefix}coding_rule_idempotency_record(expires_on);
            """;
    }
}
