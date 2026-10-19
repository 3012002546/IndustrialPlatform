using SqlSugar;

namespace IndustrialPlatform.Identity.Infrastructure.Persistence.Migrations;

/// <summary>把历史任务前缀改为 Identity 领域表名；不复制或删除安全事实。</summary>
internal static class IdentitySecurityTableNames
{
    public static async Task ApplyAsync(ISqlSugarClient database, CancellationToken cancellationToken)
    {
        foreach (var (legacy, current) in new[]
        {
            ("pf05_service_call_nonce", "identity_service_call_nonce"),
            ("pf05_step_up_grant", "identity_step_up_grant"),
        })
        {
            var oldExists = await ExistsAsync(database, legacy);
            var newExists = await ExistsAsync(database, current);
            if (oldExists && newExists)
                throw new InvalidOperationException($"安全表改名冲突：{legacy} 和 {current} 同时存在，必须先核对数据。");
            if (!oldExists && !newExists)
                throw new InvalidOperationException($"安全表缺失：{legacy} / {current}。");
            if (oldExists)
                await database.Ado.ExecuteCommandAsync($"ALTER TABLE {legacy} RENAME TO {current}", parameters: null, cancellationToken: cancellationToken);
        }

        var sql = database.CurrentConnectionConfig.DbType == DbType.Sqlite
            ? """
              DROP INDEX IF EXISTS ix_pf05_service_call_nonce_expires;
              DROP INDEX IF EXISTS ix_pf05_step_up_grant_expiry;
              CREATE INDEX IF NOT EXISTS ix_identity_service_call_nonce_expires ON identity_service_call_nonce (expires_on);
              CREATE INDEX IF NOT EXISTS ix_identity_step_up_grant_expiry ON identity_step_up_grant (expires_on);
              """
            : """
              DO $$
              BEGIN
                IF EXISTS (SELECT 1 FROM pg_constraint WHERE conrelid='identity_service_call_nonce'::regclass AND conname='pk_pf05_service_call_nonce') THEN
                  ALTER TABLE identity_service_call_nonce RENAME CONSTRAINT pk_pf05_service_call_nonce TO pk_identity_service_call_nonce;
                END IF;
                IF EXISTS (SELECT 1 FROM pg_constraint WHERE conrelid='identity_step_up_grant'::regclass AND conname='pf05_step_up_grant_pkey') THEN
                  ALTER TABLE identity_step_up_grant RENAME CONSTRAINT pf05_step_up_grant_pkey TO pk_identity_step_up_grant;
                END IF;
                IF EXISTS (SELECT 1 FROM pg_constraint WHERE conrelid='identity_step_up_grant'::regclass AND conname='uq_pf05_step_up_grant_proof') THEN
                  ALTER TABLE identity_step_up_grant RENAME CONSTRAINT uq_pf05_step_up_grant_proof TO uq_identity_step_up_grant_proof;
                END IF;
              END $$;
              ALTER INDEX IF EXISTS ix_pf05_service_call_nonce_expires RENAME TO ix_identity_service_call_nonce_expires;
              ALTER INDEX IF EXISTS ix_pf05_step_up_grant_expiry RENAME TO ix_identity_step_up_grant_expiry;
              """;
        await database.Ado.ExecuteCommandAsync(sql, parameters: null, cancellationToken: cancellationToken);
    }

    private static async Task<bool> ExistsAsync(ISqlSugarClient database, string table) =>
        database.CurrentConnectionConfig.DbType == DbType.Sqlite
            ? await database.Ado.GetIntAsync("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=@table", new SugarParameter("@table", table)) == 1
            : await database.Ado.GetIntAsync("SELECT COUNT(*) FROM information_schema.tables WHERE table_schema=current_schema() AND table_name=@table", new SugarParameter("@table", table)) == 1;
}
