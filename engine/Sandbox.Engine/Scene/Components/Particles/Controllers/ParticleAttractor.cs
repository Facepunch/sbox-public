namespace Sandbox;

/// <summary>
/// Attract particles to a GameObject in the scene
/// </summary>
[Title( "Particle Attractor" )]
[Category( "Effects" )]
[Icon( "attractions" )]
public class ParticleAttractor : ParticleController
{
	[Property]
	public GameObject Target { get; set; }

	[Property]
	public ParticleFloat Force { get; set; } = 2.0f;

	[Property]
	public ParticleFloat MaxForce { get; set; } = 10.0f;

	[Property]
	public ParticleFloat Randomness { get; set; } = 0.0f;

	[Property]
	public float Radius { get; set; } = 0.0f;


	Vector3? targetPosition;

	protected override void DrawGizmos()
	{
		base.DrawGizmos();

		if ( !Target.IsValid() )
			return;

		if ( Gizmo.IsSelected )
		{
			Gizmo.Transform = global::Transform.Zero;
			Gizmo.Draw.Color = Color.White;
			Gizmo.Draw.LineSphere( Target.WorldPosition, Radius );
		}

	}

	protected override void OnBeforeStep( float delta )
	{
		targetPosition = Target?.WorldPosition;
	}

	protected override void OnParticleStep( Particle particle, float delta )
	{
		if ( !targetPosition.HasValue ) return;

		Vector3 target = targetPosition.Value;
		var force = Force.Evaluate( delta, particle.Rand( 23562 ) );
		var maxforce = MaxForce.Evaluate( delta, particle.Rand( 6235 ) );
		var randomNess = Randomness.Evaluate( delta, particle.Rand( 71235 ) );

		if ( Radius > 0 )
		{
			target += new Vector3( particle.Rand( 1235 ) - 0.5f, particle.Rand( 6211 ) - 0.5f, particle.Rand( 1212 ) - 0.5f ).Normal * Radius * particle.Rand( 1623 ) * 2.0f;
		}

		if ( randomNess > 0 )
		{
			target += Vector3.Random * randomNess;
		}

		var dir = (target - particle.Position);
		var distance = dir.Length;
		dir = dir.Normal;

		if ( distance > maxforce ) distance = maxforce;

		particle.Velocity += dir.Normal * delta * force * distance;
	}
}
