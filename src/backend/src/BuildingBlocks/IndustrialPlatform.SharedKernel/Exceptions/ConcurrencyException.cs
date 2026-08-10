namespace IndustrialPlatform.SharedKernel.Exceptions;

/// <summary>并发冲突异常,表示双版本条件更新影响行数非预期。</summary>
public sealed class ConcurrencyException : DomainException
{
    public ConcurrencyException(string message)
        : base(message)
    {
    }

    public ConcurrencyException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
