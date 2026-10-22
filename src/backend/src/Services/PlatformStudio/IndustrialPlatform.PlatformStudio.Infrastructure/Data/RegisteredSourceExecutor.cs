using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using System.Security.Cryptography.X509Certificates;
using IndustrialPlatform.PlatformStudio.Application;
using IndustrialPlatform.PlatformStudio.Application.Data;
using IndustrialPlatform.PlatformStudio.Domain;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace IndustrialPlatform.PlatformStudio.Infrastructure.Data;

/// <summary>服务器登记目标的受控读取；调用者只能提供声明参数，不能替换地址、SQL或凭据。</summary>
public sealed class RegisteredSourceExecutor(StudioDeploymentProfile profile, IConfiguration configuration, HttpClient http, TimeProvider clock, bool allowLoopbackHttp = false, StudioSourceLimits? limits = null) : IStudioSourceExecutor, IDisposable
{
    private readonly StudioSourceLimits _limits = limits ?? new();
    public string ConfigurationHash(StudioSourceEndpoint endpoint)
    {
        var identity = "api";
        if (endpoint.Provider != "RegisteredApi")
        {
            var raw = ResolveConnection(endpoint);
            identity = endpoint.Provider switch
            {
                "PostgreSQL" => PgIdentity(raw), "SqlServer" => SqlIdentity(raw),
                "Sqlite" => Path.GetFullPath(new SqliteConnectionStringBuilder(raw).DataSource), _ => throw new StudioException("STUDIO_VALIDATION", "来源Provider未登记。"),
            };
        }
        // Credentials may rotate behind a stable secret reference without
        // changing the published non-secret source contract (data spec §3.1).
        return StudioJson.Hash(SourceAdministration.EndpointHash(endpoint) + "|" + identity);
    }
    private static string PgIdentity(string raw) { var value = new NpgsqlConnectionStringBuilder(raw); return value.Host + "|" + value.Port.ToString(CultureInfo.InvariantCulture) + "|" + value.Database + "|" + value.Username + "|" + value.SearchPath; }
    private static string SqlIdentity(string raw) { var value = new SqlConnectionStringBuilder(raw); return value.DataSource + "|" + value.InitialCatalog + "|" + value.UserID + "|" + value.IntegratedSecurity; }
    private string ResolveConnection(StudioSourceEndpoint endpoint) => endpoint.ConnectionStringRef is { Length: > 0 } reference && configuration[reference] is { Length: > 0 } connection ? connection : throw new StudioException("STUDIO_NOT_READY", "来源连接秘密引用无法解析。", 503);
    internal StudioSourceEndpoint Endpoint(StudioSource source)
    {
        if (!profile.SourceRegistry.TryGetValue(source.EndpointKey, out var endpoint) || endpoint.Provider != source.Provider || ConfigurationHash(endpoint) != source.EndpointConfigurationHash) throw new StudioException("SOURCE_CHANGED", "来源登记或实际目标已改变。", 409);
        if (!endpoint.ReadOnlyVerified) throw new StudioException("STUDIO_FORBIDDEN", "来源尚未完成部署侧只读准入。", 403);
        return endpoint;
    }
    public async Task<JsonObject> TestAsync(StudioSource source, CancellationToken cancellationToken)
    {
        var endpoint = Endpoint(source);
        if (source.Provider == "RegisteredApi")
        {
            var key = endpoint.ProbeOperationKey ?? throw new StudioException("STUDIO_NOT_READY", "API来源必须登记无参数只读探针操作。", 503);
            _ = await ExecuteApiAsync(endpoint, key, "GET", new(), source.MaxBytes, source.TimeoutSeconds, cancellationToken);
        }
        else
        {
            await using var connection = CreateConnection(endpoint);
            await connection.OpenAsync(cancellationToken);
            await VerifyReadOnlyAsync(connection, source, endpoint, cancellationToken);
            await using var command = connection.CreateCommand(); command.CommandText = "SELECT 1"; command.CommandTimeout = source.TimeoutSeconds;
            _ = await command.ExecuteScalarAsync(cancellationToken);
        }
        return new() { ["status"] = "Ready", ["checkedOn"] = clock.GetUtcNow().ToString("O", CultureInfo.InvariantCulture), ["capabilities"] = new JsonObject { ["readOnly"] = true, ["provider"] = source.Provider, ["procedures"] = source.Provider != "Sqlite" && endpoint.AllowedProcedures.Length > 0 }, ["diagnostics"] = new JsonArray() };
    }
    public async Task<StudioSourceResult> ExecuteAsync(StudioPrincipal principal, StudioSource source, JsonObject document, JsonObject parameters, bool preview, CancellationToken cancellationToken)
    {
        var endpoint = Endpoint(source);
        var policy = StudioJson.Text(document["rowPolicyKey"]);
        if (!(policy == "public" && endpoint.PublicQueriesAllowed) && !(policy == "authorized-factories" && endpoint.FactoryFieldPath.Length > 0 && principal.FactoryNIds.Count > 0)) throw new StudioException("STUDIO_FORBIDDEN", "当前主体或来源不能满足声明的数据范围。", 403);
        var declaredTimeout = document["timeoutSeconds"]?.GetValue<int>() ?? 30;
        if (declaredTimeout is < 1 or > 120) throw new StudioException("STUDIO_VALIDATION", "查询超时预算无效。", path: "timeoutSeconds");
        var gate = _limits.ForTenant(principal.TenantNId, profile.MaxQueryConcurrencyPerTenant);
        if (!await gate.WaitAsync(0, cancellationToken)) throw new StudioException("STUDIO_LIMIT", "当前租户来源并发已达上限。", 429);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Min(profile.DefaultQueryTimeoutSeconds, Math.Min(source.TimeoutSeconds, declaredTimeout))));
        var token = timeout.Token;
        var start = Stopwatch.GetTimestamp();
        try
        {
            var query = document["query"] as JsonObject ?? throw new StudioException("STUDIO_VALIDATION", "查询合同缺失。", path: "query");
            var type = StudioJson.Text(query["kind"]) ?? StudioJson.Text(query["type"]);
            JsonNode? data;
            if (type == "Api")
            {
                var operation = StudioJson.RequiredString(query, "operationKey");
                EnsureAllowed(source, operation, "Api");
                data = await ExecuteApiAsync(endpoint, operation, StudioJson.Text(query["method"]) ?? "GET", parameters, Math.Min(source.MaxBytes, StudioJson.Version(StudioJson.Text(document["maxBytes"]) ?? "10485760", "maxBytes")), source.TimeoutSeconds, token);
            }
            else if (type is "Sql" or "Procedure") data = await ExecuteDatabaseAsync(source, endpoint, query, parameters, document, preview, token);
            else throw new StudioException("STUDIO_VALIDATION", "查询种类未登记。", path: "query.kind");
            var observed = clock.GetUtcNow();
            data = Scope(principal, endpoint, document, data);
            var maxRows = Math.Min(source.MaxRows, document["maxRows"]?.GetValue<int>() ?? 10000);
            if (preview) data = Sample(data, Math.Min(maxRows, 100));
            var rows = CountRows(data);
            if (maxRows is < 1 or > 100000 || rows > maxRows) throw new StudioException("STUDIO_LIMIT", "查询行数超过允许预算。", 429);
            if (preview && rows > 100) throw new StudioException("STUDIO_LIMIT", "设计试查最多100行，请使用声明的分页/限制参数收敛样本。", 429);
            return new(data, observed, clock.GetUtcNow(), rows, (long)Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { throw new StudioException("SOURCE_TIMEOUT", "来源查询超时；未作为空集合返回。", 504); }
        catch (StudioException) { throw; }
        catch (Exception exception) when (exception is DbException or HttpRequestException or System.Text.Json.JsonException) { throw new StudioException("SOURCE_FAILED", "来源执行失败，请检查已登记目标与诊断。", 502); }
        finally { gate.Release(); }
    }
    private async Task<JsonNode?> ExecuteApiAsync(StudioSourceEndpoint endpoint, string operationKey, string method, JsonObject parameters, long byteLimit, int timeoutSeconds, CancellationToken cancellationToken)
    {
        if (!endpoint.Operations.TryGetValue(operationKey, out var operation) || !operation.ReadOnly || operation.Method != method || method is not ("GET" or "POST")) throw new StudioException("STUDIO_FORBIDDEN", "API查询操作未在无副作用白名单。", 403);
        foreach (var required in operation.RequiredParameters) if (!parameters.ContainsKey(required) || parameters[required] is null) throw new StudioException("PARAM_REQUIRED", "API操作缺少必需参数。", path: "parameters." + required);
        if (!Uri.TryCreate(endpoint.BaseAddress, UriKind.Absolute, out var baseUri) || baseUri.Scheme != "https" && !(allowLoopbackHttp && baseUri.Scheme == "http" && baseUri.IsLoopback)) throw new StudioException("STUDIO_FORBIDDEN", "API目标必须是登记的HTTPS地址。", 403);
        if (!Uri.TryCreate(baseUri, operation.Path, out var uri) || uri.Scheme != baseUri.Scheme || uri.Authority != baseUri.Authority || !string.IsNullOrEmpty(uri.Fragment)) throw new StudioException("STUDIO_FORBIDDEN", "API操作路径越出登记目标。", 403);
        if (method == "GET" && parameters.Count > 0)
        {
            var builder = new UriBuilder(uri);
            var query = string.Join('&', parameters.Select(pair => Uri.EscapeDataString(pair.Key) + "=" + Uri.EscapeDataString(StudioJson.Text(pair.Value) ?? pair.Value?.ToJsonString() ?? "")));
            builder.Query = string.IsNullOrEmpty(builder.Query) ? query : builder.Query.TrimStart('?') + "&" + query;
            uri = builder.Uri;
        }
        using var request = new HttpRequestMessage(method == "GET" ? HttpMethod.Get : HttpMethod.Post, uri);
        if (method == "POST") request.Content = new StringContent(parameters.ToJsonString(), Encoding.UTF8, "application/json");
        if (endpoint.CredentialRef is { Length: > 0 } reference) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", configuration[reference] ?? throw new StudioException("STUDIO_NOT_READY", "API服务凭据无法解析。", 503));
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        using var handler = new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false };
        X509Certificate2? certificate = null;
        if (endpoint.ClientCertificatePathRef is { Length: > 0 } certificateRef)
        {
            var path = configuration[certificateRef] ?? throw new StudioException("STUDIO_NOT_READY", "API客户端证书无法解析。", 503);
            var password = endpoint.ClientCertificatePasswordRef is { Length: > 0 } passwordRef ? configuration[passwordRef] ?? throw new StudioException("STUDIO_NOT_READY", "API客户端证书口令无法解析。", 503) : null;
            certificate = X509CertificateLoader.LoadPkcs12FromFile(path, password, X509KeyStorageFlags.EphemeralKeySet);
            handler.ClientCertificateOptions = ClientCertificateOption.Manual;
            handler.ClientCertificates.Add(certificate);
        }
        using var certificateLifetime = certificate;
        using var client = new HttpClient(handler) { Timeout = http.Timeout };
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        if ((int)response.StatusCode is >= 300 and < 400 || !response.IsSuccessStatusCode) throw new StudioException("SOURCE_FAILED", "登记API未返回成功JSON，未跟随重定向。", 502);
        if (response.Content.Headers.ContentLength > byteLimit) throw new StudioException("STUDIO_LIMIT", "来源响应字节数超过预算。", 429);
        await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
        using var buffer = new MemoryStream();
        var bytes = new byte[16384]; int count;
        while ((count = await stream.ReadAsync(bytes, timeout.Token)) > 0) { if (buffer.Length + count > byteLimit) throw new StudioException("STUDIO_LIMIT", "来源响应字节数超过预算。", 429); await buffer.WriteAsync(bytes.AsMemory(0, count), timeout.Token); }
        buffer.Position = 0;
        return await JsonNode.ParseAsync(buffer, cancellationToken: timeout.Token);
    }
    internal DbConnection CreateConnection(StudioSourceEndpoint endpoint)
    {
        var raw = ResolveConnection(endpoint);
        return endpoint.Provider switch
        {
            "PostgreSQL" => new NpgsqlConnection(raw), "SqlServer" => new SqlConnection(raw),
            "Sqlite" => new SqliteConnection(new SqliteConnectionStringBuilder(raw) { Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ConnectionString),
            _ => throw new StudioException("STUDIO_VALIDATION", "来源不支持数据库连接。"),
        };
    }
    internal static async Task VerifyReadOnlyAsync(DbConnection connection, StudioSource source, StudioSourceEndpoint endpoint, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand(); command.CommandTimeout = source.TimeoutSeconds;
        if (source.Provider == "Sqlite") command.CommandText = "PRAGMA query_only=ON";
        else if (source.Provider == "PostgreSQL") command.CommandText = "SET default_transaction_read_only=on";
        else command.CommandText = "SELECT CASE WHEN COALESCE(IS_ROLEMEMBER('db_owner'),1)=0 AND COALESCE(IS_ROLEMEMBER('db_datawriter'),1)=0 AND COALESCE(IS_SRVROLEMEMBER('sysadmin'),1)=0 THEN 1 ELSE 0 END";
        var readOnly = source.Provider == "SqlServer" ? Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture) == 1 : await command.ExecuteNonQueryAsync(cancellationToken) >= -1;
        if (!readOnly) throw new StudioException("STUDIO_FORBIDDEN", "数据库账号未满足最小只读权限。", 403);
        if (source.Provider == "SqlServer")
        {
            foreach (var name in endpoint.AllowedObjects)
            {
                await using var permission = connection.CreateCommand();
                permission.CommandText = "SELECT CASE WHEN COALESCE(HAS_PERMS_BY_NAME(@object,'OBJECT','INSERT'),1)=0 AND COALESCE(HAS_PERMS_BY_NAME(@object,'OBJECT','UPDATE'),1)=0 AND COALESCE(HAS_PERMS_BY_NAME(@object,'OBJECT','DELETE'),1)=0 AND COALESCE(HAS_PERMS_BY_NAME(@object,'OBJECT','ALTER'),1)=0 THEN 1 ELSE 0 END";
                var p = permission.CreateParameter(); p.ParameterName = "@object"; p.Value = name; permission.Parameters.Add(p);
                if (Convert.ToInt32(await permission.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture) != 1) throw new StudioException("STUDIO_FORBIDDEN", "来源对象存在写入权限或权限无法核实。", 403);
            }
        }
    }
    private async Task<JsonNode?> ExecuteDatabaseAsync(StudioSource source, StudioSourceEndpoint endpoint, JsonObject query, JsonObject parameters, JsonObject document, bool preview, CancellationToken cancellationToken)
    {
        await using var connection = CreateConnection(endpoint);
        await connection.OpenAsync(cancellationToken);
        await VerifyReadOnlyAsync(connection, source, endpoint, cancellationToken);
        await using var command = connection.CreateCommand(); command.CommandTimeout = Math.Min(source.TimeoutSeconds, document["timeoutSeconds"]?.GetValue<int>() ?? 30);
        var kind = StudioJson.Text(query["kind"]) ?? StudioJson.Text(query["type"]);
        if (kind == "Procedure")
        {
            var name = StudioJson.RequiredString(query, "qualifiedName", 256);
            EnsureAllowed(source, name, "Procedure");
            if (source.Provider == "Sqlite" || !endpoint.AllowedProcedures.Contains(name, StringComparer.Ordinal) || !endpoint.ProcedureDefinitionHashes.TryGetValue(name, out var approvedHash)) throw new StudioException("STUDIO_FORBIDDEN", "具名过程缺少只读定义摘要准入。", 403);
            await using var verify = connection.CreateCommand();
            verify.CommandText = source.Provider == "SqlServer" ? "SELECT OBJECT_DEFINITION(OBJECT_ID(@name))" : "SELECT pg_get_functiondef(CAST(@name AS regprocedure))";
            var parameter = verify.CreateParameter(); parameter.ParameterName = "@name"; parameter.Value = name; verify.Parameters.Add(parameter);
            var definition = Convert.ToString(await verify.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
            if (definition is null || StudioJson.Hash(definition.ReplaceLineEndings("\n")) != approvedHash) throw new StudioException("SOURCE_CHANGED", "过程定义已改变或无法核实，拒绝执行。", 409);
            SqlReadOnlyPolicy.VerifyProcedureDefinition(definition);
            command.CommandType = CommandType.StoredProcedure; command.CommandText = name;
        }
        else
        {
            var allowed = (JsonNode.Parse(source.AllowedObjectsJson) as JsonArray ?? []).OfType<JsonObject>().Select(item => StudioJson.RequiredString(item, "qualifiedName", 256)).Where(name => endpoint.AllowedObjects.Contains(name, StringComparer.Ordinal)).ToArray();
            command.CommandText = SqlReadOnlyPolicy.Validate(StudioJson.Text(query["statement"]), allowed);
        }
        foreach (var pair in parameters)
        {
            if (!System.Text.RegularExpressions.Regex.IsMatch(pair.Key, "^[A-Za-z_][A-Za-z0-9_]{0,127}$", System.Text.RegularExpressions.RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))) throw new StudioException("STUDIO_VALIDATION", "查询参数名非法。", path: "parameters");
            var parameter = command.CreateParameter(); parameter.ParameterName = "@" + pair.Key;
            var declaredName = pair.Key;
            var mapping = (query["parameters"] as JsonArray ?? query["parameterMap"] as JsonArray ?? []).OfType<JsonObject>().FirstOrDefault(item => StudioJson.Text(item["target"]) == pair.Key);
            if (mapping?["source"] is JsonObject binding && StudioJson.Text(binding["from"]) == "PageParam" && binding["path"] is JsonArray { Count: 1 } path) declaredName = StudioJson.Text(path[0]) ?? pair.Key;
            var declared = (document["parameters"] as JsonArray ?? []).OfType<JsonObject>().FirstOrDefault(p => StudioJson.Text(p["name"]) == declaredName);
            var declaredType = StudioJson.Text(declared?["type"]);
            declaredType = StudioJson.Text(mapping?["source"]?["transform"]?["kind"]) switch { "ToDecimal" => "Decimal", "ToString" or "DateFormat" => "String", "EnumMap" => null, _ => declaredType };
            System.Data.DbType? parameterType = declaredType switch { "Int64" => System.Data.DbType.Int64, "Decimal" => System.Data.DbType.Decimal, "Int32" => System.Data.DbType.Int32, "Boolean" => System.Data.DbType.Boolean, "Date" => System.Data.DbType.Date, "DateTime" => System.Data.DbType.DateTime, "Guid" => System.Data.DbType.Guid, "String" => System.Data.DbType.String, _ => null };
            if (parameterType.HasValue) parameter.DbType = parameterType.Value;
            parameter.Value = declaredType switch
            {
                "Int64" when pair.Value is not null => long.Parse(StudioJson.Text(pair.Value)!, CultureInfo.InvariantCulture),
                "Decimal" when pair.Value is not null => decimal.Parse(StudioJson.Text(pair.Value)!, CultureInfo.InvariantCulture),
                "DateTime" when pair.Value is not null => DateTimeOffset.Parse(StudioJson.Text(pair.Value)!, CultureInfo.InvariantCulture).UtcDateTime,
                "Date" when pair.Value is not null => DateOnly.ParseExact(StudioJson.Text(pair.Value)!, "yyyy-MM-dd", CultureInfo.InvariantCulture).ToDateTime(TimeOnly.MinValue),
                "Guid" when pair.Value is not null => Guid.Parse(StudioJson.Text(pair.Value)!),
                _ => ParameterValue(pair.Value, source.Provider),
            };
            command.Parameters.Add(parameter);
        }
        var resultSets = query["resultSets"] as JsonArray;
        var collections = new List<JsonArray>();
        var limit = Math.Min(source.MaxRows, document["maxRows"]?.GetValue<int>() ?? 10000);
        if (preview) limit = Math.Min(limit, 100);
        var byteLimit = Math.Min(source.MaxBytes, StudioJson.Version(StudioJson.Text(document["maxBytes"]) ?? "10485760", "maxBytes"));
        long bytes = 0; var rows = 0;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        do
        {
            if (reader.FieldCount == 0) continue;
            var list = new JsonArray();
            while (await reader.ReadAsync(cancellationToken))
            {
                if (rows >= limit && preview) break;
                if (++rows > limit) throw new StudioException("STUDIO_LIMIT", "查询结果超过行数预算。", 429);
                var item = new JsonObject();
                for (var i = 0; i < reader.FieldCount; i++)
                {
                    var name = reader.GetName(i);
                    if (name is "__proto__" or "constructor" or "prototype" || item.ContainsKey(name)) throw new StudioException("TYPE_MISMATCH", "来源列名非法或重复。", path: "result");
                    item[name] = reader.IsDBNull(i) ? null : Cell(reader.GetValue(i));
                }
                bytes += Encoding.UTF8.GetByteCount(item.ToJsonString());
                if (bytes > byteLimit) throw new StudioException("STUDIO_LIMIT", "查询结果超过字节预算。", 429);
                list.Add(item);
            }
            collections.Add(list);
        } while (await reader.NextResultAsync(cancellationToken));
        if (kind != "Procedure") return collections.Count == 1 ? collections[0] : throw new StudioException("TYPE_MISMATCH", "SQL查询必须恰有一个结果集。", path: "result");
        if (resultSets is null || resultSets.Count != collections.Count) throw new StudioException("TYPE_MISMATCH", "过程结果集数量与合同不一致。", path: "query.resultSets");
        var result = new JsonObject();
        for (var index = 0; index < collections.Count; index++)
        {
            var set = resultSets[index] as JsonObject ?? throw new StudioException("TYPE_MISMATCH", "结果集配置无效。");
            if (set["index"]?.GetValue<int>() != index) throw new StudioException("TYPE_MISMATCH", "结果集顺序与合同不一致。");
            result[StudioJson.RequiredString(set, "name")] = collections[index];
        }
        return result;
    }
    private static object ParameterValue(JsonNode? value, string provider)
    {
        if (value is null) return DBNull.Value;
        if (value is JsonArray array) return provider == "PostgreSQL" && array.All(item => StudioJson.Text(item) is not null) ? array.Select(item => StudioJson.Text(item)!).ToArray() : array.ToJsonString();
        if (value is JsonValue scalar)
        {
            if (scalar.TryGetValue<string>(out var text)) return text;
            if (scalar.TryGetValue<bool>(out var flag)) return flag;
            if (scalar.TryGetValue<int>(out var number)) return number;
        }
        throw new StudioException("TYPE_MISMATCH", "查询参数不是允许的标量或数组。", path: "parameters");
    }
    private static JsonNode? Cell(object value) => value switch
    {
        decimal number => JsonValue.Create(number.ToString(CultureInfo.InvariantCulture)), long number => JsonValue.Create(number.ToString(CultureInfo.InvariantCulture)),
        DateTimeOffset time => JsonValue.Create(time.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)), DateTime time => JsonValue.Create(new DateTimeOffset(time.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(time, DateTimeKind.Utc) : time).ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)),
        Guid id => JsonValue.Create(id.ToString()), byte[] => throw new StudioException("TYPE_MISMATCH", "二进制字段应通过文件合同读取。"),
        _ => System.Text.Json.JsonSerializer.SerializeToNode(value),
    };
    private static JsonNode? Scope(StudioPrincipal principal, StudioSourceEndpoint endpoint, JsonObject document, JsonNode? data)
    {
        var policy = StudioJson.Text(document["rowPolicyKey"]);
        if (policy == "public" && endpoint.PublicQueriesAllowed) return data;
        if (policy != "authorized-factories" || endpoint.FactoryFieldPath.Length == 0 || principal.FactoryNIds.Count == 0) throw new StudioException("STUDIO_FORBIDDEN", "来源无法安全限制当前数据范围。", 403);
        JsonNode? rows = data;
        foreach (var segment in endpoint.RowsPath) rows = rows is JsonObject obj ? obj[segment] : null;
        if (rows is not JsonArray array) throw new StudioException("TYPE_MISMATCH", "授权行集合路径与来源不一致。", path: "rowPolicyKey");
        var filtered = new JsonArray(array.Where(row =>
        {
            JsonNode? value = row;
            foreach (var segment in endpoint.FactoryFieldPath) value = value is JsonObject obj ? obj[segment] : null;
            if (StudioJson.Text(value) is not { } factory) throw new StudioException("TYPE_MISMATCH", "来源授权字段缺失或类型已改变。", path: "rowPolicyKey");
            return principal.FactoryNIds.Contains(factory, StringComparer.Ordinal);
        }).Select(row => row?.DeepClone()).ToArray());
        if (endpoint.RowsPath.Length == 0) return filtered;
        var result = data?.DeepClone(); JsonNode? parent = result;
        foreach (var segment in endpoint.RowsPath[..^1]) parent = parent is JsonObject obj ? obj[segment] : null;
        if (parent is not JsonObject target) throw new StudioException("TYPE_MISMATCH", "授权集合父路径无效。");
        target[endpoint.RowsPath[^1]] = filtered;
        return result;
    }
    private static void EnsureAllowed(StudioSource source, string name, string kind)
    {
        if (!(JsonNode.Parse(source.AllowedObjectsJson) as JsonArray ?? []).OfType<JsonObject>().Any(item => StudioJson.Text(item["qualifiedName"]) == name && (StudioJson.Text(item["kind"]) == kind || kind == "Api" && StudioJson.Text(item["kind"]) == "ApiOperation"))) throw new StudioException("STUDIO_FORBIDDEN", "来源未允许该登记操作。", 403);
    }
    private static long CountRows(JsonNode? data) => data is JsonObject obj && !ContainsCollection(obj) ? 1L : NestedRows(data);
    private static bool ContainsCollection(JsonNode? data) => data is JsonArray || data is JsonObject obj && obj.Any(pair => ContainsCollection(pair.Value));
    private static JsonNode? Sample(JsonNode? data, int maximum)
    {
        var remaining = maximum;
        JsonNode? Visit(JsonNode? node)
        {
            if (node is JsonArray array)
            {
                var output = new JsonArray();
                foreach (var item in array)
                {
                    if (remaining <= 0) break;
                    remaining--;
                    output.Add(Visit(item));
                }
                return output;
            }
            if (node is JsonObject obj) return new JsonObject(obj.Select(pair => new KeyValuePair<string, JsonNode?>(pair.Key, Visit(pair.Value))));
            return node?.DeepClone();
        }
        return Visit(data);
    }
    private static long NestedRows(JsonNode? data) => data switch { JsonArray array => array.Count + array.Sum(NestedRows), JsonObject obj => obj.Sum(pair => NestedRows(pair.Value)), _ => 0 };
    public void Dispose() { if (limits is null) _limits.Dispose(); }
}
