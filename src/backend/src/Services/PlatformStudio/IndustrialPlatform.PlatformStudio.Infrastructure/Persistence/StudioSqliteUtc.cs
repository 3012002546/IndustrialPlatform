using System.Collections.Concurrent;
using System.Reflection;
using SqlSugar;

namespace IndustrialPlatform.PlatformStudio.Infrastructure.Persistence;

/// <summary>SQLite DATETIME不带offset；Studio合同固定写UTC，恢复时按UTC墙钟解释，避免ORM套用机器时区。</summary>
public static class StudioSqliteUtc
{
    private static readonly ConcurrentDictionary<Type,PropertyInfo[]> Properties=new();
    public static T Normalize<T>(T entity,DbType provider)
    {
        if(provider!=DbType.Sqlite||entity is null)return entity;
        var properties=Properties.GetOrAdd(entity.GetType(),type=>type.GetProperties().Where(p=>p.CanRead&&p.CanWrite&&(p.PropertyType==typeof(DateTimeOffset)||p.PropertyType==typeof(DateTimeOffset?))).ToArray());
        foreach(var property in properties)
            if(property.GetValue(entity) is DateTimeOffset value)property.SetValue(entity,new DateTimeOffset(DateTime.SpecifyKind(value.DateTime,DateTimeKind.Utc)));
        return entity;
    }
    public static async Task<T> FirstUtcAsync<T>(this ISugarQueryable<T> query,DbType provider,CancellationToken cancellationToken) where T:class,new()=>Normalize(await query.FirstAsync(cancellationToken),provider);
    public static async Task<List<T>> ToUtcListAsync<T>(this ISugarQueryable<T> query,DbType provider,CancellationToken cancellationToken) where T:class,new()
    {
        var rows=await query.ToListAsync(cancellationToken);
        foreach(var row in rows)Normalize(row,provider);
        return rows;
    }
}
