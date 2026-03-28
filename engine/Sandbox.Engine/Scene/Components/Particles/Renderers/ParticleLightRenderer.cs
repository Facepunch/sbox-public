using System.Threading;

namespace Sandbox;

/// <summary>
/// Adds lighting to particles in your effect.
/// </summary>
[Expose]
[Title( "Particle Light Renderer" )]
[Category( "Particles" )]
[Icon( "tips_and_updates" )]
public sealed class ParticleLightRenderer : ParticleController
{
	/// <summary>
	/// If 1, then every particle will get a light. If 0, no particles will get a light. If 0.5, half will get a particle.
	/// </summary>
	[Group( "Performance" )]
	[Range( 0, 1 )]
	[Property] public float Ratio { get; set; } = 1;

	[Group( "Performance" )]
	[Property] public int MaximumLights { get; set; } = 8;

	[Group( "Performance" )]
	[Property] public bool CastShadows { get; set; }

	[Group( "Light Description" )]
	[Property] public ParticleFloat Scale { get; set; } = 32;

	[Group( "Light Description" )]
	[Property] public ParticleFloat Attenuation { get; set; } = 1;

	[Group( "Light Description" )]
	[Property] public ParticleFloat Brightness { get; set; } = 1;

	[Group( "Light Description" )]
	[Property] public ParticleGradient LightColor { get; set; } = Color.White;

	[Group( "Light Description" )]
	[Property] public bool UseParticleColor { get; set; } = true;


	internal int currentLightCount;

	protected override void OnParticleCreated( Particle p )
	{
		if ( Random.Shared.Float( 0, 1 ) > Ratio )
			return;

		if ( currentLightCount > MaximumLights )
			return;

		currentLightCount++;

		p.AddListener( new ParticleLight( this ), this );
	}
}


class ParticleLight : Particle.BaseListener
{
	public ParticleLightRenderer Renderer;
	ScenePointLight so;

	public ParticleLight( ParticleLightRenderer particleLightRenderer )
	{
		Renderer = particleLightRenderer;
	}

	public override void OnEnabled( Particle p )
	{
		so = new ScenePointLight( Renderer.Scene.SceneWorld, p.Position, 100, Color.Red );
	}

	public override void OnDisabled( Particle p )
	{
		Interlocked.Decrement( ref Renderer.currentLightCount );

		if ( !so.IsValid() ) return;

		so.Delete();
	}

	public override void OnUpdate( Particle p, float dt )
	{
		if ( !so.IsValid() ) return;

		float brightness = Renderer.Brightness.Evaluate( p, 2346 );
		Color color = Renderer.LightColor.Evaluate( p, 6342 );
		color = color.WithAlpha( 1 ) * color.a * brightness;

		so.ShadowsEnabled = Renderer.CastShadows;
		so.Transform = new Transform( p.Position, p.Angles );

		if ( !Renderer.UseParticleColor )
		{
			so.LightColor = color;
		}
		else
		{
			so.LightColor = p.Color.WithAlpha( 1 ) * p.Alpha * p.Color.a * color;
		}

		so.Radius = Renderer.Scale.Evaluate( p, 43 ) * p.Size.x;
		so.LinearAttenuation = Renderer.Attenuation.Evaluate( p, 4323 );
		so.ColorTint = p.Color.WithAlphaMultiplied( p.Alpha );
	}
}
