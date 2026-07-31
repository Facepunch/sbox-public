namespace Sandbox;

/// <summary>
/// Configures a model hitbox.
/// </summary>
public sealed class ModelHitboxBuilder
{
	internal enum ShapeType
	{
		Box,
		Sphere,
		Capsule
	}

	/// <summary>
	/// The hitbox name.
	/// </summary>
	public string Name { get; set; }

	/// <summary>
	/// The name of the bone the hitbox follows.
	/// </summary>
	public string BoneName { get; set; }

	/// <summary>
	/// The physics surface name applied to the hitbox.
	/// </summary>
	public string SurfaceName { get; set; }

	/// <summary>
	/// The hitbox tags.
	/// </summary>
	public IReadOnlyList<string> Tags => _tags;

	internal ShapeType Shape { get; set; }
	internal Vector3 Min { get; set; }
	internal Vector3 Max { get; set; }
	internal float Radius { get; set; }

	private readonly List<string> _tags = [];

	internal ModelHitboxBuilder()
	{
	}

	/// <inheritdoc cref="Name"/>
	/// <param name="name">The hitbox name.</param>
	public ModelHitboxBuilder WithName( string name )
	{
		ArgumentException.ThrowIfNullOrWhiteSpace( name );
		Name = name;
		return this;
	}

	/// <inheritdoc cref="BoneName"/>
	/// <param name="boneName">The bone name.</param>
	public ModelHitboxBuilder WithBone( string boneName )
	{
		ArgumentException.ThrowIfNullOrWhiteSpace( boneName );
		BoneName = boneName;
		return this;
	}

	/// <inheritdoc cref="SurfaceName"/>
	/// <param name="surfaceName">The surface name.</param>
	public ModelHitboxBuilder WithSurface( string surfaceName )
	{
		SurfaceName = surfaceName;
		return this;
	}

	/// <summary>
	/// Adds a tag to the hitbox.
	/// </summary>
	/// <param name="tag">The tag to add.</param>
	public ModelHitboxBuilder AddTag( string tag )
	{
		ArgumentException.ThrowIfNullOrWhiteSpace( tag );

		if ( _tags.Contains( tag ) )
			return this;

		if ( _tags.Count >= 16 )
			throw new InvalidOperationException( "A hitbox cannot have more than 16 tags." );

		_tags.Add( tag );
		return this;
	}

	/// <summary>
	/// Adds tags to the hitbox.
	/// </summary>
	/// <param name="tags">The tags to add.</param>
	public ModelHitboxBuilder AddTags( params string[] tags )
	{
		ArgumentNullException.ThrowIfNull( tags );

		foreach ( var tag in tags )
		{
			AddTag( tag );
		}

		return this;
	}
}
