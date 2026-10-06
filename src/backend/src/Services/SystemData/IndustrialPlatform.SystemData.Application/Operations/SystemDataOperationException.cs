namespace IndustrialPlatform.SystemData.Application.Operations;

public sealed class SystemDataOperationException : Exception
{
    public SystemDataOperationException(string code, string message, int statusCode = 400)
        : base(message)
    {
        Code = code;
        StatusCode = statusCode;
    }

    public string Code { get; }

    public int StatusCode { get; }
}
