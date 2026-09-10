namespace Sandbox;

/// <summary>
/// Builds a named material variant for a <see cref="Model"/>.
/// </summary>
public sealed class MaterialGroupBuilder
{
	/// <summary>
	/// The name of the material group.
	/// </summary>
	public string Name { get; internal set; }

	internal List<Material> Materials = [];

	/// <inheritdoc cref="Name"/>
	/// <param name="name">The material group name.</param>
	public MaterialGroupBuilder WithName( string name )
	{
		Name = name;
		return this;
	}

	/// <summary>
	/// Adds a material to the group.
	/// </summary>
	/// <param name="material">The material to add.</param>
	public MaterialGroupBuilder AddMaterial( Material material )
	{
		Materials.Add( material );
		return this;
	}

	/// <summary>
	/// Adds multiple materials to the group.
	/// </summary>
	/// <param name="materials">The materials to add.</param>
	public MaterialGroupBuilder AddMaterials( Span<Material> materials )
	{
		Materials.AddRange( materials );
		return this;
	}

	internal MaterialGroupBuilder()
	{
	}
}
