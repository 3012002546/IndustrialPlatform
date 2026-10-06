using System.Data.Common;
using IndustrialPlatform.SharedKernel.Entities;
using Microsoft.Extensions.Options;
using SqlSugar;

namespace IndustrialPlatform.Infrastructure.Database;

/// <summary>
/// SqlSugar 数据库上下文,以单例 SqlSugarScope 包装线程安全的数据访问。
/// </summary>
public sealed class SqlSugarDbContext : IDisposable
{
    private readonly SqlSugarScope _sqlSugar;

    /// <summary>
    /// 根据配置构建数据库上下文。
    /// </summary>
    /// <param name="options">数据库连接配置。</param>
    /// <exception cref="ArgumentException">连接字符串为空时抛出。</exception>
    public SqlSugarDbContext(IOptions<SqlSugarOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var value = options.Value;
        if (string.IsNullOrWhiteSpace(value.ConnectionString))
        {
            throw new ArgumentException("未配置 SqlSugar 连接字符串。", nameof(options));
        }

        var connectionConfig = new ConnectionConfig
        {
            ConnectionString = EnsureSqliteForeignKeys(value),
            DbType = value.DbType,
            IsAutoCloseConnection = value.IsAutoCloseConnection,
            ConfigureExternalServices = new ConfigureExternalServices
            {
                EntityService = (property, column) =>
                {
                    if (property.Name == nameof(Entity.Id))
                    {
                        column.IsPrimarykey = true;
                    }
                }
            }
        };

        DbType = value.DbType;
        if (value.CommandTimeoutSeconds is { } seconds)
        {
            if (seconds <= 0) throw new ArgumentOutOfRangeException(nameof(options), "数据库命令超时必须为正数。");
            // 在每个作用域创建其客户端时配置，避免构造时生成可被子任务继承的共享上下文。
            _sqlSugar = new SqlSugarScope(connectionConfig, client => client.Ado.CommandTimeOut = seconds);
        }
        else _sqlSugar = new SqlSugarScope(connectionConfig);
    }

    private static string EnsureSqliteForeignKeys(SqlSugarOptions options)
    {
        if (options.DbType != DbType.Sqlite)
        {
            return options.ConnectionString!;
        }

        var builder = new DbConnectionStringBuilder { ConnectionString = options.ConnectionString };
        builder["Foreign Keys"] = true;
        return builder.ConnectionString!;
    }

    /// <summary>获取 SqlSugar 客户端。</summary>
    public ISqlSugarClient SqlSugar => _sqlSugar;

    /// <summary>启动时选择的不可变 Provider；读取它不会提前创建 SqlSugar 的执行上下文。</summary>
    public DbType DbType { get; }

    /// <inheritdoc/>
    public void Dispose() => _sqlSugar.Dispose();
}
