using System.Text.RegularExpressions;
using IndustrialPlatform.Infrastructure.Database;
using IndustrialPlatform.PlatformStudio.Domain;
using IndustrialPlatform.PlatformStudio.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using SqlSugar;

namespace IndustrialPlatform.PlatformStudio.Infrastructure.Initialization;

internal static class StudioSchemaIntegrity
{
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(1);
    public static IEnumerable<string> ImmutableRevisionSql(StudioDatabase database)
    {
        var table = database.Table("studio_revision");
        if (database.Provider == DbType.Sqlite)
        {
            yield return $"CREATE TRIGGER st_revision_no_update BEFORE UPDATE ON {table} BEGIN SELECT RAISE(ABORT,'STUDIO_REVISION_IMMUTABLE'); END";
            yield return $"CREATE TRIGGER st_revision_no_delete BEFORE DELETE ON {table} BEGIN SELECT RAISE(ABORT,'STUDIO_REVISION_IMMUTABLE'); END";
        }
        else if (database.Provider == DbType.PostgreSQL)
        {
            var function = StudioSchema.Quote(database.Schema, database.Provider) + ".st_revision_immutable";
            yield return $"CREATE FUNCTION {function}() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'STUDIO_REVISION_IMMUTABLE'; END $$";
            yield return $"CREATE TRIGGER st_revision_immutable BEFORE UPDATE OR DELETE ON {table} FOR EACH ROW EXECUTE FUNCTION {function}()";
        }
        else yield return $"CREATE TRIGGER {StudioSchema.Quote(database.Schema, database.Provider)}.[st_revision_immutable] ON {table} INSTEAD OF UPDATE, DELETE AS BEGIN THROW 51000, 'STUDIO_REVISION_IMMUTABLE', 1; END";
    }

    public static async Task ValidatePhysicalAsync(StudioDatabase database, IReadOnlyList<StudioServiceInitializer.Migration> migrations, CancellationToken cancellationToken)
    {
        if (database.Provider == DbType.Sqlite)
        {
            // Replaying the shipped DDL in memory verifies types, CHECK/FK/UNIQUE,
            // indexes and immutable triggers without modifying the inspected target.
            await using var expected = new SqliteConnection("Data Source=:memory:");
            await expected.OpenAsync(cancellationToken);
            foreach (var migration in migrations)
            foreach (var sql in migration.Statements)
            {
                await using var create = expected.CreateCommand();
                create.CommandText = sql;
                await create.ExecuteNonQueryAsync(cancellationToken);
            }
            await using var read = expected.CreateCommand();
            read.CommandText = "SELECT name,sql FROM sqlite_master WHERE sql IS NOT NULL AND name NOT LIKE 'sqlite_%'";
            await using var reader = await read.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var name = reader.GetString(0);
                var actual = await database.Client.Ado.GetStringAsync("SELECT sql FROM sqlite_master WHERE name=@name", new { name }, cancellationToken);
                if (Normalize(reader.GetString(1)) != Normalize(actual)) throw Drift(name);
            }
            return;
        }

        foreach (var migration in migrations)
        {
            if (migration.Id == StudioWorkbookSchema.Step) StudioWorkbookSchema.Verify(database);
            if (migration.Id == StudioSchema.ScreenPolicy.Step)
            {
                var policy = StudioSchema.ScreenPolicy;
                SchemaPhysicalDriftGuard.Validate(database.Client, policy.Name, policy.Columns.Split(',').Select(column => column.Split(':')[0]).ToArray(), []);
                if (database.Provider == DbType.SqlServer) await StudioSqlServerDescriptions.VerifyAsync(database, policy, cancellationToken);
            }
            foreach (var table in StudioSchema.Core.Where(table => table.Step == migration.Id))
            {
                var target = database.Table(table.Name);
                var indexes = migration.Statements.Where(sql => sql.Contains(" ON " + target + " (", StringComparison.Ordinal)).Select(sql => Regex.Match(sql, @"INDEX [""\[]([a-z0-9_]+)", RegexOptions.CultureInvariant, Budget).Groups[1].Value).ToArray();
                SchemaPhysicalDriftGuard.Validate(database.Client, table.Name, table.Columns.Split(',').Select(column => column.Split(':')[0]).ToArray(), indexes);
                if (database.Provider == DbType.SqlServer) await StudioSqlServerDescriptions.VerifyAsync(database, table, cancellationToken);
            }
            // Optional shared runtime scripts carry their own table and index names.
            var text = string.Join('\n', migration.Statements).Replace("[", "", StringComparison.Ordinal).Replace("]", "", StringComparison.Ordinal);
            foreach (Match match in Regex.Matches(text, @"CREATE TABLE (?:IF NOT EXISTS )?(?:dbo\.)?([a-z_]+)\s*\(([\s\S]*?)\)\s*;", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, Budget))
            {
                var name = match.Groups[1].Value;
                var columns = Regex.Matches(match.Groups[2].Value, @"(?:^|\n)\s*([a-z_]+)\s+(?:TEXT|INTEGER|BIGINT|BOOLEAN|uuid|timestamptz|nvarchar|varchar|uniqueidentifier|datetimeoffset|bit|int)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, Budget).Select(column => column.Groups[1].Value).ToList();
                var additions = Regex.Matches(text, @"ALTER TABLE (?:dbo\.)?" + name + @" ADD (?:COLUMN )?(?:IF NOT EXISTS )?([a-z_]+)\s", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, Budget).Select(column => column.Groups[1].Value);
                columns.AddRange(additions);
                var indexes = Regex.Matches(text, @"CREATE (?:UNIQUE )?INDEX (?:IF NOT EXISTS )?([a-z_]+) ON (?:dbo\.)?" + name + @"\s*\(", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, Budget).Select(index => index.Groups[1].Value).ToArray();
                SchemaPhysicalDriftGuard.Validate(database.Client, name, columns, indexes);
            }
            if (migration.Id == "STU-001-Resources")
            {
                var count = database.Provider == DbType.PostgreSQL
                    ? await database.Client.Ado.GetIntAsync("SELECT count(*) FROM information_schema.triggers WHERE trigger_schema=@schema AND event_object_table='studio_revision' AND trigger_name='st_revision_immutable'", new { schema = database.Schema }, cancellationToken)
                    : await database.Client.Ado.GetIntAsync("SELECT count(*) FROM sys.triggers WHERE parent_id=OBJECT_ID(@table) AND name='st_revision_immutable' AND is_disabled=0", new { table = database.Schema + ".studio_revision" }, cancellationToken);
                if (count == 0) throw Drift("studio_revision");
            }
        }
    }

    private static string Normalize(string? sql) => Regex.Replace(Regex.Replace(sql ?? "", @"\bIF NOT EXISTS\b|\bmain\s*\.", "", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, Budget).Replace("\"", "", StringComparison.Ordinal).Replace("[", "", StringComparison.Ordinal).Replace("]", "", StringComparison.Ordinal), @"\s+", "", RegexOptions.CultureInvariant, Budget).TrimEnd(';').ToUpperInvariant();
    private static StudioException Drift(string name) => new("STUDIO_SCHEMA_DRIFT", "迁移账本与物理结构不一致：" + name, 409);
}
