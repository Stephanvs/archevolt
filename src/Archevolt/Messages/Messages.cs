namespace Archevolt;

internal enum MessageAccessKind { Reader, Writer, Removed }
internal readonly record struct MessageAccess(int TypeKey, MessageAccessKind Kind);
internal sealed class MessageCursor { internal ulong Next; }

public sealed class MessageDescriptor
{
	internal int TypeKey { get; }
	internal Func<int, MessageChannel> Create { get; }
	private MessageDescriptor(int typeKey, Func<int, MessageChannel> create)
	{
		TypeKey = typeKey;
		Create = create;
	}
	public static MessageDescriptor Of<T>() where T : unmanaged =>
		new(TypeKey<T>.Value, static capacity => new MessageChannel<T>(capacity));
}

internal abstract class MessageChannel
{
	internal abstract void Advance();
}

internal sealed class MessageChannel<T>(int capacity) : MessageChannel where T : unmanaged
{
	private T[] _previous = new T[capacity];
	private T[] _current = new T[capacity];
	private int _previousCount;
	private int _currentCount;
	internal ulong End { get; private set; }
	internal ulong Start => End - (ulong)_previousCount - (ulong)_currentCount;
	internal ReadOnlySpan<T> Previous => _previous.AsSpan(0, _previousCount);
	internal ReadOnlySpan<T> Current => _current.AsSpan(0, _currentCount);

	internal void Send(in T value)
	{
		ulong next = checked(End + 1);
		if (_currentCount == _current.Length)
			Array.Resize(ref _current, checked(Math.Max(4, _current.Length * 2)));
		_current[_currentCount++] = value;
		End = next;
	}

	internal override void Advance()
	{
		(_previous, _current) = (_current, _previous);
		_previousCount = _currentCount;
		_currentCount = 0;
	}
}

public readonly record struct Removal(Entity Entity, ulong Tick);

public sealed partial class ModuleDescriptor
{
	internal readonly MessageDescriptor[] Messages;
}

public sealed partial class ModuleBuilder
{
	internal readonly List<MessageDescriptor> Messages = [];
	public ModuleBuilder Message<T>() where T : unmanaged
	{
		Messages.Add(MessageDescriptor.Of<T>());
		return this;
	}
}

public sealed partial class WorldBuilder
{
	internal readonly List<MessageDescriptor> Messages = [];
	internal int MessageCapacity;
	public WorldBuilder AddMessage<T>() where T : unmanaged
	{
		Messages.Add(MessageDescriptor.Of<T>());
		return this;
	}
}

public sealed partial class SystemDescriptor
{
	internal readonly MessageAccess[] Messages;
	private void ValidateMessageAliases()
	{
		for (var i = 0; i < Messages.Length; i++)
			for (var j = i + 1; j < Messages.Length; j++)
				if (Messages[i].TypeKey == Messages[j].TypeKey &&
					Messages[i].Kind != MessageAccessKind.Removed && Messages[j].Kind != MessageAccessKind.Removed &&
					(Messages[i].Kind == MessageAccessKind.Writer || Messages[j].Kind == MessageAccessKind.Writer))
					throw new ArgumentException("Overlapping message writer parameters are not allowed.");
	}
	public sealed partial class Builder
	{
		internal readonly List<MessageAccess> Messages = [];
		public Builder MessageReader<T>() where T : unmanaged
		{
			Messages.Add(new MessageAccess(TypeKey<T>.Value, MessageAccessKind.Reader));
			return this;
		}
		public Builder MessageWriter<T>() where T : unmanaged
		{
			Messages.Add(new MessageAccess(TypeKey<T>.Value, MessageAccessKind.Writer));
			return this;
		}
		public Builder Removed<T>() where T : unmanaged
		{
			Messages.Add(new MessageAccess(TypeKey<T>.Value, MessageAccessKind.Removed));
			return this;
		}
	}
}

public readonly ref partial struct SystemBinder
{
	public MessageHandle<T> Message<T>(int index) where T : unmanaged
	{
		var instance = Instance;
		if ((uint)index >= (uint)instance.Descriptor.Messages.Length)
			throw new ArgumentOutOfRangeException(nameof(index));
		var access = instance.Descriptor.Messages[index];
		if (access.TypeKey != TypeKey<T>.Value || access.Kind == MessageAccessKind.Removed)
			throw new InvalidOperationException("The message parameter was not declared.");
		return new MessageHandle<T>(instance, instance.World.Message<T>(), access.Kind);
	}
	public RemovedHandle<T> Removed<T>(int index) where T : unmanaged
	{
		var instance = Instance;
		if ((uint)index >= (uint)instance.Descriptor.Messages.Length)
			throw new ArgumentOutOfRangeException(nameof(index));
		var access = instance.Descriptor.Messages[index];
		if (access.TypeKey != TypeKey<T>.Value || access.Kind != MessageAccessKind.Removed)
			throw new InvalidOperationException("The removal reader was not declared.");
		return new RemovedHandle<T>(instance, instance.World.RemovalChannel<T>());
	}
}

public sealed partial class World
{
	private readonly Dictionary<int, MessageChannel> _messages = [];
	private readonly Dictionary<int, MessageChannel<Removal>> _removals = [];
	private int _messageCapacity;

	internal void InitializeMessages(IEnumerable<MessageDescriptor> descriptors, int capacity)
	{
		_messageCapacity = capacity;
		foreach (var descriptor in descriptors)
			if (!_messages.ContainsKey(descriptor.TypeKey))
				_messages.Add(descriptor.TypeKey, descriptor.Create(capacity));
	}

	internal MessageChannel<T> Message<T>() where T : unmanaged =>
		_messages.TryGetValue(TypeKey<T>.Value, out var channel)
			? (MessageChannel<T>)channel
			: throw new InvalidOperationException("The message type is not registered.");

	internal MessageChannel<Removal> RemovalChannel<T>() where T : unmanaged
	{
		Registry.Id<T>();
		if (!_removals.TryGetValue(TypeKey<T>.Value, out var channel))
			_removals.Add(TypeKey<T>.Value, channel = new MessageChannel<Removal>(_messageCapacity));
		return channel;
	}

	internal void RecordRemoval(int componentKey, Entity entity, ulong tick)
	{
		if (_removals.TryGetValue(componentKey, out var channel))
			channel.Send(new Removal(entity, tick));
	}

	public void AdvanceMessages()
	{
		RequireIdle();
		foreach (var channel in _messages.Values)
			channel.Advance();
		foreach (var channel in _removals.Values)
			channel.Advance();
	}

	public void SendMessage<T>(in T message) where T : unmanaged
	{
		RequireIdle();
		Message<T>().Send(in message);
	}
}

public sealed class MessageHandle<T> where T : unmanaged
{
	private readonly SystemInstance _owner;
	private readonly MessageChannel<T> _channel;
	private readonly MessageCursor _cursor = new();
	private readonly MessageAccessKind _kind;
	internal MessageHandle(SystemInstance owner, MessageChannel<T> channel, MessageAccessKind kind)
	{
		_owner = owner;
		_channel = channel;
		_kind = kind;
	}
	public MessageReader<T> Read(SystemRun run)
	{
		run.Require(_owner);
		if (_kind != MessageAccessKind.Reader)
			throw new InvalidOperationException("Message reader access was not declared.");
		return new MessageReader<T>(_owner, run, _channel, _cursor);
	}
	public MessageWriter<T> Write(SystemRun run)
	{
		run.Require(_owner);
		if (_kind != MessageAccessKind.Writer)
			throw new InvalidOperationException("Message writer access was not declared.");
		return new MessageWriter<T>(_owner, run, _channel);
	}
}

public readonly ref struct MessageWriter<T> where T : unmanaged
{
	private readonly SystemInstance _owner;
	private readonly SystemRun _run;
	private readonly MessageChannel<T> _channel;
	internal MessageWriter(SystemInstance owner, SystemRun run, MessageChannel<T> channel)
	{
		_owner = owner;
		_run = run;
		_channel = channel;
	}
	public void Send(in T value)
	{
		_run.Require(_owner);
		_channel.Send(in value);
	}
}

public readonly ref struct MessageReader<T> where T : unmanaged
{
	private readonly SystemInstance _owner;
	private readonly SystemRun _run;
	private readonly MessageChannel<T> _channel;
	private readonly MessageCursor _cursor;
	public ulong MissedCount { get; }

	internal MessageReader(SystemInstance owner, SystemRun run, MessageChannel<T> channel, MessageCursor cursor)
	{
		_owner = owner;
		_run = run;
		_channel = channel;
		_cursor = cursor;
		MissedCount = cursor.Next < channel.Start ? channel.Start - cursor.Next : 0;
		if (cursor.Next < channel.Start)
			cursor.Next = channel.Start;
	}

	public Enumerator GetEnumerator()
	{
		_run.Require(_owner);
		return new Enumerator(_owner, _run, _channel, _cursor);
	}

	public ref struct Enumerator
	{
		private readonly SystemInstance _owner;
		private readonly SystemRun _run;
		private readonly ReadOnlySpan<T> _previous;
		private readonly ReadOnlySpan<T> _current;
		private readonly MessageCursor _cursor;
		private readonly ulong _start;
		private long _index;
		internal Enumerator(SystemInstance owner, SystemRun run, MessageChannel<T> channel, MessageCursor cursor)
		{
			_owner = owner;
			_run = run;
			_previous = channel.Previous;
			_current = channel.Current;
			_cursor = cursor;
			_start = channel.Start;
			_index = -1;
		}
		public bool MoveNext()
		{
			_run.Require(_owner);
			var next = _cursor.Next - _start;
			if (next >= (ulong)_previous.Length + (ulong)_current.Length)
			{
				_index = -1;
				return false;
			}
			_index = (long)next;
			_cursor.Next++;
			return true;
		}
		public ref readonly T Current
		{
			get
			{
				_run.Require(_owner);
				if (_index < 0)
					throw new InvalidOperationException("The reader is not positioned on a message.");
				if (_index < _previous.Length)
					return ref _previous[(int)_index];
				return ref _current[(int)(_index - _previous.Length)];
			}
		}
	}
}

public sealed class RemovedHandle<T> where T : unmanaged
{
	private readonly SystemInstance _owner;
	private readonly MessageChannel<Removal> _channel;
	private readonly MessageCursor _cursor = new();
	internal RemovedHandle(SystemInstance owner, MessageChannel<Removal> channel)
	{
		_owner = owner;
		_channel = channel;
	}
	public RemovedComponents<T> Read(SystemRun run)
	{
		run.Require(_owner);
		return new RemovedComponents<T>(new MessageReader<Removal>(_owner, run, _channel, _cursor));
	}
}

public readonly ref struct RemovedComponents<T> where T : unmanaged
{
	private readonly MessageReader<Removal> _reader;
	internal RemovedComponents(MessageReader<Removal> reader) => _reader = reader;
	public ulong MissedCount => _reader.MissedCount;
	public MessageReader<Removal>.Enumerator GetEnumerator() => _reader.GetEnumerator();
}
