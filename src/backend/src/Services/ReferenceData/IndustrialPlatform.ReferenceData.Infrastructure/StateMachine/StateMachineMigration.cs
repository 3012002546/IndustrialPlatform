namespace IndustrialPlatform.ReferenceData.Infrastructure.StateMachine;

public static class StateMachineMigration
{
    public const string Version = "reference-data-2.7-008";

    public static string Sql(bool postgres)
    {
        var prefix = postgres ? "reference_data." : "reference_data_";
        var id = postgres ? "uuid" : "TEXT";
        var time = postgres ? "timestamptz" : "TEXT";
        var boolean = postgres ? "boolean" : "INTEGER";
        var colorCheck = postgres
            ? "CHECK(color IS NULL OR color ~ '^#[0-9A-F]{6}$')"
            : "CHECK(color IS NULL OR color GLOB '#[0-9A-F][0-9A-F][0-9A-F][0-9A-F][0-9A-F][0-9A-F]')";
        var lifecycle = $"""
            is_frozen {boolean} NOT NULL, is_locked {boolean} NOT NULL, is_deleted {boolean} NOT NULL,
            entity_type TEXT NOT NULL, created_on {time} NOT NULL, last_updated_on {time} NOT NULL,
            optimistic_version bigint NOT NULL, concurrency_version {id} NOT NULL
            """;

        return $"""
            CREATE TABLE {prefix}state_machine_definition (
                id {id} PRIMARY KEY, tenant_nid TEXT NULL, scope_type TEXT NOT NULL,
                n_id varchar(64) NOT NULL CHECK(n_id=upper(n_id)),
                name varchar(200) NOT NULL, description TEXT NULL,
                revision INTEGER NOT NULL CHECK(revision>0),
                status TEXT NOT NULL CHECK(status IN ('Draft','Published','Superseded','Disabled')),
                source_revision INTEGER NULL CHECK(source_revision IS NULL OR source_revision>0),
                published_on {time} NULL, published_by TEXT NULL, {lifecycle},
                UNIQUE(id,revision),
                CHECK((scope_type='Platform' AND tenant_nid IS NULL)
                    OR (scope_type='Tenant' AND tenant_nid IS NOT NULL)),
                CHECK(source_revision IS NULL OR source_revision<revision),
                CHECK((status='Draft' AND published_on IS NULL)
                    OR (status IN ('Published','Superseded') AND published_on IS NOT NULL)
                    OR status='Disabled')
            );
            CREATE UNIQUE INDEX state_machine_scope_revision_uq
                ON {prefix}state_machine_definition(coalesce(tenant_nid,''),n_id,revision);
            CREATE UNIQUE INDEX state_machine_draft_uq
                ON {prefix}state_machine_definition(coalesce(tenant_nid,''),n_id)
                WHERE status='Draft' AND NOT is_deleted;
            CREATE UNIQUE INDEX state_machine_current_uq
                ON {prefix}state_machine_definition(coalesce(tenant_nid,''),n_id)
                WHERE status='Published' AND NOT is_deleted;

            CREATE TABLE {prefix}state_machine_node (
                id {id} PRIMARY KEY,
                state_machine_definition_id {id} NOT NULL,
                definition_revision INTEGER NOT NULL CHECK(definition_revision>0),
                n_id varchar(64) NOT NULL CHECK(n_id=upper(n_id)),
                name varchar(200) NOT NULL, description TEXT NULL,
                is_initial {boolean} NOT NULL, is_terminal {boolean} NOT NULL,
                outcome TEXT NOT NULL CHECK(outcome IN ('None','Success','Failure','Skipped')),
                color varchar(7) NULL {colorCheck}, sort INTEGER NOT NULL CHECK(sort>=0),
                FOREIGN KEY (state_machine_definition_id,definition_revision)
                    REFERENCES {prefix}state_machine_definition(id,revision),
                UNIQUE(state_machine_definition_id,definition_revision,n_id)
            );

            CREATE TABLE {prefix}state_machine_transition (
                id {id} PRIMARY KEY,
                state_machine_definition_id {id} NOT NULL,
                definition_revision INTEGER NOT NULL CHECK(definition_revision>0),
                from_status_nid varchar(64) NOT NULL CHECK(from_status_nid=upper(from_status_nid)),
                action_nid varchar(64) NOT NULL CHECK(action_nid=upper(action_nid)),
                action_name varchar(200) NOT NULL,
                to_status_nid varchar(64) NOT NULL CHECK(to_status_nid=upper(to_status_nid)),
                description TEXT NULL,
                FOREIGN KEY (state_machine_definition_id,definition_revision)
                    REFERENCES {prefix}state_machine_definition(id,revision),
                FOREIGN KEY (state_machine_definition_id,definition_revision,from_status_nid)
                    REFERENCES {prefix}state_machine_node(state_machine_definition_id,definition_revision,n_id),
                FOREIGN KEY (state_machine_definition_id,definition_revision,to_status_nid)
                    REFERENCES {prefix}state_machine_node(state_machine_definition_id,definition_revision,n_id),
                UNIQUE(state_machine_definition_id,definition_revision,from_status_nid,action_nid)
            );
            """;
    }
}
