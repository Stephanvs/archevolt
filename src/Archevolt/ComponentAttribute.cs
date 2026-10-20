namespace Archevolt;

[AttributeUsage(AttributeTargets.Struct, Inherited = false)]
public sealed class ComponentAttribute : Attribute;

[AttributeUsage(AttributeTargets.Struct | AttributeTargets.Class)]
public sealed class TrackChangesAttribute : Attribute;

[AttributeUsage(AttributeTargets.Struct | AttributeTargets.Class)]
public sealed class ResourceAttribute : Attribute
{
	public bool OwnerThread { get; init; }
}

[AttributeUsage(AttributeTargets.Struct)] public sealed class BundleAttribute : Attribute;
[AttributeUsage(AttributeTargets.Struct)] public sealed class QueryAttribute : Attribute;
[AttributeUsage(AttributeTargets.Struct)] public sealed class MessageAttribute : Attribute;
[AttributeUsage(AttributeTargets.Struct, AllowMultiple = true)]
public sealed class ReadAttribute<T> : Attribute { public string? Name { get; init; } }
[AttributeUsage(AttributeTargets.Struct, AllowMultiple = true)]
public sealed class WriteAttribute<T> : Attribute { public string? Name { get; init; } }
[AttributeUsage(AttributeTargets.Struct, AllowMultiple = true)]
public sealed class OptionalAttribute<T> : Attribute { public string? Name { get; init; } }
[AttributeUsage(AttributeTargets.Struct | AttributeTargets.Method, AllowMultiple = true)]
public sealed class WithAttribute<T> : Attribute;
[AttributeUsage(AttributeTargets.Struct | AttributeTargets.Method, AllowMultiple = true)]
public sealed class WithoutAttribute<T> : Attribute;
[AttributeUsage(AttributeTargets.Struct | AttributeTargets.Method, AllowMultiple = true)]
public sealed class AddedAttribute<T> : Attribute;
[AttributeUsage(AttributeTargets.Struct | AttributeTargets.Method, AllowMultiple = true)]
public sealed class ChangedAttribute<T> : Attribute;
[AttributeUsage(AttributeTargets.Method)] public sealed class EcsSystemAttribute : Attribute;
[AttributeUsage(AttributeTargets.Method)]
public sealed class EachAttribute : Attribute
{
	public bool Partitioned { get; init; }
}
[AttributeUsage(AttributeTargets.Method)] public sealed class RunConditionAttribute : Attribute;
[AttributeUsage(AttributeTargets.Class)] public sealed class EcsModuleAttribute : Attribute;
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class IncludeSystemsAttribute(Type owner) : Attribute { public Type Owner { get; } = owner; }
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class IncludeBundleAttribute<T> : Attribute;
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class IncludeResourceAttribute<T> : Attribute;
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class IncludeComponentAttribute<T> : Attribute;
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class IncludeMessageAttribute<T> : Attribute;
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class IncludeModuleAttribute<T> : Attribute;
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class IncludeModuleAttribute(Type module) : Attribute
{
	public Type Module { get; } = module;
}
[AttributeUsage(AttributeTargets.Class)] public sealed class ScheduleAttribute : Attribute;
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class StageAttribute(string name) : Attribute
{
	public string Name { get; } = name;
	public int Order { get; init; }
}
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class SystemSetAttribute(string name) : Attribute
{
	public string Name { get; } = name;
	public int Order { get; init; }
}
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class RunAttribute(Type owner, string method) : Attribute
{
	public Type Owner { get; } = owner;
	public string Method { get; } = method;
	public int Order { get; init; }
	public string? Instance { get; init; }
	public string? Stage { get; init; }
	public string? Set { get; init; }
	public Type? BeforeOwner { get; init; }
	public string? Before { get; init; }
	public Type? AfterOwner { get; init; }
	public string? After { get; init; }
	public Type? ConditionOwner { get; init; }
	public string? Condition { get; init; }
}
