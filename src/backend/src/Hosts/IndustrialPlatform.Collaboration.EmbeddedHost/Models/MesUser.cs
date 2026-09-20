namespace IndustrialPlatform.Collaboration.EmbeddedHost.Models;

/// <summary>MES 人员接口只需返回这两个字段。</summary>
public sealed record MesUser(string UserId, string UserName);
