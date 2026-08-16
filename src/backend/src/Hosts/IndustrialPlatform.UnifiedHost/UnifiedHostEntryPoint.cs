namespace IndustrialPlatform.UnifiedHost;

/// <summary>
/// 测试入口标记类型:WebApplicationFactory&lt;T&gt; 仅使用泛型参数所在程序集定位宿主启动,
/// 本类型消除多 API 程序集全局 Program 的歧义(各独立 API Host 亦有全局 Program)。
/// </summary>
public sealed class Program;
