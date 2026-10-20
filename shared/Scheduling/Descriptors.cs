using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Archevolt.Scheduling;

[Flags]
public enum NodeTraits : byte
{
	None = 0,
	EmitsCommands = 1,
	OwnerThread = 2,
	Exclusive = 4,
	Partitionable = 8
}

/// <summary>One query's access and structural predicate. Optional accesses do not belong in Required.</summary>
public sealed class AccessClause
{
	public AccessClause(IEnumerable<int>? reads = null, IEnumerable<int>? writes = null,
		IEnumerable<int>? required = null, IEnumerable<int>? excluded = null, IEnumerable<int>? tickReads = null)
	{
		Reads = DescriptorCollections.Ids(reads);
		Writes = DescriptorCollections.Ids(writes);
		Required = DescriptorCollections.Ids(required);
		Excluded = DescriptorCollections.Ids(excluded);
		TickReads = DescriptorCollections.Ids(tickReads);
	}

	public IReadOnlyList<int> Reads { get; }
	public IReadOnlyList<int> Writes { get; }
	public IReadOnlyList<int> Required { get; }
	public IReadOnlyList<int> Excluded { get; }
	public IReadOnlyList<int> TickReads { get; }
}

/// <summary>A system accesses the union of its clauses. Resource and message identities are global.</summary>
public sealed class SystemAccess
{
	public SystemAccess(string name, IEnumerable<AccessClause>? clauses = null,
		IEnumerable<int>? resourcesRead = null, IEnumerable<int>? resourcesWritten = null,
		IEnumerable<int>? messagesRead = null, IEnumerable<int>? messagesWritten = null,
		NodeTraits traits = NodeTraits.None, IEnumerable<AccessClause>? conditionClauses = null,
		IEnumerable<int>? conditionResources = null)
	{
		Name = name ?? throw new ArgumentNullException(nameof(name));
		Clauses = DescriptorCollections.Copy(clauses);
		ResourcesRead = DescriptorCollections.Ids(resourcesRead);
		ResourcesWritten = DescriptorCollections.Ids(resourcesWritten);
		MessagesRead = DescriptorCollections.Ids(messagesRead);
		MessagesWritten = DescriptorCollections.Ids(messagesWritten);
		Traits = traits;
		ConditionClauses = DescriptorCollections.Copy(conditionClauses);
		ConditionResources = DescriptorCollections.Ids(conditionResources);
	}

	public string Name { get; }
	public IReadOnlyList<AccessClause> Clauses { get; }
	public IReadOnlyList<int> ResourcesRead { get; }
	public IReadOnlyList<int> ResourcesWritten { get; }
	public IReadOnlyList<int> MessagesRead { get; }
	public IReadOnlyList<int> MessagesWritten { get; }
	public NodeTraits Traits { get; }
	public IReadOnlyList<AccessClause> ConditionClauses { get; }
	public IReadOnlyList<int> ConditionResources { get; }
}

public sealed class NodeInput
{
	public NodeInput(int id, string instanceKey, SystemAccess access, int order = 0, bool hasCondition = false)
	{
		Id = id;
		InstanceKey = instanceKey ?? throw new ArgumentNullException(nameof(instanceKey));
		Access = access ?? throw new ArgumentNullException(nameof(access));
		Order = order;
		HasCondition = hasCondition;
	}

	public int Id { get; }
	public string InstanceKey { get; }
	public SystemAccess Access { get; }
	public int Order { get; }
	public bool HasCondition { get; }
}

public readonly struct EdgeInput
{
	public EdgeInput(int before, int after) { Before = before; After = after; }
	public int Before { get; }
	public int After { get; }
}

public sealed class ScheduleInput
{
	public ScheduleInput(IEnumerable<NodeInput> nodes, IEnumerable<EdgeInput>? edges = null)
	{
		if (nodes is null) throw new ArgumentNullException(nameof(nodes));
		Nodes = DescriptorCollections.Copy(nodes);
		Edges = DescriptorCollections.Copy(edges);
	}

	public IReadOnlyList<NodeInput> Nodes { get; }
	public IReadOnlyList<EdgeInput> Edges { get; }
}

public enum StepKind : byte { Batch, ApplyDeferred }
public enum EdgeReason : byte { Explicit, Conflict, Barrier }

public readonly struct PlanStep
{
	public PlanStep(StepKind kind, int first = 0, int count = 0) { Kind = kind; First = first; Count = count; }
	public StepKind Kind { get; }
	public int First { get; }
	public int Count { get; }
}

public readonly struct PlanEdge
{
	public PlanEdge(int from, int to, EdgeReason reason) { From = from; To = to; Reason = reason; }
	public int From { get; }
	public int To { get; }
	public EdgeReason Reason { get; }
}

public readonly struct PlanDiagnostic
{
	public PlanDiagnostic(string code, int nodeA, int nodeB, string detail)
	{
		Code = code ?? throw new ArgumentNullException(nameof(code));
		NodeA = nodeA;
		NodeB = nodeB;
		Detail = detail ?? throw new ArgumentNullException(nameof(detail));
	}
	public string Code { get; }
	public int NodeA { get; }
	public int NodeB { get; }
	public string Detail { get; }
}

/// <summary>Portable immutable output, shared by the serial and parallel executors.</summary>
public sealed class SchedulePlan
{
	internal SchedulePlan(IEnumerable<int> nodeOrder, IEnumerable<int> batchNodes, IEnumerable<PlanStep> steps,
		IEnumerable<PlanEdge> edges, IEnumerable<PlanDiagnostic> diagnostics, ulong inputFingerprint, bool isValid)
	{
		NodeOrder = DescriptorCollections.Copy(nodeOrder);
		BatchNodes = DescriptorCollections.Copy(batchNodes);
		Steps = DescriptorCollections.Copy(steps);
		Edges = DescriptorCollections.Copy(edges);
		Diagnostics = DescriptorCollections.Copy(diagnostics);
		InputFingerprint = inputFingerprint;
		IsValid = isValid;
	}

	public IReadOnlyList<int> NodeOrder { get; }
	public IReadOnlyList<int> BatchNodes { get; }
	public IReadOnlyList<PlanStep> Steps { get; }
	public IReadOnlyList<PlanEdge> Edges { get; }
	public IReadOnlyList<PlanDiagnostic> Diagnostics { get; }
	public ulong InputFingerprint { get; }
	public bool IsValid { get; }

	/// <summary>Checks the ranges and execution ordering of an embedded generated plan.</summary>
	public static SchedulePlan FromGenerated(int[] nodeOrder, int[] batchNodes, PlanStep[] steps,
		PlanEdge[] edges, ulong fingerprint)
	{
		if (nodeOrder is null) throw new ArgumentNullException(nameof(nodeOrder));
		if (batchNodes is null) throw new ArgumentNullException(nameof(batchNodes));
		if (steps is null) throw new ArgumentNullException(nameof(steps));
		if (edges is null) throw new ArgumentNullException(nameof(edges));
		var plan = new SchedulePlan(nodeOrder, batchNodes, steps, edges, new PlanDiagnostic[0], fingerprint, true);
		var count = plan.NodeOrder.Count;
		if (plan.BatchNodes.Count != count) throw new ArgumentException("Every node must occur in exactly one batch.");
		var seen = new bool[count];
		for (var i = 0; i < count; i++)
		{
			var node = plan.NodeOrder[i];
			if (node < 0 || node >= count || seen[node] || plan.BatchNodes[i] != node)
				throw new ArgumentException("Node order must be a permutation matching the batch order.");
			seen[node] = true;
		}
		var batches = new int[count];
		var barrierEpochs = new int[count];
		var next = 0;
		var batch = 0;
		var barrierEpoch = 0;
		for (var i = 0; i < plan.Steps.Count; i++)
		{
			var step = plan.Steps[i];
			if (step.Kind == StepKind.ApplyDeferred)
			{
				if (step.First != 0 || step.Count != 0) throw new ArgumentException("Barriers cannot contain batch nodes.");
				barrierEpoch++;
				continue;
			}
			if (step.Kind != StepKind.Batch || step.First != next || step.Count <= 0 || step.Count > count - next)
				throw new ArgumentException("Batch ranges must cover all nodes once in plan order.");
			for (var j = 0; j < step.Count; j++)
			{
				var node = plan.BatchNodes[next++];
				batches[node] = batch;
				barrierEpochs[node] = barrierEpoch;
			}
			batch++;
		}
		if (next != count || plan.Steps.Count == 0 || plan.Steps[plan.Steps.Count - 1].Kind != StepKind.ApplyDeferred)
			throw new ArgumentException("Plans must execute every node and end with a deferred-command barrier.");
		foreach (var edge in plan.Edges)
		{
			if (edge.From < 0 || edge.From >= count || edge.To < 0 || edge.To >= count ||
				batches[edge.From] >= batches[edge.To] || edge.Reason > EdgeReason.Barrier)
				throw new ArgumentException("Plan edges must point to a later batch.");
			if (edge.Reason == EdgeReason.Barrier && barrierEpochs[edge.From] == barrierEpochs[edge.To])
				throw new ArgumentException("A barrier edge requires a deferred-command barrier before its destination.");
		}
		return plan;
	}
}

internal static class DescriptorCollections
{
	internal static ReadOnlyCollection<T> Copy<T>(IEnumerable<T>? values)
		=> new List<T>(values ?? new T[0]).AsReadOnly();

	internal static ReadOnlyCollection<int> Ids(IEnumerable<int>? values)
	{
		var ids = new List<int>(values ?? new int[0]);
		ids.Sort();
		for (var i = ids.Count - 1; i > 0; i--)
			if (ids[i] == ids[i - 1]) ids.RemoveAt(i);
		return ids.AsReadOnly();
	}
}
