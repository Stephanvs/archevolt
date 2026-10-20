using System.Runtime.CompilerServices;
using Archevolt.Scheduling;

namespace Archevolt;

public abstract class SystemBody
{
    protected abstract void Run(SystemRun run);
    internal void Execute(SystemRun run) => Run(run);
}

public delegate SystemBody SystemFactory(SystemBinder binder);
internal readonly record struct ResourceAccess(int TypeKey, bool Write, bool Managed, bool Optional = false);
internal sealed record LocalSpec(int TypeKey, bool Managed, Func<object> Create);

public sealed partial class SystemDescriptor
{
    internal readonly QueryShape[] Queries;
    internal readonly ResourceAccess[] Resources;
    internal readonly LocalSpec[] Locals;
    internal readonly SystemFactory Factory;
    internal readonly bool Partitionable;
    internal readonly bool IsCondition;
    internal readonly bool EmitsCommands;
    public string Name { get; }

    private SystemDescriptor(Builder builder)
    {
        Name = builder.Name;
        Factory = builder.Factory;
        Queries = builder.Queries.ToArray();
        Resources = builder.Resources.ToArray();
        Locals = builder.Locals.ToArray();
        Messages = builder.Messages.ToArray();
        Partitionable = builder.Partitionable;
        IsCondition = builder.Condition;
        EmitsCommands = builder.HasCommands;
        ValidateAliases();
        ValidateMessageAliases();
    }

    public static Builder Create(string name, SystemFactory factory) => new(name, factory);
    public sealed partial class Builder
    {
        internal readonly string Name;
        internal readonly SystemFactory Factory;
        internal readonly List<QueryShape> Queries = [];
        internal readonly List<ResourceAccess> Resources = [];
        internal readonly List<LocalSpec> Locals = [];
        internal bool Partitionable;
        internal bool Condition;
        internal bool HasCommands;
        internal Builder(string name, SystemFactory factory) { Name = name; Factory = factory; }
        public Builder Query(QueryShape shape) { Queries.Add(shape); return this; }
        public Builder AsCondition() { Condition = true; return this; }
        public Builder Commands() { HasCommands = true; return this; }
        public Builder EachQuery(QueryShape shape, bool partitionable)
        {
            Queries.Add(shape);
            Partitionable = partitionable;
            return this;
        }
        public Builder Res<T>(bool write = false, bool optional = false) where T : unmanaged
        {
            if (write && optional)
                throw new ArgumentException("Optional resource access is readonly.");
            Resources.Add(new ResourceAccess(TypeKey<T>.Value, write, false, optional));
            return this;
        }
        public Builder Managed<T>() where T : class
        {
            Resources.Add(new ResourceAccess(TypeKey<T>.Value, true, true));
            return this;
        }
        public Builder Local<T>()
        {
            Locals.Add(new LocalSpec(TypeKey<T>.Value, RuntimeHelpers.IsReferenceOrContainsReferences<T>(), static () => new LocalSlot<T>()));
            return this;
        }
        public SystemDescriptor Build()
        {
            if (Partitionable && (HasCommands || Queries.Count != 1 || Locals.Count != 0 || Messages.Count != 0 || Resources.Any(r => r.Write || r.Managed)
                || Queries[0].Terms.Any(t => (t.Access & QueryAccess.Optional) != 0)))
                throw new ArgumentException("Partitioned Each supports one required-component query and readonly unmanaged resources.");
            if (Condition && (HasCommands || Partitionable || Resources.Any(r => r.Write || r.Managed)
                || Messages.Any(m => m.Kind == MessageAccessKind.Writer)
                || Queries.Any(q => q.Terms.Any(t => (t.Access & QueryAccess.Write) != 0))))
                throw new ArgumentException("Run conditions may only read world state.");
            return new SystemDescriptor(this);
        }
    }

    internal SystemAccess ToAccess(World world)
    {
        NodeTraits traits = Partitionable ? NodeTraits.Partitionable : NodeTraits.None;
        if (EmitsCommands)
            traits |= NodeTraits.EmitsCommands;
        foreach (var resource in Resources)
        {
            var registration = world.Resource(resource.TypeKey).Descriptor;
            if (registration.OwnerThread)
                traits |= NodeTraits.OwnerThread;
            if (registration.Managed || resource.Managed)
                traits |= NodeTraits.OwnerThread | NodeTraits.Exclusive;
        }
        if (Locals.Any(l => l.Managed))
            traits |= NodeTraits.OwnerThread | NodeTraits.Exclusive;
        return new SystemAccess(Name,
            Queries.Select(q => new AccessClause(
                q.Terms.Where(t => (t.Access & QueryAccess.Read) != 0).Select(t => t.TypeKey),
                q.Terms.Where(t => (t.Access & QueryAccess.Write) != 0).Select(t => t.TypeKey),
                q.RequiredTypes, q.ExcludedTypes,
                q.Terms.Where(t => (t.Access & (QueryAccess.Added | QueryAccess.Changed)) != 0).Select(t => t.TypeKey))),
            Resources.Where(r => !r.Write).Select(r => r.TypeKey),
            Resources.Where(r => r.Write).Select(r => r.TypeKey),
            Messages.Where(m => m.Kind == MessageAccessKind.Reader).Select(m => m.TypeKey),
            Messages.Where(m => m.Kind == MessageAccessKind.Writer).Select(m => m.TypeKey), traits: traits);
    }

    private void ValidateAliases()
    {
        for (int i = 0; i < Queries.Length; i++)
            for (int j = i + 1; j < Queries.Length; j++)
            {
                var left = Queries[i];
                var right = Queries[j];
                if (left.RequiredTypes.Intersect(right.ExcludedTypes).Any() || right.RequiredTypes.Intersect(left.ExcludedTypes).Any())
                    continue;
                foreach (var a in left.Terms)
                    foreach (var b in right.Terms)
                        if (a.TypeKey == b.TypeKey && ((a.Access | b.Access) & QueryAccess.Write) != 0)
                            throw new ArgumentException("Overlapping mutable queries require a structural disjointness proof.");
            }
        for (int i = 0; i < Resources.Length; i++)
            for (int j = i + 1; j < Resources.Length; j++)
                if (Resources[i].TypeKey == Resources[j].TypeKey && (Resources[i].Write || Resources[j].Write))
                    throw new ArgumentException("Overlapping mutable resource parameters are not allowed.");
    }
}

public readonly ref partial struct SystemBinder
{
    private readonly SystemInstance? _instance;
    internal SystemBinder(SystemInstance instance) => _instance = instance;
    private SystemInstance Instance => _instance is { Binding: true } instance
        ? instance : throw new InvalidOperationException("The system binder is not active.");

    public QueryState Query(int index)
    {
        var instance = Instance;
        if ((uint)index >= (uint)instance.Queries.Length)
            throw new ArgumentOutOfRangeException(nameof(index));
        return instance.Queries[index] ??= new QueryState(instance, instance.Descriptor.Queries[index]);
    }

    public ResourceHandle<T> Resource<T>(int index) where T : unmanaged
    {
        var instance = Instance;
        var access = GetResource(instance, index, TypeKey<T>.Value, managed: false);
        return new ResourceHandle<T>(instance, (ResourceSlot<T>)instance.World.Resource(access.TypeKey), access.Write, access.Optional);
    }

    public ManagedResourceHandle<T> Managed<T>(int index) where T : class
    {
        var instance = Instance;
        var access = GetResource(instance, index, TypeKey<T>.Value, managed: true);
        return new ManagedResourceHandle<T>(instance, (ResourceSlot<T>)instance.World.Resource(access.TypeKey));
    }

    public LocalHandle<T> Local<T>(int index)
    {
        var instance = Instance;
        if ((uint)index >= (uint)instance.Descriptor.Locals.Length || instance.Descriptor.Locals[index].TypeKey != TypeKey<T>.Value)
            throw new InvalidOperationException("The local parameter was not declared.");
        return new LocalHandle<T>(instance, (LocalSlot<T>)instance.Locals[index]);
    }

    private static ResourceAccess GetResource(SystemInstance instance, int index, int key, bool managed)
    {
        if ((uint)index >= (uint)instance.Descriptor.Resources.Length)
            throw new ArgumentOutOfRangeException(nameof(index));
        var access = instance.Descriptor.Resources[index];
        if (access.TypeKey != key || access.Managed != managed)
            throw new InvalidOperationException("The resource parameter was not declared.");
        return access;
    }
}

public readonly ref struct SystemRun
{
    private readonly SystemInstance? _instance;
    private readonly ulong _epoch;
    public TickWindow Window { get; }
    internal int FirstChunk { get; }
    internal int ChunkCount { get; }
    internal bool IsPartitioned => FirstChunk != 0 || ChunkCount != int.MaxValue;

    internal SystemRun(SystemInstance instance, int firstChunk = 0, int chunkCount = int.MaxValue)
    {
        _instance = instance;
        _epoch = instance.Epoch;
        Window = new TickWindow(instance.LastRun, instance.ThisRun);
        FirstChunk = firstChunk;
        ChunkCount = chunkCount;
    }

    internal void Require(SystemInstance? owner)
    {
        if (_instance is null || owner != _instance || !_instance.Active || _epoch != _instance.Epoch || !_instance.World.IsRunning)
            throw new InvalidOperationException("The execution token is default, expired, or belongs to another system.");
    }
}

internal sealed partial class SystemInstance
{
    internal readonly World World;
    internal readonly SystemDescriptor Descriptor;
    internal readonly string Key;
    internal readonly SystemBody Body;
    internal readonly QueryState?[] Queries;
    internal readonly object[] Locals;
    internal readonly CommandBuffer? Commands;
    internal ulong LastRun;
    internal ulong ThisRun;
    internal ulong Epoch;
    internal bool Active;
    internal bool Binding;

    internal SystemInstance(World world, SystemDescriptor descriptor, string key)
    {
        World = world;
        Descriptor = descriptor;
        Key = key;
        Queries = new QueryState?[descriptor.Queries.Length];
        for (int i = 0; i < Queries.Length; i++)
            Queries[i] = new QueryState(this, descriptor.Queries[i]);
        Locals = descriptor.Locals.Select(l => l.Create()).ToArray();
        if (descriptor.EmitsCommands)
            Commands = new CommandBuffer(world.CommandCapacity);
        ValidateResources();
        Binding = true;
        try { Body = descriptor.Factory(new SystemBinder(this)) ?? throw new InvalidOperationException("System factory returned null."); }
        finally { Binding = false; }
        if (descriptor.IsCondition != (Body is ConditionBody))
            throw new ArgumentException("The system factory does not match its condition declaration.");
    }

    internal void ValidateResources()
    {
        foreach (var resource in Descriptor.Resources)
            if (!World.Resource(resource.TypeKey).Present && !resource.Optional)
                throw new InvalidOperationException($"Required resource for system {Key} is absent.");
    }

    internal void Prepare()
    {
        ValidateResources();
        foreach (var query in Queries)
            query?.Refresh();
    }

    internal SystemRun Begin(int firstChunk = 0, int chunkCount = int.MaxValue)
    {
        if (Active)
            throw new InvalidOperationException("The system instance is already executing.");
        Epoch = checked(Epoch + 1);
        ThisRun = World.NextTick();
        Active = true;
        return new SystemRun(this, firstChunk, chunkCount);
    }

    internal void Complete(bool success)
    {
        Active = false;
        if (success)
            LastRun = ThisRun;
    }
}
