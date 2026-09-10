namespace Sandbox;

public sealed partial class ModelBuilder
{
	private readonly List<MaterialGroupBuilder> _materialGroups = [];

	/// <summary>
	/// Adds a named material variant and returns a builder for its materials.
	/// </summary>
	/// <param name="name">The material group name.</param>
	/// <returns>A builder for the new material group.</returns>
	public MaterialGroupBuilder AddMaterialGroup( string name )
	{
		var builder = new MaterialGroupBuilder { Name = name };
		_materialGroups.Add( builder );
		return builder;
	}

	private void AddMaterialGroups( NativeEngine.ModelBuilder modelBuilder )
	{
		foreach ( var source in _materialGroups )
		{
			var destination = modelBuilder.AddMaterialGroup();
			destination.SetName( source.Name );

			foreach ( var material in source.Materials )
			{
				if ( !material.native.IsNull )
					destination.AddMaterial( material.native );
			}
		}
	}
}
