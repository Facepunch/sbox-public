namespace Sandbox;

public sealed partial class WaterBody
{
	/// <summary>
	/// Density of shader-generated suspended sediment. Zero skips sediment rendering.
	/// </summary>
	[Property, Feature( nameof( Underwater ) ), Group( "Particles" ), Order( -60 ), Title( "Particle Density" ), Range( 0, 32 )]
	public float UnderwaterParticleDensity
	{
		get;
		set => field = float.IsFinite( value ) ? Math.Clamp( value, 0, 32 ) : 1;
	} = 1;

	/// <summary>
	/// Colour of suspended sediment, independent of the surface and fog colours.
	/// </summary>
	[Property, Feature( nameof( Underwater ) ), Group( "Particles" ), Order( -59 ), Title( "Particle Color" ), ColorUsage( hasAlpha: false, isHDR: false )]
	public Color UnderwaterParticleColor { get; set; } = new( 0.55f, 0.62f, 0.58f );

	/// <summary>
	/// Minimum and maximum diameters in world inches. Each speck keeps a stable random size in this range.
	/// </summary>
	[Property, Feature( nameof( Underwater ) ), Group( "Particles" ), Order( -58 ), Title( "Particle Size" ), MinMaxSlider( 0.05f, 4.5f ), Step( 0.01f )]
	public Vector2 UnderwaterParticleSizeRange
	{
		get;
		set
		{
			float min = float.IsFinite( value.x ) ? Math.Clamp( value.x, 0.05f, 4.5f ) : 0.325f;
			float max = float.IsFinite( value.y ) ? Math.Clamp( value.y, 0.05f, 4.5f ) : 0.975f;
			field = new Vector2( Math.Min( min, max ), Math.Max( min, max ) );
		}
	} = new( 0.325f, 0.975f );

	float _particleDriftSpeed = 1.5f;

	/// <summary>
	/// Sediment-clock offset in seconds. Speed edits update it to preserve the current particle positions.
	/// Serialized with the body so copies and saved frozen fields retain their phase.
	/// </summary>
	[Property, Hide]
	public double UnderwaterParticleTimeOffset
	{
		get;
		set => field = double.IsFinite( value ) ? value : 0;
	}

	/// <summary>
	/// Speed of each speck's independent wandering motion. Zero freezes the world-anchored field.
	/// Changing speed preserves the current positions, including when stopping and resuming.
	/// </summary>
	[Property, Feature( nameof( Underwater ) ), Group( "Particles" ), Order( -57 ), Title( "Particle Drift Speed" ), Range( 0, 10 )]
	public float UnderwaterParticleDriftSpeed
	{
		get => _particleDriftSpeed;
		set
		{
			float speed = float.IsFinite( value ) ? Math.Clamp( value, 0, 10 ) : 1.5f;
			UnderwaterParticleTimeOffset += Time.NowDouble * ((double)_particleDriftSpeed - speed);
			_particleDriftSpeed = speed;
		}
	}

	/// <summary>
	/// Bind the procedural field and physical fluid bounds without particle objects or simulation buffers.
	/// </summary>
	internal void SetUnderwaterParticleAttributes( RenderAttributes attributes )
	{
		attributes.Set( "ParticleDensity", UnderwaterParticleDensity );
		attributes.Set( "ParticleSizeRange", UnderwaterParticleSizeRange );
		attributes.Set( "ParticleColor", UnderwaterParticleColor.WithAlpha( 1 ) );
		attributes.Set( "ParticleTime", (float)(Time.NowDouble * _particleDriftSpeed + UnderwaterParticleTimeOffset) );
		attributes.Set( "Origin", SurfaceOrigin );
		attributes.Set( "AxisX", WorldRotation.Forward );
		attributes.Set( "AxisY", WorldRotation.Left );
		attributes.Set( "SurfaceNormal", WorldRotation.Up );
		attributes.Set( "HalfSize", new Vector2( MathF.Abs( Size.x * WorldScale.x ) * 0.5f,
			MathF.Abs( Size.y * WorldScale.y ) * 0.5f ) );
		attributes.Set( "ParticleInfinite", Ocean );
		attributes.Set( "ParticleWaterDepth", WorldDepth );
		attributes.Set( "ParticleFog", new Vector3( UnderwaterFogNearDistance, UnderwaterFogAttenuationDistance, UnderwaterFogMaximumOpacity ) );
		SetWaveAttributes( attributes );
	}
}
