namespace IndustrialPlatform.SharedKernel.Exceptions;

/// <summary>参数校验异常。</summary>
public sealed class ValidationException : DomainException
{
    public ValidationException(string message)
        : base(message)
    {
    }

    public ValidationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
