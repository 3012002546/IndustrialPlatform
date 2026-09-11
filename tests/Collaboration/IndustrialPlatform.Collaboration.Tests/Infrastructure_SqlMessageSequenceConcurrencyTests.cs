using IndustrialPlatform.Collaboration.Application;
using IndustrialPlatform.Collaboration.Contracts;
using IndustrialPlatform.Collaboration.Infrastructure;
using IndustrialPlatform.Collaboration.Infrastructure.Persistence;
using IndustrialPlatform.EventBus.Consumer;
using IndustrialPlatform.Identity.Api.Controllers;
using IndustrialPlatform.Identity.Application.Authentication;
using IndustrialPlatform.Identity.Contracts.Collaboration;
using IndustrialPlatform.Identity.Domain.Passwords;
using IndustrialPlatform.Identity.Domain.Permissions;
using IndustrialPlatform.Identity.Domain.Users;
using IndustrialPlatform.Identity.Infrastructure.Authentication;
using IndustrialPlatform.Identity.Infrastructure.Passwords;
using IndustrialPlatform.Identity.Infrastructure.Persistence.Entities;
using IndustrialPlatform.Identity.Infrastructure.Persistence.Migrations;
using IndustrialPlatform.Infrastructure.Database;
using IndustrialPlatform.Security;
using IndustrialPlatform.Web.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using SqlSugar;
using System.Globalization;
using System.Security.Claims;

namespace IndustrialPlatform.Collaboration.Tests;

public sealed class Infrastructure_SqlMessageSequenceConcurrencyTests
{
    [Fact]
    public async Task Crashed_inbox_owner_uses_busy_retry_without_spending_dlq_budget_then_reclaims()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"pf05-collaboration-inbox-recovery-{Guid.NewGuid():N}.db");
        SqlSugarDbContext? context = null;
        try
        {
            context = CreateContext(databasePath);
            foreach (var migration in CollaborationSchemaMigrations.All)
                await migration.Apply(context.SqlSugar, CancellationToken.None);
            var repository = new SqlCollaborationRepository(context);
            var eventId = Guid.NewGuid();
            var receivedOn = DateTimeOffset.UtcNow;
            var firstClaim = await repository.TryClaimEventInboxAsync(eventId, "T-1", "file.status", receivedOn, CancellationToken.None);
            Assert.Equal(EventInboxClaimStatus.Claimed, firstClaim.Status);

            var busyRetryHeaders = new Dictionary<string, object?>
            {
                ["x-death"] = Enumerable.Range(0, 5)
                    .Select(_ => (object?)new Dictionary<string, object?>
                    {
                        ["queue"] = "industrial.events.busy-retry",
                        ["reason"] = "expired",
                        ["count"] = 1L,
                    })
                    .ToList(),
            };
            for (var attempt = 0; attempt < 5; attempt++)
            {
                var busy = await repository.TryClaimEventInboxAsync(eventId, "T-1", "file.status", receivedOn, CancellationToken.None);
                Assert.Equal(EventInboxClaimStatus.Busy, busy.Status);
                Assert.Equal(0, RabbitDeliveryPolicy.GetDeliveryAttempt(busyRetryHeaders, "industrial.events"));
            }

            await context.SqlSugar.Ado.ExecuteCommandAsync(
                "UPDATE collaboration_event_inbox SET lease_until = @leaseUntil WHERE event_id = @eventId",
                new[] { new SugarParameter("@leaseUntil", DateTimeOffset.UtcNow.AddMinutes(-1)), new SugarParameter("@eventId", eventId) },
                CancellationToken.None);

            var recoveredClaim = await repository.TryClaimEventInboxAsync(eventId, "T-1", "file.status", receivedOn, CancellationToken.None);
            Assert.Equal(EventInboxClaimStatus.Claimed, recoveredClaim.Status);
            Assert.True(await repository.MarkEventInboxProcessedAsync(eventId, recoveredClaim.LeaseNId!, DateTimeOffset.UtcNow, CancellationToken.None));
            var finalClaim = await repository.TryClaimEventInboxAsync(eventId, "T-1", "file.status", receivedOn, CancellationToken.None);
            Assert.Equal(EventInboxClaimStatus.AlreadyProcessed, finalClaim.Status);
        }
        finally
        {
            context?.Dispose();
            DeleteDatabase(databasePath);
        }
    }

    [Fact]
    public async Task Event_inbox_reclaims_expired_processing_and_does_not_swallow_database_conflicts()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"pf05-collaboration-inbox-{Guid.NewGuid():N}.db");
        SqlSugarDbContext? context = null;
        try
        {
            context = CreateContext(databasePath);
            foreach (var migration in CollaborationSchemaMigrations.All)
                await migration.Apply(context.SqlSugar, CancellationToken.None);
            var repository = new SqlCollaborationRepository(context);
            var eventId = Guid.NewGuid();
            var receivedOn = DateTimeOffset.UtcNow;

            var firstClaim = await repository.TryClaimEventInboxAsync(eventId, "T-1", "file.status", receivedOn, CancellationToken.None);
            Assert.Equal(EventInboxClaimStatus.Claimed, firstClaim.Status);
            Assert.NotNull(firstClaim.LeaseNId);
            var busyClaim = await repository.TryClaimEventInboxAsync(eventId, "T-1", "file.status", receivedOn, CancellationToken.None);
            Assert.Equal(EventInboxClaimStatus.Busy, busyClaim.Status);
            Assert.Null(busyClaim.LeaseNId);

            await context.SqlSugar.Ado.ExecuteCommandAsync(
                "UPDATE collaboration_event_inbox SET lease_until = @leaseUntil WHERE event_id = @eventId",
                new[] { new SugarParameter("@leaseUntil", DateTimeOffset.UtcNow.AddMinutes(-1)), new SugarParameter("@eventId", eventId) },
                CancellationToken.None);

            var secondClaim = await repository.TryClaimEventInboxAsync(eventId, "T-1", "file.status", receivedOn, CancellationToken.None);
            Assert.Equal(EventInboxClaimStatus.Claimed, secondClaim.Status);
            Assert.NotNull(secondClaim.LeaseNId);
            Assert.NotEqual(firstClaim.LeaseNId, secondClaim.LeaseNId);
            Assert.False(await repository.MarkEventInboxProcessedAsync(eventId, firstClaim.LeaseNId!, DateTimeOffset.UtcNow, CancellationToken.None));
            Assert.False(await repository.MarkEventInboxFailedAsync(eventId, "stale worker", firstClaim.LeaseNId!, DateTimeOffset.UtcNow, CancellationToken.None));
            Assert.True(await repository.MarkEventInboxProcessedAsync(eventId, secondClaim.LeaseNId!, DateTimeOffset.UtcNow, CancellationToken.None));

            var processedClaim = await repository.TryClaimEventInboxAsync(eventId, "T-1", "file.status", receivedOn, CancellationToken.None);
            Assert.Equal(EventInboxClaimStatus.AlreadyProcessed, processedClaim.Status);
            Assert.Null(processedClaim.LeaseNId);
        }
        finally
        {
            context?.Dispose();
            DeleteDatabase(databasePath);
        }
    }

    [Fact]
    public async Task Concurrent_repository_instances_allocate_each_message_sequence_once()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"pf05-collaboration-{Guid.NewGuid():N}.db");
        try
        {
            var contexts = Enumerable.Range(0, 51).Select(_ => CreateContext(databasePath)).ToArray();
            try
            {
            await CollaborationSchemaMigrations.All[0].Apply(contexts[0].SqlSugar, CancellationToken.None);
            await CollaborationSchemaMigrations.All[1].Apply(contexts[0].SqlSugar, CancellationToken.None);

            var conversation = new ConversationRecord("T-1", "CV-1", "U-1", "U-2", "Active", 0, null, null, 0, 1, Guid.NewGuid());
            var member1 = new ConversationMemberRecord("T-1", "CV-1", "U-1", "Alice", DateTimeOffset.UtcNow, "Visible", null, 0, 0, null, 0, 1, Guid.NewGuid());
            var member2 = member1 with { UserNId = "U-2", DisplayNameSnapshot = "Bob" };
            var seedRepository = new SqlCollaborationRepository(contexts[0]);
            await seedRepository.CreateConversationAsync(conversation, member1, member2, CancellationToken.None);

            var repositories = contexts.Skip(1).Select(context => new SqlCollaborationRepository(context)).ToArray();
            var tasks = Enumerable.Range(1, 50).Select(index => Task.Run(() => repositories[index % repositories.Length].AppendMessageAsync(
                conversation,
                new MessageRecord("T-1", "CV-1", $"MSG-{index}", 0, index % 2 == 0 ? "U-2" : "U-1", $"CLIENT-{index}", $"hash-{index}", "Text", $"message {index}", null, null, DateTimeOffset.UtcNow, null, null, null, 1, Guid.NewGuid()),
                CancellationToken.None)));

            var messages = await Task.WhenAll(tasks);
            var finalConversation = await new SqlCollaborationRepository(contexts[50]).GetConversationAsync("T-1", "CV-1", CancellationToken.None);

            Assert.Equal(Enumerable.Range(1, 50).Select(item => (long)item), messages.Select(item => item.Sequence).OrderBy(item => item));
            Assert.Equal(50, finalConversation!.LastMessageSequence);
            }
            finally
            {
                foreach (var context in contexts)
                    context.Dispose();
            }
        }
        finally
        {
            if (File.Exists(databasePath))
            {
                for (var attempt = 0; attempt < 10; attempt++)
                {
                    try
                    {
                        File.Delete(databasePath);
                        break;
                    }
                    catch (IOException) when (attempt < 9)
                    {
                        Thread.Sleep(25);
                    }
                    catch (IOException)
                    {
                        break;
                    }
                }
            }
        }
    }

    [Fact]
    public async Task Concurrent_repository_instances_create_one_conversation_for_the_same_pair()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"pf05-collaboration-create-{Guid.NewGuid():N}.db");
        try
        {
            var contexts = Enumerable.Range(0, 20).Select(_ => CreateContext(databasePath)).ToArray();
            try
            {
            await CollaborationSchemaMigrations.All[0].Apply(contexts[0].SqlSugar, CancellationToken.None);
            await CollaborationSchemaMigrations.All[1].Apply(contexts[0].SqlSugar, CancellationToken.None);

            var repositories = new[]
            {
                new SqlCollaborationRepository(contexts[0]),
                new SqlCollaborationRepository(contexts[1]),
                new SqlCollaborationRepository(contexts[2]),
                new SqlCollaborationRepository(contexts[3]),
                new SqlCollaborationRepository(contexts[4]),
                new SqlCollaborationRepository(contexts[5]),
                new SqlCollaborationRepository(contexts[6]),
                new SqlCollaborationRepository(contexts[7]),
                new SqlCollaborationRepository(contexts[8]),
                new SqlCollaborationRepository(contexts[9]),
                new SqlCollaborationRepository(contexts[10]),
                new SqlCollaborationRepository(contexts[11]),
                new SqlCollaborationRepository(contexts[12]),
                new SqlCollaborationRepository(contexts[13]),
                new SqlCollaborationRepository(contexts[14]),
                new SqlCollaborationRepository(contexts[15]),
                new SqlCollaborationRepository(contexts[16]),
                new SqlCollaborationRepository(contexts[17]),
                new SqlCollaborationRepository(contexts[18]),
                new SqlCollaborationRepository(contexts[19]),
            };
            var tasks = Enumerable.Range(1, 20).Select(index => Task.Run(() => repositories[index % repositories.Length].CreateConversationAsync(
                new ConversationRecord("T-1", $"CV-{index}", "U-1", "U-2", "Active", 0, null, null, 0, 1, Guid.NewGuid()),
                new ConversationMemberRecord("T-1", $"CV-{index}", "U-1", "Alice", DateTimeOffset.UtcNow, "Visible", null, 0, 0, null, 0, 1, Guid.NewGuid()),
                new ConversationMemberRecord("T-1", $"CV-{index}", "U-2", "Bob", DateTimeOffset.UtcNow, "Visible", null, 0, 0, null, 0, 1, Guid.NewGuid()),
                CancellationToken.None)));

            var results = await Task.WhenAll(tasks);

            Assert.Single(results.Select(item => item.ConversationNId).Distinct());
            Assert.Equal(1, await contexts[0].SqlSugar.Ado.GetIntAsync("SELECT COUNT(*) FROM collaboration_conversation"));
            Assert.Equal(2, await contexts[0].SqlSugar.Ado.GetIntAsync("SELECT COUNT(*) FROM collaboration_conversation_member"));
            }
            finally
            {
                foreach (var context in contexts)
                    context.Dispose();
            }
        }
        finally
        {
            DeleteDatabase(databasePath);
        }
    }

    [Fact]
    public async Task Failed_message_insert_rolls_back_sequence_allocation_before_next_send()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"pf05-collaboration-rollback-{Guid.NewGuid():N}.db");
        SqlSugarDbContext? context = null;
        try
        {
            context = CreateContext(databasePath);
            await CollaborationSchemaMigrations.All[0].Apply(context.SqlSugar, CancellationToken.None);
            await CollaborationSchemaMigrations.All[1].Apply(context.SqlSugar, CancellationToken.None);
            var repository = new SqlCollaborationRepository(context);
            var conversation = new ConversationRecord("T-1", "CV-1", "U-1", "U-2", "Active", 0, null, null, 0, 1, Guid.NewGuid());
            var member1 = new ConversationMemberRecord("T-1", "CV-1", "U-1", "Alice", DateTimeOffset.UtcNow, "Visible", null, 0, 0, null, 0, 1, Guid.NewGuid());
            var member2 = member1 with { UserNId = "U-2", DisplayNameSnapshot = "Bob" };
            await repository.CreateConversationAsync(conversation, member1, member2, CancellationToken.None);
            var first = await repository.AppendMessageAsync(conversation, Message("MSG-1", "CLIENT-1", "U-1"), CancellationToken.None);

            await Assert.ThrowsAnyAsync<Exception>(() => repository.AppendMessageAsync(
                conversation with { LastMessageSequence = first.Sequence, OptimisticVersion = 2 },
                Message("MSG-1", "CLIENT-2", "U-2"),
                CancellationToken.None));

            var afterFailure = await repository.GetConversationAsync("T-1", "CV-1", CancellationToken.None);
            Assert.Equal(1, afterFailure!.LastMessageSequence);
            Assert.Equal(1, await context.SqlSugar.Ado.GetIntAsync("SELECT COUNT(*) FROM collaboration_message"));

            var second = await repository.AppendMessageAsync(
                conversation with { LastMessageSequence = first.Sequence, OptimisticVersion = 2 },
                Message("MSG-2", "CLIENT-2", "U-2"),
                CancellationToken.None);
            Assert.Equal(2, second.Sequence);
            Assert.Equal(2, (await repository.GetConversationAsync("T-1", "CV-1", CancellationToken.None))!.LastMessageSequence);
        }
        finally
        {
            context?.Dispose();
            DeleteDatabase(databasePath);
        }
    }

    [Fact]
    public async Task Sqlite_message_sent_immediately_can_be_retracted()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"pf05-collaboration-retract-utc-{Guid.NewGuid():N}.db");
        SqlSugarDbContext? context = null;
        try
        {
            context = CreateContext(databasePath);
            foreach (var migration in CollaborationSchemaMigrations.All)
                await migration.Apply(context.SqlSugar, CancellationToken.None);
            var repository = new SqlCollaborationRepository(context);
            var conversation = new ConversationRecord("T-1", "CV-1", "U-1", "U-2", "Active", 0, null, null, 0, 1, Guid.NewGuid());
            var member = new ConversationMemberRecord("T-1", "CV-1", "U-1", "Alice", DateTimeOffset.UtcNow, "Visible", null, 0, 0, null, 0, 1, Guid.NewGuid());
            await repository.CreateConversationAsync(conversation, member, member with { UserNId = "U-2", DisplayNameSnapshot = "Bob" }, CancellationToken.None);
            var service = new CollaborationService(
                repository, new ActiveDirectory(), null!, new NoopAuditPort(), null!, null!, null!, null!,
                new PageCursorCodec("test-signing-key", TimeSpan.FromMinutes(5)));

            var sent = await service.SendMessageAsync("T-1", "U-1", "CV-1", new SendMessageRequest
            {
                ClientMessageNId = "CLIENT-1",
                MessageType = "Text",
                TextContent = "hello",
            }, CancellationToken.None);
            var retracted = await service.RetractAsync("T-1", "U-1", "CV-1", sent.MessageNId, new RetractMessageRequest(), CancellationToken.None);

            Assert.Equal("Retracted", retracted.State);
            Assert.NotNull(retracted.RetractedOn);
        }
        finally
        {
            context?.Dispose();
            DeleteDatabase(databasePath);
        }
    }

    [Fact]
    public async Task Concurrent_hide_and_retract_are_idempotent_state_transitions()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"pf05-collaboration-visibility-{Guid.NewGuid():N}.db");
        var contexts = Enumerable.Range(0, 5).Select(_ => CreateContext(databasePath)).ToArray();
        try
        {
            await CollaborationSchemaMigrations.All[0].Apply(contexts[0].SqlSugar, CancellationToken.None);
            await CollaborationSchemaMigrations.All[1].Apply(contexts[0].SqlSugar, CancellationToken.None);
            var conversation = new ConversationRecord("T-1", "CV-1", "U-1", "U-2", "Active", 0, null, null, 0, 1, Guid.NewGuid());
            var member1 = new ConversationMemberRecord("T-1", "CV-1", "U-1", "Alice", DateTimeOffset.UtcNow, "Visible", null, 0, 0, null, 0, 1, Guid.NewGuid());
            await new SqlCollaborationRepository(contexts[0]).CreateConversationAsync(
                conversation,
                member1,
                member1 with { UserNId = "U-2", DisplayNameSnapshot = "Bob" },
                CancellationToken.None);
            var repository = new SqlCollaborationRepository(contexts[0]);
            var message = await repository.AppendMessageAsync(conversation, Message("MSG-1", "CLIENT-1", "U-1"), CancellationToken.None);

            var hides = await Task.WhenAll(
                new SqlCollaborationRepository(contexts[1]).HideMemberAsync("T-1", "CV-1", "U-1", 1, null, null, CancellationToken.None),
                new SqlCollaborationRepository(contexts[2]).HideMemberAsync("T-1", "CV-1", "U-1", 1, null, null, CancellationToken.None));
            Assert.All(hides, item => Assert.Equal("Hidden", item.VisibilityState));
            Assert.Single(hides.Select(item => item.ProjectionVersion).Distinct());
            Assert.Equal(3, hides[0].ProjectionVersion);

            var retractions = await Task.WhenAll(
                new SqlCollaborationRepository(contexts[3]).RetractMessageAsync("T-1", "CV-1", "MSG-1", "U-1", "sender_retract", null, null, CancellationToken.None),
                new SqlCollaborationRepository(contexts[4]).RetractMessageAsync("T-1", "CV-1", "MSG-1", "U-1", "sender_retract", null, null, CancellationToken.None));
            Assert.All(retractions, item => Assert.NotNull(item.RetractedOn));
            Assert.Single(retractions.Select(item => item.MessageStateVersion).Distinct());
            Assert.Equal(2, retractions[0].MessageStateVersion);
            Assert.Equal(1, await contexts[0].SqlSugar.Ado.GetIntAsync("SELECT COUNT(*) FROM collaboration_message"));
            Assert.Equal(1, await contexts[0].SqlSugar.Ado.GetIntAsync("SELECT COUNT(*) FROM collaboration_conversation_member WHERE visibility_state = 'Hidden'"));
        }
        finally
        {
            foreach (var context in contexts)
                context.Dispose();
            DeleteDatabase(databasePath);
        }
    }

    [Fact]
    public async Task Concurrent_budget_reservations_are_atomic_across_repository_instances()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"pf05-collaboration-budget-{Guid.NewGuid():N}.db");
        var contexts = Enumerable.Range(0, 22).Select(_ => CreateContext(databasePath)).ToArray();
        try
        {
            await CollaborationSchemaMigrations.All[0].Apply(contexts[0].SqlSugar, CancellationToken.None);
            await CollaborationSchemaMigrations.All[1].Apply(contexts[0].SqlSugar, CancellationToken.None);
            var window = new DateTimeOffset(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);
            var repositories = contexts.Skip(1).Take(20).Select(context => new SqlCollaborationRepository(context)).ToArray();
            var reservations = await Task.WhenAll(repositories.Select(repository => Task.Run(() => repository.ReserveComplianceViewBudgetAsync("T-1", "U-1", window, 10, CancellationToken.None))));

            Assert.Equal(20, reservations.Count(item => item.ReservedCount == 10));
            Assert.Equal(0, reservations.Count(item => item.ReservedCount < 0));
            Assert.Equal(200, await contexts[0].SqlSugar.Ado.GetIntAsync("SELECT result_count FROM collaboration_compliance_view_budget WHERE tenant_n_id = 'T-1' AND actor_user_n_id = 'U-1'"));

            var rejected = await new SqlCollaborationRepository(contexts[21]).ReserveComplianceViewBudgetAsync("T-1", "U-1", window, 1, CancellationToken.None);
            Assert.Equal(-1, rejected.ReservedCount);
            Assert.Equal(0, rejected.Remaining);
        }
        finally
        {
            foreach (var context in contexts)
                context.Dispose();
            DeleteDatabase(databasePath);
        }
    }

    [Fact]
    public async Task Outbox_claim_retry_and_publish_are_durable_and_exclusive()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"pf05-collaboration-outbox-{Guid.NewGuid():N}.db");
        SqlSugarDbContext? context = null;
        try
        {
            context = CreateContext(databasePath);
            foreach (var migration in CollaborationSchemaMigrations.All)
                await migration.Apply(context.SqlSugar, CancellationToken.None);
            var repository = new SqlCollaborationRepository(context);
            var conversation = new ConversationRecord("T-1", "CV-1", "U-1", "U-2", "Active", 0, null, null, 0, 1, Guid.NewGuid());
            var member = new ConversationMemberRecord("T-1", "CV-1", "U-1", "Alice", DateTimeOffset.UtcNow, "Visible", null, 0, 0, null, 0, 1, Guid.NewGuid());
            await repository.CreateConversationAsync(conversation, member, member with { UserNId = "U-2", DisplayNameSnapshot = "Bob" }, CancellationToken.None);
            await repository.AppendMessageAsync(conversation, Message("MSG-1", "CLIENT-1", "U-1"), CancellationToken.None);

            var now = DateTimeOffset.UtcNow;
            var pending = Assert.Single(await repository.ListPendingOutboxAsync(now, 100, CancellationToken.None));
            var first = await repository.TryClaimOutboxAsync(pending.EventId, "LEASE-1", now.AddMinutes(1), now, CancellationToken.None);
            Assert.NotNull(first);
            Assert.Null(await repository.TryClaimOutboxAsync(pending.EventId, "LEASE-2", now.AddMinutes(1), now, CancellationToken.None));
            Assert.True(await repository.MarkOutboxFailedAsync(pending.EventId, "LEASE-1", 1, "temporary", false, now, CancellationToken.None));
            Assert.Empty(await repository.ListPendingOutboxAsync(now, 100, CancellationToken.None));

            var retried = Assert.Single(await repository.ListPendingOutboxAsync(now.AddSeconds(3), 100, CancellationToken.None));
            Assert.Equal(1, retried.RetryCount);
            Assert.NotNull(await repository.TryClaimOutboxAsync(retried.EventId, "LEASE-3", now.AddMinutes(2), now.AddSeconds(3), CancellationToken.None));
            Assert.True(await repository.MarkOutboxPublishedAsync(retried.EventId, "LEASE-3", now.AddSeconds(3), CancellationToken.None));
            Assert.Empty(await repository.ListPendingOutboxAsync(now.AddSeconds(3), 100, CancellationToken.None));
        }
        finally
        {
            context?.Dispose();
            DeleteDatabase(databasePath);
        }
    }

    [Fact]
    public async Task Retention_sweep_advances_checkpoint_and_floor_without_touching_new_messages()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"pf05-collaboration-retention-{Guid.NewGuid():N}.db");
        SqlSugarDbContext? context = null;
        try
        {
            context = CreateContext(databasePath);
            foreach (var migration in CollaborationSchemaMigrations.All)
                await migration.Apply(context.SqlSugar, CancellationToken.None);
            var repository = new SqlCollaborationRepository(context);
            var conversation = new ConversationRecord("T-1", "CV-1", "U-1", "U-2", "Active", 0, null, null, 0, 1, Guid.NewGuid());
            var member = new ConversationMemberRecord("T-1", "CV-1", "U-1", "Alice", DateTimeOffset.UtcNow, "Visible", null, 0, 0, null, 0, 1, Guid.NewGuid());
            await repository.CreateConversationAsync(conversation, member, member with { UserNId = "U-2", DisplayNameSnapshot = "Bob" }, CancellationToken.None);
            await repository.AppendMessageAsync(conversation, Message("MSG-1", "CLIENT-1", "U-1"), CancellationToken.None);
            await context.SqlSugar.Ado.ExecuteCommandAsync(
                "UPDATE collaboration_message SET accepted_on = @acceptedOn WHERE tenant_n_id = @tenantNId AND message_n_id = @messageNId",
                new[] { new SugarParameter("@acceptedOn", DateTimeOffset.UtcNow.AddDays(-400)), new SugarParameter("@tenantNId", "T-1"), new SugarParameter("@messageNId", "MSG-1") },
                CancellationToken.None);

            var result = await repository.RunRetentionSweepAsync("T-1", "RET-WORKER-1", DateTimeOffset.UtcNow, CancellationToken.None);
            Assert.Equal(1, result.MessagesPurged);
            Assert.True(result.CheckpointCompleted);
            Assert.Equal(1, (await repository.GetConversationAsync("T-1", "CV-1", CancellationToken.None))!.RetentionFloorSequence);
            Assert.Empty(await repository.GetMessagesAsync("T-1", "CV-1", "history", null, 100, CancellationToken.None));
            Assert.Equal(1, await context.SqlSugar.Ado.GetIntAsync("SELECT COUNT(*) FROM collaboration_retention_checkpoint WHERE stage = 'Complete'"));
        }
        finally
        {
            context?.Dispose();
            DeleteDatabase(databasePath);
        }
    }

    [Fact]
    public async Task Stale_export_owner_cannot_overwrite_a_takeover_but_current_owner_can_complete()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"pf05-collaboration-export-lease-{Guid.NewGuid():N}.db");
        var contexts = Enumerable.Range(0, 2).Select(_ => CreateContext(databasePath)).ToArray();
        try
        {
            foreach (var migration in CollaborationSchemaMigrations.All)
                await migration.Apply(contexts[0].SqlSugar, CancellationToken.None);

            var now = DateTimeOffset.UtcNow;
            var repository1 = new SqlCollaborationRepository(contexts[0]);
            var repository2 = new SqlCollaborationRepository(contexts[1]);
            await repository1.CreateExportAsync(new ComplianceExportRecord(
                "T-1", "EXP-1", "Running", "{}", "hash", "reason", "U-1", now, null, null, 1, Guid.NewGuid(),
                RequestNId: "REQ-1", RequestHash: "request-hash"), CancellationToken.None);

            var firstClaim = await repository1.TryClaimExportAsync("T-1", "EXP-1", "OLD-WORKER", now.AddMinutes(2), now, CancellationToken.None);
            Assert.NotNull(firstClaim);
            var takeover = await repository2.TryClaimExportAsync("T-1", "EXP-1", "NEW-WORKER", now.AddMinutes(4), now.AddMinutes(3), CancellationToken.None);
            Assert.NotNull(takeover);

            var completedByCurrentOwner = await repository2.UpdateExportIfOwnedAsync(
                takeover! with { State = "Succeeded", CompletedOn = now.AddMinutes(3), OptimisticVersion = takeover.OptimisticVersion + 1 },
                "NEW-WORKER",
                takeover.OptimisticVersion,
                CancellationToken.None);
            Assert.Equal("Succeeded", completedByCurrentOwner?.State);

            var staleCompletion = await repository1.UpdateExportIfOwnedAsync(
                firstClaim! with { State = "Failed", ErrorCode = "stale", OptimisticVersion = firstClaim.OptimisticVersion + 1 },
                "OLD-WORKER",
                firstClaim.OptimisticVersion,
                CancellationToken.None);
            Assert.Null(staleCompletion);

            var current = await repository1.GetExportAsync("T-1", "EXP-1", CancellationToken.None);
            Assert.Equal("Succeeded", current?.State);
            Assert.Equal("NEW-WORKER", current?.WorkerLeaseNId);
        }
        finally
        {
            foreach (var context in contexts)
                context.Dispose();
            DeleteDatabase(databasePath);
        }
    }

    [Fact]
    public async Task Personal_message_visibility_hides_only_the_requesting_users_copy_and_is_idempotent()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"pf05-collaboration-personal-visibility-{Guid.NewGuid():N}.db");
        SqlSugarDbContext? context = null;
        try
        {
            context = CreateContext(databasePath);
            foreach (var migration in CollaborationSchemaMigrations.All)
                await migration.Apply(context.SqlSugar, CancellationToken.None);
            var repository = new SqlCollaborationRepository(context);
            var conversation = new ConversationRecord("T-1", "CV-1", "U-1", "U-2", "Active", 0, null, null, 0, 1, Guid.NewGuid());
            var member = new ConversationMemberRecord("T-1", "CV-1", "U-1", "Alice", DateTimeOffset.UtcNow, "Visible", null, 0, 0, null, 0, 1, Guid.NewGuid());
            await repository.CreateConversationAsync(conversation, member, member with { UserNId = "U-2", DisplayNameSnapshot = "Bob" }, CancellationToken.None);
            await repository.AppendMessageAsync(conversation, Message("MSG-1", "CLIENT-1", "U-1"), CancellationToken.None);
            await repository.AppendMessageAsync(conversation with { LastMessageSequence = 1, OptimisticVersion = 2 }, Message("MSG-2", "CLIENT-2", "U-2"), CancellationToken.None);

            Assert.True(await repository.HideMessageForUserAsync("T-1", "CV-1", "MSG-1", "U-1", CancellationToken.None));
            Assert.False(await repository.HideMessageForUserAsync("T-1", "CV-1", "MSG-1", "U-1", CancellationToken.None));

            var senderHistory = await repository.GetMessagesForUserAsync("T-1", "CV-1", "U-1", "history", null, 100, CancellationToken.None);
            var peerHistory = await repository.GetMessagesForUserAsync("T-1", "CV-1", "U-2", "history", null, 100, CancellationToken.None);

            Assert.Equal(["MSG-2"], senderHistory.Select(item => item.MessageNId));
            Assert.Equal(["MSG-1", "MSG-2"], peerHistory.Select(item => item.MessageNId));
            Assert.Equal(1, await context.SqlSugar.Ado.GetIntAsync("SELECT COUNT(*) FROM collaboration_message_personal_visibility"));
        }
        finally
        {
            context?.Dispose();
            DeleteDatabase(databasePath);
        }
    }

    [Fact]
    public async Task Distinct_compliance_preparations_persist_without_alternating_failures()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"pf05-collaboration-preparation-{Guid.NewGuid():N}.db");
        SqlSugarDbContext? context = null;
        try
        {
            context = CreateContext(databasePath);
            foreach (var migration in CollaborationSchemaMigrations.All)
                await migration.Apply(context.SqlSugar, CancellationToken.None);
            var repository = new SqlCollaborationRepository(context);
            var now = DateTimeOffset.UtcNow;

            for (var index = 0; index < 10; index++)
            {
                var requestNId = $"R{index:D31}";
                var stored = await repository.SaveCompliancePreparationAsync(
                    new CompliancePreparationRecord(
                        "T-1", "U-1", requestNId, "SID-1", "compliance.view", null,
                        new string('a', 64), new string('b', 64), "{}", "{}", now, now.AddMinutes(5)),
                    CancellationToken.None);
                Assert.Equal(requestNId, stored.RequestNId);
            }

            Assert.Equal(10, await context.SqlSugar.Ado.GetIntAsync("SELECT COUNT(*) FROM collaboration_compliance_preparation"));
        }
        finally
        {
            context?.Dispose();
            DeleteDatabase(databasePath);
        }
    }

    [Fact]
    public async Task Step_up_context_persists_and_signs_each_distinct_request()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"pf05-collaboration-step-up-{Guid.NewGuid():N}.db");
        SqlSugarDbContext? context = null;
        try
        {
            context = CreateContext(databasePath);
            foreach (var migration in CollaborationSchemaMigrations.All)
                await migration.Apply(context.SqlSugar, CancellationToken.None);
            using var rsa = System.Security.Cryptography.RSA.Create(2048);
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TrustedServiceCalls:Signers:collaboration:PrivateKey"] = rsa.ExportPkcs8PrivateKeyPem(),
                ["TrustedServiceCalls:Signers:collaboration:KeyId"] = "kid-1",
                ["TrustedServiceCalls:Signers:collaboration:Issuer"] = "collaboration",
                ["TrustedServiceCalls:Signers:collaboration:Audience"] = "identity.pf05",
            }).Build();
            var service = new CollaborationService(
                new SqlCollaborationRepository(context),
                null!, null!, null!, null!, null!,
                new IndustrialPlatform.Collaboration.Infrastructure.StepUpBindingIssuer(configuration),
                null!,
                new PageCursorCodec("test-signing-key", TimeSpan.FromMinutes(5)));

            for (var index = 0; index < 10; index++)
            {
                var result = await service.CreateStepUpContextAsync(
                    "T-1", "U-1",
                    new IndustrialPlatform.Collaboration.Contracts.StepUpContextRequest
                    {
                        Action = "compliance.view",
                        RequestNId = $"R{index:D31}",
                        Scope = new IndustrialPlatform.Collaboration.Contracts.ComplianceScopeDto { ScopeType = "TimeRange" },
                    },
                    "SID-1", "2", CancellationToken.None);
                Assert.False(string.IsNullOrWhiteSpace(result.Binding));
            }

            Assert.Equal(10, await context.SqlSugar.Ado.GetIntAsync("SELECT COUNT(*) FROM collaboration_compliance_preparation"));
        }
        finally
        {
            context?.Dispose();
            DeleteDatabase(databasePath);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Sqlite_context_password_proof_consume_and_controlled_view_share_utc_expiry(bool isAdministrator)
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"pf05-step-up-lifecycle-{Guid.NewGuid():N}.db");
        SqlSugarDbContext? context = null;
        try
        {
            context = CreateContext(databasePath);
            foreach (var migration in IdentitySchemaMigrations.All)
                await migration.Apply(context.SqlSugar, CancellationToken.None);
            foreach (var migration in CollaborationSchemaMigrations.All)
                await migration.Apply(context.SqlSugar, CancellationToken.None);

            using var signer = System.Security.Cryptography.RSA.Create(2048);
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TrustedServiceCalls:Signers:collaboration:PrivateKey"] = signer.ExportPkcs8PrivateKeyPem(),
                ["TrustedServiceCalls:Signers:collaboration:PublicKey"] = signer.ExportSubjectPublicKeyInfoPem(),
                ["TrustedServiceCalls:Signers:collaboration:KeyId"] = "sqlite-lifecycle-kid",
                ["TrustedServiceCalls:Signers:collaboration:Issuer"] = "collaboration",
                ["TrustedServiceCalls:Signers:collaboration:Audience"] = "identity.pf05",
            }).Build();
            var grants = new StepUpGrantStore(context);
            var service = new CollaborationService(
                new SqlCollaborationRepository(context),
                null!, null!, null!, null!,
                new JwtStepUpProofVerifier(grants),
                new StepUpBindingIssuer(configuration),
                null!,
                new PageCursorCodec("test-signing-key", TimeSpan.FromMinutes(5)));
            var passwordHasher = new BcryptPasswordHasher();
            var user = User.Create("T-1", "U-1", "step-up-user", "Step-up User", null, null, passwordHasher.Hash("Correct!Passw0rd"), mustChangePassword: false);
            var authentication = new StepUpAuthenticationStore(user);
            var sessionId = "SID-1";
            var securityVersion = user.AuthVersion.ToString(CultureInfo.InvariantCulture);
            var scope = new ComplianceScopeDto
            {
                ScopeType = "TimeRange",
                FromOn = DateTimeOffset.UtcNow.AddMinutes(-10),
                ToOn = DateTimeOffset.UtcNow.AddMinutes(-1),
            };
            var contextRequest = new StepUpContextRequest
            {
                Action = "compliance.view",
                RequestNId = "R" + new string('9', 31),
                Scope = scope,
            };
            var stepUpContext = await service.CreateStepUpContextAsync("T-1", "U-1", contextRequest, sessionId, securityVersion, CancellationToken.None);
            var controller = new CollaborationStepUpController(
                authentication,
                passwordHasher,
                grants,
                new StepUpCurrentUser("T-1", "U-1"),
                configuration,
                Options.Create(new AuthenticationOptions()),
                null!,
                null!)
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext
                    {
                        User = new ClaimsPrincipal(new ClaimsIdentity(
                        [
                            new Claim(ClaimConstants.SessionId, sessionId),
                            new Claim(ClaimConstants.AuthVersion, securityVersion),
                        ], "test")),
                    },
                },
            };
            var passwordResult = await controller.Create(new CollaborationStepUpRequest
            {
                Binding = stepUpContext.Binding,
            }, CancellationToken.None);
            Assert.Equal(403, Assert.IsType<ObjectResult>(passwordResult).StatusCode);
            authentication.IsSystemAdmin = isAdministrator;
            passwordResult = await controller.Create(new CollaborationStepUpRequest
            {
                Binding = stepUpContext.Binding,
                CurrentPassword = isAdministrator ? null : "Correct!Passw0rd",
            }, CancellationToken.None);
            var proofResponse = Assert.IsType<OkObjectResult>(passwordResult);
            var proof = Assert.IsType<ApiResult<CollaborationStepUpResponse>>(proofResponse.Value).Data!.Proof;

            var page = await service.SearchComplianceAsync(
                "T-1", "U-1",
                new ComplianceSearchRequest { RequestNId = contextRequest.RequestNId, Scope = scope, PageSize = 1 },
                proof,
                sessionId,
                securityVersion,
                CancellationToken.None);

            Assert.Empty(page.Items);
            var grant = await context.SqlSugar.Queryable<StepUpGrantTable>()
                .Where(item => item.ProofHash == StepUpGrantStore.HashProof(proof))
                .FirstAsync(CancellationToken.None);
            Assert.NotNull(grant);
            Assert.NotNull(grant!.ConsumedOn);
            Assert.Equal("collaboration", grant.ConsumedByService);
        }
        finally
        {
            context?.Dispose();
            DeleteDatabase(databasePath);
        }
    }

    private sealed class StepUpAuthenticationStore(User user) : IAuthenticationStore
    {
        public bool IsSystemAdmin { get; set; }
        private AuthenticatedUser _authenticated => new(user, [], IsSystemAdmin ? ["SYSTEM_ADMIN"] : [], IsSystemAdmin);

        public Task<AuthenticatedUser?> FindByNormalizedLoginNameAsync(string tenantNId, string normalizedLoginName, CancellationToken cancellationToken) => Task.FromResult<AuthenticatedUser?>(_authenticated);
        public Task<AuthenticatedUser?> FindByNIdAsync(string userNId, CancellationToken cancellationToken) => Task.FromResult<AuthenticatedUser?>(_authenticated);
        public Task<AuthenticatedUser?> FindByUserIdAsync(Guid userId, CancellationToken cancellationToken) => Task.FromResult<AuthenticatedUser?>(_authenticated);
        public Task UpdateUserAsync(User updated, long expectedOptimisticVersion, Guid expectedConcurrencyVersion, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<IReadOnlyList<Permission>> GetPermissionsForRolesAsync(IReadOnlyCollection<Guid> roleIds, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<Permission>>([]);
    }

    private sealed class StepUpCurrentUser(string tenantNId, string userNId) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string? UserNId => userNId;
        public string? UserName => userNId;
        public string? TenantId => tenantNId;
        public IReadOnlyCollection<string> Roles => [];
    }

    private sealed class ActiveDirectory : IndustrialPlatform.Collaboration.Application.ICollaborationIdentityDirectory
    {
        public Task<DirectoryUser?> GetAsync(string tenantNId, string userNId, CancellationToken cancellationToken) => Task.FromResult<DirectoryUser?>(new(userNId, userNId, "Active", "1"));
        public Task<DirectorySearchPage> SearchAsync(string tenantNId, string actorUserNId, string keyword, string? cursor, int pageSize, CancellationToken cancellationToken) => Task.FromResult(new DirectorySearchPage([], null));
    }

    private sealed class NoopAuditPort : ICollaborationAuditPort
    {
        public Task WriteAsync(string tenantNId, TrustedCollaborationCall? serviceCall, string actorUserNId, string action, string objectType, string objectNId, object payload, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private static MessageRecord Message(string messageNId, string clientMessageNId, string senderUserNId) =>
        new("T-1", "CV-1", messageNId, 0, senderUserNId, clientMessageNId, $"hash-{clientMessageNId}", "Text", messageNId, null, null, DateTimeOffset.UtcNow, null, null, null, 1, Guid.NewGuid());

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

    private static SqlSugarDbContext CreateContext(string databasePath) =>
        new(Options.Create(new SqlSugarOptions { DbType = DbType.Sqlite, ConnectionString = $"Data Source={databasePath}", IsAutoCloseConnection = true }));
}
