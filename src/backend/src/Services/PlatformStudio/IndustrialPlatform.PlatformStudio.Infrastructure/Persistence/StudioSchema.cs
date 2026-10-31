using IndustrialPlatform.PlatformStudio.Domain;
using SqlSugar;

namespace IndustrialPlatform.PlatformStudio.Infrastructure.Persistence;

/// <summary>首次发行的固定结构清单；产品装配不改写迁移。所有标识符来自此编译期清单。</summary>
public static class StudioSchema
{
    public sealed record Definition(string Name, string Columns, string Step);
    public static Definition ScreenPolicy { get; } = new("studio_dashboard_screen", "never_expires:flag,follow_published:flag", "STU-008-DashboardScreenPolicy");
    public static IReadOnlyList<Definition> Core { get; } =
    [
        new("studio_source", "id:id,is_frozen:flag,is_locked:flag,is_deleted:flag,entity_type:txt512,created_on:time,last_updated_on:time,optimistic_version:i64,concurrency_version:id,n_id:key128,tenant_n_id:key128,name:txt128,description:txt1000?,provider:key32,endpoint_key:key128,endpoint_configuration_hash:sha,credential_ref:key256?,catalog_key:key128?,allowed_objects_json:json64,is_enabled:flag,configuration_version:i64,timeout_seconds:i32,max_rows:i32,max_bytes:i64", "STU-001-Resources"),
        new("studio_resource", "id:id,is_frozen:flag,is_locked:flag,is_deleted:flag,entity_type:txt512,created_on:time,last_updated_on:time,optimistic_version:i64,concurrency_version:id,n_id:key128,tenant_n_id:key128,kind:key32,name:txt128,description:txt1000?,folder:txt256?,document_schema_version:i32,draft_json:json2048,draft_base_revision_id:id?,published_revision_id:id?,revision_sequence:i64,is_enabled:flag,owner_user_n_id:key128", "STU-001-Resources"),
        new("studio_revision", "id:id,n_id:key128,tenant_n_id:key128,created_on:time,resource_id:id,revision_no:i64,document_schema_version:i32,document_json:json2048,dependencies_json:json512,source_pins_json:json64,content_hash:sha,published_by:key128,release_note:txt1000", "STU-001-Resources"),
        new("studio_grant", "id:id,n_id:key128,tenant_n_id:key128,created_on:time,resource_id:id,subject_kind:key16,subject_n_id:key128,capability:key16,revoked_on:time?,granted_by:key128", "STU-001-Resources"),
        new("studio_ledger_record", "id:id,is_frozen:flag,is_locked:flag,is_deleted:flag,entity_type:txt512,created_on:time,last_updated_on:time,optimistic_version:i64,concurrency_version:id,n_id:key128,tenant_n_id:key128,model_resource_id:id,schema_revision_id:id,values_json:json256,created_by:key128,last_updated_by:key128", "STU-002-Ledger"),
        new("studio_ledger_value", "record_id:id,tenant_n_id:key128,model_resource_id:id,field_id:key64,value_kind:key16,text_value:txt512?,number_scaled:i64?,bool_value:flag?,time_value:time?,unique_key:sha?", "STU-002-Ledger"),
        new("studio_operation_receipt", "id:id,n_id:key128,tenant_n_id:key128,created_on:time,operation_key:key128,actor_user_n_id:key128,operation_kind:key64,resource_n_id:key128,request_hash:sha,status:key32,response_json:json4096?,executor_key:key128?,executor_version:key64?,external_reference:key256?,error_code:key64?,completed_on:time?", "STU-002-Ledger"),
        new("studio_audit_outbox", "id:id,n_id:key128,tenant_n_id:key128,created_on:time,module_key:key32,audit_event_n_id:key128,payload_json:json64,status:key16,attempt_count:i32,next_attempt_on:time,sent_on:time?,last_error_code:key64?", "STU-003-AuditOutbox"),
        new("studio_report_run", "id:id,n_id:key128,tenant_n_id:key128,created_on:time,execution_kind:key16,template_revision_id:id?,preview_snapshot_json:json8192?,preview_expires_on:time?,preview_manifest_json:json2048?,operation_key:key128,requested_by:key128,parameters_json:json64,access_snapshot_json:json64,format:key16,status:key32,state_version:i64,scheduler_run_n_id:key128?,execution_epoch:i64,started_on:time?,completed_on:time?,source_manifest_json:json512?,progress_json:json32,error_code:key64?,error_location:txt512?,trace_id:key64,retry_of_run_id:id?,retry_mode:key16?", "STU-004-Report"),
        new("studio_report_artifact", "id:id,n_id:key128,tenant_n_id:key128,created_on:time,run_id:id,purpose:key32,format:key16,file_n_id:key128,file_reference_n_id:key128,sha256:sha,content_length:i64,page_count:i32?,engine_manifest_json:json64,retention_until:time?", "STU-004-Report"),
        new("studio_workbook", "id:id,is_frozen:flag,is_locked:flag,is_deleted:flag,entity_type:txt512,created_on:time,last_updated_on:time,optimistic_version:i64,concurrency_version:id,n_id:key128,tenant_n_id:key128,name:txt128,source_artifact_n_id:key128?,current_version_id:id?,version_sequence:i64,owner_user_n_id:key128,is_archived:flag", "STU-005-Workbook"),
        new("studio_workbook_version", "id:id,n_id:key128,tenant_n_id:key128,created_on:time,workbook_id:id,content_version:i64,base_version_id:id?,file_n_id:key128,file_reference_n_id:key128,sha256:sha,content_length:i64,saved_by:key128,save_operation_key:key128", "STU-005-Workbook"),
        new("studio_workbook_session", "id:id,n_id:key128,tenant_n_id:key128,created_on:time,workbook_id:id,base_version_id:id,user_n_id:key128,provider_key:key64,mode:key8,status:key32,access_token_hash:sha,expires_on:time,last_seen_on:time,lock_hash:sha?,lock_value_ciphertext:txt4096?,lock_expires_on:time?,last_save_operation_key:key128?,identity_evidence_json:json8", "STU-005-Workbook"),
        new("studio_auth_request", "id:id,state_hash:sha,issuer_key:key64,provider:key16,nonce_hash:sha,verifier_cipher:txt2048,return_path:txt512,status:key16,created_on:time,expires_on:time,consumed_on:time?,optimistic_version:i64", "STU-006-ExternalIdentity"),
        new("studio_auth_session", "id:id,session_hash:sha,tenant_n_id:key128,subject_n_id:key128,issuer_key:key64,provider:key16,external_subject_id:key256?,external_tenant_id:key256?,context_handle_cipher:txt4096?,principal_json:json32,auth_version:key128,policy_version:i64,created_on:time,last_seen_on:time,context_checked_on:time,expires_on:time,revoked_on:time?,optimistic_version:i64", "STU-006-ExternalIdentity"),
        new("studio_local_admin", "id:id,tenant_n_id:key128,subject_n_id:key128,login_name:key128,password_hash:txt1024,enabled:flag,must_change_password:flag,auth_version:i64,failed_count:i32,locked_until:time?,created_on:time,last_updated_on:time,optimistic_version:i64", "STU-006-ExternalIdentity"),
        new("studio_dashboard_screen", "id:id,tenant_n_id:key128,resource_n_id:key128,revision_n_id:key128,created_by:key128,issuer_key:key64,auth_version:key128,parameters_json:json64,token_hash:sha,created_on:time,expires_on:time,revoked_on:time?", "STU-007-DashboardScreen"),
    ];
    public const string SeedColumns = "id:id,is_frozen:flag,is_locked:flag,is_deleted:flag,entity_type:txt512,created_on:time,last_updated_on:time,optimistic_version:i64,concurrency_version:id,tenant_n_id:key128,seed_n_id:key128,seed_version:key128,seed_class:key32,scope:key128,checksum:sha,status:key32,applied_on:time?,system_data_operation_n_id:key128?,trace_id:key128?";

    public static string Quote(string identifier, DbType provider)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(identifier, "^[a-z_][a-z0-9_]{0,62}$", System.Text.RegularExpressions.RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))) throw new StudioException("STUDIO_VALIDATION", "数据库标识符不在白名单。", path: "schema");
        return provider == DbType.SqlServer ? "[" + identifier + "]" : "\"" + identifier + "\"";
    }

    public static string CreateTable(Definition table, DbType provider, string schema)
    {
        var columns = table.Columns.Split(',').Select(spec => Column(spec, provider)).ToList();
        columns.Add(table.Name == "studio_ledger_value" ? "PRIMARY KEY (record_id,field_id)" : "PRIMARY KEY (id)");
        if (table.Columns.Contains("n_id:key128,tenant_n_id:key128", StringComparison.Ordinal)) columns.Add("UNIQUE (tenant_n_id,n_id)");
        columns.AddRange(Constraints(table.Name, provider, schema));
        return "CREATE TABLE " + Quote(schema, provider) + "." + Quote(table.Name, provider) + " (" + string.Join(",", columns) + ")";
    }

    public static string Column(string spec, DbType provider)
    {
        var parts = spec.Split(':');
        var nullable = parts[1].EndsWith('?');
        var type = parts[1].TrimEnd('?');
        string sqlType;
        if (type == "id") sqlType = provider == DbType.PostgreSQL ? "uuid" : provider == DbType.SqlServer ? "uniqueidentifier" : "TEXT";
        else if (type == "flag") sqlType = provider == DbType.PostgreSQL ? "boolean" : provider == DbType.SqlServer ? "bit" : "INTEGER";
        else if (type == "time") sqlType = provider == DbType.PostgreSQL ? "timestamptz" : provider == DbType.SqlServer ? "datetimeoffset(7)" : "TEXT";
        else if (type == "i64") sqlType = provider == DbType.Sqlite ? "INTEGER" : "bigint";
        else if (type == "i32") sqlType = provider == DbType.SqlServer ? "int" : "INTEGER";
        else
        {
            var json = type.StartsWith("json", StringComparison.Ordinal);
            var length = type == "sha" ? 64 : int.Parse(type.AsSpan(type.StartsWith("key", StringComparison.Ordinal) || type.StartsWith("txt", StringComparison.Ordinal) ? 3 : 4), System.Globalization.CultureInfo.InvariantCulture);
            sqlType = provider == DbType.Sqlite ? "TEXT" : json ? provider == DbType.SqlServer ? "nvarchar(max)" : "text" : provider == DbType.SqlServer ? $"nvarchar({length})" : $"varchar({length})";
            if (provider == DbType.SqlServer && (type.StartsWith("key", StringComparison.Ordinal) || type == "sha")) sqlType += " COLLATE Latin1_General_100_BIN2";
        }
        var name = Quote(parts[0], provider);
        var result = name + " " + sqlType + (nullable ? " NULL" : " NOT NULL");
        if (provider == DbType.Sqlite && type == "flag") result += $" CHECK ({name} IN (0,1))";
        if (provider == DbType.Sqlite && (type.StartsWith("key", StringComparison.Ordinal) || type.StartsWith("txt", StringComparison.Ordinal) || type == "sha"))
        {
            var length = type == "sha" ? 64 : int.Parse(type.AsSpan(3), System.Globalization.CultureInfo.InvariantCulture);
            result += $" CHECK (length({name}) <= {length})";
        }
        return result;
    }

    private static IEnumerable<string> Constraints(string table, DbType provider, string schema)
    {
        string Fk(string column, string target) => $"FOREIGN KEY ({column}) REFERENCES {(provider == DbType.Sqlite ? string.Empty : Quote(schema, provider) + ".")}{Quote(target, provider)}(id) ON DELETE {(provider == DbType.SqlServer ? "NO ACTION" : "RESTRICT")}";
        switch (table)
        {
            case "studio_seed_ledger": yield return "UNIQUE (tenant_n_id,seed_n_id,seed_version)"; break;
            case "studio_source": yield return "CHECK (provider IN ('RegisteredApi','PostgreSQL','SqlServer','Sqlite'))"; yield return "CHECK (timeout_seconds BETWEEN 1 AND 120 AND max_rows BETWEEN 1 AND 100000 AND max_bytes BETWEEN 1 AND 52428800 AND configuration_version>=1)"; break;
            case "studio_resource": yield return "CHECK (kind IN ('Dataset','LedgerModel','LowCodePage','Dashboard','Playlist','Report','ReportBundle','TraceModel'))"; yield return "CHECK (optimistic_version>=0 AND revision_sequence>=0 AND document_schema_version=1)"; if (provider == DbType.Sqlite) { yield return Fk("draft_base_revision_id", "studio_revision"); yield return Fk("published_revision_id", "studio_revision"); } break;
            case "studio_revision": yield return Fk("resource_id", "studio_resource"); yield return "UNIQUE (resource_id,revision_no)"; yield return "CHECK (revision_no>0 AND document_schema_version=1)"; break;
            case "studio_grant": yield return Fk("resource_id", "studio_resource"); yield return "UNIQUE (resource_id,subject_kind,subject_n_id,capability)"; yield return "CHECK (subject_kind IN ('User','Role') AND capability IN ('View','Design','Publish','Run','Export'))"; break;
            case "studio_ledger_record": yield return Fk("model_resource_id", "studio_resource"); yield return Fk("schema_revision_id", "studio_revision"); break;
            case "studio_ledger_value": yield return Fk("record_id", "studio_ledger_record"); yield return Fk("model_resource_id", "studio_resource"); yield return "CHECK ((value_kind='Text' AND text_value IS NOT NULL AND number_scaled IS NULL AND bool_value IS NULL AND time_value IS NULL) OR (value_kind='Number' AND text_value IS NULL AND number_scaled IS NOT NULL AND bool_value IS NULL AND time_value IS NULL) OR (value_kind='Boolean' AND text_value IS NULL AND number_scaled IS NULL AND bool_value IS NOT NULL AND time_value IS NULL) OR (value_kind='Time' AND text_value IS NULL AND number_scaled IS NULL AND bool_value IS NULL AND time_value IS NOT NULL))"; break;
            case "studio_operation_receipt": yield return "UNIQUE (tenant_n_id,operation_kind,operation_key)"; yield return "CHECK (status IN ('Pending','Succeeded','Failed','Unknown'))"; break;
            case "studio_audit_outbox": yield return "UNIQUE (tenant_n_id,audit_event_n_id)"; yield return "CHECK (status IN ('Pending','Sent') AND attempt_count>=0)"; break;
            case "studio_report_run": yield return Fk("template_revision_id", "studio_revision"); yield return Fk("retry_of_run_id", "studio_report_run"); yield return "UNIQUE (tenant_n_id,operation_key)"; yield return "CHECK ((execution_kind='Production' AND template_revision_id IS NOT NULL AND preview_snapshot_json IS NULL AND preview_expires_on IS NULL AND preview_manifest_json IS NULL) OR (execution_kind='Preview' AND template_revision_id IS NULL AND preview_snapshot_json IS NOT NULL AND preview_expires_on IS NOT NULL AND format='Html'))"; yield return "CHECK (status IN ('PendingDispatch','Queued','Running','CancelRequested','Succeeded','Failed','Cancelled','Unknown'))"; yield return "CHECK ((retry_of_run_id IS NULL AND retry_mode IS NULL) OR (retry_of_run_id IS NOT NULL AND retry_mode IN ('RenderSnapshot','NewData') AND retry_of_run_id<>id))"; break;
            case "studio_report_artifact": yield return Fk("run_id", "studio_report_run"); yield return "UNIQUE (run_id,purpose,format)"; yield return "CHECK (purpose IN ('Output','DataSnapshot','ResourceManifest'))"; break;
            case "studio_workbook": if (provider == DbType.Sqlite) yield return Fk("current_version_id", "studio_workbook_version"); break;
            case "studio_workbook_version": yield return Fk("workbook_id", "studio_workbook"); yield return Fk("base_version_id", "studio_workbook_version"); yield return "UNIQUE (workbook_id,content_version)"; yield return "UNIQUE (workbook_id,save_operation_key)"; break;
            case "studio_workbook_session": yield return Fk("workbook_id", "studio_workbook"); yield return Fk("base_version_id", "studio_workbook_version"); yield return "CHECK (mode IN ('Edit','View') AND status IN ('Active','Saving','Conflict','Closed','Expired'))"; break;
            case "studio_workbook_retired_file": yield return Fk("workbook_id", "studio_workbook"); break;
            case "studio_auth_request": yield return "UNIQUE (state_hash)"; yield return "CHECK (status IN ('Created','Exchanging','Consumed','Failed'))"; break;
            case "studio_auth_session": yield return "UNIQUE (session_hash)"; break;
            case "studio_local_admin": yield return "UNIQUE (tenant_n_id)"; yield return "UNIQUE (tenant_n_id,login_name)"; yield return "UNIQUE (tenant_n_id,subject_n_id)"; yield return "CHECK (failed_count>=0 AND auth_version>0)"; break;
        }
    }
}
