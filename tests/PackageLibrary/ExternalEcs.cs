using Archevolt;

namespace Archevolt.ConsumerLibrary;

[Component]
public readonly record struct ExternalPosition(float Value);

[Bundle]
public readonly partial record struct ExternalParticle(ExternalPosition Position);

[Query, Write<ExternalPosition>]
public readonly ref partial struct ExternalMoving;

public static partial class ExternalSystems
{
    [EcsSystem]
    public static void Increment(scoped ExternalMoving moving)
    {
        foreach (var row in moving)
        {
            ref var position = ref row.ExternalPosition;
            position = new ExternalPosition(position.Value + 1);
        }
    }
}

[EcsModule, IncludeSystems(typeof(ExternalSystems)), IncludeBundle<ExternalParticle>]
public static partial class ExternalEcs;
