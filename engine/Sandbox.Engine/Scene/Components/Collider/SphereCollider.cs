using System.Buffers;

namespace Sandbox;

/// <summary>
/// Defines a sphere collider.
/// </summary>
[Expose]
[Title( "Collider - Sphere" )]
[Category( "Physics" )]
[Icon( "panorama_fish_eye" )]
[Alias( "SphereColliderComponent", "ColliderSphereComponent" )]
public sealed class SphereCollider : Collider
{
	[Property, Group( "Sphere" ), Resize]
	public Vector3 Center { get; set; } = 0.0f;

	[Property, Group( "Sphere" ), Resize]
	public float Radius { get; set; } = 32.0f;

	private PhysicsShape Shape;

	protected override void DrawGizmos()
	{
		if ( !Gizmo.IsSelected && !Gizmo.IsHovered )
			return;

		using ( Gizmo.Scope( "SphereCollider" ) )
		{
			Gizmo.Draw.Color = Gizmo.Colors.Green.WithAlpha( Gizmo.IsChildSelected ? 0.5f : 0.1f );
			Gizmo.Draw.LineSphere( new Sphere( Center, Radius ) );
		}
	}

	private static void GenerateSphere( int rings, Vector3 center, float radius, Vector3 scale, Vector3[] points )
	{
		var index = 0;

		for ( var i = 0; i < rings; ++i )
		{
			for ( var j = 0; j < rings; ++j )
			{
				var u = j / (float)(rings - 1);
				var v = i / (float)(rings - 1);
				var t = 2.0f * MathF.PI * u;
				var p = MathF.PI * v;

				var point = new Vector3( center.x + (radius * MathF.Sin( p ) * MathF.Cos( t )),
					center.y + (radius * MathF.Sin( p ) * MathF.Sin( t )),
					center.z + (radius * MathF.Cos( p )) );

				points[index] = point * scale;

				++index;
			}
		}
	}

	internal override void UpdateShape()
	{
		if ( !Shape.IsValid() )
			return;

		var body = Rigidbody;
		var world = Transform.TargetWorld;
		var local = body.IsValid() ? body.Transform.TargetWorld.WithScale( 1.0f ).ToLocal( world ) : global::Transform.Zero;
		var scale = world.Scale;

		if ( scale.x.AlmostEqual( scale.y ) && scale.y.AlmostEqual( scale.z ) )
		{
			if ( !Shape.IsSphereShape )
			{
				Rebuild();

				return;
			}

			var radius = Radius * world.UniformScale;
			Shape.native.UpdateSphereShape( local.PointToWorld( Center ), radius );
		}
		else
		{
			if ( !Shape.IsHullShape )
			{
				Rebuild();

				return;
			}

			scale.x = MathF.Sign( scale.x ) * MathF.Max( 0.01f, MathF.Abs( scale.x ) );
			scale.y = MathF.Sign( scale.y ) * MathF.Max( 0.01f, MathF.Abs( scale.y ) );
			scale.z = MathF.Sign( scale.z ) * MathF.Max( 0.01f, MathF.Abs( scale.z ) );

			const int rings = 8;
			var points = ArrayPool<Vector3>.Shared.Rent( rings * rings );
			GenerateSphere( rings, Center, Radius, scale, points );

			Shape.UpdateHull( local.Position, local.Rotation, points );

			ArrayPool<Vector3>.Shared.Return( points );
		}

		CalculateLocalBounds();
	}

	protected override IEnumerable<PhysicsShape> CreatePhysicsShapes( PhysicsBody targetBody, Transform local )
	{
		var scale = WorldScale;

		if ( scale.x.AlmostEqual( scale.y ) && scale.y.AlmostEqual( scale.z ) )
		{
			var radius = Radius * scale.x;
			var sphere = new Sphere( local.PointToWorld( Center ), radius );
			Shape = targetBody.AddSphereShape( sphere );
		}
		else
		{
			scale.x = MathF.Sign( scale.x ) * MathF.Max( 0.01f, MathF.Abs( scale.x ) );
			scale.y = MathF.Sign( scale.y ) * MathF.Max( 0.01f, MathF.Abs( scale.y ) );
			scale.z = MathF.Sign( scale.z ) * MathF.Max( 0.01f, MathF.Abs( scale.z ) );

			const int rings = 8;
			var points = ArrayPool<Vector3>.Shared.Rent( rings * rings );
			GenerateSphere( rings, Center, Radius, scale, points );

			Shape = targetBody.AddHullShape( local.Position, local.Rotation, points );

			ArrayPool<Vector3>.Shared.Return( points );
		}

		yield return Shape;
	}
}
