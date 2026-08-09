namespace IndustrialPlatform.SharedKernel.Entities;

/// <summary>
/// DDD 实体基类,提供主键、创建时间、修改时间与版本控制。
/// </summary>
public abstract class Entity
{
    /// <summary>主键。</summary>
    public Guid Id { get; protected set; }

    /// <summary>创建时间。</summary>
    public DateTimeOffset CreateTime { get; protected set; }

    /// <summary>修改时间,首次创建后为 null。</summary>
    public DateTimeOffset? ModifyTime { get; protected set; }

    /// <summary>乐观并发版本号。</summary>
    public int Version { get; protected set; }

    protected Entity()
    {
        Id = Guid.NewGuid();
        CreateTime = DateTimeOffset.UtcNow;
    }

    protected Entity(Guid id)
        : this()
    {
        Id = id;
    }

    /// <summary>更新修改时间戳并递增版本号。</summary>
    protected void Touch()
    {
        ModifyTime = DateTimeOffset.UtcNow;
        Version++;
    }
}
