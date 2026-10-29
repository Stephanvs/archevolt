namespace Archevolt;

public readonly record struct Entity
{
    internal Entity(long world, int index, uint generation)
    {
        WorldId = world;
        Index = index;
        Generation = generation;
    }

    public long WorldId { get; }
    public int Index { get; }
    public uint Generation { get; }
    public bool IsNull => WorldId == 0;
}

internal readonly record struct EntityKey(int Index, uint Generation);
internal enum EntityState : byte { Free, Reserved, Alive, Retired }

internal struct EntityEntry
{
    internal uint Generation;
    internal EntityState State;
    internal int ArchetypeId;
    internal int Row;
}

// Only the owner thread changes the free list, and only while no system can reserve. Systems claim slots by
// decrementing the cursor: a result n >= 0 takes free slot n, and -k takes the k-th slot past the directory's end.
internal sealed class EntityAllocator
{
    private int[] _free;
    private int _freeCount;
    private int _cursor;

    internal EntityAllocator(int capacity) => _free = new int[Math.Max(capacity, 16)];

    internal int FreeCount => _freeCount;
    internal int Pop() => _free[_cursor = --_freeCount];
    internal int Slot(int position) => _free[position];

    internal void Push(int index)
    {
        if (_freeCount == _free.Length)
            Array.Resize(ref _free, checked(_free.Length * 2));
        _free[_freeCount] = index;
        _cursor = ++_freeCount;
    }

    internal int Claim() => Interlocked.Decrement(ref _cursor);

    // Returns how many fresh slots were claimed; the claimed free slots are those from firstTaken onward.
    internal int Flush(out int firstTaken)
    {
        int cursor = Volatile.Read(ref _cursor);
        firstTaken = Math.Max(cursor, 0);
        int fresh = cursor < 0 ? -cursor : 0;
        _freeCount = _cursor = firstTaken;
        return fresh;
    }

    internal bool Pending => _cursor != _freeCount;
}

internal sealed class EntityTable
{
    private const int PageSize = 1024;
    private EntityEntry[][] _pages;
    private readonly EntityAllocator _allocator;
    private int _count;

    internal EntityTable(int capacity)
    {
        _allocator = new EntityAllocator(capacity);
        _pages = new EntityEntry[checked((int)(((long)capacity + PageSize - 1) / PageSize))][];
        for (int i = 0; i < _pages.Length; i++)
            _pages[i] = new EntityEntry[PageSize];
    }

    internal ref EntityEntry Entry(int index) => ref Volatile.Read(ref _pages)[(uint)index / PageSize][(uint)index % PageSize];

    internal Entity Reserve(long world)
    {
        int index = _allocator.FreeCount != 0 ? _allocator.Pop() : Append();
        ref var entry = ref Entry(index);
        entry.State = EntityState.Reserved;
        return new Entity(world, index, entry.Generation);
    }

    internal void Reserve(long world, Span<Entity> entities)
    {
        Grow(entities.Length - _allocator.FreeCount);
        for (int i = 0; i < entities.Length; i++)
            entities[i] = Reserve(world);
    }

    // Any thread while a schedule runs. Flush records the reservation in the directory.
    internal Entity ReserveConcurrent(long world)
    {
        int claim = _allocator.Claim();
        if (claim >= 0)
        {
            int index = _allocator.Slot(claim);
            return new Entity(world, index, Entry(index).Generation);
        }
        return new Entity(world, checked(_count - claim - 1), 1);
    }

    internal void Flush()
    {
        if (!_allocator.Pending)
            return;
        int free = _allocator.FreeCount;
        int fresh = _allocator.Flush(out int firstTaken);
        for (int i = firstTaken; i < free; i++)
            Entry(_allocator.Slot(i)).State = EntityState.Reserved;
        Grow(fresh);
        for (int i = 0; i < fresh; i++)
            Entry(Append()).State = EntityState.Reserved;
    }

    internal bool IsAlive(Entity entity, long world) => TryLocate(entity, world, out _, out _);

    internal bool TryLocate(Entity entity, long world, out int archetypeId, out int row)
    {
        archetypeId = 0;
        row = 0;
        if (entity.WorldId != world || (uint)entity.Index >= (uint)Volatile.Read(ref _count))
            return false;
        ref var entry = ref Entry(entity.Index);
        archetypeId = entry.ArchetypeId;
        row = entry.Row;
        return entry.Generation == entity.Generation && entry.State == EntityState.Alive;
    }

    internal void Materialize(Entity entity, int archetypeId, int row)
    {
        ref var entry = ref Entry(entity.Index);
        if (entry.State != EntityState.Reserved || entry.Generation != entity.Generation)
            throw new InvalidOperationException("The entity reservation is no longer valid.");
        entry.ArchetypeId = archetypeId;
        entry.Row = row;
        entry.State = EntityState.Alive;
    }

    internal void Relocate(int index, int archetypeId, int row)
    {
        ref var entry = ref Entry(index);
        entry.ArchetypeId = archetypeId;
        entry.Row = row;
    }

    internal void Release(int index)
    {
        ref var entry = ref Entry(index);
        entry.ArchetypeId = 0;
        entry.Row = 0;
        if (entry.Generation == uint.MaxValue)
            entry.State = EntityState.Retired;
        else
        {
            entry.Generation++;
            entry.State = EntityState.Free;
            _allocator.Push(index);
        }
    }

    internal void Cancel(Entity entity, long world)
    {
        if (entity.WorldId == world && (uint)entity.Index < (uint)_count
            && Entry(entity.Index).Generation == entity.Generation && Entry(entity.Index).State == EntityState.Reserved)
            Release(entity.Index);
    }

    private int Append()
    {
        int index = _count;
        if ((uint)index / PageSize == (uint)_pages.Length)
            Grow(1);
        ref var entry = ref Entry(index);
        entry.Generation = 1;
        Volatile.Write(ref _count, checked(index + 1));
        return index;
    }

    // Pages never move once published, so systems may hold entry references while the owner grows the directory.
    private void Grow(int additional)
    {
        long needed = ((long)_count + additional + PageSize - 1) / PageSize;
        if (needed <= _pages.Length)
            return;
        var pages = new EntityEntry[checked((int)needed)][];
        _pages.CopyTo(pages, 0);
        for (int i = _pages.Length; i < pages.Length; i++)
            pages[i] = new EntityEntry[PageSize];
        Volatile.Write(ref _pages, pages);
    }
}
