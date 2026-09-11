using IndustrialPlatform.Collaboration.Application;
using Xunit;

namespace IndustrialPlatform.Collaboration.Tests;

public sealed class PageCursorCodecTests
{
    [Fact]
    public void Cursor_BindsToQueryAndRejectsTampering()
    {
        var codec = new PageCursorCodec("test cursor signing key with enough entropy", TimeSpan.FromMinutes(5));
        var cursor = codec.Encode(2, "tenant:user:query");

        Assert.Equal(2, codec.Decode(cursor, "tenant:user:query"));
        Assert.Throws<CollaborationException>(() => codec.Decode(cursor + "x", "tenant:user:query"));
        Assert.Throws<CollaborationException>(() => codec.Decode(cursor, "tenant:user:other-query"));
    }

    [Fact]
    public void Cursor_ReportsExpirySeparately()
    {
        var codec = new PageCursorCodec("test cursor signing key with enough entropy", TimeSpan.FromSeconds(-1));
        var cursor = codec.Encode(2, "query");

        var exception = Assert.Throws<CollaborationException>(() => codec.Decode(cursor, "query"));
        Assert.Equal(410, exception.StatusCode);
        Assert.Equal("COLLAB_CURSOR_EXPIRED", exception.Code);
    }
}
