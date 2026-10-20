using AwesomeAssertions;

namespace Archevolt.Tests;

[Trait("Category", "Components")]
public sealed class ComponentAttributeTests
{
	[Component]
	readonly record struct Position(System.Numerics.Vector3 Value);

	[Fact]
	public void Marks_a_record_struct_under_the_metadata_name_the_generator_matches()
	{
		var marker = typeof(Position).GetCustomAttributes(inherit: false).OfType<ComponentAttribute>().Should().ContainSingle().Subject;

		marker.GetType().FullName.Should().Be("Archevolt.ComponentAttribute");
	}
}
