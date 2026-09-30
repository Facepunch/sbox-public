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

	public override bool IsConcave => true;

	/// <summary>
	/// The sanitized plane normal, falling back to Up if the normal is near-zero.
	/// </summary>
	private Vector3 SafeNormal => Normal.LengthSquared > 1e-12f ? Normal.Normal : Vector3.Up;

	/// <summary>
	/// The plane's orientation as a rotation.
	/// </summary>
	private Rotation PlaneRotation => Rotation.LookAt( SafeNormal );

	/// <summary>
	/// The plane size scaled by world scale.
	/// </summary>
	private Vector2 ScaledSize => Scale * (Vector2)WorldScale;

	/// <summary>
	/// Calculate the plane's center, rotation and size relative to a local transform.
	/// </summary>
	private void GetPlaneTransform( Transform local, out Vector3 center, out Rotation rotation, out Vector2 size )
	{
		var s = WorldScale;
		center = (Center * s * local.Rotation) + local.Position;
		rotation = PlaneRotation * local.Rotation;
		size = ScaledSize;
	}

	/// <summary>
	/// Get the four corner vertices of the plane in local space.
	/// </summary>
	private Vector3[] GetVertices( Transform local )
	{
		GetPlaneTransform( local, out var center, out var rotation, out var size );

		var tangent = rotation.Right;
		var bitangent = rotation.Down;
		var halfX = size.x * 0.5f;
		var halfY = size.y * 0.5f;

		return
		[
			center - tangent * halfX - bitangent * halfY,
			center + tangent * halfX - bitangent * halfY,
			center + tangent * halfX + bitangent * halfY,
			center - tangent * halfX + bitangent * halfY,
		];
	}

	protected override void DrawGizmos()
	{
		if ( !Gizmo.IsSelected && !Gizmo.IsHovered )
			return;

		Gizmo.Transform = Gizmo.Transform.WithScale( 1.0f );

		var v = GetVertices( global::Transform.Zero );
		var alpha = Gizmo.IsSelected ? 1.0f : 0.6f;

		Gizmo.Draw.LineThickness = 1;
		Gizmo.Draw.CullBackfaces = true;
		Gizmo.Draw.Color = Gizmo.Colors.Green.WithAlpha( alpha * 0.2f );
		Gizmo.Draw.SolidTriangle( v[0], v[1], v[2] );
		Gizmo.Draw.SolidTriangle( v[2], v[3], v[0] );

		Gizmo.Draw.Color = Gizmo.Colors.Green.WithAlpha( alpha );
		Gizmo.Draw.Line( v[0], v[1] );
		Gizmo.Draw.Line( v[1], v[2] );
		Gizmo.Draw.Line( v[2], v[3] );
		Gizmo.Draw.Line( v[3], v[0] );
	}

	internal override void UpdateShape()
	{
		if ( !Shape.IsValid() )
			return;

		GetPlaneTransform( GetLocalTransform(), out var center, out var rotation, out var size );
		Shape.UpdatePlane( center, rotation, size );
		CalculateLocalBounds();
	}

	protected override IEnumerable<PhysicsShape> CreatePhysicsShapes( PhysicsBody targetBody, Transform local )
	{
		GetPlaneTransform( local, out var center, out var rotation, out var size );
		Shape = targetBody.AddPlaneShape( center, rotation, size );
		yield return Shape;
	}

	private Transform GetLocalTransform()
	{
		var body = Rigidbody;
		return !body.IsValid() ? global::Transform.Zero : body.Transform.TargetWorld.WithScale( 1.0f )
			.ToLocal( Transform.TargetWorld );
	}
}
