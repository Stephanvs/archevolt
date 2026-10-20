using Archevolt.Scheduling;

namespace Archevolt.Tests;

[Trait("Category", "Scheduling")]
public sealed class SchedulePlannerTests
{
    [Fact]
    public void Ready_nodes_follow_declared_ordinals_in_a_shared_batch()
    {
        var plan = SchedulePlanner.Plan(new ScheduleInput(
        [
            new NodeInput(0, "A", new SystemAccess("A"), 10),
            new NodeInput(1, "B", new SystemAccess("B"), -2),
            new NodeInput(2, "C", new SystemAccess("C"), 0)
        ]));

        Assert.Equal([1, 2, 0], plan.NodeOrder);
        Assert.Equal([1, 2, 0], plan.BatchNodes);
        Assert.Equal([(StepKind.Batch, 0, 3), (StepKind.ApplyDeferred, 0, 0)], Steps(plan));
    }

    [Fact]
    public void Conflicts_follow_the_explicit_topological_order_instead_of_creating_a_cycle()
    {
        var plan = SchedulePlanner.Plan(new ScheduleInput(
        [
            Node(0, new AccessClause(writes: [0])),
            Node(1, new AccessClause(reads: [0], writes: [1])),
            Node(2, new AccessClause(reads: [1]))
        ], [new EdgeInput(2, 0)]));

        Assert.True(plan.IsValid);
        Assert.Equal([1, 2, 0], plan.NodeOrder);
        Assert.Equal([1, 2, 0], plan.BatchNodes);
        Assert.Equal([(StepKind.Batch, 0, 1), (StepKind.Batch, 1, 1), (StepKind.Batch, 2, 1),
            (StepKind.ApplyDeferred, 0, 0)], Steps(plan));
        Assert.Equal([(1, 2), (1, 0)], plan.Edges.Where(e => e.Reason == EdgeReason.Conflict).Select(e => (e.From, e.To)));
        Assert.Equal(["AV008", "AV008"], plan.Diagnostics.Select(d => d.Code));
    }

    [Fact]
    public void Every_conflicting_clause_pair_needs_its_own_disjointness_proof()
    {
        var multipleQueries = new SystemAccess("A", clauses:
        [
            new AccessClause(writes: [0], required: [0, 10]),
            new AccessClause(writes: [0], required: [0], excluded: [10])
        ]);
        var plan = SchedulePlanner.Plan(new ScheduleInput(
        [
            new NodeInput(0, "A", multipleQueries, 0),
            Node(1, new AccessClause(writes: [0], required: [0, 10]))
        ]));

        Assert.Equal(["AV008"], plan.Diagnostics.Select(d => d.Code));
        Assert.Equal([(StepKind.Batch, 0, 1), (StepKind.Batch, 1, 1), (StepKind.ApplyDeferred, 0, 0)], Steps(plan));
    }

    [Theory]
    [InlineData(true, 1)]
    [InlineData(false, 2)]
    public void Only_required_against_excluded_membership_proves_disjointness(bool excludesMarker, int batches)
    {
        var plan = SchedulePlanner.Plan(new ScheduleInput(
        [
            Node(0, new AccessClause(writes: [0], required: [0, 10])),
            Node(1, new AccessClause(writes: [0], required: excludesMarker ? [0] : [0, 11], excluded: excludesMarker ? [10] : []))
        ]));

        Assert.Equal(batches, plan.Steps.Count(s => s.Kind == StepKind.Batch));
        Assert.Equal(excludesMarker ? [] : ["AV008"], plan.Diagnostics.Select(d => d.Code));
    }

    [Fact]
    public void Optional_access_does_not_become_required_membership_for_disjointness()
    {
        var plan = SchedulePlanner.Plan(new ScheduleInput(
        [
            Node(0, new AccessClause(reads: [0], writes: [1], required: [1])),
            Node(1, new AccessClause(writes: [1], required: [1], excluded: [0]))
        ]));

        Assert.Equal(["AV008"], plan.Diagnostics.Select(d => d.Code));
        Assert.Equal([1, 1], plan.Steps.Where(s => s.Kind == StepKind.Batch).Select(s => s.Count));
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 2)]
    public void Tick_metadata_reads_conflict_with_writes_and_share_with_reads(bool writes, int batches)
    {
        var plan = SchedulePlanner.Plan(new ScheduleInput(
        [
            Node(0, new AccessClause(tickReads: [0], required: [0])),
            Node(1, writes ? new AccessClause(writes: [0]) : new AccessClause(reads: [0], tickReads: [0]))
        ]));

        Assert.Equal(batches, plan.Steps.Count(s => s.Kind == StepKind.Batch));
        Assert.Equal(writes ? ["AV008"] : [], plan.Diagnostics.Select(d => d.Code));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Resource_and_message_conflicts_remain_global_despite_disjoint_queries(bool messages)
    {
        var writer = new SystemAccess("A", [new AccessClause(writes: [0], required: [10])],
            resourcesWritten: messages ? [] : [5], messagesWritten: messages ? [5] : []);
        var reader = new SystemAccess("B", [new AccessClause(reads: [0], excluded: [10])],
            resourcesRead: messages ? [] : [5], messagesRead: messages ? [5] : []);
        var plan = SchedulePlanner.Plan(new ScheduleInput([new NodeInput(0, "A", writer, 0), new NodeInput(1, "B", reader, 1)]));

        Assert.Equal(["AV008"], plan.Diagnostics.Select(d => d.Code));
        Assert.Equal([1, 1], plan.Steps.Where(s => s.Kind == StepKind.Batch).Select(s => s.Count));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Condition_reads_participate_in_conflict_planning(bool resource)
    {
        var writer = new SystemAccess("A", resource ? [] : [new AccessClause(writes: [0])], resourcesWritten: resource ? [0] : []);
        var conditional = new SystemAccess("B", conditionClauses: resource ? [] : [new AccessClause(reads: [0])],
            conditionResources: resource ? [0] : []);
        var plan = SchedulePlanner.Plan(new ScheduleInput(
            [new NodeInput(0, "A", writer, 0), new NodeInput(1, "B", conditional, 1, hasCondition: true)]));

        Assert.Equal(["AV008"], plan.Diagnostics.Select(d => d.Code));
        Assert.Equal([1, 1], plan.Steps.Where(s => s.Kind == StepKind.Batch).Select(s => s.Count));
    }

    [Fact]
    public void Commands_flush_before_conditional_and_transitive_successors()
    {
        var plan = SchedulePlanner.Plan(new ScheduleInput(
        [
            new NodeInput(0, "producer", new SystemAccess("A", traits: NodeTraits.EmitsCommands), 0),
            new NodeInput(1, "conditional", new SystemAccess("B"), 1, hasCondition: true),
            new NodeInput(2, "consumer", new SystemAccess("C"), 2)
        ], [new EdgeInput(0, 1), new EdgeInput(1, 2)]));

        Assert.Empty(plan.Diagnostics);
        Assert.Equal([(StepKind.Batch, 0, 1), (StepKind.ApplyDeferred, 0, 0), (StepKind.Batch, 1, 1),
            (StepKind.Batch, 2, 1), (StepKind.ApplyDeferred, 0, 0)], Steps(plan));
        Assert.Contains(plan.Edges, e => e.From == 0 && e.To == 2 && e.Reason == EdgeReason.Barrier);
    }

    [Fact]
    public void Owner_thread_nodes_share_disjoint_work_but_exclusive_nodes_have_their_own_batch()
    {
        var plan = SchedulePlanner.Plan(new ScheduleInput(
        [
            new NodeInput(0, "owner", new SystemAccess("A", traits: NodeTraits.OwnerThread), 0),
            new NodeInput(1, "worker", new SystemAccess("B"), 1),
            new NodeInput(2, "exclusive", new SystemAccess("C", traits: NodeTraits.Exclusive), 2),
            new NodeInput(3, "next", new SystemAccess("D"), 3)
        ]));

        Assert.Equal([(StepKind.Batch, 0, 2), (StepKind.Batch, 2, 1), (StepKind.Batch, 3, 1),
            (StepKind.ApplyDeferred, 0, 0)], Steps(plan));
    }

    [Theory]
    [InlineData("cycle")]
    [InlineData("unknown dependency")]
    [InlineData("self dependency")]
    [InlineData("duplicate key")]
    [InlineData("duplicate ordinal")]
    [InlineData("wrong id")]
    [InlineData("negative component")]
    [InlineData("contradictory predicate")]
    [InlineData("mutable condition")]
    public void Invalid_input_produces_AV007_and_no_executable_steps(string invalid)
    {
        var nodes = new[] { Node(0), Node(1) };
        EdgeInput[] edges = [];
        switch (invalid)
        {
            case "cycle": edges = [new(0, 1), new(1, 0)]; break;
            case "unknown dependency": edges = [new(0, 2)]; break;
            case "self dependency": edges = [new(0, 0)]; break;
            case "duplicate key": nodes[1] = new NodeInput(1, "0", new SystemAccess("B"), 1); break;
            case "duplicate ordinal": nodes[1] = new NodeInput(1, "1", new SystemAccess("B"), 0); break;
            case "wrong id": nodes[1] = Node(2); break;
            case "negative component": nodes[0] = Node(0, new AccessClause(reads: [-1])); break;
            case "contradictory predicate": nodes[0] = Node(0, new AccessClause(required: [0], excluded: [0])); break;
            case "mutable condition": nodes[0] = new NodeInput(0, "0", new SystemAccess("A", conditionClauses: [new AccessClause(writes: [0])])); break;
        }
        var plan = SchedulePlanner.Plan(new ScheduleInput(nodes, edges));

        Assert.False(plan.IsValid);
        Assert.All(plan.Diagnostics, d => Assert.Equal("AV007", d.Code));
        Assert.NotEmpty(plan.Diagnostics);
        Assert.Empty(plan.Steps);
        Assert.Empty(plan.BatchNodes);
    }

    [Fact]
    public void Explicit_transitive_order_avoids_unordered_conflict_warnings()
    {
        var plan = SchedulePlanner.Plan(new ScheduleInput(
            [Node(0, new AccessClause(writes: [0])), Node(1), Node(2, new AccessClause(reads: [0]))],
            [new EdgeInput(0, 1), new EdgeInput(1, 2)]));

        Assert.True(plan.IsValid);
        Assert.Empty(plan.Diagnostics);
        Assert.Equal([0, 1, 2], plan.NodeOrder);
    }

    [Fact]
    public void Empty_schedules_still_have_a_completion_barrier()
    {
        var plan = SchedulePlanner.Plan(new ScheduleInput([]));

        Assert.True(plan.IsValid);
        Assert.Equal([(StepKind.ApplyDeferred, 0, 0)], Steps(plan));
    }

    [Fact]
    public void Descriptors_snapshot_caller_arrays_and_plans_reject_mutation()
    {
        int[] writes = [3];
        AccessClause[] clauses = [new AccessClause(writes: writes)];
        NodeInput[] nodes = [new(0, "A", new SystemAccess("A", clauses), 0), Node(1, new AccessClause(reads: [3]))];
        EdgeInput[] edges = [new(0, 1)];
        var input = new ScheduleInput(nodes, edges);
        writes[0] = 99;
        clauses[0] = new AccessClause();
        nodes[0] = Node(0);
        edges[0] = new(1, 0);
        var plan = SchedulePlanner.Plan(input);

        Assert.Equal([3], input.Nodes[0].Access.Clauses[0].Writes);
        Assert.Equal([0, 1], plan.NodeOrder);
        Assert.Equal(SchedulePlanner.Fingerprint(input), plan.InputFingerprint);
        Assert.Throws<NotSupportedException>(() => ((IList<int>)plan.NodeOrder)[0] = 99);
        Assert.Throws<NotSupportedException>(() => ((IList<int>)input.Nodes[0].Access.Clauses[0].Writes)[0] = 99);
    }

    [Fact]
    public void Generated_plans_copy_arrays_and_validate_execution_ranges()
    {
        int[] order = [0, 1];
        int[] batchNodes = [0, 1];
        PlanStep[] steps = [new(StepKind.Batch, 0, 1), new(StepKind.Batch, 1, 1), new(StepKind.ApplyDeferred)];
        PlanEdge[] edges = [new(0, 1, EdgeReason.Explicit)];
        var plan = SchedulePlan.FromGenerated(order, batchNodes, steps, edges, 42);
        order[0] = 7;
        batchNodes[0] = 7;
        steps[0] = new(StepKind.Batch, 99, 1);
        edges[0] = new(1, 0, EdgeReason.Explicit);

        Assert.Equal([0, 1], plan.NodeOrder);
        Assert.Equal([0, 1], plan.BatchNodes);
        Assert.Equal([(StepKind.Batch, 0, 1), (StepKind.Batch, 1, 1), (StepKind.ApplyDeferred, 0, 0)], Steps(plan));
        Assert.Equal(0, plan.Edges[0].From);
        Assert.Throws<ArgumentException>(() => SchedulePlan.FromGenerated([0, 1], [0, 1],
            [new(StepKind.Batch, 0, 2), new(StepKind.ApplyDeferred)], [new(0, 1, EdgeReason.Explicit)], 42));
        Assert.Throws<ArgumentException>(() => SchedulePlan.FromGenerated([0], [0],
            [new(StepKind.Batch, 1, 1), new(StepKind.ApplyDeferred)], [], 42));
        Assert.Throws<ArgumentException>(() => SchedulePlan.FromGenerated([0, 1], [0, 1],
            [new(StepKind.Batch, 0, 1), new(StepKind.Batch, 1, 1), new(StepKind.ApplyDeferred)],
            [new(0, 1, EdgeReason.Barrier)], 42));
    }

    private static NodeInput Node(int id, AccessClause? clause = null)
        => new(id, id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            new SystemAccess("System" + id, clause is null ? [] : [clause]), id);

    private static IEnumerable<(StepKind Kind, int First, int Count)> Steps(SchedulePlan plan)
        => plan.Steps.Select(s => (s.Kind, s.First, s.Count));
}
