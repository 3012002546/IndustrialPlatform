using System.Diagnostics;

namespace IndustrialPlatform.PlatformStudio.Application;

/// <summary>仅在开发环境大屏诊断Activity内记录阶段耗时，不记录主体、参数或凭据。</summary>
public static class StudioRuntimeTiming
{
    public static long Mark(string name, long started)
    {
        var ended = Stopwatch.GetTimestamp();
        if (Activity.Current?.OperationName == "studio.screen.runtime")
            Activity.Current.AddEvent(new ActivityEvent(name, tags: new ActivityTagsCollection
            {
                ["duration"] = Stopwatch.GetElapsedTime(started, ended).TotalMilliseconds,
            }));
        return ended;
    }
}
