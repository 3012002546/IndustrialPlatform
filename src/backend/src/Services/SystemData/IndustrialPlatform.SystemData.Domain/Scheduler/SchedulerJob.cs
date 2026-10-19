using IndustrialPlatform.SharedKernel.Entities;
using IndustrialPlatform.SharedKernel.Exceptions;

namespace IndustrialPlatform.SystemData.Domain.Scheduler;

/// <summary>租户的一项任务及唯一周期计划。运行占用和原生状态不修改配置版本。</summary>
public sealed class SchedulerJob : Entity
{
    public string TenantNId { get; private set; }
    public string NId { get; private set; }
    public string Name { get; private set; }
    public string? Description { get; private set; }
    public string HandlerKey { get; private set; }
    public string ParametersJson { get; private set; }
    public string Status { get; private set; } = "Disabled";
    public string CronExpression { get; private set; }
    public string TimeZoneId { get; private set; }
    public int TimeoutSeconds { get; private set; }
    public int MaxAttempts { get; private set; }
    public string RetrySafety { get; private set; }
    public bool IsSystemOwned { get; private set; }

    public SchedulerJob(string tenantNId, string nId, string name, string? description,
        string handlerKey, string parametersJson, string cronExpression, string timeZoneId,
        int timeoutSeconds, int maxAttempts, string retrySafety, bool isSystemOwned = false)
    {
        TenantNId = tenantNId; NId = nId; Name = name; Description = description;
        HandlerKey = handlerKey; ParametersJson = parametersJson; CronExpression = cronExpression;
        TimeZoneId = timeZoneId; TimeoutSeconds = timeoutSeconds; MaxAttempts = maxAttempts;
        RetrySafety = retrySafety; IsSystemOwned = isSystemOwned;
    }

    /// <summary>仅在停用时修改配置，保持处理器与业务标识。</summary>
    public void Update(string name, string? description, string parametersJson, string cronExpression,
        string timeZoneId, int timeoutSeconds, int maxAttempts)
    {
        EnsureCanModify();
        if (Status != "Disabled") throw new BusinessException("请先停用任务再修改配置。");
        Name = name; Description = description; ParametersJson = parametersJson;
        CronExpression = cronExpression; TimeZoneId = timeZoneId;
        TimeoutSeconds = timeoutSeconds; MaxAttempts = maxAttempts; Touch();
    }

    /// <summary>设置目标启用状态；无实际改变不推进版本。</summary>
    public bool SetEnabled(bool enabled)
    {
        EnsureCanModify();
        var desired = enabled ? "Enabled" : "Disabled";
        if (Status == desired) return false;
        Status = desired; Touch(); return true;
    }

    /// <summary>基础实体持久化重建，仍由仓储控制租户查询。</summary>
    public void LoadState(Guid id, bool frozen, bool locked, bool deleted, string status,
        DateTimeOffset createdOn, DateTimeOffset updatedOn, long version, Guid concurrencyVersion)
    {
        Id = id; IsFrozen = frozen; IsLocked = locked; IsDeleted = deleted; Status = status;
        CreatedOn = createdOn; LastUpdatedOn = updatedOn;
        OptimisticVersion = version; ConcurrencyVersion = concurrencyVersion;
    }
}
