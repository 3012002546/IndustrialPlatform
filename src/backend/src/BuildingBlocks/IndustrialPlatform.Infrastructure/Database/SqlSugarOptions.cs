using SqlSugar;

namespace IndustrialPlatform.Infrastructure.Database;

/// <summary>
/// SqlSugar 数据库连接配置。
/// </summary>
public sealed class SqlSugarOptions
{
    /// <summary>连接字符串。</summary>
    public string? ConnectionString { get; set; }

    /// <summary>数据库类型,默认 PostgreSQL。</summary>
    public DbType DbType { get; set; } = DbType.PostgreSQL;

    /// <summary>是否自动关闭连接,默认开启。</summary>
    public bool IsAutoCloseConnection { get; set; } = true;
}
