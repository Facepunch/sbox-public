namespace Sandbox;

/// <summary>
/// Defines a box collider.
/// </summary>
[Expose]
[Title( "Collider - Box" )]
[Category( "Physics" )]
[Icon( "check_box_outline_blank" )]
[Alias( "ColliderBoxComponent" )]
public sealed class BoxCollider : Collider
{
	private Vector3 _center = 0;
	private Vector3 _scale = 50.0f;

	/// <summary>
	/// The size of the box, from corner to corner.
	/// </summary>
	[Property, Title( "Size" ), Group( "Box" )]
	public Vector3 Scale
	{
		get => _scale;
		set
		{
			if ( _scale == value ) return;

			_scale = value;
			UpdateShape();
		}
	}

	/// <summary>
	/// The center of the box relative to this GameObject
	/// </summary>
	[Property, Group( "Box" )]
	public Vector3 Center
	{
		get => _center;
		set
		{
			if ( _center == value ) return;

			_center = value;
			UpdateShape();
		}
	}

	private PhysicsShape Shape;

	internal override void UpdateShape()
	{
		if ( !Shape.IsValid() )
			return;

		var body = Rigidbody;
		var world = Transform.TargetWorld;
		var local = body.IsValid() ? body.Transform.TargetWorld.WithScale( 1.0f ).ToLocal( world ) : global::Transform.Zero;
		var box = BBox.FromPositionAndSize( Center, Scale );
		box.Mins *= world.Scale;
		box.Maxs *= world.Scale;
		box.Mins += local.Position;
		box.Maxs += local.Position;

		Shape.UpdateBoxShape( box.Center, local.Rotation, box.Size * 0.5f );

		CalculateLocalBounds();
	}

	protected override void DrawGizmos()
	{
		if ( !Gizmo.IsSelected && !Gizmo.IsHovered )
			return;

		var box = BBox.FromPositionAndSize( Center, Scale );

		Gizmo.Draw.LineThickness = 1;
		Gizmo.Draw.Color = Gizmo.Colors.Green.WithAlpha( Gizmo.IsSelected ? 1.0f : 0.2f );
		Gizmo.Draw.LineBBox( box );
	}

	protected override IEnumerable<PhysicsShape> CreatePhysicsShapes( PhysicsBody targetBody, Transform local )
	{
		var box = BBox.FromPositionAndSize( Center, Scale );

		var scale = WorldScale;

		// scale by our scale!
		box.Mins *= scale;
		box.Maxs *= scale;

		// move!
		box.Mins += local.Position;
		box.Maxs += local.Position;

		var shape = targetBody.AddBoxShape( box, local.Rotation );

		Shape = shape;

		yield return shape;
	}
}
