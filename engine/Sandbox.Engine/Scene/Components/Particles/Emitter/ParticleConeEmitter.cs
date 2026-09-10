namespace Sandbox;

/// <summary>
/// Emits particles within/along a cone shape.
/// </summary>
[Title( "Cone Emitter" )]
[Category( "Effects" )]
[Icon( "change_history" )]
public sealed class ParticleConeEmitter : ParticleEmitter
{
	[Property, Group( "Placement" )]
	public bool OnEdge { get; set; } = false;
	[Property, Group( "Placement" )]
	public bool InVolume { get; set; } = false;

	[Property, Range( 0, 45 ), Group( "Cone" ), Title( "Angle" )]
	public ParticleFloat ConeAngle { get; set; } = 30.0f;
	[Property, Group( "Cone" ), Title( "Start" )]
	public ParticleFloat ConeNear { get; set; } = 1.0f;
	[Property, Group( "Cone" ), Title( "End" )]
	public ParticleFloat ConeFar { get; set; } = 50.0f;

	/// <summary>
	/// Randomize the direction of the initial velocity. 0 = no randomization, 1 = full randomization.
	/// </summary>
	[Property, Group( "Cone" ), Range( 0, 1 )]
	public ParticleFloat VelocityRandom { get; set; } = 0;

	/// <summary>
	/// When distributing should we bias the center of the cone
	/// </summary>
	[Property, Group( "Cone" ), Range( 0, 1 )]
	public ParticleFloat CenterBias { get; set; } = 0;

	/// <summary>
	/// Should particles near the center have more velocity
	/// </summary>
	[Property, Group( "Cone" ), Range( 0, 1 )]
	public ParticleFloat CenterBiasVelocity { get; set; } = 0;

	/// <summary>
	/// Multiply velocity by this
	/// </summary>
	[Property, Group( "Cone" )]
	public ParticleFloat VelocityMultiplier { get; set; } = 1.0f;


	protected override void DrawGizmos()
	{
		if ( !Gizmo.IsSelected )
			return;

		var ca = ConeAngle.Evaluate( Delta, Random.Shared.Float() );
		var cf = ConeFar.Evaluate( Delta, Random.Shared.Float() );
		var cn = ConeNear.Evaluate( Delta, Random.Shared.Float() );

		Gizmo.Draw.Color = Color.White.WithAlpha( 0.3f );
		Gizmo.Draw.LineCircle( Vector3.Forward * (cf - cn), cf * MathF.Tan( ca.DegreeToRadian() ) );
		Gizmo.Draw.LineCircle( Vector3.Forward * 0, cn * MathF.Tan( ca.DegreeToRadian() ) );
	}

	public override bool Emit( ParticleEffect target )
	{
		var ca = ConeAngle.Evaluate( Delta, Random.Shared.Float() );
		var cf = ConeFar.Evaluate( Delta, Random.Shared.Float() );
		var cn = ConeNear.Evaluate( Delta, Random.Shared.Float() );

		var len = cn;

		if ( InVolume || OnEdge )
		{
			len = Random.Shared.Float( cn, cf );
		}

		var maxRadius = MathF.Tan( ca.DegreeToRadian() ) * len;
		var radius = maxRadius;

		if ( !OnEdge )
		{
			radius = MathF.Sqrt( Random.Shared.Float( 0f, 1f ) ) * radius;
		}

		var centerBias = CenterBias.Evaluate( Delta, Random.Shared.Float() );
		if ( centerBias > 0 )
		{
			radius *= MathF.Pow( Random.Shared.Float(), centerBias );
		}

		var tip = Vector3.Backward * cn;
		var pos = Vector3.Forward * (len - cn);

		var angle = Random.Shared.Float( MathF.PI * 2.0f );
		pos += Vector3.Left * MathF.Sin( angle ) * radius;
		pos += Vector3.Up * MathF.Cos( angle ) * radius;

		var emitPos = pos;


		var p = target.Emit( WorldTransform.PointToWorld( emitPos ), Delta );

		p.Velocity = WorldTransform.NormalToWorld( (pos - tip).Normal ) * p.Velocity.Length;

		//
		// More velocity towards the center
		//
		var centerBiasVelocity = CenterBiasVelocity.Evaluate( Delta, Random.Shared.Float() );
		if ( centerBiasVelocity > 0 )
		{
			p.Velocity *= MathF.Pow( 1 - (radius / maxRadius), centerBiasVelocity );
		}


		//
		// Lerp to random if VelocityRandom is set
		//
		var velocityRandom = VelocityRandom.Evaluate( Delta, Random.Shared.Float() );
		if ( velocityRandom > 0 )
		{
			p.Velocity = p.Velocity.LerpTo( Vector3.Random, velocityRandom, false ).Normal * p.Velocity.Length;
		}

		//
		// Scale the velocity
		//
		var velocityMultiply = VelocityMultiplier.Evaluate( Delta, Random.Shared.Float() );
		p.Velocity *= velocityMultiply;

		return true;
	}
}
