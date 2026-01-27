using Sandbox.Rendering;
using System.Runtime.CompilerServices;

namespace Sandbox;

/// <summary>
/// The BeamEffect component creates a visual beam effect in the scene, simulating a continuous line or laser-like effect.
/// Unlike LineRenderer these beams can change over time, spawn multiple instances, and have various properties like color, texture, and lifetime.
/// This is a useful component for creating things like laser beams, energy effects and tracers.
/// </summary>
[Expose]
[Title( "Beam Effect" )]
[Category( "Effects" )]
[Icon( "electric_bolt" )]
[EditorHandle( Icon = "electric_bolt" )]
public sealed class BeamEffect : Component, Component.ExecuteInEditor, Component.ITemporaryEffect
{

	/// <summary>
	/// Thickness of the beam in world units. Controls how wide the beam appears.
	/// </summary>
	[Header( "Thickness" )]
	[Property] public ParticleFloat Scale { get; set; } = 32.0f;


	/// <summary>
	/// World position the beam targets if no target object is set. Used as the endpoint for the beam.
	/// </summary>
	[Header( "Target" )]
	[Property] public Vector3 TargetPosition { get; set; }

	/// <summary>
	/// GameObject to target with the beam. If assigned, overrides TargetPosition and uses the object's world position as the endpoint.
	/// </summary>
	[Property] public GameObject TargetGameObject { get; set; }

	/// <summary>
	/// Random offset applied to the target position for visual variation. Adds randomness to the endpoint.
	/// </summary>
	[Property] public Vector3 TargetRandom { get; set; }

	/// <summary>
	/// If true, the beam endpoints follow their source and target positions each frame, updating dynamically.
	/// </summary>
	[Property] public bool FollowPoints { get; set; } = true;


	/// <summary>
	/// Number of beams spawned per second. Controls the spawn rate for continuous effects.
	/// </summary>
	[Header( "Spawning" )]
	[Property] public float BeamsPerSecond { get; set; } = 0;

	/// <summary>
	/// Maximum number of beams that can exist at once. Limits the total active beams.
	/// </summary>
	[Property] public int MaxBeams { get; set; } = 1;

	/// <summary>
	/// Number of beams spawned immediately when the effect is enabled.
	/// </summary>
	[Property] public int InitialBurst { get; set; } = 1;

	/// <summary>
	/// Lifetime of each beam in seconds. Determines how long a beam remains before being removed or respawned.
	/// </summary>
	[Property] public ParticleFloat BeamLifetime { get; set; } = 1.0f;

	/// <summary>
	/// If true, beams respawn automatically when they expire, creating a looping effect.
	/// </summary>
	[Property] public bool Looped { get; set; } = false;

	/// <summary>
	/// Texture applied to the beam. Defines the visual appearance along the beam's length.
	/// </summary>
	[Property, Hide, Obsolete( "Use Material instead" )] public Texture Texture { get; set; }

	/// <summary>
	/// Material applied to the beam. Defines the visual appearance along the beam's length.
	/// The material should be based on the `line.shader`.
	/// </summary>
	[Header( "Texture" )]
	[Property] public Material Material { get; set; }

	/// <summary>
	/// Offset of the texture along the beam. Shifts the texture start position.
	/// </summary>
	[Property] public ParticleFloat TextureOffset { get; set; } = 1.0f;

	/// <summary>
	/// Scale of the texture along the beam. Controls how many world units each texture tile covers.
	/// </summary>
	[Property] public ParticleFloat TextureScale { get; set; } = 128;

	/// <summary>
	/// Speed at which the texture scrolls along the beam. Positive values scroll in one direction, negative in the other.
	/// </summary>
	[Property] public ParticleFloat TextureScrollSpeed { get; set; } = 0.0f;

	/// <summary>
	/// This is pretty much the same as TextureOffset - but it's seperate so you can use offset for offset, and scroll to scroll.
	/// </summary>
	[Property] public ParticleFloat TextureScroll { get; set; } = 0.0f;

	/// <summary>
	/// Controls texture filtering on this beam effect.
	/// </summary>
	[Property] public FilterMode FilterMode { get; set; } = FilterMode.Anisotropic;

	/// <summary>
	/// Color gradient of the beam over its lifetime. Defines how the color changes from birth to death.
	/// </summary>
	[Header( "Color" )]
	[Feature( "Rendering" ), Property] public ParticleGradient BeamColor { get; set; } = new Color( 1, 1, 1, 1 );

	/// <summary>
	/// Alpha multiplier for the beam's color. Controls transparency over the beam's lifetime.
	/// </summary>
	[Feature( "Rendering" ), Property] public ParticleFloat Alpha { get; set; } = 1.0f;

	/// <summary>
	/// Brightness multiplier for the beam's color. Adjusts intensity over the beam's lifetime.
	/// </summary>
	[Feature( "Rendering" ), Property] public ParticleFloat Brightness { get; set; } = 1.0f;

	/// <summary>
	/// If true, the beam is rendered additively, making it appear to glow.
	/// </summary>
	[Header( "Render Properties" )]
	[Feature( "Rendering" ), Property] public bool Additive { get; set; }

	/// <summary>
	/// If true, the beam casts shadows in the scene.
	/// </summary>
	[Feature( "Rendering" ), Property] public bool Shadows { get; set; }

	/// <summary>
	/// If true, the beam is affected by scene lighting.
	/// </summary>
	[Feature( "Rendering" ), Property] public bool Lighting { get; set; }

	/// <summary>
	/// If true, the beam is rendered as opaque rather than transparent.
	/// </summary>
	[Feature( "Rendering" ), Property] public bool Opaque { get; set; }

	/// <summary>
	/// Amount of feathering applied to the beam's depth, softening its intersection with geometry.
	/// </summary>
	[Header( "Rendering Misc" )]
	[Property, Range( 0, 128 ), Feature( "Rendering" )] public float DepthFeather { get; set; } = 0.0f;

	/// <summary>
	/// If true, the beam visually travels from start to end, useful for tracer effects.
	/// </summary>
	[FeatureEnabled( "Travel" )]
	[Property] public bool TravelBetweenPoints { get; set; } = false;

	/// <summary>
	/// Controls the interpolation of the beam's travel effect over its lifetime.
	/// </summary>
	[InfoBox( "Travel can be used to create tracers. The beam travels from the start to the end of the beam." )]
	[Property, Feature( "Travel" )] public ParticleFloat TravelLerp { get; set; } = new ParticleFloat { Evaluation = ParticleFloat.EvaluationType.Life, Type = ParticleFloat.ValueType.Range, ConstantA = 0, ConstantB = 1 };

	bool _disableLooping = false;

	/// <summary>
	/// Returns true if there are any active beams.
	/// </summary>
	bool ITemporaryEffect.IsActive => _beams.Count > 0;

	/// <summary>
	/// Disables automatic looping of beams, preventing them from respawning when expired.
	/// </summary>
	void ITemporaryEffect.DisableLooping() { _disableLooping = true; }

	List<BeamInstance> _beams = new();
	TimeSince _timeSinceLastSpawn = 0;

	/// <summary>
	/// Represents an individual beam instance within the effect.
	/// </summary>
	public class BeamInstance
	{
		/// <summary>
		/// Start position of the beam in world space.
		/// </summary>
		public Vector3 StartPosition;

		/// <summary>
		/// End position of the beam in world space.
		/// </summary>
		public Vector3 EndPosition;

		/// <summary>
		/// LineRenderer component used to render the beam visually.
		/// </summary>
		public LineRenderer Renderer;

		/// <summary>
		/// Time when the beam was created (born).
		/// </summary>
		public float TimeBorn;

		/// <summary>
		/// Time when the beam will expire (die).
		/// </summary>
		public float TimeDie;

		/// <summary>
		/// Returns the normalized lifetime of the beam, ranging from 0 (just born) to 1 (expired).
		/// </summary>
		public float Delta => (Time.Now - TimeBorn) / (TimeDie - TimeBorn);

		/// <summary>
		/// Random seed used to generate consistent random values for this beam instance.
		/// </summary>
		public int RandomSeed;

		/// <summary>
		/// Destroys the beam instance, cleaning up its resources.
		/// </summary>
		public void Destroy()
		{
			if ( Renderer.IsValid() )
			{
				Renderer.Destroy();
				Renderer = null;
			}
		}

		[MethodImpl( MethodImplOptions.AggressiveInlining )]
		internal float Rand( int seed = 0, [CallerLineNumber] int line = 0 )
		{
			int i = RandomSeed + (line * 20) + seed;
			return Game.Random.FloatDeterministic( i );
		}
	}

	private Material _defaultMaterial;

	protected override void OnEnabled()
	{
		_defaultMaterial ??= Material.Load( "materials/default/default_line.vmat" ).CreateCopy();

		StartLoop();
	}

	void StartLoop()
	{
		_disableLooping = false;
		_timeSinceLastSpawn = 0;

		for ( int i = 0; i < InitialBurst; i++ )
		{
			SpawnBeam();
		}
	}

	protected override void OnDisabled()
	{
		base.OnDisabled();

		foreach ( var beam in _beams )
		{
			beam.Destroy();
		}

		_beams.Clear();
	}

	protected override void OnUpdate()
	{

		_beams ??= new();

		_timeSinceLastSpawn += Time.Delta;

		if ( BeamsPerSecond > 0 )
		{
			float interval = (1.0f / BeamsPerSecond);
			if ( _timeSinceLastSpawn >= interval )
			{
				_timeSinceLastSpawn = 0;

				if ( _beams.Count < MaxBeams )
				{
					SpawnBeam();
				}
			}
		}

		for ( int i = _beams.Count - 1; i >= 0; i-- )
		{
			var beam = _beams[i];
			if ( Time.Now >= beam.TimeDie )
			{
				if ( Looped && !_disableLooping )
				{
					// respawn
					beam.TimeBorn = Time.Now;
					beam.TimeDie = Time.Now + BeamLifetime.Evaluate( 0.5f, beam.Rand( 33 ) );
				}
				else
				{
					DestroyBeam( beam );
				}
				continue;
			}

			UpdateBeam( beam );
		}

		// If we're in the editor, restart the loop, even if we're not looping
		if ( Scene.IsEditor && BeamsPerSecond <= 0 && _beams.Count == 0 )
		{
			OnEnabled();
		}
	}

	static int seed = 0;

	BeamInstance CreateBeam()
	{
		var beam = new BeamInstance
		{
			Renderer = AddComponent<LineRenderer>(),
			TimeBorn = Time.Now,
			RandomSeed = seed++
		};

		beam.TimeDie = Time.Now + BeamLifetime.Evaluate( 0.5f, beam.Rand( 33 ) );

		beam.Renderer.Flags |= ComponentFlags.NotSaved | ComponentFlags.NotEditable | ComponentFlags.Hidden | ComponentFlags.NotCloned;
		_beams.Add( beam );
		return beam;
	}

	/// <summary>
	/// Spawns a new beam and adds it to the effect.
	/// </summary>
	public BeamInstance SpawnBeam()
	{
		var beam = CreateBeam();
		UpdatePositions( beam );
		return beam;
	}

	void DestroyBeam( BeamInstance beam )
	{
		_beams.Remove( beam );
		beam.Destroy();
	}

	void UpdatePositions( BeamInstance beam )
	{
		beam.StartPosition = WorldPosition;
		beam.EndPosition = TargetPosition;
		if ( TargetGameObject.IsValid() ) beam.EndPosition = TargetGameObject.WorldPosition;

		beam.EndPosition += Vector3.Random * TargetRandom;
	}

	void UpdateBeam( BeamInstance beam )
	{
		var lineRenderer = beam.Renderer;
		var lifeDelta = beam.Delta;
		var secondsAlive = Time.Now - beam.TimeBorn;

		if ( FollowPoints )
		{
			UpdatePositions( beam );
		}

		var texScale = TextureScale.Evaluate( lifeDelta, beam.Rand( 55 ) );
		if ( texScale == 0 ) texScale = 0.01f;

		var color = BeamColor.Evaluate( lifeDelta, beam.Rand( 2 ) );
		color = color.WithAlphaMultiplied( Alpha.Evaluate( lifeDelta, beam.Rand( 88 ) ) );
		color = color.WithColorMultiplied( Brightness.Evaluate( lifeDelta, beam.Rand( 11 ) ) );

		var offset = TextureOffset.Evaluate( lifeDelta, beam.Rand( 12 ) );
		offset -= (TextureScroll.Evaluate( lifeDelta, beam.Rand( 13 ) ) / texScale);
		offset += secondsAlive * (TextureScrollSpeed.Evaluate( lifeDelta, beam.Rand( 14 ) ) / texScale);

		// Legacy support for old texture based renderers
#pragma warning disable CS0618
		if ( Texture.IsValid() )
		{
			_defaultMaterial.Set( "g_tColor", Texture );
		}
		else
		{
			_defaultMaterial.Set( "g_tColor", Texture.White );
		}
#pragma warning restore CS0618

		lineRenderer.UseVectorPoints = true;
		lineRenderer.VectorPoints ??= new();
		lineRenderer.VectorPoints.Clear();
		lineRenderer.Width = Scale.Evaluate( lifeDelta, beam.Rand( 98 ) );
		lineRenderer.Color = color;
		lineRenderer.Additive = Additive;
		lineRenderer.Opaque = Opaque;
		lineRenderer.CastShadows = Shadows;
		lineRenderer.Lighting = Lighting;
		lineRenderer.DepthFeather = DepthFeather;

		lineRenderer.Texturing = new TrailTextureConfig
		{
			Material = Material ?? _defaultMaterial,
			UnitsPerTexture = texScale,
			Offset = offset,
			FilterMode = FilterMode,
			TextureAddressMode = Rendering.TextureAddressMode.Wrap,
			WorldSpace = true,
		};

		if ( TravelBetweenPoints )
		{
			var lerp = TravelLerp.Evaluate( lifeDelta, beam.Rand( 3289 ) );
			var length = beam.StartPosition.Distance( beam.EndPosition );
			var chunklength = lineRenderer.Texturing.UnitsPerTexture;

			var chunksPerLength = length / chunklength;
			chunksPerLength += 1;

			lineRenderer.Texturing = lineRenderer.Texturing with
			{
				TextureAddressMode = Rendering.TextureAddressMode.Clamp,
				Offset = 1 + (lerp * -chunksPerLength)
			};

		}

		lineRenderer.VectorPoints.Add( beam.StartPosition );
		lineRenderer.VectorPoints.Add( beam.EndPosition );
	}
}
