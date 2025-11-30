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
	
	private readonly Vector3[] _vertices = new Vector3[4];

	public override bool IsConcave => true;

	protected override void DrawGizmos()
	{
		if ( !Gizmo.IsSelected && !Gizmo.IsHovered )
			return;

		Gizmo.Transform = Gizmo.Transform.WithScale( 1.0f );

		UpdateVerticesBuffer( global::Transform.Zero );

		Gizmo.Draw.LineThickness = 1;
		Gizmo.Draw.CullBackfaces = true;
		Gizmo.Draw.Color = Gizmo.Colors.Green.WithAlpha( Gizmo.IsSelected ? 0.2f : 0.1f );
		Gizmo.Draw.SolidTriangle( _vertices[0], _vertices[1], _vertices[2] );
		Gizmo.Draw.SolidTriangle( _vertices[2], _vertices[3], _vertices[0] );

		Gizmo.Draw.Color = Gizmo.Colors.Green.WithAlpha( Gizmo.IsSelected ? 1.0f : 0.6f );
		Gizmo.Draw.Line( _vertices[0], _vertices[1] );
		Gizmo.Draw.Line( _vertices[1], _vertices[2] );
		Gizmo.Draw.Line( _vertices[2], _vertices[3] );
		Gizmo.Draw.Line( _vertices[3], _vertices[0] );
	}

	private void UpdateVerticesBuffer( Transform local )
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

		_vertices[0] = (v0 * local.Rotation) + local.Position;
		_vertices[1] = (v1 * local.Rotation) + local.Position;
		_vertices[2] = (v2 * local.Rotation) + local.Position;
		_vertices[3] = (v3 * local.Rotation) + local.Position;
	}

	internal override void UpdateShape()
	{
		if ( !Shape.IsValid() )
			return;

		var body = Rigidbody;
		var world = Transform.TargetWorld;
		var local = body.IsValid() ? body.Transform.TargetWorld.ToLocal( world ) : global::Transform.Zero;

		UpdateVerticesBuffer( local );
		Shape.UpdateMesh( _vertices, Indices );

		CalculateLocalBounds();
	}

	protected override IEnumerable<PhysicsShape> CreatePhysicsShapes( PhysicsBody targetBody, Transform local )
	{
		UpdateVerticesBuffer( local );
		var shape = targetBody.AddMeshShape( _vertices, Indices );

		Shape = shape;

		yield return shape;
	}
}
