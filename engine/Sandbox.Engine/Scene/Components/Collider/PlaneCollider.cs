namespace Sandbox;

/// <summary>
/// Defines a plane collider.
/// </summary>
[Expose]
[Title( "Collider - Plane" )]
[Category( "Physics" )]
[Icon( "check_box_outline_blank" )]
[Alias( "ColliderPlaneComponent" )]
public sealed class PlaneCollider : Collider
{
	/// <summary>
	/// The size of the plane, from corner to corner.
	/// </summary>
	[Property, Title( "Size" ), Group( "Plane" ), Resize]
	public Vector2 Scale { get; set; } = 50.0f;

	/// <summary>
	/// The center of the plane relative to this GameObject.
	/// </summary>
	[Property, Group( "Plane" ), Resize]
	public Vector3 Center { get; set; } = 0.0f;

	/// <summary>
	/// The normal of the plane, determining its orientation.
	/// </summary>
	[Property, Title( "Normal" ), Group( "Plane" ), Normal]
	public Vector3 Normal { get; set; } = Vector3.Up;

	private PhysicsShape Shape;
	private static readonly int[] Indices = [0, 1, 2, 2, 3, 0];

	public override bool IsConcave => true;

	protected override void DrawGizmos()
	{
		if ( !Gizmo.IsSelected && !Gizmo.IsHovered )
			return;

		Gizmo.Transform = Gizmo.Transform.WithScale( 1.0f );

		var vertices = GetVertices( global::Transform.Zero );

		Gizmo.Draw.LineThickness = 1;
		Gizmo.Draw.CullBackfaces = true;
		Gizmo.Draw.Color = Gizmo.Colors.Green.WithAlpha( Gizmo.IsSelected ? 0.2f : 0.1f );
		Gizmo.Draw.SolidTriangle( vertices[0], vertices[1], vertices[2] );
		Gizmo.Draw.SolidTriangle( vertices[2], vertices[3], vertices[0] );

		Gizmo.Draw.Color = Gizmo.Colors.Green.WithAlpha( Gizmo.IsSelected ? 1.0f : 0.6f );
		Gizmo.Draw.Line( vertices[0], vertices[1] );
		Gizmo.Draw.Line( vertices[1], vertices[2] );
		Gizmo.Draw.Line( vertices[2], vertices[3] );
		Gizmo.Draw.Line( vertices[3], vertices[0] );
	}

	private Vector3[] GetVertices( Transform local )
	{
		var n = Normal.LengthSquared > 1e-12f ? Normal.Normal : Vector3.Up;
		var rot = Rotation.LookAt( n );

		var tangent = rot.Right;
		var bitangent = rot.Down;

		var center = Center * WorldScale;
		var halfX = 0.5f * Scale.x * WorldScale.x;
		var halfY = 0.5f * Scale.y * WorldScale.y;

		var v0 = center - tangent * halfX - bitangent * halfY;
		var v1 = center + tangent * halfX - bitangent * halfY;
		var v2 = center + tangent * halfX + bitangent * halfY;
		var v3 = center - tangent * halfX + bitangent * halfY;

		var vertices = new Vector3[] { v0, v1, v2, v3 };

		for ( int i = 0; i < 4; i++ )
			vertices[i] = (vertices[i] * local.Rotation) + local.Position;

		return vertices;
	}

	internal override void UpdateShape()
	{
		if ( !Shape.IsValid() )
			return;

		var body = Rigidbody;
		var world = Transform.TargetWorld;
		var local = body.IsValid() ? body.Transform.TargetWorld.WithScale( 1.0f ).ToLocal( world ) : global::Transform.Zero;

		var vertices = GetVertices( local );
		Shape.UpdateMesh( vertices, Indices );

		CalculateLocalBounds();
	}

	protected override IEnumerable<PhysicsShape> CreatePhysicsShapes( PhysicsBody targetBody, Transform local )
	{
		var vertices = GetVertices( local );
		var shape = targetBody.AddMeshShape( vertices, Indices );

		Shape = shape;

		yield return shape;
	}
}
