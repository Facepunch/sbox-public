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
	/// <summary>Inner radius of the emission volume, clamped to the outer radius.</summary>
	[Property, Range( 0, 100 )] public float MinimumRadius { get; set; }
	[Property, Range( -1000, 1000 )] public float Velocity { get; set; } = 100.0f;
	/// <summary>Additional outward speed, independent of the particle's spawn radius.</summary>
	[Property] public ParticleFloat RadialVelocity { get; set; } = 0;
	[Property] public bool OnEdge { get; set; } = false;

	/// <summary>Rotate the sphere's directional bias and emission velocity with this emitter.</summary>
	[Property] public bool LocalDirection { get; set; }

	/// <summary>
	/// Scales the random spawn direction per axis. (1,1,1) spawns in the full sphere,
	/// (0,0,1) restricts spawning to a line along Z, (0,0,0) spawns at the center only.
	/// </summary>
	[Property] public Vector3 DistanceBias { get; set; } = new Vector3( 1, 1, 1 );
	/// <summary>Let directional bias change direction without shortening the sampled radius.</summary>
	[Property] public bool NormalizeBias { get; set; }

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
		var random = ParticleRandom.Shared.VectorInSphere();
		var length = random.Length;
		var minimum = Radius > 0 ? Math.Clamp( MinimumRadius / Radius, 0, 1 ) : 0;
		if ( minimum > 0 && !random.IsNearlyZero() )
		{
			length = float.Lerp( minimum, 1, length );
			random = random.Normal * length;
		}

		if ( OneSidedAxes.HasFlag( EmitAxes.X ) ) random.x = MathF.Abs( random.x );
		if ( OneSidedAxes.HasFlag( EmitAxes.Y ) ) random.y = MathF.Abs( random.y );
		if ( OneSidedAxes.HasFlag( EmitAxes.Z ) ) random.z = MathF.Abs( random.z );

		random *= DistanceBias;
		if ( NormalizeBias && !random.IsNearlyZero() ) random = random.Normal * length;
		if ( LocalDirection ) random = WorldRotation * random;

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
		if ( !RadialVelocity.IsConstant || RadialVelocity.ConstantValue != 0 )
			p.Velocity += offset.Normal * RadialVelocity.Evaluate( Delta, ParticleRandom.Shared.Float() );

		return true;
	}
}
