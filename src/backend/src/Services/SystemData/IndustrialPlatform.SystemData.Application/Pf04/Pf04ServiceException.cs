namespace IndustrialPlatform.SystemData.Application.Pf04;

public sealed class Pf04ServiceException : Exception
{
    public Pf04ServiceException(string code, string message, int statusCode = 400)
        : base(message)
    {
        Code = code;
        StatusCode = statusCode;
    }

    public string Code { get; }

    public int StatusCode { get; }
}
