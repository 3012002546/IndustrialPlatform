using System.Data.Common;
using System.Globalization;
using System.Text.Json.Nodes;
using IndustrialPlatform.PlatformStudio.Application.Data;
using IndustrialPlatform.PlatformStudio.Domain;
using IndustrialPlatform.Querying.Schema;

namespace IndustrialPlatform.PlatformStudio.Infrastructure.Data;

/// <summary>只查已准入对象元数据；与执行器共用实际目标和只读连接，不扫描全库。</summary>
public sealed class RegisteredSourceCatalog(RegisteredSourceExecutor executor, IEnumerable<StudioSourceCatalogRegistration> registrations) : IStudioSourceCatalogReader
{
    private readonly StudioSourceCatalogRegistration[] _registrations = registrations.ToArray();

    public static StudioSourceCatalogRegistration FromQuerying(string endpointKey, string operationKey, string permissionKey, QueryResourceDefinition definition)
    {
        var fields = new JsonArray(definition.Fields.Where(pair => pair.Value.Selectable).Select(pair => (JsonNode)new JsonObject
        {
            ["id"] = pair.Key.Length <= 64 ? pair.Key : StudioJson.Hash(pair.Key), ["name"] = pair.Key, ["label"] = pair.Key,
            ["type"] = pair.Value.ValueType switch { QueryValueType.Number => "Decimal", QueryValueType.Boolean => "Boolean", QueryValueType.Date => "DateTime", _ => "String" }, ["nullable"] = true,
        }).ToArray());
        return new(endpointKey, operationKey, "ApiOperation", permissionKey, new JsonObject { ["id"] = "catalog-root", ["name"] = "rows", ["type"] = "Array", ["nullable"] = false, ["item"] = new JsonObject { ["id"] = "catalog-row", ["name"] = "row", ["type"] = "Object", ["nullable"] = false, ["children"] = fields } });
    }

    public async Task<IReadOnlyList<StudioSourceCatalogObject>> ReadAsync(StudioPrincipal principal, StudioSource source, CancellationToken cancellationToken)
    {
        var endpoint = executor.Endpoint(source);
        var allowedArray = JsonNode.Parse(source.AllowedObjectsJson) as JsonArray ?? throw new StudioException("STUDIO_NOT_READY", "来源对象目录配置无效。", 503);
        if (allowedArray.Any(item => item is not JsonObject)) throw new StudioException("STUDIO_NOT_READY", "来源对象目录项无效。", 503);
        var allowed = allowedArray.Cast<JsonObject>().ToArray();
        var selected = _registrations.Where(registration => registration.EndpointKey == source.EndpointKey && principal.HasPermission(registration.PermissionKey) && allowed.Any(item => StudioJson.Text(item["qualifiedName"]) == registration.ObjectName && EquivalentKind(StudioJson.Text(item["kind"]), registration.Kind))).ToArray();
        if (selected.Select(item => item.ObjectName).Distinct(StringComparer.Ordinal).Count() != selected.Length) throw new StudioException("STUDIO_NOT_READY", "来源目录存在重复代码登记。", 503);
        if (selected.Length > 200) throw new StudioException("STUDIO_LIMIT", "来源目录超过200个公开对象预算。", 429);
        var output = new List<StudioSourceCatalogObject>();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Min(source.TimeoutSeconds, 30)));
        DbConnection? connection = null;
        try
        {
            foreach (var registration in selected.OrderBy(item => item.ObjectName, StringComparer.Ordinal))
            {
                var errors = new List<StudioDiagnostic>();
                StudioDocuments.ValidateFieldSchema(registration.ResultSchema, "catalog.resultSchema", new(StringComparer.Ordinal), errors);
                if (errors.Count > 0) throw new StudioException("STUDIO_NOT_READY", "公开目录Schema登记无效。", 503);
                var schema = PublicSchema(registration.ResultSchema);
                if (schema is null) continue;
                var parameters = PublicParameters(registration.Parameters);
                if (source.Provider == "RegisteredApi")
                {
                    if (!endpoint.Operations.TryGetValue(registration.ObjectName, out var operation) || !operation.ReadOnly || operation.Method is not ("GET" or "POST")) continue;
                    output.Add(new(registration.ObjectName, "ApiOperation", parameters, schema, []));
                    continue;
                }
                if (registration.Kind == "Procedure")
                {
                    if (!endpoint.AllowedProcedures.Contains(registration.ObjectName, StringComparer.Ordinal) || !endpoint.ProcedureDefinitionHashes.TryGetValue(registration.ObjectName, out var approved)) continue;
                    if (source.Provider == "Sqlite") continue;
                    if (connection is null)
                    {
                        connection = executor.CreateConnection(endpoint);
                        await connection.OpenAsync(timeout.Token);
                        await RegisteredSourceExecutor.VerifyReadOnlyAsync(connection, source, endpoint, timeout.Token);
                    }
                    await using var verify = connection.CreateCommand();
                    verify.CommandTimeout = Math.Min(source.TimeoutSeconds, 30);
                    verify.CommandText = source.Provider == "SqlServer" ? "SELECT OBJECT_DEFINITION(OBJECT_ID(@name))" : "SELECT pg_get_functiondef(CAST(@name AS regprocedure))";
                    var nameParameter = verify.CreateParameter(); nameParameter.ParameterName = "@name"; nameParameter.Value = registration.ObjectName; verify.Parameters.Add(nameParameter);
                    var definition = Convert.ToString(await verify.ExecuteScalarAsync(timeout.Token), CultureInfo.InvariantCulture);
                    if (string.IsNullOrWhiteSpace(definition) || StudioJson.Hash(definition.ReplaceLineEndings("\n")) != approved) throw new StudioException("SOURCE_CHANGED", "公开过程定义已改变或不可见。", 409);
                    SqlReadOnlyPolicy.VerifyProcedureDefinition(definition);
                    output.Add(new(registration.ObjectName, registration.Kind, parameters, schema, []));
                    continue;
                }
                if (!endpoint.AllowedObjects.Contains(registration.ObjectName, StringComparer.Ordinal) || registration.Kind is not ("Table" or "View")) continue;
                if (connection is null)
                {
                    connection = executor.CreateConnection(endpoint);
                    await connection.OpenAsync(timeout.Token);
                    await RegisteredSourceExecutor.VerifyReadOnlyAsync(connection, source, endpoint, timeout.Token);
                }
                var publicNames = LeafNames(schema);
                var columns = await ColumnsAsync(connection, source.Provider, registration.ObjectName, source.TimeoutSeconds, timeout.Token);
                var visible = columns.Where(column => publicNames.Contains(column.Name)).ToArray();
                if (visible.Length != publicNames.Count) throw new StudioException("SOURCE_CHANGED", "公开目录字段已被移除或不可见。", 409);
                output.Add(new(registration.ObjectName, registration.Kind, parameters, schema, visible));
            }
            return output;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { throw new StudioException("SOURCE_TIMEOUT", "来源目录查询超时。", 504); }
        catch (DbException) { throw new StudioException("SOURCE_FAILED", "来源目录元数据不可用。", 502); }
        finally { if (connection is not null) await connection.DisposeAsync(); }
    }

    private static bool EquivalentKind(string? left, string right) => left == right || left is "Api" or "ApiOperation" && right is "Api" or "ApiOperation";
    private static JsonObject? PublicSchema(JsonObject field)
    {
        if (field["sensitive"]?.GetValue<bool>() == true) return null;
        var copy = new JsonObject();
        foreach (var key in new[] { "id", "name", "label", "type", "nullable", "format", "maxLength" })
            if (field.TryGetPropertyValue(key, out var value)) copy[key] = value?.DeepClone();
        if (field["children"] is JsonArray children) copy["children"] = new JsonArray(children.Cast<JsonObject>().Select(PublicSchema).Where(child => child is not null).Select(child => (JsonNode)child!).ToArray());
        if (field["item"] is JsonObject item)
        {
            var publicItem = PublicSchema(item);
            if (publicItem is null) return null;
            copy["item"] = publicItem;
        }
        return copy;
    }
    private static JsonArray PublicParameters(JsonArray? parameters)
    {
        var output = new JsonArray();
        foreach (var parameter in parameters ?? [])
        {
            if (parameter is not JsonObject definition) throw new StudioException("STUDIO_NOT_READY", "公开参数目录登记无效。", 503);
            if (StudioJson.Text(definition["source"]) == "TrustedContext") continue;
            var copy = new JsonObject();
            foreach (var key in new[] { "name", "label", "type", "itemType", "required", "nullable", "maxLength" })
                if (definition.TryGetPropertyValue(key, out var value)) copy[key] = value?.DeepClone();
            output.Add(copy);
        }
        return output;
    }
    private static HashSet<string> LeafNames(JsonObject schema)
    {
        if (schema["item"] is JsonObject item) return LeafNames(item);
        return (schema["children"] as JsonArray ?? []).Cast<JsonObject>().Select(field => StudioJson.RequiredString(field, "name", 128)).ToHashSet(StringComparer.Ordinal);
    }
    private static async Task<List<StudioSourceCatalogColumn>> ColumnsAsync(DbConnection connection, string provider, string objectName, int timeout, CancellationToken cancellationToken)
    {
        var parts = objectName.Split('.');
        if (parts.Length > 2 || parts.Any(part => string.IsNullOrWhiteSpace(part) || part.Length > 128)) throw new StudioException("STUDIO_NOT_READY", "代码登记对象须使用单一schema和对象名。", 503);
        var schema = parts.Length == 2 ? parts[0] : provider == "SqlServer" ? "dbo" : provider == "Sqlite" ? "main" : null;
        if (provider == "Sqlite" && schema != "main") throw new StudioException("STUDIO_FORBIDDEN", "SQLite目录仅允许main目标。", 403);
        await using var command = connection.CreateCommand();
        command.CommandTimeout = Math.Min(timeout, 30);
        command.CommandText = provider switch
        {
            "Sqlite" => "SELECT name,type,CASE WHEN \"notnull\"=0 AND pk=0 THEN 1 ELSE 0 END AS nullable,pk FROM pragma_table_xinfo(@name) WHERE hidden=0 ORDER BY cid",
            "SqlServer" => "SELECT c.name,t.name AS type,c.is_nullable AS nullable,CASE WHEN EXISTS(SELECT 1 FROM sys.index_columns ic JOIN sys.indexes i ON i.object_id=ic.object_id AND i.index_id=ic.index_id WHERE ic.object_id=c.object_id AND ic.column_id=c.column_id AND i.is_primary_key=1) THEN 1 ELSE 0 END AS pk FROM sys.columns c JOIN sys.objects o ON o.object_id=c.object_id JOIN sys.schemas s ON s.schema_id=o.schema_id JOIN sys.types t ON t.user_type_id=c.user_type_id WHERE s.name=@schema AND o.name=@name AND o.type IN ('U','V') AND c.is_hidden=0 ORDER BY c.column_id",
            "PostgreSQL" => "SELECT c.column_name AS name,c.data_type AS type,CASE WHEN c.is_nullable='YES' THEN 1 ELSE 0 END AS nullable,CASE WHEN EXISTS(SELECT 1 FROM information_schema.key_column_usage k JOIN information_schema.table_constraints t ON t.constraint_catalog=k.constraint_catalog AND t.constraint_schema=k.constraint_schema AND t.constraint_name=k.constraint_name WHERE t.constraint_type='PRIMARY KEY' AND k.table_schema=c.table_schema AND k.table_name=c.table_name AND k.column_name=c.column_name) THEN 1 ELSE 0 END AS pk FROM information_schema.columns c WHERE c.table_catalog=current_database() AND c.table_schema=COALESCE(@schema,current_schema()) AND c.table_name=@name ORDER BY c.ordinal_position",
            _ => throw new StudioException("STUDIO_NOT_READY", "来源Provider未登记目录读取能力。", 503),
        };
        var nameParameter = command.CreateParameter(); nameParameter.ParameterName = "@name"; nameParameter.Value = parts[^1]; command.Parameters.Add(nameParameter);
        if (provider != "Sqlite") { var schemaParameter = command.CreateParameter(); schemaParameter.ParameterName = "@schema"; schemaParameter.DbType = System.Data.DbType.String; schemaParameter.Value = (object?)schema ?? DBNull.Value; command.Parameters.Add(schemaParameter); }
        var columns = new List<StudioSourceCatalogColumn>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) columns.Add(new(reader.GetString(0), reader.GetString(1), Convert.ToBoolean(reader.GetValue(2), CultureInfo.InvariantCulture), Convert.ToInt32(reader.GetValue(3), CultureInfo.InvariantCulture) > 0));
        return columns;
    }
}
