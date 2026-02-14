using System.Runtime.InteropServices;

namespace Sandbox;

/// <summary>
/// Defines a box, cone, or cylinder hull collider.
/// </summary>
[Expose]
[Title( "Collider - Hull" )]
[Category( "Physics" )]
[Icon( "grain" )]
[Alias( "ColliderHullComponent" )]
public sealed class HullCollider : Collider
{
	public enum PrimitiveType
	{
		Box,
		Cone,
		Cylinder,

		[Hide]
		Points,
	}

	/// <summary>
	/// The type of primitive.
	/// </summary>
	[Property, Group( "Hull" ), Resize]
	public PrimitiveType Type { get; set; }

	/// <summary>
	/// The center of the primitive relative to this GameObject.
	/// </summary>
	[Property, Group( "Hull" ), Resize]
	public Vector3 Center { get; set; } = 0.0f;

	/// <summary>
	/// The size of the box, from corner to corner.
	/// </summary>
	[Property, Title( "Size" ), Group( "Hull" ), Resize]
	[ShowIf( nameof( Type ), PrimitiveType.Box )]
	public Vector3 BoxSize { get; set; } = 50.0f;

	[Property, Title( "Height" ), Group( "Hull" ), Resize]
	[ShowIf( nameof( Type ), PrimitiveType.Cone )]
	[ShowIf( nameof( Type ), PrimitiveType.Cylinder )]
	public float Height { get; set; } = 50.0f;

	[Property, Title( "Radius" ), Group( "Hull" ), Resize]
	[ShowIf( nameof( Type ), PrimitiveType.Cone )]
	[ShowIf( nameof( Type ), PrimitiveType.Cylinder )]
	public float Radius { get; set; } = 25.0f;

	[Property, Title( "Tip Radius" ), Group( "Hull" ), Resize]
	[ShowIf( nameof( Type ), PrimitiveType.Cone )]
	public float Radius2 { get; set; } = 0.0f;

	[Property, Title( "Slices" ), Group( "Hull" ), Resize, Range( 4, 32 )]
	[ShowIf( nameof( Type ), PrimitiveType.Cone )]
	[ShowIf( nameof( Type ), PrimitiveType.Cylinder )]
	public int Slices { get; set; } = 16;

	[Property, Hide, Resize]
	public List<Vector3> Points { get; set; } = new();

	private PhysicsShape Shape;

	protected override void DrawGizmos()
	{
		if ( !Gizmo.IsSelected && !Gizmo.IsHovered )
			return;

		Gizmo.Draw.LineThickness = 1;
		Gizmo.Draw.Color = Gizmo.Colors.Green.WithAlpha( Gizmo.IsSelected ? 1.0f : 0.2f );

		if ( Type == PrimitiveType.Box )
		{
			var box = BBox.FromPositionAndSize( Center, BoxSize );

			Gizmo.Draw.LineBBox( box );
		}
		else if ( Type == PrimitiveType.Cylinder )
		{
			var halfHeight = Height * 0.5f;

			Gizmo.Draw.LineCylinder(
				Center + Vector3.Down * halfHeight,
				Center + Vector3.Up * halfHeight,
				Radius, Radius, Slices );
		}
		else if ( Type == PrimitiveType.Cone )
		{
			var halfHeight = Height * 0.5f;

			Gizmo.Draw.LineCylinder(
				Center + Vector3.Down * halfHeight,
				Center + Vector3.Up * halfHeight,
				Radius, Radius2, Slices );
		}
	}

	private Vector3[] GetVertices( float height, float radius1, float radius2, int slices, Vector3 center, Vector3 scale )
	{
		slices = slices.Clamp( 4, 128 );

		var vertexCount = 2 * slices;
		var points = new Vector3[vertexCount];

		var alpha = 0.0f;
		var deltaAlpha = MathF.PI * 2 / slices;
		var halfHeight = height * 0.5f;

		for ( int i = 0; i < slices; ++i )
		{
			var sinAlpha = MathF.Sin( alpha );
			var cosAlpha = MathF.Cos( alpha );

			var a = center + new Vector3( radius1 * cosAlpha, radius1 * sinAlpha, -halfHeight );
			var b = center + new Vector3( radius2 * cosAlpha, radius2 * sinAlpha, halfHeight );

			points[2 * i + 0] = a * scale;
			points[2 * i + 1] = b * scale;

			alpha += deltaAlpha;
		}

		return points;
	}

	internal override void UpdateShape()
	{
		if ( !Shape.IsValid() )
			return;

		var body = Rigidbody;
		var world = Transform.TargetWorld;
		var local = body.IsValid() ? body.Transform.TargetWorld.WithScale( 1.0f ).ToLocal( world ) : global::Transform.Zero;

		if ( Type == PrimitiveType.Box )
		{
			var box = BBox.FromPositionAndSize( Center, BoxSize );
			box.Mins *= world.Scale;
			box.Maxs *= world.Scale;
			box.Mins += local.Position;
			box.Maxs += local.Position;
			Shape.UpdateBoxShape( box.Center, local.Rotation, box.Size * 0.5f );
		}
		else if ( Type == PrimitiveType.Cone )
		{
			var vertices = GetVertices( Height, Radius, Radius2, Slices, Center, world.Scale );
			Shape.UpdateHull( local.Position, local.Rotation, vertices );
		}
		else if ( Type == PrimitiveType.Cylinder )
		{
			var vertices = GetVertices( Height, Radius, Radius, Slices, Center, world.Scale );
			Shape.UpdateHull( local.Position, local.Rotation, vertices );
		}
		else if ( Type == PrimitiveType.Points )
		{
			Shape.UpdateHull( local.Position, local.Rotation, Points.Select( x => x * world.Scale ).ToArray() );
		}

		CalculateLocalBounds();
	}

	protected override IEnumerable<PhysicsShape> CreatePhysicsShapes( PhysicsBody body, Transform local )
	{
		var scale = WorldScale;

		Shape = null;

		if ( Type == PrimitiveType.Box )
		{
			var box = BBox.FromPositionAndSize( Center, BoxSize );
			box.Mins *= scale;
			box.Maxs *= scale;
			box.Mins += local.Position;
			box.Maxs += local.Position;
			Shape = body.AddBoxShape( box, local.Rotation );
		}
		else if ( Type == PrimitiveType.Cone )
		{
			var vertices = GetVertices( Height, Radius, Radius2, Slices, Center, scale );
			Shape = body.AddHullShape( local.Position, local.Rotation, vertices );
		}
		else if ( Type == PrimitiveType.Cylinder )
		{
			var vertices = GetVertices( Height, Radius, Radius, Slices, Center, scale );
			Shape = body.AddHullShape( local.Position, local.Rotation, vertices );
		}
		else if ( Type == PrimitiveType.Points )
		{
			Shape = body.AddHullShape( local.Position, local.Rotation, Points.Select( x => x * scale ).ToArray() );
		}
		else
		{
			yield break;
		}

		yield return Shape;
	}
}
