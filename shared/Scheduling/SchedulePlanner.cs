using System;
using System.Collections.Generic;

namespace Archevolt.Scheduling;

public static class SchedulePlanner
{
    /// <summary>Builds a stable execution plan without depending on a World or compiler APIs.</summary>
    public static SchedulePlan Plan(ScheduleInput input)
    {
        if (input is null) throw new ArgumentNullException(nameof(input));
        var diagnostics = new List<PlanDiagnostic>();
        Validate(input, diagnostics);
        if (diagnostics.Count != 0) return Invalid(diagnostics);
        var fingerprint = Fingerprint(input);
        var count = input.Nodes.Count;
        var graph = new bool[count, count];
        var edges = new List<PlanEdge>();
        foreach (var edge in input.Edges)
        {
            if (graph[edge.Before, edge.After]) continue;
            graph[edge.Before, edge.After] = true;
            edges.Add(new PlanEdge(edge.Before, edge.After, EdgeReason.Explicit));
        }

        var order = TopologicalOrder(input, graph);
        if (order.Count != count)
        {
            diagnostics.Add(new PlanDiagnostic("AV007", -1, -1, "The schedule contains a dependency cycle."));
            return Invalid(diagnostics, fingerprint);
        }

        var explicitPaths = Reachability(graph);
        for (var i = 0; i < count; i++)
        {
            var from = order[i];
            for (var j = i + 1; j < count; j++)
            {
                var to = order[j];
                if (explicitPaths[from, to] || !Conflicts(input.Nodes[from].Access, input.Nodes[to].Access)) continue;
                graph[from, to] = true;
                edges.Add(new PlanEdge(from, to, EdgeReason.Conflict));
                diagnostics.Add(new PlanDiagnostic("AV008", from, to,
                    "Conflicting systems '" + input.Nodes[from].InstanceKey + "' and '" + input.Nodes[to].InstanceKey +
                    "' have no explicit dependency. The planner orders them by the stable topological order."));
            }
        }

        var paths = Reachability(graph);
        for (var from = 0; from < count; from++)
            if ((input.Nodes[from].Access.Traits & NodeTraits.EmitsCommands) != 0)
                for (var to = 0; to < count; to++)
                    if (paths[from, to]) edges.Add(new PlanEdge(from, to, EdgeReason.Barrier));

        var steps = BuildSteps(input, order, graph);
        return new SchedulePlan(order, order, steps, edges, diagnostics, fingerprint, true);
    }

    private static SchedulePlan Invalid(List<PlanDiagnostic> diagnostics, ulong fingerprint = 0)
        => new SchedulePlan(new int[0], new int[0], new PlanStep[0], new PlanEdge[0], diagnostics, fingerprint, false);

    private static void Validate(ScheduleInput input, List<PlanDiagnostic> diagnostics)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var orders = new HashSet<int>();
        for (var i = 0; i < input.Nodes.Count; i++)
        {
            var node = input.Nodes[i];
            if (node is null)
            {
                diagnostics.Add(new PlanDiagnostic("AV007", i, -1, "Schedule nodes cannot be null."));
                continue;
            }
            if (node.Id != i) diagnostics.Add(new PlanDiagnostic("AV007", i, -1, "Node IDs must match their input index."));
            if (string.IsNullOrWhiteSpace(node.InstanceKey) || !keys.Add(node.InstanceKey))
                diagnostics.Add(new PlanDiagnostic("AV007", i, -1, "Every instance must have a unique nonempty key."));
            if (!orders.Add(node.Order)) diagnostics.Add(new PlanDiagnostic("AV007", i, -1, "Run ordinals must be unique."));
            if (!ValidAccess(node.Access)) diagnostics.Add(new PlanDiagnostic("AV007", i, -1, "The system access descriptor is invalid."));
        }
        foreach (var edge in input.Edges)
            if (edge.Before < 0 || edge.Before >= input.Nodes.Count || edge.After < 0 || edge.After >= input.Nodes.Count || edge.Before == edge.After)
                diagnostics.Add(new PlanDiagnostic("AV007", edge.Before, edge.After, "A dependency must connect two distinct existing nodes."));
    }

    private static bool ValidAccess(SystemAccess access)
    {
        if (string.IsNullOrWhiteSpace(access.Name) || (access.Traits & ~(NodeTraits.EmitsCommands | NodeTraits.OwnerThread | NodeTraits.Exclusive | NodeTraits.Partitionable)) != 0)
            return false;
        if (!ValidIds(access.ResourcesRead) || !ValidIds(access.ResourcesWritten) || !ValidIds(access.MessagesRead) ||
            !ValidIds(access.MessagesWritten) || !ValidIds(access.ConditionResources)) return false;
        foreach (var clause in access.Clauses) if (!ValidClause(clause)) return false;
        foreach (var clause in access.ConditionClauses)
            if (!ValidClause(clause) || clause.Writes.Count != 0) return false;
        return true;
    }

    private static bool ValidClause(AccessClause clause)
        => clause is not null && ValidIds(clause.Reads) && ValidIds(clause.Writes) && ValidIds(clause.Required) &&
            ValidIds(clause.Excluded) && ValidIds(clause.TickReads) && !Intersects(clause.Required, clause.Excluded);

    private static bool ValidIds(IReadOnlyList<int> ids) => ids.Count == 0 || ids[0] >= 0;

    private static List<int> TopologicalOrder(ScheduleInput input, bool[,] graph)
    {
        var count = input.Nodes.Count;
        var indegree = new int[count];
        var emitted = new bool[count];
        for (var from = 0; from < count; from++)
            for (var to = 0; to < count; to++) if (graph[from, to]) indegree[to]++;
        var order = new List<int>(count);
        while (order.Count < count)
        {
            var next = -1;
            for (var i = 0; i < count; i++)
                if (!emitted[i] && indegree[i] == 0 && (next == -1 || input.Nodes[i].Order < input.Nodes[next].Order)) next = i;
            if (next == -1) break;
            emitted[next] = true;
            order.Add(next);
            for (var to = 0; to < count; to++) if (graph[next, to]) indegree[to]--;
        }
        return order;
    }

    private static bool[,] Reachability(bool[,] graph)
    {
        var paths = (bool[,])graph.Clone();
        var count = graph.GetLength(0);
        for (var through = 0; through < count; through++)
            for (var from = 0; from < count; from++)
                if (paths[from, through])
                    for (var to = 0; to < count; to++) paths[from, to] |= paths[through, to];
        return paths;
    }

    private static List<PlanStep> BuildSteps(ScheduleInput input, List<int> order, bool[,] graph)
    {
        var steps = new List<PlanStep>();
        var next = 0;
        while (next < order.Count)
        {
            var first = next++;
            var firstTraits = input.Nodes[order[first]].Access.Traits;
            if ((firstTraits & NodeTraits.Exclusive) == 0)
            {
                while (next < order.Count)
                {
                    var candidate = order[next];
                    if ((input.Nodes[candidate].Access.Traits & NodeTraits.Exclusive) != 0) break;
                    var ready = true;
                    for (var i = first; i < next; i++)
                        if (graph[order[i], candidate]) { ready = false; break; }
                    if (!ready) break;
                    next++;
                }
            }
            steps.Add(new PlanStep(StepKind.Batch, first, next - first));
            for (var i = first; i < next; i++)
                if ((input.Nodes[order[i]].Access.Traits & NodeTraits.EmitsCommands) != 0)
                {
                    steps.Add(new PlanStep(StepKind.ApplyDeferred));
                    break;
                }
        }
        if (steps.Count == 0 || steps[steps.Count - 1].Kind != StepKind.ApplyDeferred)
            steps.Add(new PlanStep(StepKind.ApplyDeferred));
        return steps;
    }

    private static bool Conflicts(SystemAccess a, SystemAccess b)
    {
        if (Intersects(a.ResourcesWritten, b.ResourcesRead) || Intersects(a.ResourcesWritten, b.ResourcesWritten) ||
            Intersects(a.ResourcesWritten, b.ConditionResources) || Intersects(b.ResourcesWritten, a.ResourcesRead) ||
            Intersects(b.ResourcesWritten, a.ConditionResources) || Intersects(a.MessagesWritten, b.MessagesRead) ||
            Intersects(a.MessagesWritten, b.MessagesWritten) || Intersects(b.MessagesWritten, a.MessagesRead)) return true;
        return ClauseSetsConflict(a.Clauses, b.Clauses) || ClauseSetsConflict(a.Clauses, b.ConditionClauses) ||
            ClauseSetsConflict(a.ConditionClauses, b.Clauses) || ClauseSetsConflict(a.ConditionClauses, b.ConditionClauses);
    }

    private static bool ClauseSetsConflict(IReadOnlyList<AccessClause> a, IReadOnlyList<AccessClause> b)
    {
        foreach (var left in a)
            foreach (var right in b)
            {
                if (Intersects(left.Required, right.Excluded) || Intersects(right.Required, left.Excluded)) continue;
                if (Intersects(left.Writes, right.Reads) || Intersects(left.Writes, right.Writes) ||
                    Intersects(left.Writes, right.TickReads) || Intersects(right.Writes, left.Reads) ||
                    Intersects(right.Writes, left.TickReads)) return true;
            }
        return false;
    }

    private static bool Intersects(IReadOnlyList<int> a, IReadOnlyList<int> b)
    {
        var i = 0;
        var j = 0;
        while (i < a.Count && j < b.Count)
        {
            if (a[i] == b[j]) return true;
            if (a[i] < b[j]) i++; else j++;
        }
        return false;
    }

    /// <summary>A stable hash of the normalized descriptor snapshot, for generated-plan binding.</summary>
    public static ulong Fingerprint(ScheduleInput input)
    {
        if (input is null) throw new ArgumentNullException(nameof(input));
        var hash = new DescriptorHash();
        hash.Add(input.Nodes.Count);
        foreach (var node in input.Nodes)
        {
            hash.Add(node.Id); hash.Add(node.Order); hash.Add(node.InstanceKey); hash.Add(node.HasCondition ? 1 : 0);
            var access = node.Access;
            hash.Add(access.Name); hash.Add((int)access.Traits);
            hash.Add(access.Clauses); hash.Add(access.ResourcesRead); hash.Add(access.ResourcesWritten);
            hash.Add(access.MessagesRead); hash.Add(access.MessagesWritten);
            hash.Add(access.ConditionClauses); hash.Add(access.ConditionResources);
        }
        hash.Add(input.Edges.Count);
        foreach (var edge in input.Edges) { hash.Add(edge.Before); hash.Add(edge.After); }
        return hash.Value;
    }

    private sealed class DescriptorHash
    {
        internal ulong Value = 14695981039346656037UL;
        internal void Add(int value)
        {
            unchecked
            {
                for (var shift = 0; shift < 32; shift += 8) Value = (Value ^ (byte)(value >> shift)) * 1099511628211UL;
            }
        }
        internal void Add(string value)
        {
            Add(value.Length);
            foreach (var character in value) Add(character);
        }
        internal void Add(IReadOnlyList<int> values)
        {
            Add(values.Count);
            foreach (var value in values) Add(value);
        }
        internal void Add(IReadOnlyList<AccessClause> clauses)
        {
            Add(clauses.Count);
            foreach (var clause in clauses)
            {
                Add(clause.Reads); Add(clause.Writes); Add(clause.Required); Add(clause.Excluded); Add(clause.TickReads);
            }
        }
    }
}
