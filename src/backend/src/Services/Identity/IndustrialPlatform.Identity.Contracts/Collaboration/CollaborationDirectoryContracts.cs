namespace IndustrialPlatform.Identity.Contracts.Collaboration;

using IndustrialPlatform.Security;

/// <summary>PF05 目录搜索的最小公开输入。</summary>
public sealed record CollaborationUserSearchRequest(string Keyword, string? Cursor, int Limit = 20);

/// <summary>PF05 目录搜索结果，不包含管理字段。</summary>
public sealed record CollaborationDirectoryUser(string UserNId, string DisplayName, bool CanStart);

/// <summary>PF05 目录键集分页结果。</summary>
public sealed record CollaborationDirectoryPage(IReadOnlyList<CollaborationDirectoryUser> Items, string? NextCursor);

/// <summary>已存在会话可用的最小身份投影。</summary>
public sealed record CollaborationDirectoryUserDetail(
    string UserNId,
    string DisplayName,
    string Status,
    string SecurityVersion,
    DateTimeOffset VerifiedOn,
    DateTimeOffset ValidUntil);

public sealed record CollaborationStepUpRequest
{
    public string? Binding { get; init; }
    public string? CurrentPassword { get; init; }
    public string? Password { get; init; }
    public string? Action { get; init; }
    public string? RequestNId { get; init; }
    public string? ScopeChecksum { get; init; }
}

public sealed record CollaborationStepUpResponse(string Proof, DateTimeOffset ExpiresAt);

public sealed record CollaborationStepUpConsumeRequest(
    string Proof,
    string Action,
    string RequestNId,
    string ScopeChecksum,
    string RequestHash);

public sealed record CollaborationStepUpConsumeResponse(string ReceiptNId, DateTimeOffset ConsumedOn);

/// <summary>Collaboration 只能消费的最小 Identity 目录端口。</summary>
public interface ICollaborationIdentityDirectory
{
    Task<CollaborationDirectoryPage> SearchAsync(
        TrustedCollaborationCall caller,
        CollaborationUserSearchRequest request,
        CancellationToken cancellationToken);

    Task<CollaborationDirectoryUserDetail> GetAsync(
        TrustedCollaborationCall caller,
        string userNId,
        CancellationToken cancellationToken);
}
