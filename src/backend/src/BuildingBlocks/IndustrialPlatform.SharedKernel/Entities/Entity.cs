using IndustrialPlatform.SharedKernel.Exceptions;

namespace IndustrialPlatform.SharedKernel.Entities;

/// <summary>
/// DDD 实体基类,提供生命周期状态、审计时间、实体类型与双版本并发控制。
/// </summary>
public abstract class Entity
{
    /// <summary>主键。</summary>
    public Guid Id { get; protected set; }

    /// <summary>是否已冻结(禁止普通业务修改)。</summary>
    public bool IsFrozen { get; protected set; }

    /// <summary>是否已锁定(禁止管理操作外的任何修改)。</summary>
    public bool IsLocked { get; protected set; }

    /// <summary>是否已软删除。</summary>
    public bool IsDeleted { get; protected set; }

    /// <summary>具体运行时实体的完整类型名,无法提供完整名时回退为短名。</summary>
    public string EntityType { get; protected set; }

    /// <summary>创建时间,与 LastUpdatedOn 在构造时取同一个 UTC 值。</summary>
    public DateTimeOffset CreatedOn { get; protected set; }

    /// <summary>最近一次实际状态变化的 UTC 时间。</summary>
    public DateTimeOffset LastUpdatedOn { get; protected set; }

    /// <summary>乐观并发版本号,每次实际变化递增。</summary>
    public long OptimisticVersion { get; protected set; }

    /// <summary>并发令牌,每次实际变化重新生成,创建时不为空。</summary>
    public Guid ConcurrencyVersion { get; protected set; }

    protected Entity()
    {
        Id = Guid.NewGuid();
        EntityType = GetType().FullName ?? GetType().Name;
        var now = DateTimeOffset.UtcNow;
        CreatedOn = now;
        LastUpdatedOn = now;
        ConcurrencyVersion = Guid.NewGuid();
    }

    protected Entity(Guid id)
        : this()
    {
        Id = id;
    }

    /// <summary>
    /// 派生实体在修改自身字段前调用,集中校验删除、锁定与冻结状态。
    /// </summary>
    protected void EnsureCanModify()
    {
        ThrowIfDeleted(OperationName.Modify);
        ThrowIfLocked(OperationName.Modify);
        ThrowIfFrozen(OperationName.Modify);
    }

    /// <summary>普通业务修改完成后调用,推进审计与并发字段。</summary>
    protected void Touch()
    {
        Advance();
    }

    /// <summary>冻结业务修改;已冻结时幂等。</summary>
    public void Freeze()
    {
        ThrowIfDeleted(OperationName.Freeze);
        ThrowIfLocked(OperationName.Freeze);

        if (IsFrozen)
        {
            return;
        }

        IsFrozen = true;
        Advance();
    }

    /// <summary>解冻业务修改;未冻结时幂等。</summary>
    public void Unfreeze()
    {
        ThrowIfDeleted(OperationName.Unfreeze);
        ThrowIfLocked(OperationName.Unfreeze);

        if (!IsFrozen)
        {
            return;
        }

        IsFrozen = false;
        Advance();
    }

    /// <summary>锁定管理操作;已锁定时幂等。</summary>
    public void Lock()
    {
        ThrowIfDeleted(OperationName.Lock);
        ThrowIfFrozen(OperationName.Lock);

        if (IsLocked)
        {
            return;
        }

        IsLocked = true;
        Advance();
    }

    /// <summary>解锁管理操作;未锁定时幂等。</summary>
    public void Unlock()
    {
        ThrowIfDeleted(OperationName.Unlock);

        if (!IsLocked)
        {
            return;
        }

        IsLocked = false;
        Advance();
    }

    /// <summary>标记软删除;已删除时幂等。</summary>
    public void MarkDeleted()
    {
        if (IsDeleted)
        {
            return;
        }

        ThrowIfLocked(OperationName.Delete);
        ThrowIfFrozen(OperationName.Delete);

        IsDeleted = true;
        Advance();
    }

    /// <summary>恢复软删除实体;未删除时幂等。</summary>
    public void Restore()
    {
        if (!IsDeleted)
        {
            return;
        }

        IsDeleted = false;
        Advance();
    }

    /// <summary>统一推进更新时间与双版本字段。</summary>
    private void Advance()
    {
        LastUpdatedOn = DateTimeOffset.UtcNow;
        OptimisticVersion++;
        ConcurrencyVersion = Guid.NewGuid();
    }

    private void ThrowIfDeleted(string operation)
    {
        if (IsDeleted)
        {
            throw new BusinessException($"实体 {EntityType} 已删除,禁止{operation}。");
        }
    }

    private void ThrowIfLocked(string operation)
    {
        if (IsLocked)
        {
            throw new BusinessException($"实体 {EntityType} 已锁定,禁止{operation}。");
        }
    }

    private void ThrowIfFrozen(string operation)
    {
        if (IsFrozen)
        {
            throw new BusinessException($"实体 {EntityType} 已冻结,禁止{operation}。");
        }
    }

    private static class OperationName
    {
        public const string Modify = "修改";
        public const string Freeze = "冻结";
        public const string Unfreeze = "解冻";
        public const string Lock = "锁定";
        public const string Unlock = "解锁";
        public const string Delete = "删除";
        public const string Restore = "恢复";
    }
}
