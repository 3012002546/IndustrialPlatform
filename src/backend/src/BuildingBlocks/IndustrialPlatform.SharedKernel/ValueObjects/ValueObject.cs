namespace IndustrialPlatform.SharedKernel.ValueObjects;

/// <summary>
/// DDD 值对象基类:无唯一 ID、不可变,基于所有相等性组成分量的值语义。
/// </summary>
public abstract class ValueObject
{
    /// <summary>返回参与相等性比较的所有分量。</summary>
    protected abstract IEnumerable<object> GetEqualityComponents();

    public override bool Equals(object? obj)
    {
        if (obj is not ValueObject other)
        {
            return false;
        }

        return GetEqualityComponents().SequenceEqual(other.GetEqualityComponents());
    }

    public override int GetHashCode()
    {
        unchecked
        {
            return GetEqualityComponents()
                .Aggregate(17, (hash, component) => (hash * 31) + (component?.GetHashCode() ?? 0));
        }
    }

    public static bool operator ==(ValueObject? left, ValueObject? right)
        => Equals(left, right);

    public static bool operator !=(ValueObject? left, ValueObject? right)
        => !Equals(left, right);
}
