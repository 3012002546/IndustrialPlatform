namespace IndustrialPlatform.Collaboration.Domain;

public static class CollaborationDomainRules
{
    public static bool IsCanonicalPair(string left, string right) =>
        string.CompareOrdinal(left, right) < 0;

    public static bool IsMessageTypeAllowed(string? messageType) =>
        messageType is "Text" or "Image" or "File";

    public static bool IsPresenceStateAllowed(string? state) =>
        state is "Online" or "Away" or "Offline";
}

public enum ConversationStatus
{
    Active,
    Suspended,
}

public enum MessageState
{
    Accepted,
    Retracted,
}

public enum AttachmentReferenceState
{
    Pending,
    Authorized,
    Released,
}
