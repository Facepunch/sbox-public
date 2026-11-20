using System.Threading;

namespace Sandbox;

/// <summary>
/// Adds lighting to particles in your effect.
/// </summary>
[Expose]
[Title( "Particle Light Renderer" )]
[Category( "Effects" )]
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
	int tagsVersion;

	protected override void OnEnabled()
	{
		// we dont get OnTagsChanged when disabled so increment here for safety
		Interlocked.Increment( ref tagsVersion );
		base.OnEnabled();
	}

	protected override void OnTagsChanged()
	{
		base.OnTagsChanged();
		Interlocked.Increment( ref tagsVersion );
	}

	internal void UpdateTags( ScenePointLight light, ref int? appliedVersion )
	{
		// Don't mark tags applied until the light exists.
		if ( !light.IsValid() ) return;

		var version = Volatile.Read( ref tagsVersion );
		if ( appliedVersion == version ) return;

		light.Tags.SetFrom( GameObject.Tags );
		appliedVersion = version;
	}

	protected override void OnParticleCreated( Particle p )
	{
		if ( ParticleRandom.Shared.Float( 0, 1 ) > Ratio )
			return;

		if ( currentLightCount >= MaximumLights )
			return;

		currentLightCount++;

		p.AddListener( new ParticleLight( this ), this );
	}
}


class ParticleLight : Particle.BaseListener
{
	public ParticleLightRenderer Renderer;
	ScenePointLight so;
	bool hasState;
	bool lastShadows;
	Transform lastTransform;
	Color lastColor;
	Color lastTint;
	float lastRadius;
	float lastAttenuation;
	int? appliedTagsVersion;

	public ParticleLight( ParticleLightRenderer particleLightRenderer )
	{
		Renderer = particleLightRenderer;
	}

	public override void OnEnabled( Particle p )
	{
		so = new ScenePointLight( Renderer.Scene.SceneWorld, p.Position, 100, Color.Red );
		appliedTagsVersion = null;
		Renderer.UpdateTags( so, ref appliedTagsVersion );
		hasState = false;
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
		Renderer.UpdateTags( so, ref appliedTagsVersion );
		Color color = Renderer.LightColor.Evaluate( p, 6342 );
		color = color.WithAlpha( 1 ) * color.a * brightness;

		var shadows = Renderer.CastShadows;
		var transform = new Transform( p.Position, p.Angles );
		var lightColor = Renderer.UseParticleColor ? p.Color.WithAlpha( 1 ) * p.Alpha * p.Color.a * color : color;
		var radius = Renderer.Scale.Evaluate( p, 43 ) * p.Size.x;
		var attenuation = Renderer.Attenuation.Evaluate( p, 4323 );
		var tint = p.Color.WithAlphaMultiplied( p.Alpha );

		// Avoid native calls and renderer invalidations for unchanged properties, but still evaluate every value so frame-random settings keep their sequence.
		if ( !hasState || lastShadows != shadows ) so.ShadowsEnabled = shadows;
		if ( !hasState || lastTransform != transform ) so.Transform = transform;
		if ( !hasState || lastColor != lightColor ) so.LightColor = lightColor;
		if ( !hasState || lastRadius != radius ) so.Radius = radius;
		if ( !hasState || lastAttenuation != attenuation ) so.LinearAttenuation = attenuation;
		if ( !hasState || lastTint != tint ) so.ColorTint = tint;

		lastShadows = shadows;
		lastTransform = transform;
		lastColor = lightColor;
		lastRadius = radius;
		lastAttenuation = attenuation;
		lastTint = tint;
		hasState = true;
	}
}
