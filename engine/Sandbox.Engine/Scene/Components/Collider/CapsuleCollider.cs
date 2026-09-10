namespace Sandbox;

/// <summary>
/// Defines a capsule collider.
/// </summary>
[Expose]
[Title( "Collider - Capsule" )]
[Category( "Physics" )]
[Icon( "rounded_corner" )]
[Alias( "ColliderCapsuleComponent" )]
public class CapsuleCollider : Collider
{
	Vector3 _start = 0;
	Vector3 _end = Vector3.Up * 10;
	float _radius = 16;

	/// <summary>
	/// Bottom point of the capsule
	/// </summary>
	[Property, Group( "Capsule" )]
	public Vector3 Start
	{
		get => _start;
		set
		{
			if ( _start == value ) return;

			_start = value;
			UpdateShape();
		}
	}

	/// <summary>
	/// Top point of the capsule
	/// </summary>
	[Property, Group( "Capsule" )]
	public Vector3 End
	{
		get => _end;
		set
		{
			if ( _end == value ) return;

			_end = value;
			UpdateShape();
		}
	}

	/// <summary>
	/// Radius of the capsule
	/// </summary>
	[Property, Group( "Capsule" )]
	public float Radius
	{
		get => _radius;
		set
		{
			if ( _radius == value ) return;

			_radius = value;
			UpdateShape();
		}
	}

	private PhysicsShape Shape;

	protected override void DrawGizmos()
	{
		if ( !Gizmo.IsSelected && !Gizmo.IsHovered )
			return;

		Gizmo.Hitbox.DepthBias = 0.01f;
		Gizmo.Draw.IgnoreDepth = true;
		Gizmo.Draw.LineThickness = 1;
		Gizmo.Draw.Color = Gizmo.Colors.Green.WithAlpha( Gizmo.IsSelected ? 1.0f : 0.2f );

		if ( Scene.Is2D )
		{
			DrawCapsule2D( Start, End, Radius );
		}
		else
		{
			Gizmo.Draw.LineCapsule( new Capsule( Start, End, Radius ) );
		}
	}

	private static void DrawCapsule2D( Vector3 start, Vector3 end, float radius )
	{
		var diff = end - start;

		if ( diff.IsNearZeroLength )
		{
			Gizmo.Draw.LineCircle( start, Vector3.Up, radius, sections: 32 );
			return;
		}

		var dir = diff.Normal;
		var perp = new Vector3( -dir.y, dir.x, 0 ) * radius;

		Gizmo.Draw.Line( start + perp, end + perp );
		Gizmo.Draw.Line( start - perp, end - perp );

		var angle = MathF.Atan2( dir.y, dir.x ).RadianToDegree();
		Gizmo.Draw.LineCircle( start, Vector3.Up, radius, angle + 90, 180, sections: 16 );
		Gizmo.Draw.LineCircle( end, Vector3.Up, radius, angle - 90, 180, sections: 16 );
	}

	internal override void UpdateShape()
	{
		if ( !Shape.IsValid() )
			return;

		var body = Rigidbody;
		var world = Transform.TargetWorld;
		var local = body.IsValid() ? body.Transform.TargetWorld.WithScale( 1.0f ).ToLocal( world ) : global::Transform.Zero;
		var scale = world.UniformScale;
		var center1 = local.PointToWorld( Start );
		var center2 = local.PointToWorld( End );
		var radius = Radius * scale;

		Shape.UpdateCapsuleShape( center1, center2, radius );

		CalculateLocalBounds();
	}

	protected override IEnumerable<PhysicsShape> CreatePhysicsShapes( PhysicsBody targetBody, Transform local )
	{
		var center1 = local.PointToWorld( Start );
		var center2 = local.PointToWorld( End );
		var radius = Radius * WorldScale.x;
		var shape = targetBody.AddCapsuleShape( center1, center2, radius );

		Shape = shape;

		yield return shape;
	}
}
