/// <summary>
/// Marks a <see cref="BuilderTool"/> setting as a shape parameter. Its row is hidden unless the selected shape's
/// <see cref="BuilderShapeDefinition"/> declares a parameter called <see cref="Name"/>.
/// </summary>
[AttributeUsage( AttributeTargets.Property )]
public sealed class BuilderParameterAttribute : InspectorVisibilityAttribute
{
	public string Name { get; }

	public BuilderParameterAttribute( string name )
	{
		Name = name;
	}

	/// <summary>True hides the row.</summary>
	public override bool TestCondition( SerializedObject so )
	{
		if ( so is null || !so.TryGetProperty( nameof( BuilderTool.Shape ), out var shape ) ) return false;

		var definition = BuilderShapes.Get( shape.GetValue<BuilderCatalog.Shape>() );
		return definition is null || definition.IndexOf( Name ) < 0;
	}
}
