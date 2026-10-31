using System.Reflection;
using IndustrialPlatform.RuntimeSupport.Files;
using IndustrialPlatform.SystemData.Contracts.Files;

namespace IndustrialPlatform.PlatformStudio.Tests;

public sealed class OwnedConfirmationTests
{
    public class CommitOnly : DispatchProxy
    {
        public int Calls { get; private set; }
        public Func<OwnedFileCommitV1, CancellationToken, Task<OwnedFileStatusV1>> Commit { get; set; } = null!;
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            Assert.Equal(nameof(IOwnedFileService.CommitAsync), targetMethod!.Name);
            Calls++;
            return Commit((OwnedFileCommitV1)args![1]!, (CancellationToken)args[2]!);
        }
    }
    private static readonly TrustedServiceContext Context = new("tenant", "platform-studio", "user");
    private static OwnedFileUploads.Result Upload(string status = "Quarantined") => new(
        new("upload", "reference", DateTimeOffset.UtcNow.AddHours(1)),
        new FileObjectV1 { FileNId = "file", Sha256 = new string('a', 64) },
        new("reference", null, status, "1", null));

    [Fact]
    public async Task Waits_for_the_same_upload_confirmation_without_reuploading()
    {
        var service = DispatchProxy.Create<IOwnedFileService, CommitOnly>();
        ((CommitOnly)service).Commit = (request, _) =>
        {
            Assert.Equal(new("reference", "file", new string('a', 64)), request);
            return Task.FromResult(new OwnedFileStatusV1("reference", "file", "Active", "2", null));
        };
        var result = await OwnedFileUploads.WaitForConfirmationAsync(service, Context, Upload(), TimeSpan.FromSeconds(2), default);
        Assert.Equal("Active", result.Status);
        Assert.Equal(1, ((CommitOnly)service).Calls);
    }

    [Theory]
    [InlineData("Quarantined")]
    [InlineData("Rejected")]
    [InlineData("Released")]
    [InlineData("Active")]
    public async Task Does_not_turn_unconfirmed_or_terminal_content_into_success(string status)
    {
        var service = DispatchProxy.Create<IOwnedFileService, CommitOnly>();
        var upload = Upload(status);
        Assert.Equal(upload.Reference, await OwnedFileUploads.WaitForConfirmationAsync(service, Context, upload, TimeSpan.Zero, default));
        Assert.Equal(0, ((CommitOnly)service).Calls);
    }

    [Fact]
    public async Task Request_cancellation_stops_waiting_and_never_commits()
    {
        var service = DispatchProxy.Create<IOwnedFileService, CommitOnly>();
        using var cancellation = new CancellationTokenSource();
        var waiting = OwnedFileUploads.WaitForConfirmationAsync(service, Context, Upload(), TimeSpan.FromSeconds(30), cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
        Assert.Equal(0, ((CommitOnly)service).Calls);
    }
}
