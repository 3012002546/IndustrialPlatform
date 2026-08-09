namespace IndustrialPlatform.SharedKernel.Exceptions;

/// <summary>业务规则异常,如状态不允许、编码重复等。</summary>
public sealed class BusinessException : DomainException
{
    public BusinessException(string message)
        : base(message)
    {
    }

    public BusinessException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
