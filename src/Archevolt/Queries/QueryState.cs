using System.Runtime.CompilerServices;

namespace Archevolt;

public sealed class QueryState
{
    internal readonly SystemInstance Owner;
    internal readonly QueryShape Shape;
    internal readonly int[] ComponentIds;
    private readonly int[] _required;
    private readonly int[] _excluded;
    // Terms with an Added or Changed filter. QueryCursor.NextChanged skips a table whose summary rules out every row.
    internal readonly QueryFilter[] Filters;
    internal readonly List<QueryMatch> Matches = [];
    private int _seen;

    internal QueryState(SystemInstance owner, QueryShape shape)
    {
        Owner = owner;
        Shape = shape;
        ComponentIds = shape.Terms.Select(t => owner.World.Registry.Id(t.TypeKey)).ToArray();
        _required = shape.RequiredTypes.Select(owner.World.Registry.Id).ToArray();
        _excluded = shape.ExcludedTypes.Select(owner.World.Registry.Id).ToArray();
        var filters = new List<QueryFilter>();
        for (int i = 0; i < shape.Terms.Length; i++)
        {
            var component = owner.World.Registry.Components[ComponentIds[i]];
            var access = shape.Terms[i].Access;
            if (component.IsTag && (access & (QueryAccess.Read | QueryAccess.Write)) != 0)
                throw new ArgumentException("Tags support membership and tick filters, not value access.");
            if (!component.Tracked && (access & (QueryAccess.Added | QueryAccess.Changed)) != 0)
                throw new ArgumentException($"Change filter on untracked component {component.Name}.");
            if ((access & QueryAccess.Added) != 0)
                filters.Add(new QueryFilter(i, Added: true));
            if ((access & QueryAccess.Changed) != 0)
                filters.Add(new QueryFilter(i, Added: false));
        }
        Filters = filters.ToArray();
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
                block = new BlockView(this, match, 0, match.Archetype.Table.Count, run.Window);
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

internal readonly record struct QueryFilter(int Term, bool Added);

internal sealed record QueryMatch(Archetype Archetype, int[] Columns)
{
    internal bool RulesOut(QueryFilter[] filters, TickWindow window)
    {
        var table = Archetype.Table;
        foreach (var filter in filters)
        {
            int column = Columns[filter.Term];
            if (window.Precedes(filter.Added ? table.AddedSummary[column] : table.ChangedSummary[column]))
                return true;
        }
        return false;
    }
}

// QueryState.Begin checks the system's lease. A cursor and its blocks are ref structs, so they cannot outlive the
// body that began the query, and the lease holds for as long as that body runs. They keep the tick window and the
// row range of the run, not the run itself, and do not check the lease again.
public ref struct QueryCursor
{
    private readonly QueryState? _state;
    private readonly TickWindow _window;
    private readonly long _first;
    private readonly long _end;
    private int _match;
    private long _offset;

    internal QueryCursor(QueryState state, SystemRun run)
    {
        _state = state;
        _window = run.Window;
        _first = run.FirstRow;
        _end = _first + run.RowCount;
        _match = 0;
        _offset = 0;
    }

    public TickWindow Window => _window;

    public bool Next(out BlockView block)
    {
        block = default;
        if (_state is null)
            throw new InvalidOperationException("A default query has no active execution.");
        long first = _first;
        long end = _end;
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
            block = new BlockView(_state, match, (int)(start - tableStart), (int)(stop - start), _window);
            return true;
        }
        return false;
    }

    // Like Next, but skips a block when a change-filtered term's summary rules out every row of its table. Generated
    // code calls it only for queries with Added or Changed terms. It stays out of line, because the extra code inlined
    // through Next exhausted the JIT's inlining budget in generated systems and left their row loops on the stack.
    [MethodImpl(MethodImplOptions.NoInlining)]
    public bool NextChanged(out BlockView block)
    {
        while (Next(out block))
            if (!block.RulesOut())
                return true;
        return false;
    }
}

public readonly ref struct BlockView
{
    private readonly QueryState? _state;
    private readonly QueryMatch? _match;
    private readonly Table? _table;
    private readonly TickWindow _window;
    private readonly int _start;
    private readonly int _count;

    internal BlockView(QueryState state, QueryMatch match, int start, int count, TickWindow window)
    {
        _state = state;
        _match = match;
        _table = match.Archetype.Table;
        _window = window;
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
            _table.Changed(column).Slice(_start, _count), ref _table.ChangedSummary[column], _window.Now);
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
            if ((access & QueryAccess.Added) != 0 && !_window.Contains(_table!.Added(column)[_start + row]))
                return false;
            if ((access & QueryAccess.Changed) != 0 && !_window.Contains(_table!.Changed(column)[_start + row]))
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

    internal bool RulesOut() => _match!.RulesOut(_state!.Filters, _window);

    private void Validate()
    {
        if (_state is null)
            throw new InvalidOperationException("A default block has no active execution.");
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
    private readonly ref ulong _summary;
    private readonly ulong _now;
    internal TrackedColumn(Span<T> values, Span<ulong> changed, ref ulong summary, ulong now)
    {
        _values = values;
        _changed = changed;
        _summary = ref summary;
        _now = now;
    }
    public int Length => _values.Length;
    // Generated named-query rows stamp one row per access, so they take both spans and bounds-check once per block.
    // A row stamped through Changed must also raise Summary to the stamp, or change filters can skip the row.
    public Span<T> Values => _values;
    public Span<ulong> Changed => _changed;
    public ref ulong Summary => ref _summary;
    public ref T Write(int row)
    {
        _changed[row] = _now;
        Raise(ref _summary, _now);
        return ref _values[row];
    }
    public Span<T> WriteAll()
    {
        _changed.Fill(_now);
        Raise(ref _summary, _now);
        return _values;
    }
    public StampedRef<T> At(int row) => new(ref _values[row], ref _changed[row], ref _summary, _now);

    // Partitioned workers stamp one column of one table, so the summary is written only when it is behind. After the
    // first row every worker only reads it, and its cache line is not passed between cores.
    internal static void Raise(ref ulong summary, ulong now)
    {
        if (summary < now)
            summary = now;
    }
}

public readonly ref struct StampedRef<T> where T : unmanaged
{
    private readonly ref T _value;
    private readonly ref ulong _changed;
    private readonly ref ulong _summary;
    private readonly ulong _now;
    internal StampedRef(ref T value, ref ulong changed, ref ulong summary, ulong now)
    {
        _value = ref value;
        _changed = ref changed;
        _summary = ref summary;
        _now = now;
    }
    public ref T Value
    {
        get
        {
            _changed = _now;
            TrackedColumn<T>.Raise(ref _summary, _now);
            return ref _value;
        }
    }
}
