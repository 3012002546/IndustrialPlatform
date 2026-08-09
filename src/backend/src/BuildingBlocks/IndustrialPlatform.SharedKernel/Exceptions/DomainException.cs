namespace IndustrialPlatform.SharedKernel.Exceptions;

/// <summary>
/// 领域异常基类,所有业务异常统一继承。
/// </summary>
public abstract class DomainException : Exception
{
    protected DomainException(string message)
        : base(message)
    {
    }

    protected DomainException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
