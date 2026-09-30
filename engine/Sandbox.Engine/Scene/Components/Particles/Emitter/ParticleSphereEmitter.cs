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
	/// Axes restricted to one side of the sphere. Restrict Z for a dome, two axes for a
	/// quarter sphere. Flip the side with a negative <see cref="DistanceBias"/> on that axis.
	/// </summary>
	[Property] public EmitAxes OneSidedAxes { get; set; } = EmitAxes.None;

	/// <summary>
	/// A set of local axes.
	/// </summary>
	[Flags]
	public enum EmitAxes
	{
		None = 0,
		X = 1,
		Y = 2,
		Z = 4
	}

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

		if ( OneSidedAxes.HasFlag( EmitAxes.X ) ) random.x = MathF.Abs( random.x );
		if ( OneSidedAxes.HasFlag( EmitAxes.Y ) ) random.y = MathF.Abs( random.y );
		if ( OneSidedAxes.HasFlag( EmitAxes.Z ) ) random.z = MathF.Abs( random.z );

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
