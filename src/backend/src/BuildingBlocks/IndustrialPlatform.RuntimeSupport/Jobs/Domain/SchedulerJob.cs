using IndustrialPlatform.SharedKernel.Entities;
using IndustrialPlatform.SharedKernel.Exceptions;

namespace IndustrialPlatform.SystemData.Domain.Scheduler;

/// <summary>租户的一项任务及唯一周期计划。运行占用和原生状态不修改配置版本。</summary>
public sealed class SchedulerJob : Entity
{
    /// <summary>Recurring为原周期任务，OneShot为所属业务管理的不可编辑一次执行。</summary>
    public string JobKind { get; private set; } = "Recurring";
    public string? OwnerService { get; private set; }
    public string? OwnerObjectNId { get; private set; }
    public void LoadOwnership(string kind, string? service, string? objectNId)
    { JobKind = kind; OwnerService = service; OwnerObjectNId = objectNId; }
    public void MakeOneShot(string service, string objectNId)
    {
        if (Status != "Disabled" || MaxAttempts != 1 || RetrySafety != "NoAutomaticRecovery") throw new BusinessException("一次性任务配置无效。");
        JobKind = "OneShot"; OwnerService = service; OwnerObjectNId = objectNId; IsSystemOwned = true;
    }
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
        if (JobKind == "OneShot") throw new BusinessException("一次性任务由所属业务管理，不允许编辑或启停。");
        if (Status != "Disabled") throw new BusinessException("请先停用任务再修改配置。");
        Name = name; Description = description; ParametersJson = parametersJson;
        CronExpression = cronExpression; TimeZoneId = timeZoneId;
        TimeoutSeconds = timeoutSeconds; MaxAttempts = maxAttempts; Touch();
    }

    /// <summary>设置目标启用状态；无实际改变不推进版本。</summary>
    public bool SetEnabled(bool enabled)
    {
        EnsureCanModify();
        if (JobKind == "OneShot") throw new BusinessException("一次性任务由所属业务管理，不允许编辑或启停。");
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
