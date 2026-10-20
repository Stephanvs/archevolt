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
	internal Archetype? Archetype;
	internal NativeChunk? Chunk;
	internal int Row;
}

internal sealed class EntityTable
{
	private const int PageSize = 1024;
	private EntityEntry[][] _pages;
	private readonly object _gate = new();
	private readonly Stack<int> _free = new();
	private int _count;

	internal EntityTable(int capacity)
	{
		_free.EnsureCapacity(capacity);
		_pages = new EntityEntry[checked((int)(((long)capacity + PageSize - 1) / PageSize))][];
		for (int i = 0; i < _pages.Length; i++)
			_pages[i] = new EntityEntry[PageSize];
	}

	internal ref EntityEntry Entry(int index) => ref Volatile.Read(ref _pages)[index / PageSize][index % PageSize];

	internal Entity Reserve(long world)
	{
		lock (_gate)
		{
			int index;
			if (_free.Count != 0)
				index = _free.Pop();
			else
			{
				index = _count;
				if (index / PageSize == _pages.Length)
				{
					var pages = new EntityEntry[checked(_pages.Length + 1)][];
					_pages.CopyTo(pages, 0);
					pages[^1] = new EntityEntry[PageSize];
					Volatile.Write(ref _pages, pages);
				}
				Volatile.Write(ref _count, checked(_count + 1));
			}
			ref var entry = ref Entry(index);
			if (entry.Generation == 0)
				entry.Generation = 1;
			entry.State = EntityState.Reserved;
			return new Entity(world, index, entry.Generation);
		}
	}

	internal bool IsAlive(Entity entity, long world) =>
		entity.WorldId == world && (uint)entity.Index < (uint)Volatile.Read(ref _count)
		&& Entry(entity.Index).Generation == entity.Generation
		&& Entry(entity.Index).State == EntityState.Alive;

	internal void Materialize(Entity entity, Archetype archetype, NativeChunk chunk, int row)
	{
		ref var entry = ref Entry(entity.Index);
		if (entry.State != EntityState.Reserved || entry.Generation != entity.Generation)
			throw new InvalidOperationException("The entity reservation is no longer valid.");
		Relocate(entity.Index, archetype, chunk, row);
		entry.State = EntityState.Alive;
	}

	internal void Relocate(int index, Archetype archetype, NativeChunk chunk, int row)
	{
		ref var entry = ref Entry(index);
		entry.Archetype = archetype;
		entry.Chunk = chunk;
		entry.Row = row;
	}

	internal void Release(int index)
	{
		lock (_gate)
			ReleaseCore(index);
	}

	internal void Cancel(Entity entity, long world)
	{
		lock (_gate)
			if (entity.WorldId == world && (uint)entity.Index < (uint)_count
				&& Entry(entity.Index).Generation == entity.Generation && Entry(entity.Index).State == EntityState.Reserved)
				ReleaseCore(entity.Index);
	}

	private void ReleaseCore(int index)
	{
		ref var entry = ref Entry(index);
		entry.Archetype = null;
		entry.Chunk = null;
		entry.Row = 0;
		if (entry.Generation == uint.MaxValue)
			entry.State = EntityState.Retired;
		else
		{
			entry.Generation++;
			entry.State = EntityState.Free;
			_free.Push(index);
		}
	}
}
