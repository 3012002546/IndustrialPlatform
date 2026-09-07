using IndustrialPlatform.SystemData.Application.Auditing;
using IndustrialPlatform.SystemData.Application.Pf04;
using IndustrialPlatform.SystemData.Contracts.Auditing;
using IndustrialPlatform.SystemData.Infrastructure.Reliability;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace IndustrialPlatform.SystemData.Tests;

public sealed class AuditResilienceTests
{
    [Fact]
    public async Task Primary_store_failure_is_spooled_outside_the_database_and_replayed_after_recovery()
    {
        var root = Path.Combine(Path.GetTempPath(), $"pf04-audit-failure-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var sink = new FileAuditFailureSink(Options.Create(new AuditFailureSpoolOptions { RootPath = root }));
            var service = new AuditService(new FailingAuditStore(), TimeProvider.System, sink);

            var failure = await Assert.ThrowsAsync<Pf04ServiceException>(() => service.IngestAsync("tenant-1", Request(), CancellationToken.None));

            Assert.Equal("AUDIT_WRITE_UNAVAILABLE", failure.Code);
            var pending = await ReadAsync(sink);
            Assert.Single(pending);
            Assert.Equal("audit-1", pending[0].AuditEventNId);

            var recoveryStore = new RecoveryAuditStore();
            var recovery = new AuditIngressFailureRecoveryHostedService(sink, recoveryStore, NullLogger<AuditIngressFailureRecoveryHostedService>.Instance);

            Assert.Equal(1, await recovery.RecoverPendingAsync(CancellationToken.None));
            Assert.Single(recoveryStore.FailureRecords);
            Assert.Empty(await ReadAsync(sink));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Primary_transaction_rollback_is_spooled_without_reusing_the_failed_connection()
    {
        var root = Path.Combine(Path.GetTempPath(), $"pf04-audit-rollback-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var sink = new FileAuditFailureSink(Options.Create(new AuditFailureSpoolOptions { RootPath = root }));
            var service = new AuditService(new FailingInsertAuditStore(), TimeProvider.System, sink);

            var failure = await Assert.ThrowsAsync<Pf04ServiceException>(() => service.IngestAsync("tenant-1", Request(), CancellationToken.None));

            Assert.Equal("AUDIT_WRITE_UNAVAILABLE", failure.Code);
            var pending = await ReadAsync(sink);
            Assert.Single(pending);
            Assert.Equal("systemdata", pending[0].ProducerServiceKey);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Corrupt_spool_record_is_quarantined_and_not_silently_skipped()
    {
        var root = Path.Combine(Path.GetTempPath(), $"pf04-audit-corrupt-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "corrupt.json"), "{\"failureNId\":");
            var sink = new FileAuditFailureSink(Options.Create(new AuditFailureSpoolOptions { RootPath = root }));

            Assert.Empty(await ReadAsync(sink));
            Assert.True(Directory.Exists(Path.Combine(root, ".quarantine")));
            Assert.Single(Directory.EnumerateFiles(Path.Combine(root, ".quarantine"), "*.corrupt"));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Failure_spool_error_is_logged_while_the_stable_api_error_is_preserved()
    {
        var logger = new RecordingLogger<AuditService>();
        var service = new AuditService(new FailingAuditStore(), TimeProvider.System, new FailingAuditFailureSink(), logger: logger);

        var failure = await Assert.ThrowsAsync<Pf04ServiceException>(() => service.IngestAsync("tenant-1", Request(), CancellationToken.None));

        Assert.Equal("AUDIT_WRITE_UNAVAILABLE", failure.Code);
        Assert.Contains(LogLevel.Error, logger.Levels);
    }

    [Fact]
    public async Task Lifecycle_update_and_access_audit_share_one_transaction()
    {
        var store = new LifecycleAuditStore(legalHold: false);
        var transaction = new RecordingTransaction();
        var service = new AuditService(store, TimeProvider.System, transaction: transaction);

        await service.UpdateLifecycleAsync(
            "tenant-1", "user-1", "systemdata", "audit-1",
            new AuditLifecycleRequest { State = "Archived" },
            CancellationToken.None);

        Assert.True(transaction.WasEntered);
        Assert.True(store.UpdateLifecycleInsideTransaction);
        Assert.True(store.RecordAccessInsideTransaction);
    }

    [Fact]
    public async Task Legal_hold_rejects_delete_and_missing_retention_rejects_delete()
    {
        var held = new LifecycleAuditStore(legalHold: true);
        var service = new AuditService(held, TimeProvider.System);

        var legalHoldFailure = await Assert.ThrowsAsync<Pf04ServiceException>(() => service.UpdateLifecycleAsync(
            "tenant-1", "user-1", "systemdata", "audit-1",
            new AuditLifecycleRequest { State = "Deleted", RetentionUntil = DateTimeOffset.UtcNow.AddMinutes(-1) },
            CancellationToken.None));
        Assert.Equal("AUDIT_LEGAL_HOLD_ACTIVE", legalHoldFailure.Code);

        var noHold = new LifecycleAuditStore(legalHold: false);
        var noHoldService = new AuditService(noHold, TimeProvider.System);
        var retentionFailure = await Assert.ThrowsAsync<Pf04ServiceException>(() => noHoldService.UpdateLifecycleAsync(
            "tenant-1", "user-1", "systemdata", "audit-1",
            new AuditLifecycleRequest { State = "Deleted" },
            CancellationToken.None));
        Assert.Equal("AUDIT_RETENTION_REQUIRED", retentionFailure.Code);
    }

    [Fact]
    public void Cleanup_policy_requires_deleted_state_expired_retention_and_no_legal_hold()
    {
        var now = DateTimeOffset.UtcNow;

        Assert.True(AuditLifecyclePolicy.CanDelete("Deleted", now.AddMinutes(-1), legalHold: false, now: now));
        Assert.False(AuditLifecyclePolicy.CanDelete("Archived", now.AddMinutes(-1), legalHold: false, now: now));
        Assert.False(AuditLifecyclePolicy.CanDelete("Deleted", now.AddMinutes(1), legalHold: false, now: now));
        Assert.False(AuditLifecyclePolicy.CanDelete("Deleted", now.AddMinutes(-1), legalHold: true, now: now));
    }

    private static AuditFactIngestRequest Request() => new()
    {
        ProducerServiceKey = "systemdata",
        AuditEventNId = "audit-1",
        Action = "test",
        ObjectType = "Test",
        PayloadJson = "{}",
    };

    private static async Task<IReadOnlyList<AuditIngressFailureRecord>> ReadAsync(FileAuditFailureSink sink)
    {
        var result = new List<AuditIngressFailureRecord>();
        await foreach (var failure in sink.ReadPendingAsync(CancellationToken.None)) result.Add(failure);
        return result;
    }

    private sealed class FailingAuditStore : StubAuditStore
    {
        public override Task<AuditFactRecord?> GetAsync(string tenantNId, string producerServiceKey, string auditEventNId, CancellationToken cancellationToken) =>
            Task.FromException<AuditFactRecord?>(new InvalidOperationException("database unavailable"));
    }

    private sealed class FailingInsertAuditStore : StubAuditStore
    {
        public override Task InsertAsync(AuditFactRecord fact, CancellationToken cancellationToken) =>
            Task.FromException(new InvalidOperationException("transaction rolled back"));
    }

    private sealed class FailingAuditFailureSink : IAuditFailureSink
    {
        public Task EnqueueAsync(AuditIngressFailureRecord failure, CancellationToken cancellationToken) =>
            Task.FromException(new IOException("spool unavailable"));

        public async IAsyncEnumerable<AuditIngressFailureRecord> ReadPendingAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            yield break;
        }

        public Task AcknowledgeAsync(AuditIngressFailureRecord failure, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class LifecycleAuditStore : StubAuditStore
    {
        private readonly bool _legalHold;
        public bool UpdateLifecycleInsideTransaction { get; private set; }
        public bool RecordAccessInsideTransaction { get; private set; }

        public LifecycleAuditStore(bool legalHold) => _legalHold = legalHold;

        public override Task<AuditFactRecord?> GetAsync(string tenantNId, string producerServiceKey, string auditEventNId, CancellationToken cancellationToken) =>
            Task.FromResult<AuditFactRecord?>(new AuditFactRecord(tenantNId, producerServiceKey, auditEventNId, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "user-1", "test", "Test", "object-1", "{}", "hash", null, "Info", null, null));

        public override Task<AuditLifecycleRecord?> GetLifecycleAsync(string tenantNId, string producerServiceKey, string auditEventNId, CancellationToken cancellationToken) =>
            Task.FromResult<AuditLifecycleRecord?>(new AuditLifecycleRecord(tenantNId, producerServiceKey, auditEventNId, "Active", DateTimeOffset.UtcNow.AddDays(1), _legalHold, _legalHold ? "retention order" : null, DateTimeOffset.UtcNow));

        public override Task UpdateLifecycleAsync(AuditLifecycleUpdate update, CancellationToken cancellationToken)
        {
            UpdateLifecycleInsideTransaction = RecordingTransaction.CurrentDepth > 0;
            return Task.CompletedTask;
        }

        public override Task RecordAccessAsync(string tenantNId, string actorUserNId, string action, string scope, CancellationToken cancellationToken)
        {
            RecordAccessInsideTransaction = RecordingTransaction.CurrentDepth > 0;
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingTransaction : ISystemDataWriteTransaction
    {
        private static readonly AsyncLocal<int> Depth = new();
        public static int CurrentDepth => Depth.Value;
        public bool WasEntered { get; private set; }

        public async Task ExecuteAsync(Func<Task> action, CancellationToken cancellationToken)
        {
            WasEntered = true;
            Depth.Value++;
            try { await action(); }
            finally { Depth.Value--; }
        }
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<LogLevel> Levels { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => EmptyDisposable.Instance;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) => Levels.Add(logLevel);

        private sealed class EmptyDisposable : IDisposable
        {
            public static EmptyDisposable Instance { get; } = new();
            public void Dispose() { }
        }
    }

    private sealed class RecoveryAuditStore : StubAuditStore
    {
        public List<AuditIngressFailureRecord> FailureRecords { get; } = [];

        public override Task RecordIngressFailureAsync(string tenantNId, string producerServiceKey, string? auditEventNId, string errorCode, string errorSummary, string? payloadHash, CancellationToken cancellationToken)
        {
            FailureRecords.Add(new AuditIngressFailureRecord("recovered", tenantNId, producerServiceKey, auditEventNId, errorCode, errorSummary, payloadHash, DateTimeOffset.UtcNow));
            return Task.CompletedTask;
        }
    }

    private abstract class StubAuditStore : IAuditStore
    {
        public virtual Task<AuditFactRecord?> GetAsync(string tenantNId, string producerServiceKey, string auditEventNId, CancellationToken cancellationToken) => Task.FromResult<AuditFactRecord?>(null);
        public virtual Task InsertAsync(AuditFactRecord fact, CancellationToken cancellationToken) => Task.CompletedTask;
        public virtual Task RecordIngressFailureAsync(string tenantNId, string producerServiceKey, string? auditEventNId, string errorCode, string errorSummary, string? payloadHash, CancellationToken cancellationToken) => Task.CompletedTask;
        public virtual Task RecordAccessAsync(string tenantNId, string actorUserNId, string action, string scope, CancellationToken cancellationToken) => Task.CompletedTask;
        public virtual Task<AuditFactPageV1> QueryAsync(string tenantNId, string? producerServiceKey, string? action, DateTimeOffset? from, DateTimeOffset? until, int page, int pageSize, CancellationToken cancellationToken) => Task.FromResult(new AuditFactPageV1());
        public virtual Task<AuditLifecycleRecord?> GetLifecycleAsync(string tenantNId, string producerServiceKey, string auditEventNId, CancellationToken cancellationToken) => Task.FromResult<AuditLifecycleRecord?>(null);
        public virtual Task UpdateLifecycleAsync(AuditLifecycleUpdate update, CancellationToken cancellationToken) => Task.CompletedTask;
        public virtual Task RecordLifecycleAuditAsync(string tenantNId, string producerServiceKey, string auditEventNId, string action, string reason, CancellationToken cancellationToken) => Task.CompletedTask;
        public virtual Task<int> CleanupAsync(DateTimeOffset now, CancellationToken cancellationToken) => Task.FromResult(0);
        public virtual Task<bool> RecoverOutboxAsync(string tenantNId, Guid eventId, CancellationToken cancellationToken) => Task.FromResult(false);
    }
}
