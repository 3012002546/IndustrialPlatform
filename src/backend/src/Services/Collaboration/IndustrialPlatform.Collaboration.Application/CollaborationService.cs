using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IndustrialPlatform.Collaboration.Contracts;
using IndustrialPlatform.Collaboration.Domain;
using IndustrialPlatform.Security;

namespace IndustrialPlatform.Collaboration.Application;

public sealed class CollaborationService
{
    private readonly ICollaborationRepository _repository;
    private readonly ICollaborationIdentityDirectory _directory;
    private readonly ICollaborationFilePort _files;
    private readonly ICollaborationAuditPort _audit;
    private readonly ICollaborationPresence _presence;
    private readonly IComplianceStepUpVerifier _stepUp;
    private readonly IStepUpBindingIssuer _stepUpBinding;
    private readonly ICollaborationPermissionEvaluator _permissions;
    private readonly PageCursorCodec _cursor;

    public CollaborationService(
        ICollaborationRepository repository,
        ICollaborationIdentityDirectory directory,
        ICollaborationFilePort files,
        ICollaborationAuditPort audit,
        ICollaborationPresence presence,
        IComplianceStepUpVerifier stepUp,
        IStepUpBindingIssuer stepUpBinding,
        ICollaborationPermissionEvaluator permissions,
        PageCursorCodec cursor)
    {
        _repository = repository;
        _directory = directory;
        _files = files;
        _audit = audit;
        _presence = presence;
        _stepUp = stepUp;
        _stepUpBinding = stepUpBinding;
        _permissions = permissions;
        _cursor = cursor;
    }

    public async Task<CollaborationDirectoryPageDto> SearchUsersAsync(string tenantNId, string actorUserNId, string keyword, string? cursor, int limit, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(keyword) || keyword.Trim().Length > 100 || limit is < 1 or > 50)
            throw new CollaborationException(400, "COLLAB_DIRECTORY_QUERY_INVALID", "搜索关键字或分页参数无效。");
        var page = await _directory.SearchAsync(tenantNId, actorUserNId, keyword.Trim(), cursor, limit, cancellationToken);
        var items = page.Items.Where(user => !string.Equals(user.UserNId, actorUserNId, StringComparison.Ordinal))
            .Select(user => new CollaborationDirectoryUserDto(user.UserNId, user.DisplayName, string.Equals(user.Status, "Active", StringComparison.Ordinal)))
            .ToList();
        return new CollaborationDirectoryPageDto(items, page.NextCursor);
    }

    public async Task<StepUpContextDto> CreateStepUpContextAsync(string tenantNId, string actorUserNId, StepUpContextRequest request, string actorSessionNId, string actorSecurityVersion, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var action = RequireNId(request.Action, "提权动作不能为空.").ToLowerInvariant();
        var allowed = new HashSet<string>(StringComparer.Ordinal)
        {
            "compliance.view",
            "compliance.read-original",
            "compliance.dispose",
            "compliance.export.request",
            "compliance.export.approve",
            "compliance.export.download",
            "legal-hold.create",
            "legal-hold.review",
            "legal-hold.release-request",
            "legal-hold.release-approve",
            "retention.update",
        };
        if (!allowed.Contains(action))
            throw new CollaborationException(400, "COLLAB_STEP_UP_ACTION_INVALID", "提权动作不受支持。");
        var requestNId = RequireRequestNId(request.RequestNId, "提权请求标识不能为空。");
        var targetNId = request.TargetNId?.Trim();
        var reason = string.IsNullOrWhiteSpace(request.Reason) ? null : ValidateReason(request.Reason, "提权原因不能为空。");
        ComplianceScopeDto scope;
        string computedScopeChecksum;
        if (action == "compliance.dispose")
        {
            targetNId = RequireNId(targetNId, "处置目标消息不能为空。");
            var message = await _repository.GetMessageByIdAsync(tenantNId, targetNId, cancellationToken)
                ?? throw new CollaborationException(404, "COLLAB_MESSAGE_NOT_FOUND", "处置目标消息不存在。");
            scope = ValidateScope(new ComplianceScopeDto { ScopeType = "MessageSet", MessageNIds = [message.MessageNId] }, allowEmpty: false);
            computedScopeChecksum = ScopeChecksum(scope);
        }
        else if (action is "legal-hold.review" or "legal-hold.release-request" or "legal-hold.release-approve")
        {
            targetNId = RequireNId(targetNId, "法律保全目标不能为空。");
            var hold = await _repository.GetLegalHoldAsync(tenantNId, targetNId, cancellationToken)
                ?? throw new CollaborationException(404, "COLLAB_HOLD_NOT_FOUND", "法律保全案件不存在。");
            scope = JsonSerializer.Deserialize<ComplianceScopeDto>(hold.ScopeJson) ?? new ComplianceScopeDto();
            computedScopeChecksum = hold.ScopeChecksum;
            reason ??= hold.Reason;
        }
        else if (action is "compliance.export.approve" or "compliance.export.download")
        {
            targetNId = RequireNId(targetNId, "导出目标不能为空。");
            var export = await _repository.GetExportAsync(tenantNId, targetNId, cancellationToken)
                ?? throw new CollaborationException(404, "COLLAB_EXPORT_NOT_FOUND", "导出任务不存在。");
            var parsedExportScope = ParseExportScope(export.ScopeJson, export.FieldsJson);
            scope = parsedExportScope.Scope ?? new ComplianceScopeDto();
            computedScopeChecksum = export.ScopeChecksum;
        }
        else
        {
            scope = ValidateScope(request.Scope, allowEmpty: true);
            computedScopeChecksum = ScopeChecksum(scope);
        }
        var scopeChecksum = computedScopeChecksum;
        if (!string.IsNullOrWhiteSpace(request.ScopeChecksum) && !string.Equals(request.ScopeChecksum.Trim(), scopeChecksum, StringComparison.OrdinalIgnoreCase))
            throw new CollaborationException(400, "COLLAB_SCOPE_CHECKSUM_INVALID", "范围摘要无效。");
        if (scopeChecksum.Length != 64 || scopeChecksum.Any(character => !Uri.IsHexDigit(character)))
            throw new CollaborationException(400, "COLLAB_SCOPE_CHECKSUM_INVALID", "范围摘要无效。");
        var keyword = action is "compliance.view" or "compliance.read-original" ? request.Keyword?.Trim() : null;
        if (keyword?.Length > 200)
            throw new CollaborationException(400, "COLLAB_COMPLIANCE_KEYWORD_INVALID", "搜索关键字不能超过 200 个字符。");
        var fields = action.StartsWith("compliance.export", StringComparison.Ordinal)
            ? ValidateExportFields(request.Fields)
            : [];
        var effectiveAction = action == "compliance.export.request"
            && fields.Any(field => field is "textContent" or "attachmentMetadata")
            ? "compliance.read-original"
            : action;
        var command = await BuildStepUpCommandAsync(
            tenantNId,
            actorUserNId,
            effectiveAction,
            requestNId,
            targetNId,
            scopeChecksum,
            reason,
            keyword,
            request,
            scope,
            fields,
            cancellationToken);
        var requestHash = ComplianceCommandCanonicalizer.Hash(command);
        if (string.IsNullOrWhiteSpace(actorSessionNId) || string.IsNullOrWhiteSpace(actorSecurityVersion))
            throw new CollaborationException(401, "COLLAB_SESSION_INVALID", "当前登录会话已失效，请重新登录。");

        var now = DateTimeOffset.UtcNow;
        var expiresOn = now.AddMinutes(2);
        var commandJson = ComplianceCommandCanonicalizer.Serialize(command);
        var preparation = new CompliancePreparationRecord(
            tenantNId,
            actorUserNId,
            requestNId,
            actorSessionNId,
            effectiveAction,
            targetNId,
            requestHash,
            command.ScopeChecksum,
            commandJson,
            commandJson,
            now,
            now.AddMinutes(5));
        var stored = await _repository.SaveCompliancePreparationAsync(preparation, cancellationToken);
        if (!string.Equals(stored.ActorSessionNId, actorSessionNId, StringComparison.Ordinal)
            || stored.ExpiresOn <= now)
            throw new CollaborationException(403, "COLLAB_STEP_UP_INVALID", "提权准备已失效，请重新发起操作。");

        var binding = _stepUpBinding.Issue(
            tenantNId,
            actorUserNId,
            actorSessionNId,
            actorSecurityVersion,
            stored.Action,
            stored.RequestNId,
            stored.ScopeChecksum,
            stored.RequestHash,
            now,
            TimeSpan.FromMinutes(2));
        return new StepUpContextDto { RequestNId = stored.RequestNId, Action = stored.Action, ScopeChecksum = stored.ScopeChecksum, RequestHash = stored.RequestHash, Binding = binding, ExpiresOn = expiresOn };
    }

    public async Task<ConversationSummaryDto> CreateConversationAsync(string tenantNId, string actorUserNId, CreateConversationRequest request, CancellationToken cancellationToken)
    {
        var peerNId = RequireNId(request.PeerUserNId, "对端用户不能为空。");
        if (string.Equals(peerNId, actorUserNId, StringComparison.Ordinal))
            throw new CollaborationException(400, "COLLAB_SELF_CONVERSATION_FORBIDDEN", "不能与自己创建会话。");
        var peer = await RequireActiveUserAsync(tenantNId, peerNId, cancellationToken);
        var (low, high) = CanonicalPair(actorUserNId, peer.UserNId);
        var existing = await _repository.FindConversationByPairAsync(tenantNId, low, high, cancellationToken);
        if (existing is not null)
            return (await ToSummaryAsync(existing, actorUserNId, cancellationToken)) with { Created = false };

        var now = DateTimeOffset.UtcNow;
        var conversation = new ConversationRecord(
            tenantNId,
            "CV-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(12)).ToLowerInvariant(),
            low,
            high,
            "Active",
            0,
            null,
            null,
            0,
            1,
            Guid.NewGuid());
        var current = new ConversationMemberRecord(tenantNId, conversation.ConversationNId, actorUserNId, actorUserNId, now, "Visible", null, 0, 0, null, 0, 1);
        var other = new ConversationMemberRecord(tenantNId, conversation.ConversationNId, peer.UserNId, peer.DisplayName, now, "Visible", null, 0, 0, null, 0, 1);
        var created = await _repository.CreateConversationAsync(conversation, current, other, cancellationToken);
        await _audit.WriteAsync(tenantNId, null, actorUserNId, "conversation.create", "conversation", created.ConversationNId, new { peerUserNId = peer.UserNId }, cancellationToken);
        return (await ToSummaryAsync(created, actorUserNId, cancellationToken)) with { Created = true };
    }

    public async Task<ConversationPageDto> ListConversationsAsync(string tenantNId, string actorUserNId, string? cursor, int pageSize, CancellationToken cancellationToken)
        => await ListConversationsAsync(tenantNId, actorUserNId, cursor, pageSize, "Visible", false, cancellationToken);

    public async Task<ConversationPageDto> ListConversationsAsync(string tenantNId, string actorUserNId, string? cursor, int pageSize, string visibility, bool unreadOnly, CancellationToken cancellationToken)
    {
        visibility = string.IsNullOrWhiteSpace(visibility) ? "Visible" : visibility.Trim();
        if (pageSize is < 1 or > 100 || visibility is not ("Visible" or "Hidden"))
            throw new CollaborationException(400, "COLLAB_PAGE_INVALID", "分页参数无效。");
        var binding = $"{actorUserNId}:{visibility}:{unreadOnly}";
        var page = _cursor.Decode(cursor, binding);
        var conversations = await _repository.ListConversationsAsync(tenantNId, actorUserNId, page, pageSize, visibility, unreadOnly, cancellationToken);
        var conversationNIds = conversations.Select(item => item.ConversationNId).ToArray();
        var peerNames = await _directory.GetDisplayNamesAsync(tenantNId, conversations
            .Select(item => item.ParticipantLowUserNId == actorUserNId ? item.ParticipantHighUserNId : item.ParticipantLowUserNId)
            .Distinct(StringComparer.Ordinal).ToArray(), cancellationToken);
        var members = await _repository.ListMembersForConversationsAsync(
            tenantNId,
            conversationNIds,
            cancellationToken);
        var previews = await _repository.ListLatestVisibleMessagesForUserAsync(
            tenantNId,
            conversationNIds,
            actorUserNId,
            cancellationToken);
        var restrictedMessageNIds = (await _repository.ListDispositionsAsync(tenantNId, cancellationToken))
            .Where(IsActiveDisposition)
            .Where(item => string.Equals(item.SubjectType, "message", StringComparison.OrdinalIgnoreCase))
            .Select(item => item.SubjectNId)
            .ToHashSet(StringComparer.Ordinal);
        var membersByConversation = members
            .GroupBy(item => item.ConversationNId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        var items = new List<ConversationSummaryDto>(conversations.Count);
        foreach (var conversation in conversations)
        {
            previews.TryGetValue(conversation.ConversationNId, out var preview);
            membersByConversation.TryGetValue(conversation.ConversationNId, out var conversationMembers);
            var currentMember = conversationMembers?.SingleOrDefault(item => item.UserNId == actorUserNId);
            var peerMember = conversationMembers?.SingleOrDefault(item => item.UserNId != actorUserNId);
            var summary = ToSummary(conversation, actorUserNId, currentMember, peerMember);
            if (peerNames.TryGetValue(summary.PeerUserNId, out var peerName))
                summary = summary with { PeerDisplayName = peerName, DisplayName = peerName };
            items.Add(summary with
            {
                LastMessagePreview = ToConversationPreview(preview, restrictedMessageNIds),
            });
        }
        return new ConversationPageDto { Items = items, NextCursor = conversations.Count == pageSize ? _cursor.Encode(page + 1, binding) : null, Total = items.Count };
    }

    public async Task<ConversationDetailDto> GetConversationAsync(string tenantNId, string actorUserNId, string conversationNId, CancellationToken cancellationToken)
    {
        var conversation = await RequireConversationAsync(tenantNId, conversationNId, actorUserNId, cancellationToken, requireActive: false);
        var members = await _repository.GetMembersAsync(tenantNId, conversationNId, cancellationToken);
        var current = members.Single(member => member.UserNId == actorUserNId);
        var peer = members.Single(member => member.UserNId != actorUserNId);
        var names = await _directory.GetDisplayNamesAsync(tenantNId, [current.UserNId, peer.UserNId], cancellationToken);
        return new ConversationDetailDto
        {
            ConversationNId = conversation.ConversationNId,
            Status = conversation.Status,
            CurrentMember = ToMemberDto(current) with { DisplayName = names.GetValueOrDefault(current.UserNId, current.DisplayNameSnapshot) },
            PeerMember = ToMemberDto(peer) with { DisplayName = names.GetValueOrDefault(peer.UserNId, peer.DisplayNameSnapshot) },
            LastMessageSequence = conversation.LastMessageSequence,
            RetentionFloorSequence = conversation.RetentionFloorSequence,
            OptimisticVersion = conversation.OptimisticVersion,
            ConcurrencyVersion = conversation.ConcurrencyVersion,
        };
    }

    /// <summary>Returns the conversation users used by the cross-instance realtime fan-out.</summary>
    public async Task<IReadOnlyList<string>> GetConversationParticipantUserNIdsAsync(
        string tenantNId,
        string conversationNId,
        CancellationToken cancellationToken)
        => (await _repository.GetMembersAsync(tenantNId, conversationNId, cancellationToken))
            .Select(member => member.UserNId)
            .Where(userNId => !string.IsNullOrWhiteSpace(userNId))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    public async Task<MessageDto> SendMessageAsync(string tenantNId, string actorUserNId, string conversationNId, SendMessageRequest request, CancellationToken cancellationToken)
    {
        var conversation = await RequireConversationAsync(tenantNId, conversationNId, actorUserNId, cancellationToken);
        var members = await _repository.GetMembersAsync(tenantNId, conversationNId, cancellationToken);
        var peerNId = members.FirstOrDefault(member => !string.Equals(member.UserNId, actorUserNId, StringComparison.Ordinal))?.UserNId;
        if (peerNId is null || await _directory.GetAsync(tenantNId, peerNId, cancellationToken) is not { Status: "Active" })
            throw new CollaborationException(409, "COLLAB_RECIPIENT_INACTIVE", "对端用户当前不可接收消息，请稍后重试。");
        var clientNId = RequireNId(request.ClientMessageNId, "客户端消息标识不能为空。");
        if (clientNId.Length > 32)
            throw new CollaborationException(400, "COLLAB_CLIENT_MESSAGE_ID_INVALID", "客户端消息标识长度不能超过 32 个字符。");
        var messageType = request.MessageType?.Trim() ?? "Text";
        if (!CollaborationDomainRules.IsMessageTypeAllowed(messageType))
            throw new CollaborationException(400, "COLLAB_MESSAGE_TYPE_INVALID", "消息类型不受支持。");
        string? text;
        try
        {
            text = MessageTextRules.NormalizeAndValidate(request.TextContent);
        }
        catch (ArgumentException exception)
        {
            throw new CollaborationException(400, "COLLAB_MESSAGE_TEXT_INVALID", exception.Message);
        }
        if (messageType == "Text" && string.IsNullOrWhiteSpace(text))
            throw new CollaborationException(400, "COLLAB_MESSAGE_TEXT_REQUIRED", "文本消息不能为空。");
        if (text is not null && text.Length > CollaborationServiceConstants.MaxMessageLength)
            throw new CollaborationException(400, "COLLAB_MESSAGE_TOO_LONG", "消息正文不能超过 4000 个字符。");
        if (messageType != "Text" && string.IsNullOrWhiteSpace(request.AttachmentNId))
            throw new CollaborationException(400, "COLLAB_ATTACHMENT_REQUIRED", "媒体消息必须绑定附件意图。");
        AttachmentRecord? attachmentToSend = null;
        if (!string.IsNullOrWhiteSpace(request.AttachmentNId))
        {
            var attachment = await _repository.GetAttachmentAsync(tenantNId, conversationNId, request.AttachmentNId.Trim(), cancellationToken)
                ?? throw new CollaborationException(404, "COLLAB_ATTACHMENT_NOT_FOUND", "附件意图不存在。");
            if (!string.Equals(attachment.UploaderUserNId, actorUserNId, StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(attachment.FileNId)
                || !string.Equals(attachment.ReferenceState, "Authorized", StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(attachment.ReferenceNId)
                || !string.Equals(attachment.FileStateProjection, "Clean", StringComparison.Ordinal))
                throw new CollaborationException(409, "COLLAB_ATTACHMENT_NOT_READY", "附件尚未完成安全扫描或授权。");
            var file = await _files.GetAsync(tenantNId, actorUserNId, attachment.FileNId, cancellationToken)
                ?? throw new CollaborationException(503, "COLLAB_FILE_UNAVAILABLE", "文件服务暂时无法确认附件状态。");
            if (IsFileBlocked(file))
                throw new CollaborationException(409, "COLLAB_ATTACHMENT_NOT_READY", "附件当前不可用。");
            if (!string.Equals(file.ScanStatus, "Clean", StringComparison.Ordinal))
                throw new CollaborationException(409, "COLLAB_ATTACHMENT_NOT_READY", "附件尚未完成安全扫描。");
            attachmentToSend = attachment;
        }
        var existing = await _repository.FindMessageByClientAsync(tenantNId, actorUserNId, clientNId, cancellationToken);
        var requestHash = Hash(new { conversationNId, clientNId, messageType, text, request.ReplyToMessageNId, request.AttachmentNId });
        if (existing is not null)
        {
            if (!string.Equals(existing.RequestHash, requestHash, StringComparison.Ordinal))
                throw new CollaborationException(409, "COLLAB_MESSAGE_IDEMPOTENCY_CONFLICT", "相同客户端消息标识对应了不同内容。");
            if (attachmentToSend is not null)
                await BindAttachmentReferenceAsync(tenantNId, actorUserNId, attachmentToSend, existing.MessageNId, cancellationToken);
            return await ToMessageDtoAsync(existing, tenantNId, conversationNId, actorUserNId, cancellationToken);
        }
        if (!string.IsNullOrWhiteSpace(request.ReplyToMessageNId)
            && await _repository.GetMessageAsync(tenantNId, conversationNId, request.ReplyToMessageNId, cancellationToken) is null)
            throw new CollaborationException(404, "COLLAB_REPLY_NOT_FOUND", "回复目标消息不存在。");
        var now = DateTimeOffset.UtcNow;
        var message = new MessageRecord(tenantNId, conversationNId, "MSG-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(12)).ToLowerInvariant(), conversation.LastMessageSequence + 1, actorUserNId, clientNId, requestHash, messageType, messageType == "Text" ? text : null, request.ReplyToMessageNId, request.AttachmentNId, now, null, null, null, 1, Guid.NewGuid());
        var accepted = attachmentToSend is null
            ? await _repository.AppendMessageAsync(conversation, message, cancellationToken)
            : await _repository.AppendMessageAndBindAttachmentAsync(conversation, message, attachmentToSend.AttachmentNId, cancellationToken);
        if (attachmentToSend is not null)
            await BindAttachmentReferenceAsync(tenantNId, actorUserNId, attachmentToSend, accepted.MessageNId, cancellationToken);
        await _audit.WriteAsync(tenantNId, null, actorUserNId, "message.send", "message", accepted.MessageNId, new { conversationNId, messageType }, cancellationToken);
        return await ToMessageDtoAsync(accepted, tenantNId, conversationNId, actorUserNId, cancellationToken);
    }

    public async Task<MessagePageDto> GetMessagesAsync(string tenantNId, string actorUserNId, string conversationNId, string mode, long? sequence, int pageSize, CancellationToken cancellationToken)
        => await GetMessagesAsync(tenantNId, actorUserNId, conversationNId, mode, null, sequence, null, null, null, pageSize, cancellationToken);

    public async Task<MessagePageDto> GetMessagesAsync(
        string tenantNId,
        string actorUserNId,
        string conversationNId,
        string mode,
        string? cursor,
        long? sequence,
        long? afterSequence,
        long? fromSequence,
        long? toSequence,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var conversation = await RequireConversationAsync(tenantNId, conversationNId, actorUserNId, cancellationToken, requireActive: false);
        mode = string.IsNullOrWhiteSpace(mode) ? "history" : mode.Trim().ToLowerInvariant();
        if (pageSize is < 1 or > 100 || mode is not ("history" or "after" or "window"))
            throw new CollaborationException(400, "COLLAB_MESSAGE_QUERY_INVALID", "消息查询参数无效。");

        var after = afterSequence ?? sequence;
        var historyPage = 1;
        if (mode == "history")
        {
            if (after is not null || fromSequence is not null || toSequence is not null)
                throw new CollaborationException(400, "COLLAB_MESSAGE_QUERY_INVALID", "历史模式不能混用序号锚点。");
            historyPage = _cursor.Decode(cursor, $"{tenantNId}:{actorUserNId}:{conversationNId}:history");
        }
        else if (mode == "after")
        {
            if (cursor is not null || fromSequence is not null || toSequence is not null || after is null)
                throw new CollaborationException(400, "COLLAB_MESSAGE_QUERY_INVALID", "补拉模式必须只提供afterSequence。");
            if (after.Value < conversation.RetentionFloorSequence)
                throw HistoryExpired(conversation);
            if (after.Value > conversation.LastMessageSequence)
                throw new CollaborationException(400, "COLLAB_MESSAGE_QUERY_INVALID", "afterSequence不能超过当前高水位。");
        }
        else
        {
            if (cursor is not null || after is not null || fromSequence is null || toSequence is null
                || fromSequence.Value < 1 || fromSequence.Value > toSequence.Value
                || toSequence.Value > conversation.LastMessageSequence
                || toSequence.Value - fromSequence.Value > 99)
                throw new CollaborationException(400, "COLLAB_MESSAGE_QUERY_INVALID", "窗口序号范围无效。");
            if (fromSequence.Value <= conversation.RetentionFloorSequence)
                throw HistoryExpired(conversation);
        }

        var messages = await _repository.GetMessagesForUserAsync(
            tenantNId,
            conversationNId,
            actorUserNId,
            mode,
            after,
            fromSequence,
            toSequence,
            pageSize,
            historyPage,
            conversation.RetentionFloorSequence,
            cancellationToken);
        var items = new List<MessageDto>(messages.Count);
        foreach (var message in messages)
            items.Add(await ToMessageDtoAsync(message, tenantNId, conversationNId, actorUserNId, cancellationToken));

        var nextAfter = mode switch
        {
            "after" => items.Count == 0 ? after : items[^1].Sequence,
            "window" => items.Count == 0 ? fromSequence!.Value - 1 : items[^1].Sequence,
            _ => null,
        };
        var hasMore = mode == "history"
            ? items.Count == pageSize
            : mode == "after"
                ? items.Count == pageSize
                : items.Count == pageSize && items[^1].Sequence < toSequence!.Value;
        return new MessagePageDto
        {
            Items = items,
            HighWatermarkSequence = conversation.LastMessageSequence,
            RetentionFloorSequence = conversation.RetentionFloorSequence,
            EarliestAvailableSequence = conversation.RetentionFloorSequence + 1,
            NextAfterSequence = nextAfter,
            HasMore = hasMore,
            NextCursor = mode == "history" && hasMore
                ? _cursor.Encode(historyPage + 1, $"{tenantNId}:{actorUserNId}:{conversationNId}:history")
                : null,
        };
    }

    public async Task<MessageDto> GetMessageByClientAsync(string tenantNId, string actorUserNId, string clientMessageNId, CancellationToken cancellationToken)
    {
        var clientId = RequireNId(clientMessageNId, "客户端消息标识不能为空。");
        if (clientId.Length > 32)
            throw new CollaborationException(400, "COLLAB_CLIENT_MESSAGE_ID_INVALID", "客户端消息标识长度不能超过 32 个字符。");
        var message = await _repository.FindMessageByClientAsync(tenantNId, actorUserNId, clientId, cancellationToken)
            ?? throw new CollaborationException(404, "COLLAB_MESSAGE_NOT_FOUND", "消息不存在。");
        await RequireConversationAsync(tenantNId, message.ConversationNId, actorUserNId, cancellationToken, requireActive: false);
        if (await _repository.IsMessageHiddenForUserAsync(tenantNId, message.ConversationNId, message.MessageNId, actorUserNId, cancellationToken))
            throw new CollaborationException(404, "COLLAB_MESSAGE_NOT_FOUND", "消息不存在。");
        return await ToMessageDtoAsync(message, tenantNId, message.ConversationNId, actorUserNId, cancellationToken);
    }

    /// <summary>Builds the safe projection used by the durable realtime dispatcher.</summary>
    public async Task<MessageDto?> GetRealtimeMessageAsync(
        string tenantNId,
        string messageNId,
        CancellationToken cancellationToken)
    {
        var message = await _repository.GetMessageByIdAsync(tenantNId, RequireNId(messageNId, "消息标识不能为空。"), cancellationToken);
        return message is null
            ? null
            : await ToMessageDtoAsync(message, tenantNId, message.ConversationNId, string.Empty, cancellationToken);
    }

    public async Task<ReadCursorDto> MarkReadAsync(string tenantNId, string actorUserNId, string conversationNId, ReadCursorRequest request, CancellationToken cancellationToken)
    {
        var conversation = await RequireConversationAsync(tenantNId, conversationNId, actorUserNId, cancellationToken);
        var sequence = request.Sequence ?? 0;
        if (sequence < 0 || sequence > conversation.LastMessageSequence)
            throw new CollaborationException(400, "COLLAB_READ_CURSOR_INVALID", "阅读游标不能超过会话当前高水位。");
        await _repository.UpdateReadCursorAsync(tenantNId, conversationNId, actorUserNId, sequence, cancellationToken);
        var member = await _repository.GetMemberAsync(tenantNId, conversationNId, actorUserNId, cancellationToken)
            ?? throw new CollaborationException(404, "COLLAB_CONVERSATION_MEMBER_NOT_FOUND", "会话成员不存在。");
        return new ReadCursorDto { LastReadSequence = member.LastReadSequence, UnreadCount = member.UnreadCount, ProjectionVersion = member.ProjectionVersion, ConcurrencyVersion = member.ConcurrencyVersion };
    }

    public async Task<ConversationVisibilityDto> HideAsync(string tenantNId, string actorUserNId, string conversationNId, HideConversationRequest request, CancellationToken cancellationToken)
    {
        var conversation = await RequireConversationAsync(tenantNId, conversationNId, actorUserNId, cancellationToken);
        var member = await _repository.HideMemberAsync(tenantNId, conversationNId, actorUserNId, Math.Clamp(request.ThroughSequence ?? conversation.LastMessageSequence, 0, conversation.LastMessageSequence), request.ExpectedOptimisticVersion, request.ExpectedConcurrencyVersion, cancellationToken);
        await _audit.WriteAsync(tenantNId, null, actorUserNId, "conversation.hide", "conversation", conversationNId, new { }, cancellationToken);
        return ToVisibilityDto(member);
    }

    public async Task<ConversationVisibilityDto> RestoreAsync(string tenantNId, string actorUserNId, string conversationNId, HideConversationRequest? request, CancellationToken cancellationToken)
    {
        await RequireConversationAsync(tenantNId, conversationNId, actorUserNId, cancellationToken);
        var member = await _repository.RestoreMemberAsync(tenantNId, conversationNId, actorUserNId, request?.ExpectedOptimisticVersion, request?.ExpectedConcurrencyVersion, cancellationToken);
        await _audit.WriteAsync(tenantNId, null, actorUserNId, "conversation.restore", "conversation", conversationNId, new { }, cancellationToken);
        return ToVisibilityDto(member);
    }

    public async Task<MessageDto> RetractAsync(string tenantNId, string actorUserNId, string conversationNId, string messageNId, RetractMessageRequest request, CancellationToken cancellationToken)
    {
        await RequireConversationAsync(tenantNId, conversationNId, actorUserNId, cancellationToken);
        var message = await _repository.GetMessageAsync(tenantNId, conversationNId, messageNId, cancellationToken)
            ?? throw new CollaborationException(404, "COLLAB_MESSAGE_NOT_FOUND", "消息不存在。");
        if (!string.Equals(message.SenderUserNId, actorUserNId, StringComparison.Ordinal))
            throw new CollaborationException(403, "COLLAB_MESSAGE_RETRACT_FORBIDDEN", "只能撤回自己发送的消息。");
        if (DateTimeOffset.UtcNow - message.AcceptedOn > TimeSpan.FromMinutes(2))
            throw new CollaborationException(409, "COLLAB_MESSAGE_RETRACT_EXPIRED", "消息撤回窗口已过期。");
        var retracted = await _repository.RetractMessageAsync(tenantNId, conversationNId, messageNId, actorUserNId, request.Reason?.Trim() ?? "sender_retract", request.ExpectedOptimisticVersion, request.ExpectedConcurrencyVersion, cancellationToken);
        await _audit.WriteAsync(tenantNId, null, actorUserNId, "message.retract", "message", messageNId, new { reason = request.Reason?.Trim() }, cancellationToken);
        return await ToMessageDtoAsync(retracted, tenantNId, conversationNId, actorUserNId, cancellationToken);
    }

    public async Task<MessageDto> RetractAsync(string tenantNId, string actorUserNId, string messageNId, RetractMessageRequest request, CancellationToken cancellationToken)
    {
        var message = await _repository.GetMessageByIdAsync(tenantNId, RequireNId(messageNId, "消息标识不能为空。"), cancellationToken)
            ?? throw new CollaborationException(404, "COLLAB_MESSAGE_NOT_FOUND", "消息不存在。");
        return await RetractAsync(tenantNId, actorUserNId, message.ConversationNId, message.MessageNId, request, cancellationToken);
    }

    public async Task<PersonalMessageVisibilityDto> SetPersonalMessageVisibilityAsync(
        string tenantNId,
        string actorUserNId,
        string conversationNId,
        string messageNId,
        PersonalMessageVisibilityRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Hidden is not true)
            throw new CollaborationException(400, "COLLAB_PERSONAL_VISIBILITY_INVALID", "个人删除仅支持隐藏消息。");
        await RequireConversationAsync(tenantNId, conversationNId, actorUserNId, cancellationToken, requireActive: false);
        var message = await _repository.GetMessageAsync(tenantNId, conversationNId, RequireNId(messageNId, "消息标识不能为空。"), cancellationToken)
            ?? throw new CollaborationException(404, "COLLAB_MESSAGE_NOT_FOUND", "消息不存在。");
        if (!string.Equals(message.SenderUserNId, actorUserNId, StringComparison.Ordinal))
            throw new CollaborationException(403, "COLLAB_PERSONAL_VISIBILITY_FORBIDDEN", "只能删除自己发送的消息。" );
        var changed = await _repository.HideMessageForUserAsync(tenantNId, conversationNId, message.MessageNId, actorUserNId, cancellationToken);
        if (changed)
            await _audit.WriteAsync(tenantNId, null, actorUserNId, "message.personal-hide", "message", message.MessageNId, new { conversationNId }, cancellationToken);
        return new PersonalMessageVisibilityDto { MessageNId = message.MessageNId, Hidden = true };
    }

    public async Task<AttachmentDetailDto> GetAttachmentAsync(string tenantNId, string actorUserNId, string attachmentNId, CancellationToken cancellationToken)
    {
        var attachment = await _repository.GetAttachmentByIdAsync(tenantNId, RequireNId(attachmentNId, "附件标识不能为空。"), cancellationToken)
            ?? throw new CollaborationException(404, "COLLAB_ATTACHMENT_NOT_FOUND", "附件不存在。");
        await RequireConversationAsync(tenantNId, attachment.ConversationNId, actorUserNId, cancellationToken);
        var fileNId = attachment.FileNId;
        var file = fileNId is null ? null : await _files.GetAsync(tenantNId, actorUserNId, fileNId, cancellationToken);
        if (fileNId is not null && file is null)
            throw new CollaborationException(503, "COLLAB_FILE_UNAVAILABLE", "文件服务暂时无法确认附件状态。");
        var restricted = file is not null && IsFileBlocked(file);
        return new AttachmentDetailDto
        {
            AttachmentNId = attachment.AttachmentNId,
            State = restricted ? "Restricted" : attachment.FileStateProjection,
            FileName = restricted ? null : attachment.FileNameSnapshot,
            MediaType = restricted ? null : attachment.ContentTypeSnapshot,
            SizeBytes = restricted ? null : attachment.SizeSnapshot,
            ObservedOn = attachment.FileObservedOn,
            FileNId = restricted ? null : attachment.FileNId,
            ReferenceState = attachment.ReferenceState,
        };
    }

    public async Task<AttachmentAuthorizationDto> AuthorizeAttachmentAsync(string tenantNId, string actorUserNId, string attachmentNId, AttachmentAuthorizationRequest request, CancellationToken cancellationToken)
    {
        var attachment = await _repository.GetAttachmentByIdAsync(tenantNId, RequireNId(attachmentNId, "附件标识不能为空。"), cancellationToken)
            ?? throw new CollaborationException(404, "COLLAB_ATTACHMENT_NOT_FOUND", "附件不存在。");
        await RequireConversationAsync(tenantNId, attachment.ConversationNId, actorUserNId, cancellationToken);
        var purpose = request.Purpose?.Trim() ?? "Download";
        if (purpose is not ("Download" or "Display"))
            throw new CollaborationException(400, "COLLAB_ATTACHMENT_PURPOSE_INVALID", "附件授权用途必须为 Download 或 Display。");
        var requestNId = RequireShortRequestNId(request.RequestNId, "附件授权请求标识不能为空。");
        if (attachment.FileNId is null || attachment.ReferenceState != "Authorized")
            throw new CollaborationException(409, "COLLAB_ATTACHMENT_NOT_READY", "附件尚未完成文件绑定。");
        var file = await _files.GetAsync(tenantNId, actorUserNId, attachment.FileNId, cancellationToken)
            ?? throw new CollaborationException(503, "COLLAB_FILE_UNAVAILABLE", "文件服务暂时无法确认附件状态。");
        if (IsFileBlocked(file))
            throw new CollaborationException(403, "COLLAB_ATTACHMENT_RESTRICTED", "附件当前不可访问。");
        if (file.ScanStatus is "Pending" or "PendingScan" or "Scanning" or "Unknown")
            throw new CollaborationException(409, "COLLAB_ATTACHMENT_NOT_READY", "附件安全扫描尚未完成。");
        if (file.ScanStatus is "Malicious")
            throw new CollaborationException(422, "COLLAB_ATTACHMENT_MALICIOUS", "附件未通过安全扫描。");
        if (file.ScanStatus is not "Clean")
            throw new CollaborationException(403, "COLLAB_ATTACHMENT_RESTRICTED", "附件未通过安全扫描。");
        var referenceNId = AuthorizationReference(tenantNId, attachment.AttachmentNId, actorUserNId, purpose, requestNId);
        await _audit.WriteAsync(tenantNId, null, actorUserNId, "attachment.authorize", "attachment", attachment.AttachmentNId, new { purpose, requestNId, fileNId = attachment.FileNId, referenceNId }, cancellationToken);
        await _files.BindReferenceAsync(tenantNId, actorUserNId, attachment.FileNId, referenceNId, attachment.ConversationNId, attachment.BoundMessageNId ?? throw new CollaborationException(409, "COLLAB_ATTACHMENT_MESSAGE_REQUIRED", "附件尚未绑定消息。"), attachment.AttachmentNId, attachment.UploaderUserNId, "CollaborationMessageAttachment", requestNId, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        await _repository.UpdateAttachmentAsync(attachment with { ReferenceNId = referenceNId, FileObservedOn = now, LastUpdatedOn = now }, cancellationToken);
        return new AttachmentAuthorizationDto
        {
            AttachmentNId = attachment.AttachmentNId,
            FileNId = attachment.FileNId,
            ReferenceNId = referenceNId,
            State = "Authorized",
            Authorization = new { method = "GET", url = $"/collaboration/api/v1/attachments/{Uri.EscapeDataString(attachment.AttachmentNId)}/content?referenceNId={Uri.EscapeDataString(referenceNId)}", requiresAuthentication = true, purpose },
            ExpiresOn = DateTimeOffset.UtcNow.AddSeconds(30),
        };
    }

    public async Task<CollaborationFileContent> OpenAttachmentContentAsync(string tenantNId, string actorUserNId, string attachmentNId, string? referenceNId, CancellationToken cancellationToken)
    {
        var attachment = await _repository.GetAttachmentByIdAsync(tenantNId, RequireNId(attachmentNId, "附件标识不能为空。"), cancellationToken)
            ?? throw new CollaborationException(404, "COLLAB_ATTACHMENT_NOT_FOUND", "附件不存在。");
        await RequireConversationAsync(tenantNId, attachment.ConversationNId, actorUserNId, cancellationToken, requireActive: false);
        var message = await _repository.GetMessageByAttachmentAsync(tenantNId, attachment.AttachmentNId, cancellationToken);
        if (message?.RetractedOn is not null)
            throw new CollaborationException(403, "COLLAB_ATTACHMENT_RETRACTED", "消息已撤回，附件不可访问。");
        if (message is not null && (await _repository.ListDispositionsAsync(tenantNId, cancellationToken)).Any(item => item.State == "Active" && item.SubjectType == "message" && item.SubjectNId == message.MessageNId))
            throw new CollaborationException(403, "COLLAB_ATTACHMENT_DISPOSED", "消息已处置，附件不可访问。");
        var fileNId = attachment.FileNId;
        var file = fileNId is null ? null : await _files.GetAsync(tenantNId, actorUserNId, fileNId, cancellationToken);
        if (file is null)
            throw new CollaborationException(503, "COLLAB_FILE_UNAVAILABLE", "文件服务暂时无法确认附件状态。");
        if (file.ScanStatus is "Pending" or "PendingScan" or "Scanning" or "Unknown")
            throw new CollaborationException(409, "COLLAB_ATTACHMENT_NOT_READY", "附件安全扫描尚未完成。");
        if (file.ScanStatus is "Malicious")
            throw new CollaborationException(422, "COLLAB_ATTACHMENT_MALICIOUS", "附件未通过安全扫描。");
        if (file.ScanStatus is not "Clean")
            throw new CollaborationException(403, "COLLAB_ATTACHMENT_RESTRICTED", "附件未通过安全扫描。");
        if (IsFileBlocked(file))
            throw new CollaborationException(403, "COLLAB_ATTACHMENT_RESTRICTED", "附件当前不可访问。");
        var observed = DateTimeOffset.UtcNow;
        await _repository.UpdateAttachmentAsync(attachment with { FileStateProjection = file.ScanStatus, FileObservedOn = observed, LastUpdatedOn = observed }, cancellationToken);
        var reference = RequireNId(referenceNId, "附件授权已过期，请重新授权。");
        if (!string.Equals(reference, attachment.ReferenceNId, StringComparison.Ordinal))
            throw new CollaborationException(403, "COLLAB_ATTACHMENT_AUTHORIZATION_REQUIRED", "附件授权已过期，请重新授权。");
        try
        {
            var stream = await _files.OpenContentAsync(tenantNId, actorUserNId, fileNId ?? throw new CollaborationException(409, "COLLAB_ATTACHMENT_NOT_READY", "附件尚未完成文件绑定。"), reference, cancellationToken);
            return new CollaborationFileContent(stream, file.ContentType, file.FileName);
        }
        catch (CollaborationException)
        {
            throw;
        }
        catch (Exception)
        {
            throw new CollaborationException(503, "COLLAB_FILE_UNAVAILABLE", "文件服务暂时无法提供附件内容。");
        }
    }

    public async Task<AttachmentIntentDto> CreateAttachmentIntentAsync(string tenantNId, string actorUserNId, string conversationNId, AttachmentIntentRequest request, CancellationToken cancellationToken)
    {
        await RequireConversationAsync(tenantNId, conversationNId, actorUserNId, cancellationToken);
        var size = request.SizeBytes ?? request.Size ?? 0;
        if (size is < 1 or > CollaborationServiceConstants.MaxAttachmentLength)
            throw new CollaborationException(400, "COLLAB_ATTACHMENT_SIZE_INVALID", "附件大小必须在 1 到 50 MiB 之间。");
        var fileName = RequireNId(request.FileName, "附件文件名不能为空。");
        if (fileName.Length > 255)
            throw new CollaborationException(400, "COLLAB_ATTACHMENT_FILENAME_INVALID", "附件文件名不能超过 255 个字符。");
        var now = DateTimeOffset.UtcNow;
        var mediaType = request.MediaType?.Trim() ?? request.ContentType?.Trim() ?? "application/octet-stream";
        if (mediaType.Length is < 1 or > 128)
            throw new CollaborationException(400, "COLLAB_ATTACHMENT_MEDIA_TYPE_INVALID", "附件媒体类型长度无效。");
        var attachment = new AttachmentRecord(tenantNId, conversationNId, request.AttachmentNId?.Trim() is { Length: > 0 } provided ? provided : "ATT-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(12)).ToLowerInvariant(), actorUserNId, null, fileName, mediaType, size, CollaborationServiceConstants.AttachmentPurpose, "Pending", 0, null, "Pending", "Active", null, RequireShortRequestNId(request.RequestNId, "附件请求标识不能为空。"), Hash(request), now, now);
        var created = await _repository.CreateAttachmentAsync(attachment, cancellationToken);
        var upload = await _files.CreateUploadAsync(tenantNId, actorUserNId, created, cancellationToken);
        if (upload.UploadSessionNId is not null)
            created = created with { LastUpdatedOn = DateTimeOffset.UtcNow };
        return new AttachmentIntentDto
        {
            AttachmentNId = created.AttachmentNId,
            ConversationNId = conversationNId,
            UploadSessionNId = upload.UploadSessionNId,
            TransportId = upload.TransportId,
            FileName = created.FileNameSnapshot,
            ContentType = created.ContentTypeSnapshot,
            MediaType = created.ContentTypeSnapshot,
            Size = created.SizeSnapshot,
            State = created.FileStateProjection,
            ExpiresOn = upload.ExpiresOn,
            FileUpload = upload.UploadSessionNId is null || upload.TransportId is null ? null : new AttachmentFileUploadDto
            {
                Session = new AttachmentUploadSessionDto
                {
                    SessionNId = upload.UploadSessionNId,
                    TransportId = upload.TransportId,
                    FileName = upload.FileName ?? created.FileNameSnapshot,
                    MediaType = upload.ContentType ?? created.ContentTypeSnapshot,
                    SizeBytes = upload.Length ?? created.SizeSnapshot,
                    Offset = upload.Offset ?? 0,
                    WriterEpoch = upload.WriterEpoch ?? 0,
                    State = upload.Status ?? "Pending",
                    ExpiresOn = upload.ExpiresOn,
                    ResumeTicket = upload.ResumeTicket,
                },
                UploadUrl = $"/collaboration/api/v1/attachments/{Uri.EscapeDataString(created.AttachmentNId)}/upload",
            },
        };
    }

    public async Task<AttachmentUploadSessionDto> GetAttachmentUploadAsync(string tenantNId, string actorUserNId, string attachmentNId, CancellationToken cancellationToken)
    {
        var attachment = await RequireAttachmentOwnerAsync(tenantNId, actorUserNId, attachmentNId, cancellationToken);
        return ToUploadSession(await _files.GetUploadAsync(tenantNId, actorUserNId, attachment.AttachmentNId, cancellationToken));
    }

    public async Task<AttachmentUploadSessionDto> SetAttachmentContentHashAsync(string tenantNId, string actorUserNId, string attachmentNId, string sha256, CancellationToken cancellationToken)
    {
        var attachment = await RequireAttachmentOwnerAsync(tenantNId, actorUserNId, attachmentNId, cancellationToken);
        var result = await _files.SetContentHashAsync(tenantNId, actorUserNId, attachment.AttachmentNId, RequireNId(sha256, "文件 SHA-256 不能为空。"), cancellationToken);
        return ToUploadSession(result);
    }

    public async Task<AttachmentUploadSessionDto> ResumeAttachmentProofAsync(string tenantNId, string actorUserNId, string attachmentNId, int writerEpoch, string proof, CancellationToken cancellationToken)
    {
        var attachment = await RequireAttachmentOwnerAsync(tenantNId, actorUserNId, attachmentNId, cancellationToken);
        return ToUploadSession(await _files.ResumeProofAsync(tenantNId, actorUserNId, attachment.AttachmentNId, writerEpoch, RequireNId(proof, "断点续传证明不能为空。"), cancellationToken));
    }

    public async Task<AttachmentUploadSessionDto> TakeoverAttachmentUploadAsync(string tenantNId, string actorUserNId, string attachmentNId, int expectedWriterEpoch, string? idempotencyKey, string? proof, CancellationToken cancellationToken)
    {
        var attachment = await RequireAttachmentOwnerAsync(tenantNId, actorUserNId, attachmentNId, cancellationToken);
        return ToUploadSession(await _files.TakeoverAsync(tenantNId, actorUserNId, attachment.AttachmentNId, expectedWriterEpoch, idempotencyKey, proof, cancellationToken));
    }

    public async Task<AttachmentUploadSessionDto> PauseAttachmentUploadAsync(string tenantNId, string actorUserNId, string attachmentNId, CancellationToken cancellationToken)
    {
        var attachment = await RequireAttachmentOwnerAsync(tenantNId, actorUserNId, attachmentNId, cancellationToken);
        return ToUploadSession(await _files.PauseAsync(tenantNId, actorUserNId, attachment.AttachmentNId, cancellationToken));
    }

    public async Task<AttachmentUploadSessionDto> ResumeAttachmentUploadAsync(string tenantNId, string actorUserNId, string attachmentNId, int writerEpoch, string proof, CancellationToken cancellationToken)
    {
        var attachment = await RequireAttachmentOwnerAsync(tenantNId, actorUserNId, attachmentNId, cancellationToken);
        return ToUploadSession(await _files.ResumeAsync(tenantNId, actorUserNId, attachment.AttachmentNId, writerEpoch, RequireNId(proof, "上传恢复证明不能为空。"), cancellationToken));
    }

    public async Task<AttachmentUploadSessionDto> CancelAttachmentUploadAsync(string tenantNId, string actorUserNId, string attachmentNId, string? reason, CancellationToken cancellationToken)
    {
        var attachment = await RequireAttachmentOwnerAsync(tenantNId, actorUserNId, attachmentNId, cancellationToken);
        var result = await _files.CancelAsync(tenantNId, actorUserNId, attachment.AttachmentNId, reason, cancellationToken);
        await _repository.UpdateAttachmentAsync(attachment with { FileStateProjection = "Rejected", ReferenceState = "Failed", LastUpdatedOn = DateTimeOffset.UtcNow }, cancellationToken);
        return ToUploadSession(result);
    }

    public async Task<AttachmentUploadSessionDto> AppendAttachmentUploadAsync(string tenantNId, string actorUserNId, string attachmentNId, string transportId, long expectedOffset, int writerEpoch, string? resumeTicket, Stream content, CancellationToken cancellationToken)
    {
        var attachment = await RequireAttachmentOwnerAsync(tenantNId, actorUserNId, attachmentNId, cancellationToken);
        var session = await _files.GetUploadAsync(tenantNId, actorUserNId, attachment.AttachmentNId, cancellationToken);
        if (!string.Equals(session.TransportId, transportId, StringComparison.Ordinal))
            throw new CollaborationException(409, "COLLAB_UPLOAD_TRANSPORT_INVALID", "上传传输标识与附件会话不匹配。");
        if (expectedOffset < 0 || expectedOffset > attachment.SizeSnapshot)
            throw new CollaborationException(400, "COLLAB_UPLOAD_OFFSET_INVALID", "上传偏移量无效。");
        return ToUploadSession(await _files.AppendAsync(tenantNId, actorUserNId, transportId, expectedOffset, writerEpoch, content, resumeTicket, cancellationToken));
    }

    public async Task<AttachmentUploadSessionDto> CompleteAttachmentUploadAsync(string tenantNId, string actorUserNId, string attachmentNId, CancellationToken cancellationToken)
    {
        var attachment = await RequireAttachmentOwnerAsync(tenantNId, actorUserNId, attachmentNId, cancellationToken);
        var result = await _files.CompleteUploadAsync(tenantNId, actorUserNId, attachment.AttachmentNId, cancellationToken);
        var fileState = result.FileNId is null ? "Scanning" : "Scanning";
        if (result.FileNId is not null && await _files.GetAsync(tenantNId, actorUserNId, result.FileNId, cancellationToken) is { } file)
            fileState = file.ScanStatus;
        await _repository.UpdateAttachmentAsync(attachment with { FileNId = result.FileNId, FileStateProjection = fileState, ReferenceState = "Pending", FileObservedOn = DateTimeOffset.UtcNow, LastUpdatedOn = DateTimeOffset.UtcNow }, cancellationToken);
        return ToUploadSession(result);
    }

    public async Task<AttachmentAuthorizationDto> AuthorizeAttachmentAsync(string tenantNId, string actorUserNId, string conversationNId, string attachmentNId, AttachmentAuthorizationRequest request, CancellationToken cancellationToken)
    {
        var attachment = await _repository.GetAttachmentAsync(tenantNId, conversationNId, attachmentNId, cancellationToken) ?? throw new CollaborationException(404, "COLLAB_ATTACHMENT_NOT_FOUND", "附件意图不存在。");
        if (!string.Equals(attachment.UploaderUserNId, actorUserNId, StringComparison.Ordinal))
            throw new CollaborationException(403, "COLLAB_ATTACHMENT_FORBIDDEN", "只能授权自己上传的附件。");
        await RequireConversationAsync(tenantNId, attachment.ConversationNId, actorUserNId, cancellationToken);
        var fileNId = RequireNId(attachment.FileNId, "附件尚未完成文件绑定。");
        if (!string.IsNullOrWhiteSpace(request.FileNId) && !string.Equals(request.FileNId.Trim(), fileNId, StringComparison.Ordinal))
            throw new CollaborationException(409, "COLLAB_ATTACHMENT_FILE_MISMATCH", "请求文件与已绑定文件不一致。");
        var file = await _files.GetAsync(tenantNId, actorUserNId, fileNId, cancellationToken)
            ?? throw new CollaborationException(503, "COLLAB_FILE_UNAVAILABLE", "文件服务暂时无法确认上传状态。");
        if (file.Length != attachment.SizeSnapshot || !string.Equals(file.FileName, attachment.FileNameSnapshot, StringComparison.Ordinal))
            throw new CollaborationException(409, "COLLAB_ATTACHMENT_FILE_MISMATCH", "实际文件与上传意图不匹配。");
        if (IsFileBlocked(file))
            throw new CollaborationException(403, "COLLAB_ATTACHMENT_RESTRICTED", "文件当前不可授权。");
        if (file.ScanStatus is "Pending" or "PendingScan" or "Scanning" or "Unknown")
            throw new CollaborationException(409, "COLLAB_ATTACHMENT_NOT_READY", "附件安全扫描尚未完成。");
        if (file.ScanStatus is "Malicious" or "Error" || !string.Equals(file.ScanStatus, "Clean", StringComparison.Ordinal))
            throw new CollaborationException(422, "COLLAB_ATTACHMENT_MALICIOUS", "附件未通过安全扫描。");
        var requestNId = RequireShortRequestNId(request.RequestNId, "附件授权请求标识不能为空。");
        var purpose = request.Purpose?.Trim() ?? "Download";
        if (purpose is not ("Download" or "Display"))
            throw new CollaborationException(400, "COLLAB_ATTACHMENT_PURPOSE_INVALID", "附件授权用途必须为 Download 或 Display。");
        var referenceNId = AuthorizationReference(tenantNId, attachment.AttachmentNId, actorUserNId, purpose, requestNId);
        await _audit.WriteAsync(tenantNId, null, actorUserNId, "attachment.bind", "attachment", attachmentNId, new { fileNId, referenceNId, purpose, requestNId }, cancellationToken);
        if (attachment.BoundMessageNId is not null)
            await _files.BindReferenceAsync(tenantNId, actorUserNId, fileNId, referenceNId, attachment.ConversationNId, attachment.BoundMessageNId, attachment.AttachmentNId, attachment.UploaderUserNId, "CollaborationMessageAttachment", requestNId, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        await _repository.UpdateAttachmentAsync(attachment with { FileNId = fileNId, ReferenceNId = referenceNId, ReferenceState = "Authorized", FileStateProjection = "Clean", FileObservedOn = now, LastUpdatedOn = now }, cancellationToken);
        return new AttachmentAuthorizationDto { AttachmentNId = attachmentNId, FileNId = fileNId, ReferenceNId = referenceNId, State = "Authorized" };
    }

    public PresenceDto SetPresence(string tenantNId, string actorUserNId, PresenceUpdateRequest request, string? connectionNId = null)
    {
        var state = request.State?.Trim() ?? "Offline";
        if (!CollaborationDomainRules.IsPresenceStateAllowed(state))
            throw new CollaborationException(400, "COLLAB_PRESENCE_STATE_INVALID", "在线状态无效。");
        return _presence.SetPresence(tenantNId, actorUserNId, state, connectionNId);
    }

    public PresenceDto GetPresence(string tenantNId, string userNId) => _presence.GetPresence(tenantNId, userNId);

    public void RemovePresence(string tenantNId, string actorUserNId, string connectionNId) => _presence.RemovePresence(tenantNId, actorUserNId, connectionNId);

    public Task<ComplianceSearchPageDto> SearchComplianceAsync(string tenantNId, string actorUserNId, ComplianceSearchRequest request, CancellationToken cancellationToken) =>
        SearchComplianceAsync(tenantNId, actorUserNId, request, null, string.Empty, string.Empty, cancellationToken);

    public async Task<ComplianceSearchPageDto> SearchComplianceAsync(string tenantNId, string actorUserNId, ComplianceSearchRequest request, string? stepUpProof, string actorSessionNId, string actorSecurityVersion, CancellationToken cancellationToken)
    {
        var scope = ValidateScope(request.Scope, allowEmpty: true);
        var scopeChecksum = ScopeChecksum(scope);
        var keyword = request.Keyword?.Trim();
        if (keyword?.Length > 200)
            throw new CollaborationException(400, "COLLAB_COMPLIANCE_KEYWORD_INVALID", "搜索关键字不能超过 200 个字符。");
        var command = CreateCommand(tenantNId, actorUserNId, request.ReadOriginal ? "compliance.read-original" : "compliance.view", requestNId: RequireRequestNId(request.RequestNId, "合规查看请求标识不能为空。"), targetNId: null, scopeChecksum, request.Reason, request.CaseReference, keyword, request.ReadOriginal, scope: scope);
        var requestHash = ComplianceCommandCanonicalizer.Hash(command);
        var pageSize = request.Limit ?? request.PageSize;
        if (pageSize is < 1 or > 50)
            throw new CollaborationException(400, "COLLAB_COMPLIANCE_PAGE_INVALID", "合规查看每页必须在 1 到 50 条之间。");
        var requestNId = command.RequestNId;
        if (request.ReadOriginal)
            await _stepUp.EnsureValidAsync(stepUpProof ?? string.Empty, tenantNId, actorUserNId, actorSessionNId, actorSecurityVersion, "compliance.read-original", requestNId, scopeChecksum, requestHash, cancellationToken);
        else
            await _stepUp.EnsureValidAsync(stepUpProof ?? string.Empty, tenantNId, actorUserNId, actorSessionNId, actorSecurityVersion, "compliance.view", requestNId, scopeChecksum, requestHash, cancellationToken);
        await EnsurePreparedAsync(tenantNId, actorUserNId, requestNId, actorSessionNId, command.Action, scopeChecksum, requestHash, command, cancellationToken);
        var windowStartOn = ViewBudgetWindow(DateTimeOffset.UtcNow);
        var reservation = await _repository.ReserveComplianceViewBudgetAsync(tenantNId, actorUserNId, windowStartOn, pageSize, cancellationToken);
        if (reservation.ReservedCount < 0)
            throw new CollaborationException(429, "COLLAB_COMPLIANCE_EXPORT_REQUIRED", "本时间窗口的受控查看额度已用尽，请申请合规导出。");
        var page = _cursor.Decode(request.Cursor, $"{tenantNId}:{actorUserNId}:{scopeChecksum}:{request.ReadOriginal}:{requestHash}");
        IReadOnlyList<MessageRecord> items;
        try
        {
            items = await _repository.SearchComplianceMessagesAsync(tenantNId, scope, keyword, page, pageSize, cancellationToken);
        }
        catch
        {
            await _repository.ReleaseComplianceViewBudgetAsync(tenantNId, actorUserNId, windowStartOn, pageSize, cancellationToken);
            throw;
        }
        var disposedMessageNIds = request.ReadOriginal
            ? new HashSet<string>(StringComparer.Ordinal)
            : (await _repository.ListDispositionsAsync(tenantNId, cancellationToken))
                .Where(IsActiveDisposition)
                .Where(item => string.Equals(item.SubjectType, "message", StringComparison.OrdinalIgnoreCase))
                .Select(item => item.SubjectNId)
                .ToHashSet(StringComparer.Ordinal);
        var released = await _repository.ReleaseComplianceViewBudgetAsync(tenantNId, actorUserNId, windowStartOn, pageSize - items.Count, cancellationToken);
        if (request.ReadOriginal)
            await _audit.WriteAsync(tenantNId, null, actorUserNId, "compliance.view.original", "scope", scopeChecksum, new { requestNId, scopeChecksum, count = items.Count }, cancellationToken);
        var hasMore = items.Count == pageSize;
        return new ComplianceSearchPageDto
        {
            Items = items.Select(item => ToComplianceMessage(item, request.ReadOriginal, disposedMessageNIds.Contains(item.MessageNId))).ToList(),
            ScopeChecksum = scopeChecksum,
            NextCursor = hasMore ? _cursor.Encode(page + 1, $"{tenantNId}:{actorUserNId}:{scopeChecksum}:{request.ReadOriginal}:{requestHash}") : null,
            RemainingViewBudget = released.Remaining,
            ExpiresOn = DateTimeOffset.UtcNow.AddMinutes(5),
        };
    }

    public async Task<ComplianceDispositionDto> CreateDispositionAsync(string tenantNId, string actorUserNId, CreateDispositionRequest request, string? stepUpProof, string requestNIdHeader, string actorSessionNId, string actorSecurityVersion, CancellationToken cancellationToken)
    {
        var messageNId = RequireNId(request.MessageNId ?? request.SubjectNId, "处置消息不能为空。");
        var subjectType = string.IsNullOrWhiteSpace(request.SubjectType) ? "message" : request.SubjectType.Trim();
        if (!string.Equals(subjectType, "message", StringComparison.OrdinalIgnoreCase))
            throw new CollaborationException(400, "COLLAB_DISPOSITION_SUBJECT_INVALID", "首版仅支持按消息处置。");
        var subjectNId = messageNId;
        var reason = ValidateReason(request.Reason, "处置原因不能为空。");
        var requestNId = RequireRequestNId(request.RequestNId ?? requestNIdHeader, "处置请求标识不能为空。");
        var message = await _repository.GetMessageByIdAsync(tenantNId, messageNId, cancellationToken)
            ?? throw new CollaborationException(404, "COLLAB_MESSAGE_NOT_FOUND", "处置目标消息不存在。");
        EnsureVersion(message.MessageStateVersion, message.ConcurrencyVersion, request.ExpectedOptimisticVersion, request.ExpectedConcurrencyVersion, "COLLAB_MESSAGE_CONCURRENCY_CONFLICT", "消息状态已发生变化，请刷新后重试。");
        var proofScope = ValidateScope(new ComplianceScopeDto { ScopeType = "MessageSet", MessageNIds = [messageNId] }, allowEmpty: false);
        var command = CreateCommand(tenantNId, actorUserNId, "compliance.dispose", requestNId, messageNId, ScopeChecksum(proofScope), reason, request.CaseReference, null, false, request.ExpiresOn, subjectType, subjectNId, scope: proofScope, expectedOptimisticVersion: request.ExpectedOptimisticVersion, expectedConcurrencyVersion: request.ExpectedConcurrencyVersion);
        var requestHash = ComplianceCommandCanonicalizer.Hash(command);
        var prior = (await _repository.ListDispositionsAsync(tenantNId, cancellationToken))
            .FirstOrDefault(item => string.Equals(item.CreatedByUserNId, actorUserNId, StringComparison.Ordinal) && string.Equals(item.RequestNId, requestNId, StringComparison.Ordinal));
        if (prior is not null)
        {
            if (!string.Equals(prior.RequestHash, requestHash, StringComparison.Ordinal))
                throw new CollaborationException(409, "COLLAB_COMPLIANCE_IDEMPOTENCY_CONFLICT", "相同请求标识对应了不同处置内容。");
            return ToDisposition(prior, message);
        }
        await _stepUp.EnsureValidAsync(stepUpProof ?? string.Empty, tenantNId, actorUserNId, actorSessionNId, actorSecurityVersion, "compliance.dispose", requestNId, ScopeChecksum(proofScope), requestHash, cancellationToken);
        await EnsurePreparedAsync(tenantNId, actorUserNId, requestNId, actorSessionNId, command.Action, command.ScopeChecksum, requestHash, command, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var pendingRecord = new ComplianceDispositionRecord(tenantNId, "DSP-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(12)).ToLowerInvariant(), subjectType.ToLowerInvariant(), subjectNId, "Active", reason, actorUserNId, now, request.ExpiresOn, 1, Guid.NewGuid(), requestNId, requestHash);
        await _audit.WriteAsync(tenantNId, null, actorUserNId, "compliance.disposition.create", subjectType, subjectNId, new { requestNId, caseReference = request.CaseReference }, cancellationToken);
        var record = await _repository.CreateDispositionAsync(pendingRecord, cancellationToken);
        message = await _repository.GetMessageByIdAsync(tenantNId, messageNId, cancellationToken) ?? message;
        return ToDisposition(record, message);
    }

    public async Task<IReadOnlyList<ComplianceDispositionDto>> ListDispositionsAsync(string tenantNId, CancellationToken cancellationToken) => (await _repository.ListDispositionsAsync(tenantNId, cancellationToken)).Select(item => ToDisposition(item)).ToList();

    public async Task<LegalHoldPageDto> ListLegalHoldsAsync(string tenantNId, string? cursor, string? status, int pageSize, CancellationToken cancellationToken)
    {
        if (pageSize is < 1 or > 100)
            throw new CollaborationException(400, "COLLAB_PAGE_INVALID", "分页参数无效。");
        var binding = $"legal-holds:{status?.Trim()}";
        var page = _cursor.Decode(cursor, binding);
        var items = await _repository.ListLegalHoldsAsync(tenantNId, status?.Trim(), page, pageSize, cancellationToken);
        return new LegalHoldPageDto { Items = items.Select(ToLegalHold).ToList(), NextCursor = items.Count == pageSize ? _cursor.Encode(page + 1, binding) : null };
    }

    public async Task<LegalHoldCaseDto> GetLegalHoldAsync(string tenantNId, string holdCaseNId, CancellationToken cancellationToken) =>
        ToLegalHold(await _repository.GetLegalHoldAsync(tenantNId, RequireNId(holdCaseNId, "保全案件标识不能为空。"), cancellationToken)
            ?? throw new CollaborationException(404, "COLLAB_HOLD_NOT_FOUND", "法律保全案件不存在。"));

    public async Task<LegalHoldCaseDto> CreateLegalHoldAsync(string tenantNId, string actorUserNId, CreateLegalHoldRequest request, string? stepUpProof, string requestNIdHeader, string actorSessionNId, string actorSecurityVersion, CancellationToken cancellationToken)
    {
        var scope = ValidateScope(request.Scope, allowEmpty: false);
        var reason = ValidateReason(request.Reason, "法律保全原因不能为空。");
        var requestNId = RequireRequestNId(request.RequestNId ?? requestNIdHeader, "法律保全请求标识不能为空。");
        var caseReference = ValidateCaseReference(request.CaseReference);
        var command = CreateCommand(tenantNId, actorUserNId, "legal-hold.create", requestNId, null, ScopeChecksum(scope), reason, caseReference, scope: scope);
        var requestHash = ComplianceCommandCanonicalizer.Hash(command);
        var existing = (await _repository.ListLegalHoldsAsync(tenantNId, null, 1, 100, cancellationToken))
            .FirstOrDefault(item => string.Equals(item.CreatedByUserNId, actorUserNId, StringComparison.Ordinal) && string.Equals(item.RequestNId, requestNId, StringComparison.Ordinal));
        if (existing is not null)
        {
            if (!string.Equals(existing.RequestHash, requestHash, StringComparison.Ordinal))
                throw new CollaborationException(409, "COLLAB_COMPLIANCE_IDEMPOTENCY_CONFLICT", "相同请求标识对应了不同保全内容。");
            return ToLegalHold(existing);
        }
        await _stepUp.EnsureValidAsync(stepUpProof ?? string.Empty, tenantNId, actorUserNId, actorSessionNId, actorSecurityVersion, "legal-hold.create", requestNId, ScopeChecksum(scope), requestHash, cancellationToken);
        await EnsurePreparedAsync(tenantNId, actorUserNId, requestNId, actorSessionNId, command.Action, command.ScopeChecksum, requestHash, command, cancellationToken);
        var scopeJson = JsonSerializer.Serialize(scope);
        var pendingRecord = new LegalHoldRecord(tenantNId, "HLD-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(12)).ToLowerInvariant(), "ActivePendingReview", scopeJson, CaseScopeChecksum(tenantNId, scope), reason, actorUserNId, DateTimeOffset.UtcNow, null, 1, Guid.NewGuid(), requestNId, requestHash, "OP-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(12)).ToLowerInvariant(), null, null, null, null, null, null, caseReference, "Pending");
        await _audit.WriteAsync(tenantNId, null, actorUserNId, "compliance.legal-hold.create", "legal-hold", pendingRecord.HoldCaseNId, new { requestNId, caseReference, scopeChecksum = pendingRecord.ScopeChecksum }, cancellationToken);
        var record = await _repository.CreateLegalHoldAsync(pendingRecord, cancellationToken);
        return ToLegalHold(record);
    }

    public async Task<LegalHoldCaseDto> UpdateLegalHoldAsync(string tenantNId, string actorUserNId, string holdCaseNId, UpdateLegalHoldRequest request, string? stepUpProof, string requestNId, string actorSessionNId, string actorSecurityVersion, CancellationToken cancellationToken)
    {
        var record = await _repository.GetLegalHoldAsync(tenantNId, holdCaseNId, cancellationToken) ?? throw new CollaborationException(404, "COLLAB_HOLD_NOT_FOUND", "法律保全案件不存在。");
        var action = (request.Action?.Trim() ?? string.Empty).ToLowerInvariant();
        if (action == "release")
            action = "release-approve";
        var commandRequestNId = RequireRequestNId(request.RequestNId ?? requestNId, "保全操作请求标识不能为空。");
        if (!string.IsNullOrWhiteSpace(request.ScopeChecksum) && !string.Equals(request.ScopeChecksum, record.ScopeChecksum, StringComparison.Ordinal))
            throw new CollaborationException(409, "COLLAB_COMPLIANCE_SCOPE_CHANGED", "保全范围已发生变化，请刷新后重试。");
        EnsureVersion(record.OptimisticVersion, record.ConcurrencyVersion, request.ExpectedOptimisticVersion, request.ExpectedConcurrencyVersion, "COLLAB_HOLD_CONCURRENCY_CONFLICT", "保全案件状态已发生变化，请刷新后重试。");
        if (action is not ("release-request" or "release-approve" or "review"))
            throw new CollaborationException(400, "COLLAB_HOLD_ACTION_INVALID", "法律保全动作无效。");
        var now = DateTimeOffset.UtcNow;
        var reason = string.IsNullOrWhiteSpace(request.Reason) ? record.Reason : ValidateReason(request.Reason, "保全操作原因不能为空。");
        var command = CreateCommand(
            tenantNId,
            actorUserNId,
            $"legal-hold.{action}",
            commandRequestNId,
            holdCaseNId,
            record.ScopeChecksum,
            reason,
            request.CaseReference ?? record.ExternalCaseReference,
            subjectNId: holdCaseNId,
            scope: JsonSerializer.Deserialize<ComplianceScopeDto>(record.ScopeJson),
            expectedOptimisticVersion: request.ExpectedOptimisticVersion ?? record.OptimisticVersion,
            expectedConcurrencyVersion: request.ExpectedConcurrencyVersion ?? record.ConcurrencyVersion);
        var requestHash = ComplianceCommandCanonicalizer.Hash(command);
        LegalHoldRecord updated;
        switch (action)
        {
            case "review":
                if (record.State != "ActivePendingReview")
                    throw new CollaborationException(409, "COLLAB_HOLD_STATE_INVALID", "只有待复核的保全案件可以复核。");
                if (string.Equals(record.CreatedByUserNId, actorUserNId, StringComparison.Ordinal))
                    throw new CollaborationException(403, "COLLAB_HOLD_SELF_APPROVAL_FORBIDDEN", "保全创建者不能复核自己的案件。");
                await _stepUp.EnsureValidAsync(stepUpProof ?? string.Empty, tenantNId, actorUserNId, actorSessionNId, actorSecurityVersion, "legal-hold.review", commandRequestNId, record.ScopeChecksum, requestHash, cancellationToken);
                await EnsurePreparedAsync(tenantNId, actorUserNId, commandRequestNId, actorSessionNId, command.Action, command.ScopeChecksum, requestHash, command, cancellationToken);
                updated = record with { State = "ActiveReviewed", Reason = reason, ReviewedByUserNId = actorUserNId, ReviewedOn = now, OperationId = "OP-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(12)).ToLowerInvariant(), OptimisticVersion = record.OptimisticVersion + 1, ConcurrencyVersion = Guid.NewGuid() };
                break;
            case "release-request":
                if (record.State is not ("ActivePendingReview" or "ActiveReviewed"))
                    throw new CollaborationException(409, "COLLAB_HOLD_STATE_INVALID", "当前保全案件不能提交释放申请。");
                await _stepUp.EnsureValidAsync(stepUpProof ?? string.Empty, tenantNId, actorUserNId, actorSessionNId, actorSecurityVersion, "legal-hold.release-request", commandRequestNId, record.ScopeChecksum, requestHash, cancellationToken);
                await EnsurePreparedAsync(tenantNId, actorUserNId, commandRequestNId, actorSessionNId, command.Action, command.ScopeChecksum, requestHash, command, cancellationToken);
                updated = record with { State = "ReleasePendingApproval", ReleaseRequestedByUserNId = actorUserNId, ReleaseRequestNId = commandRequestNId, ReleaseApprovalExpiresOn = now.AddMinutes(15), OperationId = "OP-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(12)).ToLowerInvariant(), Reason = reason, OptimisticVersion = record.OptimisticVersion + 1, ConcurrencyVersion = Guid.NewGuid() };
                break;
            default:
                if (record.State != "ReleasePendingApproval")
                    throw new CollaborationException(409, "COLLAB_HOLD_STATE_INVALID", "当前保全案件没有待审批的释放申请。");
                if (string.Equals(record.ReleaseRequestedByUserNId, actorUserNId, StringComparison.Ordinal))
                    throw new CollaborationException(403, "COLLAB_HOLD_SELF_APPROVAL_FORBIDDEN", "保全释放申请者不能批准自己的申请。");
                if (record.ReleaseApprovalExpiresOn is null || record.ReleaseApprovalExpiresOn <= now)
                    throw new CollaborationException(409, "COLLAB_HOLD_RELEASE_APPROVAL_EXPIRED", "保全释放审批已过期，请重新提交申请。");
                await _stepUp.EnsureValidAsync(stepUpProof ?? string.Empty, tenantNId, actorUserNId, actorSessionNId, actorSecurityVersion, "legal-hold.release-approve", commandRequestNId, record.ScopeChecksum, requestHash, cancellationToken);
                await EnsurePreparedAsync(tenantNId, actorUserNId, commandRequestNId, actorSessionNId, command.Action, command.ScopeChecksum, requestHash, command, cancellationToken);
                updated = record with { State = "ReleasePendingFileSync", ReleasedOn = null, ReleaseApprovedByUserNId = actorUserNId, FileSyncState = "Pending", OperationId = "OP-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(12)).ToLowerInvariant(), Reason = reason, OptimisticVersion = record.OptimisticVersion + 1, ConcurrencyVersion = Guid.NewGuid() };
                break;
        }
        await _audit.WriteAsync(tenantNId, null, actorUserNId, $"compliance.legal-hold.{action}", "legal-hold", holdCaseNId, new { requestNId = commandRequestNId, state = updated.State, scopeChecksum = updated.ScopeChecksum }, cancellationToken);
        var result = await _repository.UpdateLegalHoldAsync(updated, cancellationToken);
        return ToLegalHold(result);
    }

    public async Task<ComplianceExportDto> PrepareExportAsync(string tenantNId, string actorUserNId, PrepareComplianceExportRequest request, string? stepUpProof, string requestNIdHeader, string actorSessionNId, string actorSecurityVersion, CancellationToken cancellationToken)
    {
        var scope = ValidateScope(request.Scope, allowEmpty: false);
        var reason = ValidateReason(request.Reason, "导出原因不能为空。");
        var requestNId = RequireRequestNId(request.RequestNId ?? requestNIdHeader, "导出请求标识不能为空。");
        var fields = ValidateExportFields(request.Fields);
        var proofAction = fields.Any(field => field is "textContent" or "attachmentMetadata") ? "compliance.read-original" : "compliance.export.request";
        var firstPage = await _repository.SearchComplianceMessagesAsync(tenantNId, scope, null, 1, 10001, cancellationToken);
        if (firstPage.Count > 10000)
            throw new CollaborationException(400, "COLLAB_EXPORT_SCOPE_TOO_LARGE", "导出范围不能超过 10000 条消息。");
        var messageVersions = firstPage.Select(item => new { messageNId = item.MessageNId, messageStateVersion = item.MessageStateVersion }).ToArray();
        var scopeChecksum = ExportScopeChecksum(tenantNId, scope, fields, messageVersions);
        var command = CreateCommand(tenantNId, actorUserNId, proofAction, requestNId, null, ScopeChecksum(scope), reason, request.CaseReference, readOriginal: proofAction == "compliance.read-original", fields: fields, scope: scope);
        var requestHash = ComplianceCommandCanonicalizer.Hash(command);
        await _stepUp.EnsureValidAsync(stepUpProof ?? string.Empty, tenantNId, actorUserNId, actorSessionNId, actorSecurityVersion, proofAction, requestNId, ScopeChecksum(scope), requestHash, cancellationToken);
        await EnsurePreparedAsync(tenantNId, actorUserNId, requestNId, actorSessionNId, command.Action, command.ScopeChecksum, requestHash, command, cancellationToken);
        var existing = (await _repository.ListExportsAsync(tenantNId, null, 1, 100, cancellationToken))
            .FirstOrDefault(item => string.Equals(item.CreatedByUserNId, actorUserNId, StringComparison.Ordinal) && string.Equals(item.RequestNId, requestNId, StringComparison.Ordinal));
        if (existing is not null)
        {
            if (!string.Equals(existing.RequestHash, requestHash, StringComparison.Ordinal))
                throw new CollaborationException(409, "COLLAB_COMPLIANCE_IDEMPOTENCY_CONFLICT", "相同请求标识对应了不同导出内容。");
            return ToExport(existing);
        }
        var retention = await _repository.GetRetentionPolicyAsync(tenantNId, cancellationToken);
        var scopeJson = JsonSerializer.Serialize(new { scope, fields, messageVersions, watermark = firstPage.Count, exportRetentionHours = retention.ExportRetentionHours });
        var now = DateTimeOffset.UtcNow;
        var pendingRecord = new ComplianceExportRecord(tenantNId, "EXP-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(12)).ToLowerInvariant(), "PendingApproval", scopeJson, scopeChecksum, reason, actorUserNId, now, null, null, 1, Guid.NewGuid(), requestNId, requestHash, ValidateCaseReference(request.CaseReference), null, null, null, null, null, null, null, FieldsJson: JsonSerializer.Serialize(fields), OperationId: "OP-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(12)).ToLowerInvariant(), ExportRetentionHours: retention.ExportRetentionHours);
        var administratorAuthorized = await _permissions.IsSystemAdministratorAsync(tenantNId, actorUserNId, actorSessionNId, actorSecurityVersion, cancellationToken);
        if (administratorAuthorized)
            pendingRecord = pendingRecord with { State = "Queued", ApprovedByUserNId = actorUserNId, ApprovedOn = now };
        await _audit.WriteAsync(tenantNId, null, actorUserNId, "compliance.export.prepare", "export", pendingRecord.ExportNId, new { requestNId, scopeChecksum, retentionHours = retention.ExportRetentionHours, administratorAuthorized }, cancellationToken);
        var record = await _repository.CreateExportAsync(pendingRecord, cancellationToken);
        return ToExport(record);
    }

    public async Task<ComplianceExportDto> ApproveExportAsync(string tenantNId, string actorUserNId, string exportNId, ApproveComplianceExportRequest request, string? stepUpProof, string requestNId, string actorSessionNId, string actorSecurityVersion, CancellationToken cancellationToken)
    {
        var record = await _repository.GetExportAsync(tenantNId, exportNId, cancellationToken) ?? throw new CollaborationException(404, "COLLAB_EXPORT_NOT_FOUND", "导出任务不存在。");
        var commandRequestNId = RequireRequestNId(request.RequestNId ?? requestNId, "导出审批请求标识不能为空。");
        if (record.State != "PendingApproval")
            throw new CollaborationException(409, "COLLAB_EXPORT_STATE_INVALID", "当前导出任务不在待审批状态。");
        var administratorAuthorized = await _permissions.IsSystemAdministratorAsync(tenantNId, actorUserNId, actorSessionNId, actorSecurityVersion, cancellationToken);
        if (!administratorAuthorized && string.Equals(record.CreatedByUserNId, actorUserNId, StringComparison.Ordinal))
            throw new CollaborationException(403, "COLLAB_EXPORT_SELF_APPROVAL_FORBIDDEN", "导出申请者不能批准自己的申请。");
        if (!string.IsNullOrWhiteSpace(request.ScopeChecksum) && !string.Equals(request.ScopeChecksum, record.ScopeChecksum, StringComparison.Ordinal))
            throw new CollaborationException(409, "COLLAB_COMPLIANCE_SCOPE_CHANGED", "导出冻结范围已发生变化，请重新申请。");
        EnsureVersion(record.OptimisticVersion, record.ConcurrencyVersion, request.ExpectedOptimisticVersion, request.ExpectedConcurrencyVersion, "COLLAB_EXPORT_CONCURRENCY_CONFLICT", "导出任务状态已发生变化，请刷新后重试。");
        var parsedExport = ParseExportScope(record.ScopeJson, record.FieldsJson);
        var command = CreateCommand(tenantNId, actorUserNId, "compliance.export.approve", commandRequestNId, exportNId, record.ScopeChecksum, request.Reason, record.CaseReference, subjectNId: exportNId, fields: parsedExport.Fields, scope: parsedExport.Scope, expectedOptimisticVersion: request.ExpectedOptimisticVersion ?? record.OptimisticVersion, expectedConcurrencyVersion: request.ExpectedConcurrencyVersion ?? record.ConcurrencyVersion);
        var requestHash = ComplianceCommandCanonicalizer.Hash(command);
        await _stepUp.EnsureValidAsync(stepUpProof ?? string.Empty, tenantNId, actorUserNId, actorSessionNId, actorSecurityVersion, "compliance.export.approve", commandRequestNId, record.ScopeChecksum, requestHash, cancellationToken);
        await EnsurePreparedAsync(tenantNId, actorUserNId, commandRequestNId, actorSessionNId, command.Action, command.ScopeChecksum, requestHash, command, cancellationToken);
        var updated = record with { State = "Queued", ApprovedByUserNId = actorUserNId, ApprovedOn = DateTimeOffset.UtcNow, ApprovalExpiresOn = DateTimeOffset.UtcNow.AddMinutes(15), OptimisticVersion = record.OptimisticVersion + 1, ConcurrencyVersion = Guid.NewGuid() };
        await _audit.WriteAsync(tenantNId, null, actorUserNId, "compliance.export.approve", "export", exportNId, new { requestNId = commandRequestNId, scopeChecksum = record.ScopeChecksum, administratorAuthorized }, cancellationToken);
        return ToExport(await _repository.UpdateExportAsync(updated, cancellationToken));
    }

    public async Task<ComplianceExportDto> GetExportAsync(string tenantNId, string exportNId, CancellationToken cancellationToken) => ToExport(await _repository.GetExportAsync(tenantNId, exportNId, cancellationToken) ?? throw new CollaborationException(404, "COLLAB_EXPORT_NOT_FOUND", "导出任务不存在。"));

    public async Task<ComplianceDownloadAuthorizationDto> AuthorizeExportDownloadAsync(string tenantNId, string actorUserNId, string exportNId, ComplianceDownloadAuthorizationRequest request, string? stepUpProof, string actorSessionNId, string actorSecurityVersion, CancellationToken cancellationToken)
    {
        var exportRecord = await _repository.GetExportAsync(tenantNId, RequireNId(exportNId, "导出标识不能为空。"), cancellationToken)
            ?? throw new CollaborationException(404, "COLLAB_EXPORT_NOT_FOUND", "导出任务不存在。");
        if (!string.Equals(exportRecord.CreatedByUserNId, actorUserNId, StringComparison.Ordinal))
            throw new CollaborationException(403, "COLLAB_EXPORT_DOWNLOAD_FORBIDDEN", "只有导出申请者可以下载导出结果。");
        if (exportRecord.State != "Succeeded" || string.IsNullOrWhiteSpace(exportRecord.ArtifactReference))
            throw new CollaborationException(409, "COLLAB_EXPORT_NOT_READY", "导出结果尚未生成。");
        if (exportRecord.ExpiresOn is not null && exportRecord.ExpiresOn <= DateTimeOffset.UtcNow)
            throw new CollaborationException(410, "COLLAB_EXPORT_EXPIRED", "导出结果已过期。");
        var requestNId = RequireRequestNId(request.RequestNId, "下载授权请求标识不能为空。");
        var parsedExport = ParseExportScope(exportRecord.ScopeJson, exportRecord.FieldsJson);
        var command = CreateCommand(tenantNId, actorUserNId, "compliance.export.download", requestNId, exportNId, exportRecord.ScopeChecksum, null, exportRecord.CaseReference, subjectNId: exportNId, fields: parsedExport.Fields, scope: parsedExport.Scope);
        var requestHash = ComplianceCommandCanonicalizer.Hash(command);
        var existingCommand = await _repository.GetComplianceCommandAsync(tenantNId, actorUserNId, requestNId, cancellationToken);
        if (existingCommand is not null)
        {
            if (!string.Equals(existingCommand.RequestHash, requestHash, StringComparison.Ordinal)
                || !string.Equals(existingCommand.Action, "compliance.export.download", StringComparison.Ordinal)
                || !string.Equals(existingCommand.TargetNId, exportNId, StringComparison.Ordinal)
                || !string.Equals(existingCommand.ScopeChecksum, exportRecord.ScopeChecksum, StringComparison.Ordinal)
                || !string.Equals(existingCommand.CommandJson, ComplianceCommandCanonicalizer.Serialize(command), StringComparison.Ordinal))
                throw new CollaborationException(409, "COLLAB_COMPLIANCE_IDEMPOTENCY_CONFLICT", "相同请求标识对应了不同下载命令。");
            if (existingCommand.Status != "Authorized")
                throw new CollaborationException(409, "COLLAB_EXPORT_DOWNLOAD_ALREADY_CLAIMED", "该下载授权已被领取。");
            var existingExpiresOn = ReadAuthorizationExpiry(existingCommand.ResultJson);
            if (existingExpiresOn <= DateTimeOffset.UtcNow)
                throw new CollaborationException(410, "COLLAB_EXPORT_DOWNLOAD_AUTHORIZATION_EXPIRED", "下载授权已过期，请重新授权。");
            return CreateDownloadAuthorization(exportRecord.ExportNId, existingExpiresOn);
        }
        await _stepUp.EnsureValidAsync(stepUpProof ?? string.Empty, tenantNId, actorUserNId, actorSessionNId, actorSecurityVersion, "compliance.export.download", requestNId, exportRecord.ScopeChecksum, requestHash, cancellationToken);
        await EnsurePreparedAsync(tenantNId, actorUserNId, requestNId, actorSessionNId, command.Action, command.ScopeChecksum, requestHash, command, cancellationToken);
        var expiresOn = DateTimeOffset.UtcNow.AddSeconds(30);
        if (exportRecord.ExpiresOn is not null && exportRecord.ExpiresOn < expiresOn)
            expiresOn = exportRecord.ExpiresOn.Value;
        await _audit.WriteAsync(tenantNId, null, actorUserNId, "compliance.export.download.authorize", "export", exportRecord.ExportNId, new { requestNId, scopeChecksum = exportRecord.ScopeChecksum }, cancellationToken);
        await _repository.SaveComplianceCommandAsync(new ComplianceCommandRecord(tenantNId, actorUserNId, requestNId, "compliance.export.download", exportRecord.ExportNId, requestHash, exportRecord.ScopeChecksum, "Authorized", JsonSerializer.Serialize(new { expiresOn }), expiresOn, ComplianceCommandCanonicalizer.Serialize(command)), cancellationToken);
        return CreateDownloadAuthorization(exportRecord.ExportNId, expiresOn);
    }

    public async Task<CollaborationFileContent> OpenExportContentAsync(string tenantNId, string actorUserNId, string exportNId, string? requestNId, string? stepUpProof, string actorSessionNId, string actorSecurityVersion, CancellationToken cancellationToken)
    {
        var exportRecord = await _repository.GetExportAsync(tenantNId, RequireNId(exportNId, "导出标识不能为空。"), cancellationToken)
            ?? throw new CollaborationException(404, "COLLAB_EXPORT_NOT_FOUND", "导出任务不存在。");
        if (!string.Equals(exportRecord.CreatedByUserNId, actorUserNId, StringComparison.Ordinal))
            throw new CollaborationException(403, "COLLAB_EXPORT_DOWNLOAD_FORBIDDEN", "只有导出申请者可以下载导出结果。");
        if (exportRecord.State != "Succeeded" || string.IsNullOrWhiteSpace(exportRecord.ArtifactReference))
            throw new CollaborationException(409, "COLLAB_EXPORT_NOT_READY", "导出结果尚未生成。");
        if (exportRecord.ExpiresOn is not null && exportRecord.ExpiresOn <= DateTimeOffset.UtcNow)
            throw new CollaborationException(410, "COLLAB_EXPORT_EXPIRED", "导出结果已过期。");
        var downloadRequestNId = RequireRequestNId(requestNId, "下载请求标识不能为空。");
        var parsedExport = ParseExportScope(exportRecord.ScopeJson, exportRecord.FieldsJson);
        var commandSnapshot = CreateCommand(tenantNId, actorUserNId, "compliance.export.download", downloadRequestNId, exportNId, exportRecord.ScopeChecksum, null, exportRecord.CaseReference, subjectNId: exportNId, fields: parsedExport.Fields, scope: parsedExport.Scope);
        var commandJson = ComplianceCommandCanonicalizer.Serialize(commandSnapshot);
        var requestHash = ComplianceCommandCanonicalizer.Hash(commandSnapshot);
        await EnsurePreparedAsync(tenantNId, actorUserNId, downloadRequestNId, actorSessionNId, commandSnapshot.Action, commandSnapshot.ScopeChecksum, requestHash, commandSnapshot, cancellationToken);
        var command = await _repository.GetComplianceCommandAsync(tenantNId, actorUserNId, downloadRequestNId, cancellationToken);
        if (command is null
            || command.Status != "Authorized"
            || !string.Equals(command.Action, "compliance.export.download", StringComparison.Ordinal)
            || !string.Equals(command.TargetNId, exportNId, StringComparison.Ordinal)
            || !string.Equals(command.RequestHash, requestHash, StringComparison.Ordinal)
            || !string.Equals(command.ScopeChecksum, exportRecord.ScopeChecksum, StringComparison.Ordinal)
            || !string.Equals(command.CommandJson, commandJson, StringComparison.Ordinal))
            throw new CollaborationException(403, "COLLAB_EXPORT_DOWNLOAD_AUTHORIZATION_REQUIRED", "请先完成下载授权。");
        if (ReadAuthorizationExpiry(command.ResultJson) <= DateTimeOffset.UtcNow)
            throw new CollaborationException(410, "COLLAB_EXPORT_DOWNLOAD_AUTHORIZATION_EXPIRED", "下载授权已过期，请重新授权。");
        var artifactParts = exportRecord.ArtifactReference.Split('|', 2, StringSplitOptions.TrimEntries);
        var fileNId = RequireNId(artifactParts[0], "导出文件标识不能为空。");
        var referenceNId = artifactParts.Length == 2 ? RequireNId(artifactParts[1], "导出文件引用标识不能为空。") : $"REF-{exportRecord.ExportNId}";
        var file = await _files.GetAsync(tenantNId, actorUserNId, fileNId, cancellationToken)
            ?? throw new CollaborationException(503, "COLLAB_FILE_UNAVAILABLE", "导出文件服务暂不可用。");
        if (IsFileBlocked(file) || file.ScanStatus is "Pending" or "PendingScan" or "Scanning" or "Unknown" or "Malicious" or "Error")
            throw new CollaborationException(403, "COLLAB_EXPORT_FILE_RESTRICTED", "导出文件当前不可访问。");
        if (parsedExport.Fields.Any(field => field is "textContent" or "attachmentMetadata")
            && !await _permissions.HasPermissionAsync(CollaborationPermissions.ComplianceReadOriginal, tenantNId, actorUserNId, actorSessionNId, actorSecurityVersion, cancellationToken))
            throw new CollaborationException(403, "COLLAB_EXPORT_ORIGINAL_PERMISSION_REQUIRED", "当前会话没有访问导出原文的权限。");
        await _audit.WriteAsync(tenantNId, null, actorUserNId, "compliance.export.download.claim", "export", exportRecord.ExportNId, new { requestNId = downloadRequestNId }, cancellationToken);
        if (!await _repository.TryClaimComplianceCommandAsync(tenantNId, actorUserNId, downloadRequestNId, requestHash, DateTimeOffset.UtcNow, cancellationToken))
            throw new CollaborationException(409, "COLLAB_EXPORT_DOWNLOAD_ALREADY_CLAIMED", "该下载授权已被其他请求领取。");
        try
        {
            var content = await _files.OpenContentAsync(tenantNId, actorUserNId, fileNId, referenceNId, cancellationToken);
            return new CollaborationFileContent(content, file.ContentType, file.FileName);
        }
        catch (CollaborationException)
        {
            throw;
        }
        catch (Exception)
        {
            throw new CollaborationException(503, "COLLAB_FILE_UNAVAILABLE", "导出文件服务暂时无法提供内容。");
        }
    }

    private static ComplianceDownloadAuthorizationDto CreateDownloadAuthorization(string exportNId, DateTimeOffset expiresOn) => new()
    {
        Authorization = new { method = "GET", url = $"/collaboration/api/v1/compliance/exports/{Uri.EscapeDataString(exportNId)}/content", requiresAuthentication = true },
        ExpiresOn = expiresOn,
    };

    private static DateTimeOffset ReadAuthorizationExpiry(string? resultJson)
    {
        if (string.IsNullOrWhiteSpace(resultJson))
            return DateTimeOffset.MinValue;
        try
        {
            using var document = JsonDocument.Parse(resultJson);
            return document.RootElement.TryGetProperty("expiresOn", out var expiresOn)
                && expiresOn.TryGetDateTimeOffset(out var value)
                ? value
                : DateTimeOffset.MinValue;
        }
        catch (JsonException)
        {
            return DateTimeOffset.MinValue;
        }
    }

    public async Task<ComplianceExportPageDto> ListExportsAsync(string tenantNId, string? cursor, string? status, int pageSize, CancellationToken cancellationToken)
    {
        if (pageSize is < 1 or > 100)
            throw new CollaborationException(400, "COLLAB_PAGE_INVALID", "分页参数无效。");
        var binding = $"exports:{status?.Trim()}";
        var page = _cursor.Decode(cursor, binding);
        var items = await _repository.ListExportsAsync(tenantNId, status?.Trim(), page, pageSize, cancellationToken);
        return new ComplianceExportPageDto { Items = items.Select(ToExport).ToList(), NextCursor = items.Count == pageSize ? _cursor.Encode(page + 1, binding) : null };
    }

    public async Task<RetentionPolicyDto> GetRetentionPolicyAsync(string tenantNId, CancellationToken cancellationToken) => ToRetention(await _repository.GetRetentionPolicyAsync(tenantNId, cancellationToken));

    public async Task<RetentionPolicyDto> UpdateRetentionPolicyAsync(string tenantNId, string actorUserNId, UpdateRetentionPolicyRequest request, string? stepUpProof, string requestNIdHeader, string actorSessionNId, string actorSecurityVersion, CancellationToken cancellationToken)
    {
        var current = await _repository.GetRetentionPolicyAsync(tenantNId, cancellationToken);
        if (request.ExpectedOptimisticVersion is not null && request.ExpectedOptimisticVersion != current.OptimisticVersion)
            throw new CollaborationException(409, "COLLAB_RETENTION_CONCURRENCY_CONFLICT", "保留策略已被其他操作更新。");
        if (!string.IsNullOrWhiteSpace(request.RequestNId))
            RequireRequestNId(request.RequestNId, "保留策略请求标识无效。");
        var requestNId = RequireRequestNId(request.RequestNId ?? requestNIdHeader, "保留策略请求标识不能为空。");
        var proofScope = new ComplianceScopeDto { ScopeType = "TimeRange" };
        var messageRetentionDays = ValidateMessageRetentionDays(request.MessageRetentionDays ?? current.MessageRetentionDays);
        var attachmentRetentionDays = ValidateDays(request.AttachmentRetentionDays ?? current.AttachmentRetentionDays);
        var auditRetentionDays = ValidateDays(request.AuditRetentionDays ?? current.AuditRetentionDays);
        var enabled = request.Enabled ?? current.Enabled;
        var exportRetentionHours = ValidateHours(request.ExportRetentionHours ?? current.ExportRetentionHours);
        var reviewDueHours = ValidateHours(request.ReviewDueHours ?? current.ReviewDueHours);
        var command = CreateCommand(tenantNId, actorUserNId, "retention.update", requestNId, null, ScopeChecksum(proofScope), request.Reason, fields: [], scope: proofScope, messageRetentionDays: messageRetentionDays, attachmentRetentionDays: attachmentRetentionDays, auditRetentionDays: auditRetentionDays, enabled: enabled, exportRetentionHours: exportRetentionHours, reviewDueHours: reviewDueHours, expectedOptimisticVersion: request.ExpectedOptimisticVersion ?? current.OptimisticVersion, expectedConcurrencyVersion: request.ExpectedConcurrencyVersion ?? current.ConcurrencyVersion);
        var requestHash = ComplianceCommandCanonicalizer.Hash(command);
        await _stepUp.EnsureValidAsync(stepUpProof ?? string.Empty, tenantNId, actorUserNId, actorSessionNId, actorSecurityVersion, "retention.update", requestNId, ScopeChecksum(proofScope), requestHash, cancellationToken);
        await EnsurePreparedAsync(tenantNId, actorUserNId, requestNId, actorSessionNId, command.Action, command.ScopeChecksum, requestHash, command, cancellationToken);
        var updated = current with { MessageRetentionDays = messageRetentionDays, AttachmentRetentionDays = attachmentRetentionDays, AuditRetentionDays = auditRetentionDays, Enabled = enabled, ExportRetentionHours = exportRetentionHours, ReviewDueHours = reviewDueHours, Status = enabled ? "Active" : "Disabled", OptimisticVersion = current.OptimisticVersion + 1, ConcurrencyVersion = Guid.NewGuid() };
        await _audit.WriteAsync(tenantNId, null, actorUserNId, "compliance.retention.update", "retention-policy", current.PolicyNId, new { requestNId, expectedOptimisticVersion = request.ExpectedOptimisticVersion }, cancellationToken);
        return ToRetention(await _repository.UpdateRetentionPolicyAsync(updated, cancellationToken));
    }

    private async Task<ComplianceCommandSnapshot> BuildStepUpCommandAsync(
        string tenantNId,
        string actorUserNId,
        string action,
        string requestNId,
        string? targetNId,
        string scopeChecksum,
        string? reason,
        string? keyword,
        StepUpContextRequest request,
        ComplianceScopeDto scope,
        IReadOnlyList<string> fields,
        CancellationToken cancellationToken)
    {
        var caseReference = ValidateCaseReference(request.CaseReference);
        var subjectType = string.IsNullOrWhiteSpace(request.SubjectType)
            ? action == "compliance.dispose" ? "message" : null
            : request.SubjectType.Trim().ToLowerInvariant();
        var subjectNId = string.IsNullOrWhiteSpace(request.SubjectNId) ? targetNId : request.SubjectNId.Trim();
        var commandFields = fields;
        var expectedOptimisticVersion = request.ExpectedOptimisticVersion;
        var expectedConcurrencyVersion = request.ExpectedConcurrencyVersion;
        if (action is "compliance.export.approve" or "compliance.export.download")
        {
            var export = await _repository.GetExportAsync(tenantNId, RequireNId(targetNId, "导出目标不能为空。"), cancellationToken)
                ?? throw new CollaborationException(404, "COLLAB_EXPORT_NOT_FOUND", "导出任务不存在。");
            var parsed = ParseExportScope(export.ScopeJson, export.FieldsJson);
            commandFields = parsed.Fields;
            caseReference ??= export.CaseReference;
            if (action == "compliance.export.approve")
            {
                expectedOptimisticVersion = request.ExpectedOptimisticVersion ?? export.OptimisticVersion;
                expectedConcurrencyVersion = request.ExpectedConcurrencyVersion ?? export.ConcurrencyVersion;
            }
        }
        else if (action.StartsWith("legal-hold.", StringComparison.Ordinal) && action != "legal-hold.create")
        {
            var hold = await _repository.GetLegalHoldAsync(tenantNId, RequireNId(targetNId, "法律保全目标不能为空。"), cancellationToken)
                ?? throw new CollaborationException(404, "COLLAB_HOLD_NOT_FOUND", "法律保全案件不存在。");
            caseReference ??= hold.ExternalCaseReference;
            expectedOptimisticVersion = request.ExpectedOptimisticVersion ?? hold.OptimisticVersion;
            expectedConcurrencyVersion = request.ExpectedConcurrencyVersion ?? hold.ConcurrencyVersion;
        }

        var messageRetentionDays = request.MessageRetentionDays;
        var attachmentRetentionDays = request.AttachmentRetentionDays;
        var auditRetentionDays = request.AuditRetentionDays;
        var enabled = request.Enabled;
        var exportRetentionHours = request.ExportRetentionHours;
        var reviewDueHours = request.ReviewDueHours;
        if (action == "retention.update")
        {
            var current = await _repository.GetRetentionPolicyAsync(tenantNId, cancellationToken);
            messageRetentionDays = ValidateMessageRetentionDays(messageRetentionDays ?? current.MessageRetentionDays);
            attachmentRetentionDays = ValidateDays(attachmentRetentionDays ?? current.AttachmentRetentionDays);
            auditRetentionDays = ValidateDays(auditRetentionDays ?? current.AuditRetentionDays);
            enabled ??= current.Enabled;
            exportRetentionHours = ValidateHours(exportRetentionHours ?? current.ExportRetentionHours);
            reviewDueHours = ValidateHours(reviewDueHours ?? current.ReviewDueHours);
            expectedOptimisticVersion ??= current.OptimisticVersion;
            expectedConcurrencyVersion ??= current.ConcurrencyVersion;
        }

        return new ComplianceCommandSnapshot(
            action,
            requestNId,
            targetNId,
            scopeChecksum.ToLowerInvariant(),
            reason,
            caseReference,
            keyword,
            action == "compliance.read-original" || request.ReadOriginal == true,
            request.ExpiresOn,
            subjectType,
            subjectNId,
            commandFields.Order(StringComparer.Ordinal).ToArray(),
            messageRetentionDays,
            attachmentRetentionDays,
            auditRetentionDays,
            enabled,
            exportRetentionHours,
            reviewDueHours,
            expectedOptimisticVersion,
            expectedConcurrencyVersion).BindRequest(tenantNId, actorUserNId, scope);
    }

    private async Task EnsurePreparedAsync(string tenantNId, string actorUserNId, string requestNId, string actorSessionNId, string action, string scopeChecksum, string requestHash, ComplianceCommandSnapshot command, CancellationToken cancellationToken)
    {
        var preparation = await _repository.GetCompliancePreparationAsync(tenantNId, actorUserNId, requestNId, cancellationToken);
        var expectedCommandJson = ComplianceCommandCanonicalizer.Serialize(command);
        if (preparation is null
            || preparation.ExpiresOn <= DateTimeOffset.UtcNow
            || !string.Equals(preparation.ActorSessionNId, actorSessionNId, StringComparison.Ordinal)
            || !string.Equals(preparation.Action, action, StringComparison.Ordinal)
            || !string.Equals(preparation.ScopeChecksum, scopeChecksum, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(preparation.RequestHash, requestHash, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(preparation.CommandJson, expectedCommandJson, StringComparison.Ordinal))
            throw new CollaborationException(403, "COLLAB_STEP_UP_INVALID", "合规命令未完成当前会话的服务端准备绑定。");
    }

    private async Task<DirectoryUser> RequireActiveUserAsync(string tenantNId, string userNId, CancellationToken cancellationToken)
    {
        var user = await _directory.GetAsync(tenantNId, userNId, cancellationToken);
        if (user is null || !string.Equals(user.Status, "Active", StringComparison.Ordinal))
            throw new CollaborationException(404, "COLLAB_USER_NOT_FOUND", "对端用户不存在或不可用。");
        return user;
    }

    private async Task<AttachmentRecord> RequireAttachmentOwnerAsync(string tenantNId, string actorUserNId, string attachmentNId, CancellationToken cancellationToken)
    {
        var attachment = await _repository.GetAttachmentByIdAsync(tenantNId, RequireNId(attachmentNId, "附件标识不能为空。"), cancellationToken)
            ?? throw new CollaborationException(404, "COLLAB_ATTACHMENT_NOT_FOUND", "附件不存在。");
        await RequireConversationAsync(tenantNId, attachment.ConversationNId, actorUserNId, cancellationToken);
        if (!string.Equals(attachment.UploaderUserNId, actorUserNId, StringComparison.Ordinal))
            throw new CollaborationException(403, "COLLAB_ATTACHMENT_UPLOAD_FORBIDDEN", "只能操作自己创建的附件上传会话。");
        return attachment;
    }

    private async Task<ConversationRecord> RequireConversationAsync(string tenantNId, string conversationNId, string actorUserNId, CancellationToken cancellationToken, bool requireActive = true)
    {
        var conversation = await _repository.GetConversationAsync(tenantNId, conversationNId, cancellationToken);
        if (conversation is null || await _repository.GetMemberAsync(tenantNId, conversationNId, actorUserNId, cancellationToken) is null)
            throw new CollaborationException(404, "COLLAB_CONVERSATION_NOT_FOUND", "会话不存在。");
        if (requireActive && conversation.Status != "Active")
            throw new CollaborationException(409, "COLLAB_CONVERSATION_SUSPENDED", "会话当前不可写入。");
        return conversation;
    }

    private async Task<ConversationSummaryDto> ToSummaryAsync(ConversationRecord conversation, string actorUserNId, CancellationToken cancellationToken)
    {
        var peerNId = conversation.ParticipantLowUserNId == actorUserNId ? conversation.ParticipantHighUserNId : conversation.ParticipantLowUserNId;
        var peer = await _directory.GetAsync(conversation.TenantNId, peerNId, cancellationToken);
        var member = await _repository.GetMemberAsync(conversation.TenantNId, conversation.ConversationNId, actorUserNId, cancellationToken);
        return new ConversationSummaryDto { ConversationNId = conversation.ConversationNId, PeerUserNId = peerNId, PeerDisplayName = peer?.DisplayName ?? peerNId, DisplayName = peer?.DisplayName ?? peerNId, Status = conversation.Status, LastMessageSequence = conversation.LastMessageSequence, LastMessageNId = conversation.LastMessageNId, LastMessageOn = conversation.LastMessageOn, UnreadCount = member?.UnreadCount ?? 0, OptimisticVersion = conversation.OptimisticVersion, ConcurrencyVersion = conversation.ConcurrencyVersion, VisibilityState = member?.VisibilityState ?? "Visible", Presence = _presence.GetPresence(conversation.TenantNId, peerNId), ProjectionVersion = member?.ProjectionVersion ?? 0 };
    }

    private ConversationSummaryDto ToSummary(ConversationRecord conversation, string actorUserNId, ConversationMemberRecord? currentMember, ConversationMemberRecord? peerMember)
    {
        var peerNId = conversation.ParticipantLowUserNId == actorUserNId ? conversation.ParticipantHighUserNId : conversation.ParticipantLowUserNId;
        var peerDisplayName = peerMember?.DisplayNameSnapshot ?? peerNId;
        return new ConversationSummaryDto { ConversationNId = conversation.ConversationNId, PeerUserNId = peerNId, PeerDisplayName = peerDisplayName, DisplayName = peerDisplayName, Status = conversation.Status, LastMessageSequence = conversation.LastMessageSequence, LastMessageNId = conversation.LastMessageNId, LastMessageOn = conversation.LastMessageOn, UnreadCount = currentMember?.UnreadCount ?? 0, OptimisticVersion = conversation.OptimisticVersion, ConcurrencyVersion = conversation.ConcurrencyVersion, VisibilityState = currentMember?.VisibilityState ?? "Visible", Presence = _presence.GetPresence(conversation.TenantNId, peerNId), ProjectionVersion = currentMember?.ProjectionVersion ?? 0 };
    }

    private static ConversationLastMessagePreviewDto? ToConversationPreview(MessageRecord? message, HashSet<string> restrictedMessageNIds)
    {
        if (message is null)
            return null;
        var state = message.RetractedOn is not null
            ? "Retracted"
            : restrictedMessageNIds.Contains(message.MessageNId)
                ? "Restricted"
                : "Accepted";
        var text = state == "Accepted" && string.Equals(message.MessageType, "Text", StringComparison.OrdinalIgnoreCase)
            ? message.TextContent?.Length > 160 ? message.TextContent[..160] : message.TextContent
            : null;
        return new ConversationLastMessagePreviewDto
        {
            MessageNId = message.MessageNId,
            Sequence = message.Sequence,
            AcceptedOn = message.AcceptedOn,
            MessageType = message.MessageType,
            State = state,
            Text = text,
        };
    }

    private async Task<MessageDto> ToMessageDtoAsync(MessageRecord message, string tenantNId, string conversationNId, string actorUserNId, CancellationToken cancellationToken)
    {
        var sender = await _directory.GetAsync(tenantNId, message.SenderUserNId, cancellationToken);
        var isDisposed = (await _repository.ListDispositionsAsync(tenantNId, cancellationToken))
            .Any(item => item.SubjectType.Equals("message", StringComparison.OrdinalIgnoreCase)
                && item.SubjectNId == message.MessageNId
                && IsActiveDisposition(item));
        var attachment = !isDisposed && message.RetractedOn is null && message.AttachmentNId is not null ? await _repository.GetAttachmentAsync(tenantNId, conversationNId, message.AttachmentNId, cancellationToken) : null;
        var state = message.RetractedOn is not null ? "Retracted" : isDisposed ? "Disposed" : "Accepted";
        return new MessageDto { MessageNId = message.MessageNId, ConversationNId = message.ConversationNId, Sequence = message.Sequence, SenderUserNId = message.SenderUserNId, SenderDisplayName = sender?.DisplayName ?? message.SenderUserNId, ClientMessageNId = string.Equals(message.SenderUserNId, actorUserNId, StringComparison.Ordinal) ? message.ClientMessageNId : string.Empty, MessageType = message.MessageType, TextContent = state == "Accepted" ? message.TextContent : null, ReplyToMessageNId = message.ReplyToMessageNId, Attachment = attachment is null ? null : new MessageAttachmentDto { AttachmentNId = attachment.AttachmentNId, FileNId = attachment.FileNId, FileName = attachment.FileNameSnapshot, ContentType = attachment.ContentTypeSnapshot, MediaType = attachment.ContentTypeSnapshot, Size = attachment.SizeSnapshot, FileState = attachment.FileStateProjection, ReferenceState = attachment.ReferenceState }, AcceptedOn = message.AcceptedOn, RetractedOn = message.RetractedOn, RetractionReason = message.RetractedOn is null ? null : message.RetractionReason, State = state, MessageStateVersion = message.MessageStateVersion, OptimisticVersion = message.MessageStateVersion, ConcurrencyVersion = message.ConcurrencyVersion };
    }

    private static ConversationMemberDto ToMemberDto(ConversationMemberRecord member) => new() { UserNId = member.UserNId, DisplayName = member.DisplayNameSnapshot, VisibilityState = member.VisibilityState, JoinedOn = member.JoinedOn, LastReadSequence = member.LastReadSequence, UnreadCount = member.UnreadCount, OptimisticVersion = member.ProjectionVersion, ConcurrencyVersion = member.ConcurrencyVersion, ProjectionVersion = member.ProjectionVersion };
    private static ConversationVisibilityDto ToVisibilityDto(ConversationMemberRecord member) => new() { VisibilityState = member.VisibilityState, OptimisticVersion = member.ProjectionVersion, ConcurrencyVersion = member.ConcurrencyVersion, ProjectionVersion = member.ProjectionVersion };
    private static ComplianceMessageDto ToComplianceMessage(MessageRecord item, bool readOriginal, bool disposed) => new() { ConversationNId = item.ConversationNId, MessageNId = item.MessageNId, Sequence = item.Sequence, SenderUserNId = item.SenderUserNId, MessageType = item.MessageType, TextContent = readOriginal && item.RetractedOn is null ? item.TextContent : null, AcceptedOn = item.AcceptedOn, State = item.RetractedOn is null ? disposed ? "Disposed" : "Accepted" : "Retracted", MessageStateVersion = item.MessageStateVersion, OptimisticVersion = item.MessageStateVersion, ConcurrencyVersion = item.ConcurrencyVersion, AttachmentNId = readOriginal && item.RetractedOn is null ? item.AttachmentNId : null };
    private static bool IsActiveDisposition(ComplianceDispositionRecord item) => item.State == "Active" && (item.ExpiresOn is null || item.ExpiresOn > DateTimeOffset.UtcNow);
    private static ComplianceDispositionDto ToDisposition(ComplianceDispositionRecord item, MessageRecord? message = null) => new() { DispositionNId = item.DispositionNId, MessageNId = item.SubjectType.Equals("message", StringComparison.OrdinalIgnoreCase) ? item.SubjectNId : string.Empty, SubjectType = item.SubjectType, SubjectNId = item.SubjectNId, State = item.State, Reason = item.Reason, CreatedByUserNId = item.CreatedByUserNId, CreatedOn = item.CreatedOn, ExpiresOn = item.ExpiresOn, DispositionVersion = item.OptimisticVersion, MessageStateVersion = message?.MessageStateVersion ?? 0, OptimisticVersion = item.OptimisticVersion, ConcurrencyVersion = item.ConcurrencyVersion };
    private static LegalHoldCaseDto ToLegalHold(LegalHoldRecord item) => new() { HoldCaseNId = item.HoldCaseNId, State = item.State, Reason = item.Reason, Scope = JsonSerializer.Deserialize<ComplianceScopeDto>(item.ScopeJson) ?? new(), CreatedByUserNId = item.CreatedByUserNId, CreatedOn = item.CreatedOn, ReleasedOn = item.ReleasedOn, OperationId = item.OperationId, ScopeChecksum = item.ScopeChecksum, OptimisticVersion = item.OptimisticVersion, ConcurrencyVersion = item.ConcurrencyVersion, ExternalCaseReference = item.ExternalCaseReference, FileSyncState = item.FileSyncState, ReviewedByUserNId = item.ReviewedByUserNId, ReviewedOn = item.ReviewedOn, ReleaseRequestedByUserNId = item.ReleaseRequestedByUserNId, ReleaseApprovalExpiresOn = item.ReleaseApprovalExpiresOn, ReleaseApprovedByUserNId = item.ReleaseApprovedByUserNId };
    private static ComplianceExportDto ToExport(ComplianceExportRecord item)
    {
        var (scope, fields) = ParseExportScope(item.ScopeJson, item.FieldsJson);
        return new ComplianceExportDto { ExportNId = item.ExportNId, State = item.State, ScopeChecksum = item.ScopeChecksum, DownloadToken = null, CreatedOn = item.CreatedOn, ExpiresOn = item.ExpiresOn, OperationId = item.OperationId, Scope = scope, Fields = fields, CaseReference = item.CaseReference, RequestedByUserNId = item.CreatedByUserNId, ApprovedByUserNId = item.ApprovedByUserNId, ApprovedOn = item.ApprovedOn, ApprovalExpiresOn = item.ApprovalExpiresOn, ApprovalConsumedOn = item.ApprovalConsumedOn, RunDeadlineOn = item.RunDeadlineOn, CompletedOn = item.CompletedOn, ErrorCode = item.ErrorCode, ExportRetentionHours = item.ExportRetentionHours, OptimisticVersion = item.OptimisticVersion, ConcurrencyVersion = item.ConcurrencyVersion };
    }
    private static RetentionPolicyDto ToRetention(RetentionPolicyRecord item) => new() { PolicyNId = item.PolicyNId, MessageRetentionDays = item.MessageRetentionDays, AttachmentRetentionDays = item.AttachmentRetentionDays, AuditRetentionDays = item.AuditRetentionDays, Enabled = item.Enabled, ExportRetentionHours = item.ExportRetentionHours, ReviewDueHours = item.ReviewDueHours, Status = item.Status, OptimisticVersion = item.OptimisticVersion, ConcurrencyVersion = item.ConcurrencyVersion };
    private static AttachmentUploadSessionDto ToUploadSession(AttachmentUploadResult result) => new() { SessionNId = result.UploadSessionNId ?? string.Empty, TransportId = result.TransportId ?? string.Empty, FileName = result.FileName ?? string.Empty, MediaType = result.ContentType ?? "application/octet-stream", SizeBytes = result.Length ?? 0, Offset = result.Offset ?? 0, WriterEpoch = result.WriterEpoch ?? 0, State = result.Status ?? "Pending", FileNId = result.FileNId, ExpiresOn = result.ExpiresOn, ResumeTicket = result.ResumeTicket };
    private static (string Low, string High) CanonicalPair(string left, string right) => string.CompareOrdinal(left, right) < 0 ? (left, right) : (right, left);
    private static string RequireNId(string? value, string message) => string.IsNullOrWhiteSpace(value) ? throw new CollaborationException(400, "COLLAB_VALIDATION_FAILED", message) : value.Trim();
    private static string Hash(object value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value)))).ToLowerInvariant();
    private static ComplianceCommandSnapshot CreateCommand(
        string tenantNId,
        string actorUserNId,
        string action,
        string requestNId,
        string? targetNId,
        string scopeChecksum,
        string? reason,
        string? caseReference = null,
        string? keyword = null,
        bool readOriginal = false,
        DateTimeOffset? expiresOn = null,
        string? subjectType = null,
        string? subjectNId = null,
        IReadOnlyList<string>? fields = null,
        int? messageRetentionDays = null,
        int? attachmentRetentionDays = null,
        int? auditRetentionDays = null,
        bool? enabled = null,
        int? exportRetentionHours = null,
        int? reviewDueHours = null,
        long? expectedOptimisticVersion = null,
        Guid? expectedConcurrencyVersion = null,
        ComplianceScopeDto? scope = null)
        => new ComplianceCommandSnapshot(
            action,
            requestNId,
            targetNId,
            scopeChecksum.ToLowerInvariant(),
            reason,
            ValidateCaseReference(caseReference),
            keyword,
            readOriginal,
            expiresOn,
            subjectType,
            subjectNId,
            (fields ?? []).Order(StringComparer.Ordinal).ToArray(),
            messageRetentionDays,
            attachmentRetentionDays,
            auditRetentionDays,
            enabled,
            exportRetentionHours,
            reviewDueHours,
            expectedOptimisticVersion,
            expectedConcurrencyVersion).BindRequest(tenantNId, actorUserNId, scope);
    private static string ScopeChecksum(ComplianceScopeDto? scope) => Hash(scope ?? new ComplianceScopeDto());

    private static bool IsFileBlocked(CollaborationFileState file) =>
        file.Restricted || file.DeletionStatus is "Requested" or "DeletionRequested" or "Deleted";

    private async Task BindAttachmentReferenceAsync(string tenantNId, string actorUserNId, AttachmentRecord attachment, string messageNId, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(attachment.BoundMessageNId)
            && !string.Equals(attachment.BoundMessageNId, messageNId, StringComparison.Ordinal))
            throw new CollaborationException(409, "COLLAB_ATTACHMENT_MESSAGE_CONFLICT", "附件已绑定其他消息。");
        await _files.BindReferenceAsync(
            tenantNId,
            actorUserNId,
            attachment.FileNId ?? throw new CollaborationException(409, "COLLAB_ATTACHMENT_NOT_READY", "附件尚未完成文件绑定。"),
            attachment.ReferenceNId ?? throw new CollaborationException(409, "COLLAB_ATTACHMENT_NOT_READY", "附件尚未完成授权引用。"),
            attachment.ConversationNId,
            messageNId,
            attachment.AttachmentNId,
            attachment.UploaderUserNId,
            "CollaborationMessageAttachment",
            attachment.IntentRequestNId,
            cancellationToken);
    }

    private static string AuthorizationReference(string tenantNId, string attachmentNId, string actorUserNId, string purpose, string requestNId)
    {
        // One durable File business reference belongs to one chat attachment.
        // The actor/request are authorization facts, not new File bindings;
        // using them here would violate File's (attachment,purpose) uniqueness
        // and make a later member authorization replace the prior reference.
        var input = Encoding.UTF8.GetBytes($"{tenantNId}|{attachmentNId}|CollaborationMessageAttachment");
        return "REF-" + Convert.ToHexString(SHA256.HashData(input)[..16]).ToLowerInvariant();
    }
    private static string CaseScopeChecksum(string tenantNId, ComplianceScopeDto scope) => Hash(new { tenantNId, scope, revision = 1 });
    private static string ExportScopeChecksum(string tenantNId, ComplianceScopeDto scope, IReadOnlyList<string> fields, object messageVersions) => Hash(new { tenantNId, scope, fields = fields.Order(StringComparer.Ordinal).ToArray(), messageVersions });
    private static int ValidateDays(int days) => days is < 1 or > 3650 ? throw new CollaborationException(400, "COLLAB_RETENTION_DAYS_INVALID", "保留天数必须在 1 到 3650 之间。") : days;
    private static int ValidateMessageRetentionDays(int days) => days is < 365 or > 3650 ? throw new CollaborationException(400, "COLLAB_RETENTION_DAYS_INVALID", "消息保留天数必须在 365 到 3650 之间。") : days;
    private static int ValidateHours(int hours) => hours is < 1 or > 168 ? throw new CollaborationException(400, "COLLAB_RETENTION_HOURS_INVALID", "保留小时数必须在 1 到 168 之间。") : hours;
    private static string ValidateReason(string? value, string message)
    {
        var reason = RequireNId(value, message);
        return reason.Length > 500 ? throw new CollaborationException(400, "COLLAB_REASON_INVALID", "原因不能超过 500 个字符。") : reason;
    }
    private static string? ValidateCaseReference(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var result = value.Trim();
        return result.Length > 200 ? throw new CollaborationException(400, "COLLAB_CASE_REFERENCE_INVALID", "案件引用不能超过 200 个字符。") : result;
    }
    private static string RequireRequestNId(string? value, string message)
    {
        var result = RequireNId(value, message);
        return result.Length > 64 ? throw new CollaborationException(400, "COLLAB_REQUEST_ID_INVALID", "请求标识不能超过 64 个字符。") : result;
    }
    private static string RequireShortRequestNId(string? value, string message)
    {
        var result = RequireRequestNId(value, message);
        return result.Length > 32 ? throw new CollaborationException(400, "COLLAB_REQUEST_ID_INVALID", "请求标识不能超过 32 个字符。") : result;
    }
    private static DateTimeOffset ViewBudgetWindow(DateTimeOffset now)
    {
        var utc = now.ToUniversalTime();
        var minute = utc.Minute - utc.Minute % 10;
        return new DateTimeOffset(utc.Year, utc.Month, utc.Day, utc.Hour, minute, 0, TimeSpan.Zero);
    }
    private static void EnsureVersion(long currentOptimisticVersion, Guid currentConcurrencyVersion, long? expectedOptimisticVersion, Guid? expectedConcurrencyVersion, string code, string message)
    {
        if ((expectedOptimisticVersion is not null && expectedOptimisticVersion.Value != currentOptimisticVersion)
            || (expectedConcurrencyVersion is not null && expectedConcurrencyVersion.Value != currentConcurrencyVersion))
            throw new CollaborationException(409, code, message);
    }
    private static string[] ValidateExportFields(IReadOnlyList<string>? values)
    {
        var fields = (values ?? ["messageNId", "sequence", "senderUserNId", "acceptedOn", "messageType"])
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var allowed = new HashSet<string>(["messageNId", "sequence", "senderUserNId", "acceptedOn", "messageType", "textContent", "attachmentMetadata"], StringComparer.Ordinal);
        if (fields.Length == 0 || fields.Any(value => !allowed.Contains(value)))
            throw new CollaborationException(400, "COLLAB_EXPORT_FIELDS_INVALID", "导出字段不受支持或不能为空。");
        return fields;
    }
    private static ComplianceScopeDto ValidateScope(ComplianceScopeDto? input, bool allowEmpty)
    {
        var scope = input ?? new ComplianceScopeDto { ScopeType = "TimeRange" };
        if (scope.SchemaVersion != 1)
            throw new CollaborationException(400, "COLLAB_SCOPE_INVALID", "范围版本不受支持。");
        var type = (scope.ScopeType?.Trim() ?? string.Empty);
        if (string.IsNullOrWhiteSpace(type))
            type = "TimeRange";
        if (type is not ("Conversation" or "TimeRange" or "MessageSet"))
            throw new CollaborationException(400, "COLLAB_SCOPE_INVALID", "范围类型不受支持。");
        var conversationNId = scope.ConversationNId?.Trim();
        var from = scope.FromOn ?? scope.From;
        var until = scope.ToOn ?? scope.Until;
        var messageNIds = scope.MessageNIds?.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim()).Distinct(StringComparer.Ordinal).ToArray();
        if (type == "Conversation")
        {
            if (string.IsNullOrWhiteSpace(conversationNId) || from is not null || until is not null || messageNIds is { Length: > 0 })
                throw new CollaborationException(400, "COLLAB_SCOPE_INVALID", "Conversation范围必须只包含会话标识。");
        }
        else if (type == "TimeRange")
        {
            if (from is null || until is null)
            {
                if (!allowEmpty || conversationNId is not null || messageNIds is { Length: > 0 })
                    throw new CollaborationException(400, "COLLAB_SCOPE_INVALID", "TimeRange范围必须提供起止时间。");
            }
            else if (from >= until || until - from > TimeSpan.FromDays(31))
                throw new CollaborationException(400, "COLLAB_SCOPE_INVALID", "时间范围必须为[from,to)且不超过31天。");
        }
        else if (messageNIds is null or { Length: < 1 or > 1000 } || conversationNId is not null || from is not null || until is not null)
            throw new CollaborationException(400, "COLLAB_SCOPE_INVALID", "MessageSet范围必须包含1到1000条消息标识。");
        return scope with { ScopeType = type, ConversationNId = conversationNId, FromOn = from, ToOn = until, From = null, Until = null, MessageNIds = messageNIds };
    }
    private static (ComplianceScopeDto? Scope, IReadOnlyList<string> Fields) ParseExportScope(string scopeJson, string? fieldsJson)
    {
        try
        {
            using var document = JsonDocument.Parse(scopeJson);
            if (document.RootElement.TryGetProperty("scope", out var scopeElement))
            {
                var scope = scopeElement.Deserialize<ComplianceScopeDto>() ?? new();
                var fields = document.RootElement.TryGetProperty("fields", out var fieldsElement)
                    ? fieldsElement.Deserialize<string[]>() ?? []
                    : fieldsJson is null ? [] : JsonSerializer.Deserialize<string[]>(fieldsJson) ?? [];
                return (scope, fields);
            }
        }
        catch (JsonException)
        {
        }
        return (JsonSerializer.Deserialize<ComplianceScopeDto>(scopeJson), fieldsJson is null ? [] : JsonSerializer.Deserialize<string[]>(fieldsJson) ?? []);
    }
    private static CollaborationException HistoryExpired(ConversationRecord conversation) =>
        new(410, "COLLAB_MESSAGE_HISTORY_EXPIRED", $"消息历史已过保留边界，最早可用序号为 {conversation.RetentionFloorSequence + 1}。");
}
