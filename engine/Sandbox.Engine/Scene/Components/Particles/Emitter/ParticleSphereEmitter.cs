namespace Sandbox;

/// <summary>
/// Emits particles within a sphere shape.
/// </summary>
[Title( "Sphere Emitter" )]
[Category( "Effects" )]
[Icon( "radio_button_unchecked" )]
public sealed class ParticleSphereEmitter : ParticleEmitter
{
	[Property, Range( 0, 100 )] public float Radius { get; set; } = 20.0f;
	[Property, Range( -1000, 1000 )] public float Velocity { get; set; } = 100.0f;
	[Property] public bool OnEdge { get; set; } = false;

	/// <summary>
	/// Scales the random spawn direction per axis. (1,1,1) spawns in the full sphere,
	/// (0,0,1) restricts spawning to a line along Z, (0,0,0) spawns at the center only.
	/// </summary>
	[Property] public Vector3 DistanceBias { get; set; } = new Vector3( 1, 1, 1 );

	/// <summary>
	/// Takes the absolute value of the random spawn direction per axis, where non-zero.
	/// For example (0,0,1) with a distance bias of (0,0,1) spawns only from the center upwards.
	/// </summary>
	[Property] public Vector3 DistanceBiasAbsoluteValue { get; set; } = Vector3.Zero;


	protected override void DrawGizmos()
	{
		if ( !Gizmo.IsSelected )
			return;

		Gizmo.Draw.Color = Color.White.WithAlpha( 0.1f );
		Gizmo.Draw.LineSphere( 0, Radius );

		// TODO - Sphere Gizmo

	}

	public override bool Emit( ParticleEffect target )
	{
		var random = Vector3.Random;

		if ( DistanceBiasAbsoluteValue.x != 0.0f ) random.x = MathF.Abs( random.x );
		if ( DistanceBiasAbsoluteValue.y != 0.0f ) random.y = MathF.Abs( random.y );
		if ( DistanceBiasAbsoluteValue.z != 0.0f ) random.z = MathF.Abs( random.z );

		random *= DistanceBias;

		var offset = random;
		var radius = Radius * WorldScale;
		var pos = WorldPosition;

		if ( OnEdge && !random.IsNearlyZero() )
		{
			pos += random.Normal * radius;
		}
		else
		{
			pos += random * radius;
		}

		var p = target.Emit( pos, Delta );

		if ( Velocity != 0.0f )
		{
			p.Velocity += offset * Velocity;
		}

		return true;
	}
}
