namespace Sandbox;

public sealed partial class ModelBuilder
{
	private readonly List<HitboxSetBuilder> _hitboxSets = [];

	/// <summary>
	/// Adds a named hitbox set.
	/// </summary>
	/// <param name="name">The hitbox set name.</param>
	/// <returns>A builder for the new hitbox set.</returns>
	public HitboxSetBuilder AddHitboxSet( string name = "default" )
	{
		ArgumentException.ThrowIfNullOrWhiteSpace( name );

		var builder = new HitboxSetBuilder { Name = name };
		_hitboxSets.Add( builder );
		return builder;
	}

	private void ApplyHitboxes( NativeEngine.ModelBuilder modelBuilder )
	{
		foreach ( var set in _hitboxSets )
		{
			foreach ( var source in set.Hitboxes )
			{
				var destination = modelBuilder.AddHitBox( set.Name, source.Name );
				destination.SetBoneName( source.BoneName );
				destination.SetSurface( source.SurfaceName ?? string.Empty );

				foreach ( var tag in source.Tags )
				{
					destination.AddTag( tag );
				}

				switch ( source.Shape )
				{
					case ModelHitboxBuilder.ShapeType.Box:
						destination.SetBox( source.Min, source.Max );
						break;

					case ModelHitboxBuilder.ShapeType.Sphere:
						destination.SetSphere( source.Min, source.Radius );
						break;

					case ModelHitboxBuilder.ShapeType.Capsule:
						destination.SetCapsule( source.Min, source.Max, source.Radius );
						break;
				}
			}
		}
	}
}
