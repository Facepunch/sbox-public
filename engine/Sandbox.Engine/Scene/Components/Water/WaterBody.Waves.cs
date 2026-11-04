namespace Sandbox;

public sealed partial class WaterBody
{
	const int WaveBands = 4;
	static readonly string[] WaveAttributeNames = ["Wave0", "Wave1", "Wave2", "Wave3"];
	static readonly float[] WaveRatios = [1f, 0.62f, 0.37f, 0.21f];
	static readonly float[] WaveAngles = [0f, 31f, -24f, 57f];
	readonly Vector4[] _waves = new Vector4[WaveBands];
	(float Direction, float Length, float Width)? _builtWaveSettings;
	float _waveSpeed = 1;

	/// <summary>
	/// Propagation-clock offset in seconds. Speed changes update it to preserve phase.
	/// Serialized with the water so clones and newly joined clients keep the same surface.
	/// </summary>
	[Property, Hide]
	public double WaveTimeOffset
	{
		get;
		set => field = double.IsFinite( value ) ? value : 0;
	}

	/// <summary>
	/// Enable geometric surface waves without changing their saved settings.
	/// </summary>
	[Property, FeatureEnabled( nameof( Waves ), Icon = "waves" )]
	public bool Waves { get; set; }

	/// <summary>
	/// How far the waves rise above and fall below the mean surface, in inches. Zero disables wave deformation.
	/// </summary>
	[Property, Feature( nameof( Waves ) ), Order( -199 ), Title( "Amplitude" ), Range( 0, 256 )]
	public float WaveHeight
	{
		get;
		set => field = float.IsFinite( value ) ? Math.Clamp( value, 0, 256 ) : 0;
	} = 16;

	/// <summary>
	/// Wavelength of the longest band in inches. The others are shorter multiples of it.
	/// </summary>
	[Property, Feature( nameof( Waves ) ), Order( -198 ), Title( "Length" ), Range( 16, 8192 )]
	public float WaveLength
	{
		get;
		set => field = float.IsFinite( value ) ? Math.Clamp( value, 4, 8192 ) : 600;
	} = 600;

	/// <summary>
	/// Sideways scale of crest variation in inches, across WaveDirection. Smaller values give
	/// narrower, more frequent peaks along each crest; larger values give broader ridges.
	/// </summary>
	[Property, Feature( nameof( Waves ) ), Order( -197 ), Title( "Crest Width" ), Range( 16, 8192 )]
	public float WaveWidth
	{
		get;
		set => field = float.IsFinite( value ) ? Math.Clamp( value, 4, 8192 ) : 600;
	} = 600;

	/// <summary>
	/// Speed multiplier for swell propagation. One is normal speed; zero stops propagation.
	/// Changes preserve the current wave phase, including when stopping and resuming.
	/// Surface Detail has its own animation controls.
	/// </summary>
	[Property, Feature( nameof( Waves ) ), Order( -196 ), Title( "Speed" ), Range( 0, 4 )]
	public float WaveSpeed
	{
		get => _waveSpeed;
		set
		{
			value = float.IsFinite( value ) ? Math.Clamp( value, 0, 4 ) : 0;
			// Keep the current phase while changing the rate of future propagation.
			WaveTimeOffset += Time.NowDouble * (Math.Max( (double)_waveSpeed, 0 ) - Math.Max( (double)value, 0 ));
			_waveSpeed = value;
		}
	}

	/// <summary>
	/// Shared CPU/GPU propagation clock. Surface detail keeps its independent animation clock.
	/// </summary>
	float WaveTime => (float)(Time.NowDouble * Math.Max( (double)_waveSpeed, 0 ) + WaveTimeOffset);

	/// <summary>
	/// Sharpens crests and widens troughs independently of wave height. Zero gives rounded waves;
	/// one uses the full shared compression budget for the strongest chop without folding.
	/// </summary>
	[Property, Feature( nameof( Waves ) ), Order( -195 ), Title( "Steepness" ), Range( 0, 1 )]
	public float WaveSteepness
	{
		get;
		set => field = float.IsFinite( value ) ? Math.Clamp( value, 0, 1 ) : 0.5f;
	} = 0.5f;

	/// <summary>
	/// Direction of the dominant swell, in degrees from local +X toward +Y.
	/// Shorter bands spread around this heading.
	/// </summary>
	[Property, Feature( nameof( Waves ) ), Order( -194 ), Title( "Direction" ), Range( -180, 180 )]
	public float WaveDirection
	{
		get;
		set => field = float.IsFinite( value ) ? value % 360 : 0;
	}

	/// <summary>
	/// Whether geometric deformation is active, independently of the render detail setting.
	/// </summary>
	internal bool HasWaves => Waves && WaveHeight > 0;

	/// <summary>
	/// Rebuild the four shared CPU/GPU bands only when their propagation settings change.
	/// </summary>
	void UpdateWaves()
	{
		var settings = (WaveDirection, WaveLength, WaveWidth);
		if ( _builtWaveSettings == settings )
			return;

		_builtWaveSettings = settings;

		for ( int i = 0; i < WaveBands; i++ )
		{
			_waves[i] = WaterWaveMath.BuildBand( WaveLength, WaveWidth, WaveDirection, WaveRatios[i], WaveAngles[i] );
		}
	}

	/// <summary>
	/// Height of the animated surface above the mean plane at surface XY in world inches.
	/// </summary>
	public float SampleHeight( Vector2 point )
	{
		if ( !HasWaves )
			return 0;

		UpdateWaves();
		return WaterWaveMath.Sample( point, _waves, WaveHeight, WaveSteepness, WaveTime, out _ );
	}

	/// <summary>
	/// Height and world normal at displaced surface XY, including Gerstner crest compression.
	/// Matches the unfiltered wave surface.
	/// </summary>
	public float SampleSurface( Vector2 point, out Vector3 normal )
	{
		normal = WorldRotation.Up;
		if ( !HasWaves )
			return 0;

		// Queries can run before OnUpdate, including immediately after editing wave properties.
		UpdateWaves();
		float height = WaterWaveMath.Sample( point, _waves, WaveHeight, WaveSteepness,
			WaveTime, out var gradient );

		normal = (WorldRotation.Up - WorldRotation.Forward * gradient.x - WorldRotation.Left * gradient.y).Normal;
		return height;
	}

	/// <summary>
	/// Bind the same band parameters and propagation clock used by gameplay.
	/// </summary>
	internal void SetWaveAttributes( RenderAttributes attributes )
	{
		UpdateWaves();
		attributes.Set( "WaveTime", WaveTime );
		attributes.Set( "WaveHeight", RenderWaves ? WaveHeight : 0 );
		attributes.Set( "WaveSteepness", WaveSteepness );
		for ( int i = 0; i < WaveBands; i++ )
			attributes.Set( WaveAttributeNames[i], _waves[i] );
	}
}
