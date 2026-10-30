namespace Archevolt;

public sealed class QueryState
{
    internal readonly SystemInstance Owner;
    internal readonly QueryShape Shape;
    internal readonly int[] ComponentIds;
    private readonly int[] _required;
    private readonly int[] _excluded;
    internal readonly List<QueryMatch> Matches = [];
    private int _seen;

    internal QueryState(SystemInstance owner, QueryShape shape)
    {
        Owner = owner;
        Shape = shape;
        ComponentIds = shape.Terms.Select(t => owner.World.Registry.Id(t.TypeKey)).ToArray();
        _required = shape.RequiredTypes.Select(owner.World.Registry.Id).ToArray();
        _excluded = shape.ExcludedTypes.Select(owner.World.Registry.Id).ToArray();
        for (int i = 0; i < shape.Terms.Length; i++)
        {
            var component = owner.World.Registry.Components[ComponentIds[i]];
            var access = shape.Terms[i].Access;
            if (component.IsTag && (access & (QueryAccess.Read | QueryAccess.Write)) != 0)
                throw new ArgumentException("Tags support membership and tick filters, not value access.");
            if (!component.Tracked && (access & (QueryAccess.Added | QueryAccess.Changed)) != 0)
                throw new ArgumentException($"Change filter on untracked component {component.Name}.");
        }
    }

    internal void Refresh()
    {
        var archetypes = Owner.World.Archetypes;
        while (_seen < archetypes.Count)
        {
            var archetype = archetypes[_seen++];
            bool matches = true;
            foreach (int id in _required)
                if (!archetype.Has(id)) { matches = false; break; }
            if (matches)
                foreach (int id in _excluded)
                    if (archetype.Has(id)) { matches = false; break; }
            if (matches)
                Matches.Add(new QueryMatch(archetype, ComponentIds.Select(id => archetype.Columns[id]).ToArray()));
        }
    }

    public QueryCursor Begin(SystemRun run)
    {
        run.Require(Owner);
        return new QueryCursor(this, run);
    }

    public bool TryLocate(SystemRun run, Entity entity, out BlockView block, out int row)
    {
        run.Require(Owner);
        block = default;
        row = 0;
        if (run.IsPartitioned)
            throw new InvalidOperationException("Entity lookup is not allowed in a partitioned invocation.");
        if (!Owner.World.Entities.IsAlive(entity, Owner.World.Id))
            return false;
        ref var entry = ref Owner.World.Entities.Entry(entity.Index);
        foreach (var match in Matches)
            if (match.Archetype.Index == entry.ArchetypeId)
            {
                block = new BlockView(this, match, 0, match.Archetype.Table.Count, run);
                row = entry.Row;
                return block.MatchesRow(row);
            }
        return false;
    }

    internal int TotalRows
    {
        get
        {
            int count = 0;
            foreach (var match in Matches)
                count = checked(count + match.Archetype.Table.Count);
            return count;
        }
    }
}

internal sealed record QueryMatch(Archetype Archetype, int[] Columns);

public ref struct QueryCursor
{
    private readonly QueryState? _state;
    private readonly SystemRun _run;
    private int _match;
    private long _offset;

    internal QueryCursor(QueryState state, SystemRun run)
    {
        _state = state;
        _run = run;
        _match = 0;
        _offset = 0;
    }

    public TickWindow Window => _run.Window;

    public QueryCursor Enumerate()
    {
        if (_state is null)
            throw new InvalidOperationException("A default query has no active execution.");
        _run.Require(_state.Owner);
        return new QueryCursor(_state, _run);
    }

    public bool Next(out BlockView block)
    {
        block = default;
        if (_state is null)
            throw new InvalidOperationException("A default query has no active execution.");
        _run.Require(_state.Owner);
        long first = _run.FirstRow;
        long end = first + _run.RowCount;
        while (_match < _state.Matches.Count)
        {
            var match = _state.Matches[_match++];
            long tableStart = _offset;
            _offset += match.Archetype.Table.Count;
            if (tableStart >= end)
                return false;
            long start = Math.Max(first, tableStart);
            long stop = Math.Min(end, _offset);
            if (start >= stop)
                continue;
            block = new BlockView(_state, match, (int)(start - tableStart), (int)(stop - start), _run);
            return true;
        }
        return false;
    }
}

public readonly ref struct BlockView
{
    private readonly QueryState? _state;
    private readonly QueryMatch? _match;
    private readonly Table? _table;
    private readonly SystemRun _run;
    private readonly int _start;
    private readonly int _count;

    internal BlockView(QueryState state, QueryMatch match, int start, int count, SystemRun run)
    {
        _state = state;
        _match = match;
        _table = match.Archetype.Table;
        _run = run;
        _start = start;
        _count = count;
    }

    public int Count => _count;

    public ReadOnlySpan<T> Read<T>(int term) where T : unmanaged
    {
        int column = Column<T>(term, QueryAccess.Read);
        if (column < 0)
            throw new InvalidOperationException("The optional component is absent.");
        return _table!.Values<T>(column).Slice(_start, _count);
    }

    public bool TryRead<T>(int term, out ReadOnlySpan<T> values) where T : unmanaged
    {
        int column = Column<T>(term, QueryAccess.Read | QueryAccess.Optional);
        values = column < 0 ? default : _table!.Values<T>(column).Slice(_start, _count);
        return column >= 0;
    }

    public Span<T> Write<T>(int term) where T : unmanaged
    {
        int column = Column<T>(term, QueryAccess.Write);
        if (_table!.Components[column].Tracked)
            throw new InvalidOperationException("Tracked values require the stamping accessor.");
        return _table.Values<T>(column).Slice(_start, _count);
    }

    public TrackedColumn<T> Tracked<T>(int term) where T : unmanaged
    {
        int column = Column<T>(term, QueryAccess.Write);
        if (!_table!.Components[column].Tracked)
            throw new InvalidOperationException("The component is not tracked.");
        return new TrackedColumn<T>(_table.Values<T>(column).Slice(_start, _count),
            _table.Changed(column).Slice(_start, _count), _run.Window.Now);
    }

    public ReadOnlySpan<ulong> Added(int term) => Ticks(term, QueryAccess.Added);
    public ReadOnlySpan<ulong> Changed(int term) => Ticks(term, QueryAccess.Changed);
    public EntityColumn Entities { get { Validate(); return new(_table!.Entities.Slice(_start, _count), _state!.Owner.World.Id); } }

    public bool MatchesRow(int row)
    {
        Validate();
        if ((uint)row >= (uint)_count)
            throw new ArgumentOutOfRangeException(nameof(row));
        for (int i = 0; i < _state!.Shape.Terms.Length; i++)
        {
            var access = _state.Shape.Terms[i].Access;
            int column = _match!.Columns[i];
            if ((access & QueryAccess.Added) != 0 && !_run.Window.Contains(_table!.Added(column)[_start + row]))
                return false;
            if ((access & QueryAccess.Changed) != 0 && !_run.Window.Contains(_table!.Changed(column)[_start + row]))
                return false;
        }
        return true;
    }

    private int Column<T>(int term, QueryAccess access) where T : unmanaged
    {
        Validate();
        if ((uint)term >= (uint)_state!.Shape.Terms.Length)
            throw new ArgumentOutOfRangeException(nameof(term));
        var declared = _state.Shape.Terms[term];
        if (declared.TypeKey != TypeKey<T>.Value || (declared.Access & access) != access)
            throw new InvalidOperationException("The requested type or access was not declared.");
        return _match!.Columns[term];
    }

    private ReadOnlySpan<ulong> Ticks(int term, QueryAccess kind)
    {
        Validate();
        if ((uint)term >= (uint)_state!.Shape.Terms.Length || (_state.Shape.Terms[term].Access & kind) == 0)
            throw new InvalidOperationException("The requested tick access was not declared.");
        int column = _match!.Columns[term];
        return (kind == QueryAccess.Added ? _table!.Added(column) : _table!.Changed(column)).Slice(_start, _count);
    }

    private void Validate()
    {
        if (_state is null)
            throw new InvalidOperationException("A default block has no active execution.");
        _run.Require(_state.Owner);
    }
}

public readonly ref struct EntityColumn
{
    private readonly ReadOnlySpan<EntityKey> _entities;
    private readonly long _world;
    internal EntityColumn(ReadOnlySpan<EntityKey> entities, long world) { _entities = entities; _world = world; }
    public int Length => _entities.Length;
    public Entity this[int row] => new(_world, _entities[row].Index, _entities[row].Generation);
}

public readonly ref struct TrackedColumn<T> where T : unmanaged
{
    private readonly Span<T> _values;
    private readonly Span<ulong> _changed;
    private readonly ulong _now;
    internal TrackedColumn(Span<T> values, Span<ulong> changed, ulong now)
    {
        _values = values;
        _changed = changed;
        _now = now;
    }
    public int Length => _values.Length;
    // Generated named-query rows stamp one row per access, so they take both spans and bounds-check once per block.
    public Span<T> Values => _values;
    public Span<ulong> Changed => _changed;
    public ref T Write(int row) { _changed[row] = _now; return ref _values[row]; }
    public Span<T> WriteAll() { _changed.Fill(_now); return _values; }
    public StampedRef<T> At(int row) => new(ref _values[row], ref _changed[row], _now);
}

public readonly ref struct StampedRef<T> where T : unmanaged
{
    private readonly ref T _value;
    private readonly ref ulong _changed;
    private readonly ulong _now;
    internal StampedRef(ref T value, ref ulong changed, ulong now)
    {
        _value = ref value;
        _changed = ref changed;
        _now = now;
    }
    public ref T Value { get { _changed = _now; return ref _value; } }
}
