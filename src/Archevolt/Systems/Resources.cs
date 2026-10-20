namespace Archevolt;

public sealed class ResourceDescriptor
{
    private ResourceDescriptor(int key, Type type, bool tracked, bool ownerThread, bool managed, Func<ResourceSlot> create)
    {
        TypeKey = key;
        Type = type;
        Tracked = tracked;
        OwnerThread = ownerThread;
        Managed = managed;
        Create = create;
    }

    public Type Type { get; }
    public bool Tracked { get; }
    public bool OwnerThread { get; }
    internal int TypeKey { get; }
    internal bool Managed { get; }
    internal Func<ResourceSlot> Create { get; }

    public static ResourceDescriptor Of<T>(bool tracked = false, bool ownerThread = false) where T : unmanaged =>
        new(TypeKey<T>.Value, typeof(T), tracked, ownerThread, false, static () => new ResourceSlot<T>());

    public static ResourceDescriptor OfManaged<T>(bool tracked = false) where T : class =>
        new(TypeKey<T>.Value, typeof(T), tracked, true, true, static () => new ResourceSlot<T>());

    internal bool Equivalent(ResourceDescriptor other) =>
        Type == other.Type && Tracked == other.Tracked && OwnerThread == other.OwnerThread && Managed == other.Managed;
}

internal abstract class ResourceSlot
{
    internal ResourceDescriptor Descriptor = null!;
    internal bool Present;
    internal ulong Added;
    internal ulong Changed;
    internal abstract void Clear(bool dispose);
}

internal sealed class ResourceSlot<T> : ResourceSlot
{
    internal T Value = default!;
    internal IDisposable? Owned;

    internal void Set(T value, IDisposable? owner, ulong tick)
    {
        IDisposable? previous = Owned;
        bool sameOwner = ReferenceEquals(previous, value);
        if (sameOwner && previous is not null && owner is null)
            throw new InvalidOperationException("Use TakeResource to transfer an owned resource back to the caller.");
        Value = value;
        Owned = owner;
        if (!Present)
            Added = tick;
        Changed = tick;
        Present = true;
        if (!sameOwner)
            previous?.Dispose();
    }

    internal override void Clear(bool dispose)
    {
        IDisposable? previous = Owned;
        Value = default!;
        Owned = null;
        Present = false;
        if (dispose)
            previous?.Dispose();
    }
}

public sealed partial class World
{
    private readonly Dictionary<int, ResourceSlot> _resources = [];

    private void InitializeResources(IEnumerable<ResourceDescriptor> descriptors)
    {
        foreach (var descriptor in descriptors)
        {
            if (_resources.TryGetValue(descriptor.TypeKey, out var prior))
            {
                if (!prior.Descriptor.Equivalent(descriptor))
                    throw new ArgumentException($"Conflicting resource metadata for {descriptor.Type.Name}.");
                continue;
            }
            var slot = descriptor.Create();
            slot.Descriptor = descriptor;
            _resources.Add(descriptor.TypeKey, slot);
        }
    }

    internal ResourceSlot Resource(int key) => _resources.TryGetValue(key, out var slot)
        ? slot : throw new InvalidOperationException("Resource type is not registered in this world.");

    public void InsertResource<T>(T value)
    {
        RequireIdle();
        if (Resource(TypeKey<T>.Value) is not ResourceSlot<T> slot)
            throw new InvalidOperationException("Resource type does not match its registration.");
        slot.Set(value, null, NextTick());
    }

    public void InsertOwnedResource<T>(T value) where T : class, IDisposable
    {
        ArgumentNullException.ThrowIfNull(value);
        RequireIdle();
        var slot = (ResourceSlot<T>)Resource(TypeKey<T>.Value);
        slot.Set(value, value, NextTick());
    }

    public T GetResource<T>()
    {
        RequireIdle();
        var slot = (ResourceSlot<T>)Resource(TypeKey<T>.Value);
        if (!slot.Present)
            throw new InvalidOperationException("Resource is absent.");
        return slot.Value;
    }

    public bool RemoveResource<T>()
    {
        RequireIdle();
        var slot = Resource(TypeKey<T>.Value);
        if (!slot.Present)
            return false;
        NextTick();
        slot.Clear(dispose: true);
        return true;
    }

    public T TakeResource<T>()
    {
        RequireIdle();
        var slot = (ResourceSlot<T>)Resource(TypeKey<T>.Value);
        if (!slot.Present)
            throw new InvalidOperationException("Resource is absent.");
        NextTick();
        T value = slot.Value;
        slot.Clear(dispose: false);
        return value;
    }

    private void DisposeResources()
    {
        List<Exception>? failures = null;
        foreach (var resource in _resources.Values)
        {
            try { resource.Clear(dispose: true); }
            catch (Exception exception) { (failures ??= []).Add(exception); }
        }
        if (failures is not null)
            throw new AggregateException("One or more owned resources failed during disposal.", failures);
    }
}

public readonly struct ResourceHandle<T> where T : unmanaged
{
    private readonly SystemInstance _owner;
    private readonly ResourceSlot<T> _slot;
    private readonly bool _write;
    private readonly bool _optional;
    internal ResourceHandle(SystemInstance owner, ResourceSlot<T> slot, bool write, bool optional)
    {
        _owner = owner;
        _slot = slot;
        _write = write;
        _optional = optional;
    }

    public Res<T> Read(SystemRun run)
    {
        run.Require(_owner);
        if (!_slot.Present)
            throw new InvalidOperationException("Resource is absent.");
        return new Res<T>(_slot, run.Window);
    }

    public ResMut<T> Write(SystemRun run)
    {
        run.Require(_owner);
        if (!_write)
            throw new InvalidOperationException("Writable resource access was not declared.");
        if (!_slot.Present)
            throw new InvalidOperationException("Resource is absent.");
        return new ResMut<T>(_slot, run.Window.Now);
    }

    public OptionalRes<T> ReadOptional(SystemRun run)
    {
        run.Require(_owner);
        if (!_optional)
            throw new InvalidOperationException("Optional resource access was not declared.");
        return new OptionalRes<T>(_slot, run.Window);
    }
}

public readonly ref struct OptionalRes<T> where T : unmanaged
{
    private readonly ResourceSlot<T> _slot;
    private readonly TickWindow _window;
    internal OptionalRes(ResourceSlot<T> slot, TickWindow window) { _slot = slot; _window = window; }
    public bool HasValue => _slot is { Present: true };
    public ref readonly T Value
    {
        get
        {
            if (!HasValue)
                throw new InvalidOperationException("The optional resource is absent.");
            return ref _slot.Value;
        }
    }
    public bool IsAdded => HasValue && _slot.Descriptor.Tracked && _window.Contains(_slot.Added);
    public bool IsChanged => HasValue && _slot.Descriptor.Tracked && _window.Contains(_slot.Changed);
}

public readonly ref struct Res<T> where T : unmanaged
{
    private readonly ResourceSlot<T> _slot;
    private readonly TickWindow _window;
    internal Res(ResourceSlot<T> slot, TickWindow window) { _slot = slot; _window = window; }
    public ref readonly T Value => ref _slot.Value;
    public bool IsAdded => _slot.Descriptor.Tracked && _window.Contains(_slot.Added);
    public bool IsChanged => _slot.Descriptor.Tracked && _window.Contains(_slot.Changed);
}

public readonly ref struct ResMut<T> where T : unmanaged
{
    private readonly ResourceSlot<T> _slot;
    private readonly ulong _now;
    internal ResMut(ResourceSlot<T> slot, ulong now) { _slot = slot; _now = now; }
    public ref T Value
    {
        get
        {
            if (_slot.Descriptor.Tracked)
                _slot.Changed = _now;
            return ref _slot.Value;
        }
    }
}

public readonly ref struct ManagedRes<T> where T : class
{
    private readonly T _value;
    internal ManagedRes(T value) => _value = value;
    public T Value => _value;
}

public readonly struct ManagedResourceHandle<T> where T : class
{
    private readonly SystemInstance _owner;
    private readonly ResourceSlot<T> _slot;
    internal ManagedResourceHandle(SystemInstance owner, ResourceSlot<T> slot) { _owner = owner; _slot = slot; }
    public ManagedRes<T> Read(SystemRun run)
    {
        run.Require(_owner);
        _owner.World.RequireOwner();
        if (!_slot.Present)
            throw new InvalidOperationException("Resource is absent.");
        return new ManagedRes<T>(_slot.Value);
    }
}

internal sealed class LocalSlot<T> { internal T Value = default!; }
public readonly ref struct Local<T>
{
    private readonly LocalSlot<T> _slot;
    internal Local(LocalSlot<T> slot) => _slot = slot;
    public ref T Value => ref _slot.Value;
}
public readonly struct LocalHandle<T>
{
    private readonly SystemInstance _owner;
    private readonly LocalSlot<T> _slot;
    internal LocalHandle(SystemInstance owner, LocalSlot<T> slot) { _owner = owner; _slot = slot; }
    public Local<T> Get(SystemRun run) { run.Require(_owner); return new Local<T>(_slot); }
}
