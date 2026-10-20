namespace Archevolt;

[Flags]
internal enum QueryAccess : byte { Read = 1, Write = 2, Optional = 4, Added = 8, Changed = 16 }
internal readonly record struct QueryTerm(int TypeKey, Type Type, QueryAccess Access);

public sealed class QueryShape
{
	internal readonly QueryTerm[] Terms;
	internal readonly int[] RequiredTypes;
	internal readonly int[] ExcludedTypes;

	private QueryShape(string name, QueryTerm[] terms, int[] required, int[] excluded)
	{
		Name = name;
		Terms = terms;
		RequiredTypes = required;
		ExcludedTypes = excluded;
	}

	public string Name { get; }
	public static Builder Create(string name) => new(name);

	public sealed class Builder
	{
		private readonly string _name;
		private readonly SortedDictionary<int, QueryTerm> _terms = [];
		private readonly HashSet<int> _required = [];
		private readonly HashSet<int> _excluded = [];

		internal Builder(string name) => _name = name;
		public Builder Read<T>(int term) where T : unmanaged => Add<T>(term, QueryAccess.Read);
		public Builder Write<T>(int term) where T : unmanaged => Add<T>(term, QueryAccess.Write);
		public Builder Optional<T>(int term) where T : unmanaged => Add<T>(term, QueryAccess.Read | QueryAccess.Optional);
		public Builder Added<T>(int term) where T : unmanaged => Add<T>(term, QueryAccess.Added);
		public Builder Changed<T>(int term) where T : unmanaged => Add<T>(term, QueryAccess.Changed);
		public Builder With<T>() where T : unmanaged { _required.Add(TypeKey<T>.Value); return this; }
		public Builder Without<T>() where T : unmanaged { _excluded.Add(TypeKey<T>.Value); return this; }

		private Builder Add<T>(int index, QueryAccess access) where T : unmanaged
		{
			ArgumentOutOfRangeException.ThrowIfNegative(index);
			if (_terms.TryGetValue(index, out var existing))
			{
				if (existing.TypeKey != TypeKey<T>.Value)
					throw new ArgumentException("A query term cannot describe different component types.");
				if ((existing.Access & (QueryAccess.Read | QueryAccess.Write)) != 0
					&& (access & (QueryAccess.Read | QueryAccess.Write)) != 0)
					throw new ArgumentException("A component value access was declared twice.");
				access |= existing.Access;
			}
			_terms[index] = new QueryTerm(TypeKey<T>.Value, typeof(T), access);
			return this;
		}

		public QueryShape Build()
		{
			var terms = new QueryTerm[_terms.Count];
			foreach (var pair in _terms)
			{
				if (pair.Key >= terms.Length)
					throw new ArgumentException("Query term indices must be contiguous starting at zero.");
				terms[pair.Key] = pair.Value;
			}
			var types = new HashSet<int>();
			foreach (var term in terms)
			{
				if (!types.Add(term.TypeKey))
					throw new ArgumentException("Use one term per component, including its change filters.");
				if ((term.Access & QueryAccess.Optional) == 0 || (term.Access & (QueryAccess.Added | QueryAccess.Changed)) != 0)
					_required.Add(term.TypeKey);
			}
			return new QueryShape(_name, terms, _required.Order().ToArray(), _excluded.Order().ToArray());
		}
	}
}

public readonly struct TickWindow
{
	private readonly ulong _last;
	public ulong Now { get; }
	internal TickWindow(ulong last, ulong now) { _last = last; Now = now; }
	public bool Contains(ulong stamp) => stamp > _last && stamp <= Now;
}
