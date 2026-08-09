namespace IndustrialPlatform.SharedKernel.Exceptions;

/// <summary>资源不存在异常。</summary>
public sealed class NotFoundException : DomainException
{
    public NotFoundException(string message)
        : base(message)
    {
    }

    public NotFoundException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
