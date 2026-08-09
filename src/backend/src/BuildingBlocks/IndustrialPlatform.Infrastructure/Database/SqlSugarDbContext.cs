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
            ConnectionString = value.ConnectionString,
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

        _sqlSugar = new SqlSugarScope(connectionConfig);
    }

    /// <summary>获取 SqlSugar 客户端。</summary>
    public ISqlSugarClient SqlSugar => _sqlSugar;

    /// <inheritdoc/>
    public void Dispose() => _sqlSugar.Dispose();
}
