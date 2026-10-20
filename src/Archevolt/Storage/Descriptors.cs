using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Archevolt;

internal static class TypeKeys
{
	private static readonly object s_gate = new();
	private static int s_next;
	internal static int Next()
	{
		lock (s_gate)
			return s_next = checked(s_next + 1);
	}
}

internal static class TypeKey<T>
{
	internal static readonly int Value = TypeKeys.Next();
}

public sealed class ComponentDescriptor
{
	private ComponentDescriptor(Type type, int key, string name, int size, bool tracked, bool tag)
	{
		Type = type;
		TypeKey = key;
		Name = name;
		Size = size;
		Tracked = tracked;
		IsTag = tag;
	}

	public Type Type { get; }
	public string Name { get; }
	public bool Tracked { get; }
	public bool IsTag { get; }
	internal int TypeKey { get; }
	internal int Size { get; }

	public static ComponentDescriptor Of<T>(string? name = null, bool tracked = false, bool tag = false)
		where T : unmanaged => new(typeof(T), TypeKey<T>.Value, name ?? typeof(T).FullName ?? typeof(T).Name,
			tag ? 0 : Unsafe.SizeOf<T>(), tracked, tag);

	internal bool Equivalent(ComponentDescriptor other) =>
		Type == other.Type && Size == other.Size && Tracked == other.Tracked && IsTag == other.IsTag;
}

public sealed class BundleDescriptor
{
	private BundleDescriptor(Type type, int key, ComponentDescriptor[] components)
	{
		Type = type;
		TypeKey = key;
		Components = (ComponentDescriptor[])components.Clone();
		var seen = new HashSet<int>();
		int bytes = 0;
		foreach (var component in Components)
		{
			if (!seen.Add(component.TypeKey))
				throw new ArgumentException($"Bundle repeats component {component.Name}.", nameof(components));
			bytes = checked(bytes + component.Size);
		}
		PayloadBytes = bytes;
	}

	public Type Type { get; }
	internal int TypeKey { get; }
	internal ComponentDescriptor[] Components { get; }
	internal int PayloadBytes { get; }

	public static BundleDescriptor Create<TBundle>(params ComponentDescriptor[] components) where TBundle : unmanaged =>
		new(typeof(TBundle), TypeKey<TBundle>.Value, components);
}

public interface IBundle<TSelf> where TSelf : unmanaged, IBundle<TSelf>
{
	static abstract BundleDescriptor Descriptor { get; }
	static abstract void Write(in TSelf value, ref BundleWriter writer);
}

public ref struct BundleWriter
{
	private readonly BundleDescriptor _descriptor;
	private readonly Span<byte> _payload;
	private int _member;
	private int _offset;

	internal BundleWriter(BundleDescriptor descriptor, Span<byte> payload)
	{
		_descriptor = descriptor;
		_payload = payload;
		_member = 0;
		_offset = 0;
	}

	public void Put<T>(in T value) where T : unmanaged
	{
		if (_descriptor is null || (uint)_member >= (uint)_descriptor.Components.Length)
			throw new InvalidOperationException("The bundle has no remaining member.");
		var component = _descriptor.Components[_member];
		if (component.TypeKey != TypeKey<T>.Value)
			throw new InvalidOperationException("The bundle member type differs from its descriptor.");
		if (!component.IsTag)
			MemoryMarshal.Write(_payload.Slice(_offset, component.Size), in value);
		_offset += component.Size;
		_member++;
	}

	internal readonly void Complete()
	{
		if (_member != _descriptor.Components.Length || _offset != _payload.Length)
			throw new InvalidOperationException("The bundle did not write every declared member.");
	}
}

internal sealed class ComponentRegistry
{
	internal readonly ComponentDescriptor[] Components;
	private readonly int[] _byType;

	internal ComponentRegistry(IEnumerable<ComponentDescriptor> descriptors)
	{
		var components = new Dictionary<int, ComponentDescriptor>();
		foreach (var descriptor in descriptors)
		{
			if (components.TryGetValue(descriptor.TypeKey, out var prior) && !prior.Equivalent(descriptor))
				throw new ArgumentException($"Conflicting metadata for {descriptor.Name}.");
			components[descriptor.TypeKey] = descriptor;
		}
		Components = components.Values.ToArray();
		_byType = new int[components.Count == 0 ? 1 : checked(components.Keys.Max() + 1)];
		Array.Fill(_byType, -1);
		for (int i = 0; i < Components.Length; i++)
			_byType[Components[i].TypeKey] = i;
	}

	internal int Id<T>() where T : unmanaged => Id(TypeKey<T>.Value);
	internal int Id(int typeKey)
	{
		if ((uint)typeKey >= (uint)_byType.Length || _byType[typeKey] < 0)
			throw new InvalidOperationException("The component type is not registered in this world.");
		return _byType[typeKey];
	}
}
