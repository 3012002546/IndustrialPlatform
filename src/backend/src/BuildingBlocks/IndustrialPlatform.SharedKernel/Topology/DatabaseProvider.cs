namespace IndustrialPlatform.SharedKernel.Topology;

/// <summary>目标数据库提供程序。</summary>
public enum DatabaseProvider
{
    /// <summary>本地 SQLite(Development 专属替身)。</summary>
    Sqlite,

    /// <summary>PostgreSQL(生产与云端开发目标)。</summary>
    PostgreSQL,
}
