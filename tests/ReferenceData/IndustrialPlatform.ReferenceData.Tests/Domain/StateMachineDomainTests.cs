using IndustrialPlatform.ReferenceData.Domain.Common;
using IndustrialPlatform.ReferenceData.Domain.StateMachine;

namespace IndustrialPlatform.ReferenceData.Tests.Domain;

public sealed class StateMachineDomainTests
{
    [Fact]
    public void Valid_cycle_publishes_and_evaluation_only_checks_the_definition_path()
    {
        var machine = Create();
        machine.Update("Order", null,
        [
            Node("draft", initial: true),
            Node("review"),
        ],
        [
            Transition("draft", "submit", "Submit", "review"),
            Transition("review", "return", "Return", "draft"),
        ]);

        machine.Publish("ADMIN");

        var allowed = machine.Evaluate("draft", "submit");
        Assert.True(allowed.AllowedByDefinition);
        Assert.Equal("REVIEW", allowed.ToStatusNId);
        Assert.Null(allowed.ReasonCode);
        var denied = machine.Evaluate("review", "approve");
        Assert.False(denied.AllowedByDefinition);
        Assert.Null(denied.ToStatusNId);
        Assert.Equal("TRANSITION_NOT_DEFINED", denied.ReasonCode);
    }

    [Fact]
    public void Publish_rejects_initial_reachability_terminal_and_action_name_violations()
    {
        AssertInvalidPublish([Node("a"), Node("b")], [Transition("a", "go", "Go", "b")]);
        AssertInvalidPublish([Node("a", initial: true), Node("b", initial: true)], []);
        AssertInvalidPublish([Node("a", initial: true), Node("b")], []);
        AssertInvalidPublish([Node("a", initial: true, terminal: true), Node("b")],
            [Transition("a", "go", "Go", "b")]);
        AssertInvalidPublish([Node("a", initial: true), Node("b"), Node("c")],
        [
            Transition("a", "go", "Go", "b"),
            Transition("b", "go", "Continue", "c"),
        ]);
    }

    [Fact]
    public void Draft_rejects_duplicate_nodes_determinism_breaks_dangling_edges_and_capacity_overflow()
    {
        AssertInvalidUpdate([Node("a", initial: true), Node("A")], []);
        AssertInvalidUpdate([Node("a", initial: true), Node("b")],
        [
            Transition("a", "go", "Go", "b"),
            Transition("A", "GO", "Go", "a"),
        ]);
        AssertInvalidUpdate([Node("a", initial: true)], [Transition("a", "go", "Go", "missing")]);
        AssertInvalidUpdate(Enumerable.Range(0, 201).Select(index => Node($"n{index}", initial: index == 0)).ToArray(), []);
        AssertInvalidUpdate([Node("a", initial: true)], Enumerable.Range(0, 1001)
            .Select(index => Transition("a", $"go{index}", $"Go {index}", "a")).ToArray());
        Assert.Equal("REF-STATE-MACHINE-INVALID", Assert.Throws<ReferenceDataException>(() =>
            Node("bad", color: "red")).ErrorCode);
    }

    [Fact]
    public void Published_content_stays_immutable_and_clone_has_new_root_node_transition_and_token_ids()
    {
        var original = Create();
        original.Update("Order", "v1", [Node("draft", initial: true), Node("done", terminal: true)],
            [Transition("draft", "finish", "Finish", "done")]);
        original.Publish("ADMIN");
        original.Unfreeze();

        Assert.Equal("REF-INVALID-STATE", Assert.Throws<ReferenceDataException>(() =>
            original.Update("Changed", null, [Node("draft", initial: true)], [])).ErrorCode);

        var clone = original.Clone(2);
        Assert.Equal(PublicationStatus.Draft, clone.Status);
        Assert.Equal(1, clone.SourceRevision);
        Assert.NotEqual(original.Id, clone.Id);
        Assert.NotEqual(original.ConcurrencyVersion, clone.ConcurrencyVersion);
        Assert.All(clone.Nodes, node => Assert.DoesNotContain(original.Nodes, old => old.Id == node.Id));
        Assert.All(clone.Transitions, transition =>
            Assert.DoesNotContain(original.Transitions, old => old.Id == transition.Id));

        clone.Freeze();
        Assert.Equal("REF-INVALID-STATE", Assert.Throws<ReferenceDataException>(() =>
            clone.Update("Changed", null, clone.Nodes, clone.Transitions)).ErrorCode);

        var locked = Create();
        locked.Update("Order", null, [Node("draft", initial: true)], []);
        locked.Lock();
        Assert.Equal("REF-INVALID-STATE", Assert.Throws<ReferenceDataException>(() =>
            locked.Update("Changed", null, locked.Nodes, locked.Transitions)).ErrorCode);
    }

    [Fact]
    public void Version_exhaustion_rejects_update_before_mutating_the_snapshot()
    {
        var source = Create();
        source.Update("Order", null, [Node("draft", initial: true)], []);
        var exhausted = StateMachineDefinition.Restore(new(
            source.Id, source.NId, source.Name, source.Description, source.ScopeType, source.TenantNId,
            source.Revision, source.Status, source.SourceRevision, source.PublishedOn, source.PublishedBy,
            source.IsFrozen, source.IsLocked, source.IsDeleted, source.CreatedOn, source.LastUpdatedOn,
            long.MaxValue, source.ConcurrencyVersion), source.Nodes, source.Transitions);

        var error = Assert.Throws<ReferenceDataException>(() => exhausted.Update(
            "Changed", null, [Node("other", initial: true)], []));

        Assert.Equal("REF-CONCURRENCY-CONFLICT", error.ErrorCode);
        Assert.Equal("Order", exhausted.Name);
        Assert.Equal(["DRAFT"], exhausted.Nodes.Select(node => node.NId).ToArray());
    }

    [Fact]
    public void Sample_business_instance_keeps_state_and_history_when_guard_or_local_transaction_fails()
    {
        var definition = Create();
        definition.Update("Order", null,
            [Node("draft", initial: true), Node("done", terminal: true)],
            [Transition("draft", "finish", "Finish", "done")]);
        definition.Publish("ADMIN");
        var instance = new SampleBusinessInstance("DRAFT");

        Assert.False(instance.TryApply(definition, "FINISH", guardAllows: false, transactionCommits: true));
        Assert.False(instance.TryApply(definition, "FINISH", guardAllows: true, transactionCommits: false));
        Assert.Equal("DRAFT", instance.CurrentStatusNId);
        Assert.Empty(instance.History);

        Assert.True(instance.TryApply(definition, "FINISH", guardAllows: true, transactionCommits: true));
        Assert.Equal("DONE", instance.CurrentStatusNId);
        Assert.Equal(["DRAFT->DONE"], instance.History);
    }

    private static StateMachineDefinition Create() =>
        new("Order", "Order", null, ReferenceScopeType.Tenant, "TENANT-A", null);

    private static StateNode Node(string nId, bool initial = false, bool terminal = false,
        string? color = "#0088FF") =>
        new(nId, nId, null, initial, terminal, StateOutcome.None, color, 0);

    private static StateTransition Transition(
        string from, string action, string actionName, string to) =>
        new(from, action, actionName, to, null);

    private static void AssertInvalidPublish(
        IReadOnlyList<StateNode> nodes, IReadOnlyList<StateTransition> transitions)
    {
        var machine = Create();
        machine.Update("Order", null, nodes, transitions);
        Assert.Equal("REF-STATE-MACHINE-INVALID",
            Assert.Throws<ReferenceDataException>(() => machine.Publish("ADMIN")).ErrorCode);
    }

    private static void AssertInvalidUpdate(
        IReadOnlyList<StateNode> nodes, IReadOnlyList<StateTransition> transitions)
    {
        Assert.Equal("REF-STATE-MACHINE-INVALID", Assert.Throws<ReferenceDataException>(() =>
            Create().Update("Order", null, nodes, transitions)).ErrorCode);
    }

    private sealed class SampleBusinessInstance(string currentStatusNId)
    {
        public string CurrentStatusNId { get; private set; } = currentStatusNId;
        public List<string> History { get; } = [];

        public bool TryApply(
            StateMachineDefinition definition, string actionNId, bool guardAllows, bool transactionCommits)
        {
            var evaluation = definition.Evaluate(CurrentStatusNId, actionNId);
            if (!evaluation.AllowedByDefinition || !guardAllows || !transactionCommits) return false;
            History.Add($"{CurrentStatusNId}->{evaluation.ToStatusNId}");
            CurrentStatusNId = evaluation.ToStatusNId!;
            return true;
        }
    }
}
