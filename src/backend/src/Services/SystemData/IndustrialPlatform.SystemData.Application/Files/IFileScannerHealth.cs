namespace IndustrialPlatform.SystemData.Application.Files;

/// <summary>扫描器所属模块的受控只读状态；只证明端点可用，不代表文件已扫描。</summary>
public interface IFileScannerHealth
{
    Task<string> ReadHealthAsync(CancellationToken cancellationToken);
}
