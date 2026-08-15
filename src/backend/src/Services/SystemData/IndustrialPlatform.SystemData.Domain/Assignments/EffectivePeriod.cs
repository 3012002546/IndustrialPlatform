using IndustrialPlatform.SharedKernel.Exceptions;
using IndustrialPlatform.SharedKernel.ValueObjects;

namespace IndustrialPlatform.SystemData.Domain.Assignments;

/// <summary>
/// 任职有效期区间值对象(05 方案 §7.4):左闭右开 <c>[EffectiveFrom, EffectiveTo)</c>。
/// <c>EffectiveTo</c> 可空(表示开放区间);非空时必须晚于 <c>EffectiveFrom</c>。
/// </summary>
public sealed class EffectivePeriod : ValueObject
{
    /// <summary>区间开始(含)。</summary>
    public DateTimeOffset EffectiveFrom { get; }

    /// <summary>区间结束(不含,可空表示开放)。</summary>
    public DateTimeOffset? EffectiveTo { get; }

    private EffectivePeriod(DateTimeOffset effectiveFrom, DateTimeOffset? effectiveTo)
    {
        EffectiveFrom = effectiveFrom;
        EffectiveTo = effectiveTo;
    }

    /// <summary>创建并校验区间;结束时间非空时晚于开始时间。</summary>
    public static EffectivePeriod Create(DateTimeOffset effectiveFrom, DateTimeOffset? effectiveTo)
    {
        if (effectiveTo is { } to && to <= effectiveFrom)
        {
            throw new ValidationException("任职结束时间必须晚于开始时间。");
        }

        return new EffectivePeriod(effectiveFrom, effectiveTo);
    }

    /// <summary>时刻是否落在区间内(左闭右开)。</summary>
    public bool Contains(DateTimeOffset instant) =>
        EffectiveFrom <= instant && (EffectiveTo is null || instant < EffectiveTo.Value);

    /// <summary>两区间是否重叠(左闭右开):A.From &lt; B.To 且 B.From &lt; A.To。</summary>
    public bool OverlapsWith(EffectivePeriod other)
    {
        var aEnd = EffectiveTo ?? DateTimeOffset.MaxValue;
        var bEnd = other.EffectiveTo ?? DateTimeOffset.MaxValue;
        return EffectiveFrom < bEnd && other.EffectiveFrom < aEnd;
    }

    /// <inheritdoc />
    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return EffectiveFrom;
        yield return EffectiveTo ?? DateTimeOffset.MinValue;
    }
}
