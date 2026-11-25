namespace Sandbox;

/// <summary>
/// Builds a named set of model hitboxes.
/// </summary>
public sealed class HitboxSetBuilder
{
	/// <summary>
	/// The name of the hitbox set.
	/// </summary>
	public string Name { get; internal set; }

	internal List<ModelHitboxBuilder> Hitboxes { get; } = [];

	internal HitboxSetBuilder()
	{
	}

	/// <inheritdoc cref="Name"/>
	/// <param name="name">The hitbox set name.</param>
	public HitboxSetBuilder WithName( string name )
	{
		ArgumentException.ThrowIfNullOrWhiteSpace( name );
		Name = name;
		return this;
	}

	/// <summary>
	/// Adds a box hitbox in bone-local space.
	/// </summary>
	/// <param name="name">The hitbox name.</param>
	/// <param name="boneName">The bone the hitbox follows.</param>
	/// <param name="bounds">The box bounds relative to the bone.</param>
	/// <returns>A builder for the new hitbox.</returns>
	public ModelHitboxBuilder AddBox( string name, string boneName, BBox bounds )
	{
		return AddHitbox( name, boneName, ModelHitboxBuilder.ShapeType.Box, bounds.Mins, bounds.Maxs, 0.0f );
	}

	/// <summary>
	/// Adds a sphere hitbox in bone-local space.
	/// </summary>
	/// <param name="name">The hitbox name.</param>
	/// <param name="boneName">The bone the hitbox follows.</param>
	/// <param name="sphere">The sphere relative to the bone.</param>
	/// <returns>A builder for the new hitbox.</returns>
	public ModelHitboxBuilder AddSphere( string name, string boneName, Sphere sphere )
	{
		ArgumentOutOfRangeException.ThrowIfNegative( sphere.Radius );
		return AddHitbox( name, boneName, ModelHitboxBuilder.ShapeType.Sphere, sphere.Center, sphere.Center, sphere.Radius );
	}

	/// <summary>
	/// Adds a capsule hitbox in bone-local space.
	/// </summary>
	/// <param name="name">The hitbox name.</param>
	/// <param name="boneName">The bone the hitbox follows.</param>
	/// <param name="capsule">The capsule relative to the bone.</param>
	/// <returns>A builder for the new hitbox.</returns>
	public ModelHitboxBuilder AddCapsule( string name, string boneName, Capsule capsule )
	{
		ArgumentOutOfRangeException.ThrowIfNegative( capsule.Radius );
		return AddHitbox( name, boneName, ModelHitboxBuilder.ShapeType.Capsule, capsule.CenterA, capsule.CenterB, capsule.Radius );
	}

	/// <summary>
	/// Adds a convex hull hitbox from at least four non-coplanar points in bone-local space.
	/// </summary>
	public ModelHitboxBuilder AddHull( string name, string boneName, ReadOnlySpan<Vector3> points )
	{
		if ( points.Length < 4 )
			throw new ArgumentException( "A convex hull needs at least four points.", nameof( points ) );

		if ( !HasVolume( points ) )
			throw new ArgumentException( "A convex hull needs finite points that span a volume.", nameof( points ) );

		var hitbox = AddHitbox( name, boneName, ModelHitboxBuilder.ShapeType.Hull, default, default, 0 );
		hitbox.Points = points.ToArray();
		return hitbox;
	}

	// Choose a long edge and a wide triangle so duplicate or nearly collinear
	// leading points don't determine the plane used for the volume check.
	private static bool HasVolume( ReadOnlySpan<Vector3> points )
	{
		var origin = points[0];
		var edge = Vector3.Zero;
		foreach ( var point in points )
		{
			if ( !point.IsFinite ) return false;
			var offset = point - origin;
			if ( offset.LengthSquared > edge.LengthSquared ) edge = offset;
		}

		var normal = Vector3.Zero;
		foreach ( var point in points )
		{
			var cross = Vector3.Cross( edge, point - origin );
			if ( cross.LengthSquared > normal.LengthSquared ) normal = cross;
		}

		float tolerance = normal.Length * edge.Length * 1e-6f;
		foreach ( var point in points )
		{
			if ( MathF.Abs( Vector3.Dot( normal, point - origin ) ) > tolerance ) return true;
		}

		return false;
	}

	private ModelHitboxBuilder AddHitbox( string name, string boneName, ModelHitboxBuilder.ShapeType shape, Vector3 min, Vector3 max, float radius )
	{
		ArgumentException.ThrowIfNullOrWhiteSpace( name );
		ArgumentException.ThrowIfNullOrWhiteSpace( boneName );

		var builder = new ModelHitboxBuilder
		{
			Name = name,
			BoneName = boneName,
			Shape = shape,
			Min = min,
			Max = max,
			Radius = radius
		};

		Hitboxes.Add( builder );
		return builder;
	}
}
