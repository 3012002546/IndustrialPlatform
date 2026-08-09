namespace IndustrialPlatform.SharedKernel.Exceptions;

/// <summary>身份认证或权限不足异常。</summary>
public sealed class UnauthorizedException : DomainException
{
    public UnauthorizedException(string message)
        : base(message)
    {
    }

    public UnauthorizedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
