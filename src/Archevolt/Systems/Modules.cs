namespace Archevolt;

public sealed partial class ModuleDescriptor
{
    internal readonly ComponentDescriptor[] Components;
    internal readonly BundleDescriptor[] Bundles;
    internal readonly ResourceDescriptor[] Resources;
    internal readonly SystemDescriptor[] Systems;
    public string Name { get; }
    public int AbiVersion { get; }

    internal ModuleDescriptor(ModuleBuilder builder)
    {
        Name = builder.Name;
        AbiVersion = builder.AbiVersion;
        Components = builder.Components.ToArray();
        Bundles = builder.Bundles.ToArray();
        Resources = builder.Resources.ToArray();
        Messages = builder.Messages.ToArray();
        Systems = builder.Systems.ToArray();
    }
}

public static class RuntimeAbi
{
    public const int Version = 1;
}

public sealed partial class ModuleBuilder
{
    internal readonly string Name;
    internal readonly int AbiVersion;
    internal readonly List<ComponentDescriptor> Components = [];
    internal readonly List<BundleDescriptor> Bundles = [];
    internal readonly List<ResourceDescriptor> Resources = [];
    internal readonly List<SystemDescriptor> Systems = [];

    public ModuleBuilder(string name, int abiVersion = RuntimeAbi.Version)
    {
        if (abiVersion != RuntimeAbi.Version)
            throw new ArgumentException("The generated module uses an incompatible Archevolt ABI.", nameof(abiVersion));
        Name = name;
        AbiVersion = abiVersion;
    }

    public ModuleBuilder Component<T>(string? name = null, bool tracked = false) where T : unmanaged
    {
        Components.Add(ComponentDescriptor.Of<T>(name, tracked));
        return this;
    }

    public ModuleBuilder Tag<T>(string? name = null, bool tracked = false) where T : unmanaged
    {
        Components.Add(ComponentDescriptor.Of<T>(name, tracked, tag: true));
        return this;
    }

    public ModuleBuilder Resource<T>(string? name = null, bool tracked = false, bool ownerThread = false) where T : unmanaged
    {
        Resources.Add(ResourceDescriptor.Of<T>(tracked, ownerThread));
        return this;
    }

    public ModuleBuilder ManagedResource<T>(string? name = null, bool tracked = false) where T : class
    {
        Resources.Add(ResourceDescriptor.OfManaged<T>(tracked));
        return this;
    }

    public ModuleBuilder Bundle<TBundle>(string? name = null) where TBundle : unmanaged, IBundle<TBundle>
    {
        Bundles.Add(TBundle.Descriptor);
        Components.AddRange(TBundle.Descriptor.Components);
        return this;
    }

    public ModuleBuilder System(SystemDescriptor system) { Systems.Add(system); return this; }

    public ModuleBuilder Include(ModuleDescriptor module)
    {
        Components.AddRange(module.Components);
        Bundles.AddRange(module.Bundles);
        Resources.AddRange(module.Resources);
        Messages.AddRange(module.Messages);
        Systems.AddRange(module.Systems);
        return this;
    }

    public ModuleDescriptor Build() => new(this);
}
