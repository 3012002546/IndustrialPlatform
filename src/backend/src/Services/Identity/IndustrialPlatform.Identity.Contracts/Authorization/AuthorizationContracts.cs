namespace IndustrialPlatform.Identity.Contracts.Authorization;

/// <summary>单权限评估请求。调用方只提交权限标识，用户与租户上下文始终取自 Bearer Token。</summary>
public sealed record EvaluatePermissionRequest(string? PermissionNId);

/// <summary>单权限评估结果。</summary>
public sealed record EvaluatePermissionResponse(bool Allowed, string Reason);
