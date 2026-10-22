using IndustrialPlatform.PlatformStudio.Domain;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Npgsql;
namespace IndustrialPlatform.PlatformStudio.Infrastructure.Persistence;

/// <summary>仅将数据库明确的唯一冲突转成可处理领域错误，不泄漏SQL或连接信息。</summary>
public static class StudioStorageErrors
{
    public static bool IsUnique(Exception exception)
    {
        for(Exception? current=exception;current is not null;current=current.InnerException)
            if(current is SqliteException { SqliteErrorCode:19 } sqlite&&sqlite.SqliteExtendedErrorCode is 1555 or 2067||current is PostgresException { SqlState:"23505" }||current is SqlException { Number:2601 or 2627 })return true;
        return false;
    }
    public static StudioException Unique()=>new("STUDIO_UNIQUE_CONFLICT","唯一值已被另一条活动记录使用，请修改后重试。",409);
}
