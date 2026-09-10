namespace Sandbox;

/// <summary>
/// Emits particles in a ring. The ring can be flat or have a tube-like quality.
/// 
/// Velocity can either be added from the center of the ring, or from the ring itself.
/// </summary>
[Title( "Ring Emitter" )]
[Category( "Effects" )]
[Icon( "check_box_outline_blank" )]
public sealed class ParticleRingEmitter : ParticleEmitter
{
	[Property] public ParticleFloat Radius { get; set; } = 50.0f;
	[Property] public ParticleFloat Thickness { get; set; } = 10.0f;
	[Property, Range( 0, 360 )] public ParticleFloat AngleStart { get; set; } = 0.0f;
	[Property, Range( 0, 360 )] public ParticleFloat Angle { get; set; } = 360.0f;
	[Property, Range( 0, 1 )] public ParticleFloat Flatness { get; set; } = 0.0f;
	[Property, Range( -100, 100 )] public ParticleFloat VelocityFromCenter { get; set; } = 0.0f;
	[Property, Range( -100, 100 )] public ParticleFloat VelocityFromRing { get; set; } = 0.0f;

	protected override void DrawGizmos()
	{
		//using ( Gizmo.Scope( "ring", new Transform( 0, new Angles( 90, 0, 0 ) ) ) )
		//{
		//	Gizmo.Draw.Color = Color.White.WithAlpha( 0.2f );
		//	Gizmo.Draw.SolidRing( 0, Radius.Evaluate( Delta, 0 ), Radius.Evaluate( Delta, 0 ) + 1 + Thickness.Evaluate( Delta, EmitRandom ), AngleStart.Evaluate( Delta, 0 ), AngleEnd.Evaluate( Delta, 0 ), 16 );
		//}

	}

	public override bool Emit( ParticleEffect target )
	{
		var angle = Random.Shared.Float( 0, Angle.Evaluate( Delta, EmitRandom ).DegreeToRadian() );
		angle += AngleStart.Evaluate( Delta, EmitRandom ).DegreeToRadian();

		var x = MathF.Sin( angle );
		var y = MathF.Cos( angle );

		var size = new Vector3( x, y, 0 ) * Radius.Evaluate( Delta, EmitRandom );
		var ringOffset = Vector3.Zero;

		var thickness = Thickness.Evaluate( Delta, EmitRandom );

		if ( thickness > 0 )
		{
			ringOffset = Vector3.Random * thickness;
			ringOffset.z *= (1 - Flatness.Evaluate( Delta, EmitRandom ));

			size += ringOffset;
		}

		size = (size * WorldScale) * WorldRotation;


		var p = target.Emit( WorldPosition + size, Delta );
		if ( p is not null )
		{
			var velFromCenter = VelocityFromCenter.Evaluate( Delta, EmitRandom );
			if ( velFromCenter != 0 )
			{
				p.Velocity += (size.Normal * velFromCenter);
			}

			var velFromRing = VelocityFromRing.Evaluate( Delta, EmitRandom );
			if ( velFromRing != 0 )
			{
				ringOffset = (ringOffset * WorldScale) * WorldRotation;
				p.Velocity += (ringOffset.Normal * velFromRing);
			}
		}

		return true;
	}
}
