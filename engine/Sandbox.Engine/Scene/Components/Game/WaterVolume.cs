namespace Sandbox;

/// <summary>
/// Makes objects float, sink and drift. Attach to any trigger collider to create water.
/// </summary>
[Title( "Water Volume" )]
[Category( "Physics" )]
[Icon( "water" )]
public sealed class WaterVolume : Component
{
	/// <summary>
	/// How heavy the fluid is. Higher values make things float more.
	/// Water is 1000, oil is around 900, something very heavy like mercury is 13600.
	/// </summary>
	[Property, Group( "Fluid" ), Range( 0, 15000 )]
	public float FluidDensity { get; set; } = 1000.0f;

	/// <summary>
	/// How much the fluid slows down movement.
	/// </summary>
	[Property, Group( "Fluid" ), Range( 0, 1 )]
	public float LinearDrag { get; set; } = 0.1f;

	/// <summary>
	/// How much the fluid slows down spinning.
	/// </summary>
	[Property, Group( "Fluid" ), Range( 0, 2 )]
	public float AngularDrag { get; set; } = 0.5f;

	/// <summary>
	/// Direction and speed of the water current.
	/// </summary>
	[Property, Group( "Fluid" )]
	public Vector3 FluidVelocity { get; set; } = Vector3.Zero;

	/// <summary>
	/// Moves the water surface up or down.
	/// </summary>
	[Property, Group( "Surface" )]
	public float SurfaceOffset { get; set; } = 0.0f;

	/// <summary>
	/// How tall the waves are. Set to 0 for calm water.
	/// </summary>
	[Property, Group( "Surface" ), Range( 0, 100 )]
	public float WaveAmplitude { get; set; } = 0.0f;

	/// <summary>
	/// How fast the waves move.
	/// </summary>
	[Property, Group( "Surface" ), Range( 0, 5 )]
	public float WaveFrequency { get; set; } = 0.5f;

	/// <summary>
	/// Objects with any of these tags won't be affected.
	/// </summary>
	[Property, Group( "Filtering" )]
	public TagSet IgnoreTags { get; set; } = [];

	static readonly Color GizmoFill = new( 0.2f, 0.5f, 0.8f, 0.3f );
	static readonly Color GizmoLine = new( 0.3f, 0.6f, 1f, 0.8f );
	static readonly Color GizmoWaveRange = new( 0.3f, 0.6f, 1f, 0.3f );

	float _wavePhase;
	float _smoothAmplitude;

	/// <summary>
	/// Returns the current water surface as a plane.
	/// </summary>
	Plane GetWaterSurface()
	{
		var normal = GetSurfaceNormal();
		var wave = _smoothAmplitude > 0f ? (MathF.Sin( _wavePhase ) - 1f) * 0.5f * _smoothAmplitude : 0f;
		return new Plane( WorldPosition + normal * (SurfaceOffset + wave), normal );
	}

	Vector3 GetSurfaceNormal()
	{
		return -Scene.PhysicsWorld.Gravity.Normal;
	}

	protected override void OnFixedUpdate()
	{
		var collider = GetComponent<Collider>();
		if ( !collider.IsValid() ) return;

		var dt = Time.Delta;
		if ( dt <= 0f ) return;

		_wavePhase += WaveFrequency * MathF.PI * 2f * dt;
		_smoothAmplitude = MathX.Lerp( _smoothAmplitude, WaveAmplitude, dt * 5f );

		var plane = GetWaterSurface();

		foreach ( var body in collider.Touching.Where( x => x.IsValid() ).Select( x => x.GetComponentInParent<Rigidbody>() ).Distinct() )
		{
			if ( !body.IsValid() ) continue;
			if ( body.IsProxy ) continue;
			if ( !body.PhysicsBody.IsValid() ) continue;
			if ( body.PhysicsBody.BodyType != PhysicsBodyType.Dynamic ) continue;
			if ( !IgnoreTags.IsEmpty && body.GameObject.Tags.HasAny( IgnoreTags ) ) continue;

			body.ApplyBuoyancy( plane, FluidDensity, LinearDrag, AngularDrag, FluidVelocity, dt );
		}
	}

	protected override void DrawGizmos()
	{
		if ( !Gizmo.IsSelected && !Gizmo.IsHovered ) return;

		var collider = GetComponent<Collider>();
		if ( !collider.IsValid() ) return;

		var normal = GetSurfaceNormal();
		var tangent = Vector3.Cross( normal, Vector3.Forward );
		if ( tangent.LengthSquared < 1e-6f )
			tangent = Vector3.Cross( normal, Vector3.Right );
		tangent = tangent.Normal;
		var bitangent = Vector3.Cross( normal, tangent ).Normal;

		var half = (collider.LocalBounds.Size * WorldScale * 0.5f).Abs();
		var hx = Vector3.Dot( (WorldRotation.Inverse * tangent).Abs(), half );
		var hy = Vector3.Dot( (WorldRotation.Inverse * bitangent).Abs(), half );

		var origin = WorldTransform.PointToWorld( collider.LocalBounds.Center );
		var wave = _smoothAmplitude > 0f ? (MathF.Sin( _wavePhase ) - 1f) * 0.5f * _smoothAmplitude : 0f;

		using ( Gizmo.Scope( "WaterSurface" ) )
		{
			Gizmo.Transform = global::Transform.Zero;

			var center = origin + normal * (SurfaceOffset + wave);
			DrawSurfaceQuad( center, tangent, bitangent, hx, hy );

			if ( WaveAmplitude > 0f )
			{
				Gizmo.Draw.Color = GizmoWaveRange;
				Gizmo.Draw.LineThickness = 1;
				DrawWireRect( origin + normal * SurfaceOffset, tangent, bitangent, hx, hy );
				DrawWireRect( origin + normal * (SurfaceOffset - WaveAmplitude), tangent, bitangent, hx, hy );
			}

			Gizmo.Draw.Color = GizmoLine;
			Gizmo.Draw.Arrow( center, center + normal * 20f, 4f, 2f );
		}
	}

	static void DrawSurfaceQuad( Vector3 center, Vector3 tangent, Vector3 bitangent, float hx, float hy )
	{
		var v0 = center - tangent * hx - bitangent * hy;
		var v1 = center + tangent * hx - bitangent * hy;
		var v2 = center + tangent * hx + bitangent * hy;
		var v3 = center - tangent * hx + bitangent * hy;

		Gizmo.Draw.Color = GizmoFill;
		Gizmo.Draw.SolidTriangle( v0, v1, v2 );
		Gizmo.Draw.SolidTriangle( v0, v2, v3 );

		Gizmo.Draw.Color = GizmoLine;
		Gizmo.Draw.LineThickness = 2;
		Gizmo.Draw.Line( v0, v1 );
		Gizmo.Draw.Line( v1, v2 );
		Gizmo.Draw.Line( v2, v3 );
		Gizmo.Draw.Line( v3, v0 );
	}

	static void DrawWireRect( Vector3 center, Vector3 tangent, Vector3 bitangent, float hx, float hy )
	{
		var v0 = center - tangent * hx - bitangent * hy;
		var v1 = center + tangent * hx - bitangent * hy;
		var v2 = center + tangent * hx + bitangent * hy;
		var v3 = center - tangent * hx + bitangent * hy;

		Gizmo.Draw.Line( v0, v1 );
		Gizmo.Draw.Line( v1, v2 );
		Gizmo.Draw.Line( v2, v3 );
		Gizmo.Draw.Line( v3, v0 );
	}
}
