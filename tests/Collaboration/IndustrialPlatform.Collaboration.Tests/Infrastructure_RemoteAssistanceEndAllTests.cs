using System.Text.Json;
using IndustrialPlatform.Collaboration.Application.RemoteAssistance;
using IndustrialPlatform.Collaboration.Infrastructure;
using IndustrialPlatform.Collaboration.Infrastructure.Persistence;
using IndustrialPlatform.Infrastructure.Database;
using Microsoft.Extensions.Options;
using SqlSugar;

namespace IndustrialPlatform.Collaboration.Tests;

public sealed class Infrastructure_RemoteAssistanceEndAllTests
{
    [Fact]
    public async Task EndAll_rolls_back_both_sessions_and_outbox_when_first_event_write_fails()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"pf06-remote-assistance-end-all-{Guid.NewGuid():N}.db");
        SqlSugarDbContext? context = null;
        try
        {
            context = CreateContext(databasePath);
            foreach (var migration in CollaborationSchemaMigrations.All)
                await migration.Apply(context.SqlSugar, CancellationToken.None);

            var repository = new SqlCollaborationRepository(context);
            var now = DateTimeOffset.UtcNow;
            var screen = CreateScreen(now);
            var voice = CreateVoice(now);
            await repository.CreateScreenAsync(screen, new { action = "Invite" }, CancellationToken.None);
            await repository.CreateVoiceAsync(
                voice,
                [
                    new VoiceUserSlotRecord("T-1", "U-1", "VC-1", now, now.AddMinutes(5)),
                    new VoiceUserSlotRecord("T-1", "U-2", "VC-1", now, now.AddMinutes(5)),
                ],
                new { action = "Invite" },
                CancellationToken.None);

            var outboxBefore = await context.SqlSugar.Ado.GetIntAsync("SELECT COUNT(*) FROM collaboration_outbox_message WHERE tenant_n_id = 'T-1'");
            var endedOn = now.AddSeconds(1);
            var endedScreen = screen with
            {
                State = "Ended",
                EndedOn = endedOn,
                EndReason = "EndedAll",
                LastUpdatedOn = endedOn,
                Version = 2,
                ConcurrencyVersion = Guid.NewGuid(),
            };
            var endedVoice = voice with
            {
                State = "Ended",
                EndedOn = endedOn,
                EndReason = "EndedAll",
                LastUpdatedOn = endedOn,
                Version = 2,
                ConcurrencyVersion = Guid.NewGuid(),
            };
            var cyclicPayload = new CyclicPayload();
            cyclicPayload.Self = cyclicPayload;

            await Assert.ThrowsAsync<JsonException>(() => repository.EndAllAsync(
                endedScreen,
                screen.Version,
                cyclicPayload,
                endedVoice,
                voice.Version,
                new { capability = "Voice", action = "End" },
                CancellationToken.None));

            var persistedScreen = await repository.GetScreenAsync("T-1", "SS-1", CancellationToken.None);
            var persistedVoice = await repository.GetVoiceAsync("T-1", "VC-1", CancellationToken.None);
            var slotsAfter = await context.SqlSugar.Ado.GetIntAsync("SELECT COUNT(*) FROM collaboration_remote_assistance_voice_slot WHERE tenant_n_id = 'T-1' AND voice_call_n_id = 'VC-1'");
            var outboxAfter = await context.SqlSugar.Ado.GetIntAsync("SELECT COUNT(*) FROM collaboration_outbox_message WHERE tenant_n_id = 'T-1'");

            Assert.NotNull(persistedScreen);
            Assert.Equal("Accepted", persistedScreen!.State);
            Assert.Equal(1, persistedScreen.Version);
            Assert.NotNull(persistedVoice);
            Assert.Equal("Accepted", persistedVoice!.State);
            Assert.Equal(1, persistedVoice.Version);
            Assert.Equal(2, slotsAfter);
            Assert.Equal(outboxBefore, outboxAfter);

            var endedBoth = await repository.EndAllAsync(
                endedScreen,
                screen.Version,
                new { capability = "Screen", action = "End" },
                endedVoice,
                voice.Version,
                new { capability = "Voice", action = "End" },
                CancellationToken.None);
            var slotsAfterSuccess = await context.SqlSugar.Ado.GetIntAsync("SELECT COUNT(*) FROM collaboration_remote_assistance_voice_slot WHERE tenant_n_id = 'T-1' AND voice_call_n_id = 'VC-1'");
            var outboxAfterSuccess = await context.SqlSugar.Ado.GetIntAsync("SELECT COUNT(*) FROM collaboration_outbox_message WHERE tenant_n_id = 'T-1'");
            Assert.Equal("Ended", endedBoth!.Screen!.State);
            Assert.Equal("Ended", endedBoth.Voice!.State);
            Assert.Equal(0, slotsAfterSuccess);
            Assert.Equal(outboxBefore + 2, outboxAfterSuccess);

            var replacement = screen with
            {
                Id = Guid.NewGuid(),
                SessionNId = "SS-2",
                RequestNId = "REQ-S-2",
                RequestHash = "HASH-S-2",
                CreatedOn = endedOn,
                LastUpdatedOn = endedOn,
                ConcurrencyVersion = Guid.NewGuid(),
            };
            await repository.CreateScreenAsync(replacement, new { action = "Invite" }, CancellationToken.None);
            var oldEnded = await repository.EndAllAsync(
                endedScreen,
                endedScreen.Version,
                null,
                null,
                null,
                null,
                CancellationToken.None);
            var persistedReplacement = await repository.GetScreenAsync("T-1", "SS-2", CancellationToken.None);

            Assert.NotNull(oldEnded?.Screen);
            Assert.Equal("SS-1", oldEnded!.Screen!.SessionNId);
            Assert.Equal("Ended", oldEnded.Screen.State);
            Assert.NotNull(persistedReplacement);
            Assert.Equal("Accepted", persistedReplacement!.State);
        }
        finally
        {
            context?.Dispose();
            DeleteDatabase(databasePath);
        }
    }

    private static ScreenShareSessionRecord CreateScreen(DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        TenantNId = "T-1",
        SessionNId = "SS-1",
        ConversationNId = "CV-1",
        InitiatorUserNId = "U-1",
        InviteeUserNId = "U-2",
        Direction = "ShareMine",
        SharerUserNId = "U-1",
        ViewerUserNId = "U-2",
        State = "Accepted",
        RequestNId = "REQ-S-1",
        RequestHash = "HASH-S-1",
        DeadlineOn = now.AddMinutes(5),
        AcceptedOn = now,
        InitiatorConnectionId = "CONN-U-1",
        CreatedOn = now,
        LastUpdatedOn = now,
        Version = 1,
        ConcurrencyVersion = Guid.NewGuid(),
    };

    private static VoiceCallSessionRecord CreateVoice(DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        TenantNId = "T-1",
        CallNId = "VC-1",
        ConversationNId = "CV-1",
        CallerUserNId = "U-1",
        CalleeUserNId = "U-2",
        State = "Accepted",
        RequestNId = "REQ-V-1",
        RequestHash = "HASH-V-1",
        DeadlineOn = now.AddMinutes(5),
        AcceptedOn = now,
        CallerConnectionId = "CONN-U-1",
        CreatedOn = now,
        LastUpdatedOn = now,
        Version = 1,
        ConcurrencyVersion = Guid.NewGuid(),
    };

    private sealed class CyclicPayload
    {
        public CyclicPayload? Self { get; set; }
    }

    private static SqlSugarDbContext CreateContext(string databasePath) =>
        new(Options.Create(new SqlSugarOptions { DbType = DbType.Sqlite, ConnectionString = $"Data Source={databasePath}", IsAutoCloseConnection = true }));

    private static void DeleteDatabase(string databasePath)
    {
        if (!File.Exists(databasePath))
            return;
        for (var attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                File.Delete(databasePath);
                return;
            }
            catch (IOException) when (attempt < 9)
            {
                Thread.Sleep(25);
            }
            catch (IOException)
            {
                return;
            }
        }
    }
}
