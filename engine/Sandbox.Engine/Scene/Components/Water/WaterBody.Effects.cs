namespace Sandbox;

public sealed partial class WaterBody
{
	/// <summary>
	/// Emit splashes when objects enter or players move across the surface.
	/// </summary>
	[Property, Group( "Effects" ), Title( "Splashes" )]
	public bool Effects { get; set; } = true;

	/// <summary>
	/// Create a small, medium or large splash, projected onto this surface.
	/// Sizes up to 32 use the small prefab; sizes up to 64 use medium.
	/// The returned effect is local and destroys itself after its particles and sound finish.
	/// </summary>
	public GameObject CreateSplash( Vector3 position, float size = 32 )
	{
		return SpawnSplash( size <= 32 ? "splash_small" : size <= 64 ? "splash_medium" : "splash_large", position );
	}

	/// <summary>
	/// Create the upright bullet splash and its sound at this surface.
	/// The returned effect is local and destroys itself after playback.
	/// </summary>
	public GameObject CreateBulletSplash( Vector3 position ) => SpawnSplash( "bullet", position );

	GameObject SpawnSplash( string prefab, Vector3 position )
	{
		if ( !Active || !Effects || Application.IsDedicatedServer )
			return null;

		using var scope = Scene.Push();
		var local = ToSurface( position );
		float height = SampleSurface( local, out var normal );
		var effect = GameObject.Clone( $"prefabs/effects/water/{prefab}.prefab",
			new CloneConfig( global::Transform.Zero, GameObject, startEnabled: false ) );
		if ( !effect.IsValid() )
			return null;

		effect.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked | GameObjectFlags.Absolute;
		effect.WorldTransform = new Transform( FromSurface( local.WithZ( height ) ) + normal,
			Rotation.FromToRotation( Vector3.Up, normal ), 1 );
		effect.Enabled = true;
		return effect;
	}

	/// <summary>
	/// Find the first surface crossing within a segment. Flat water uses an exact plane test;
	/// waves sample only the portion inside their vertical envelope and finite footprint.
	/// </summary>
	internal bool TraceSurface( Vector3 start, Vector3 end, out Vector3 position, out float fraction )
	{
		position = default;
		fraction = 0;
		if ( !HasWaves )
		{
			float from = SurfaceDistance( start );
			float to = SurfaceDistance( end );
			if ( from == to )
				return false;

			fraction = from / (from - to);
			if ( fraction <= 0 || fraction > 1 )
				return false;

			position = Vector3.Lerp( start, end, fraction );
			return InFootprint( position );
		}

		float distance = (end - start).Length;
		if ( distance <= 0 || !float.IsFinite( distance ) )
			return false;
		var a = ToSurface( start );
		var b = ToSurface( end );
		float first = 0, last = 1;
		if ( !Clip( a.z, b.z, -WaveHeight, WaveHeight ) )
			return false;
		if ( !Ocean )
		{
			var half = new Vector2( MathF.Abs( Size.x * WorldScale.x ), MathF.Abs( Size.y * WorldScale.y ) ) * 0.5f;
			if ( !Clip( a.x, b.x, -half.x, half.x ) || !Clip( a.y, b.y, -half.y, half.y ) )
				return false;
		}

		float step = Math.Clamp( MathF.Min( WaveLength, WaveWidth ) * 0.21f / 16, 0.1f, 8 ) / distance;
		UpdateWaves();
		// The sampling loop interpolates in world space before transforming each point.
		var worldError = (start.Abs() + end.Abs() + SurfaceOrigin.Abs()) * 0.000001f;
		var positionError = new Vector3( Vector3.Dot( worldError, WorldRotation.Forward.Abs() ),
			Vector3.Dot( worldError, WorldRotation.Left.Abs() ), Vector3.Dot( worldError, WorldRotation.Up.Abs() ) );
		if ( !WaterWaveMath.MayIntersectEnvelope( a, b, first, last, _waves, WaveHeight, WaveSteepness, WaveTime, positionError ) )
			return false;

		float slopeBound = _waves[0].z * 0.5f + _waves[1].z * 0.27f + _waves[2].z * 0.15f + _waves[3].z * 0.08f;
		slopeBound *= WaveHeight / (1 - WaveSteepness * 0.99f);
		float heightChangeBound = MathF.Abs( b.z - a.z ) + new Vector2( b.x - a.x, b.y - a.y ).Length * slopeBound;
		float previous = WaveDistance( Vector3.Lerp( start, end, first ) );
		for ( float t = first; t < last; )
		{
			// The global gradient bound certifies empty space: this larger step cannot pass a root.
			// Near the surface retain the wavelength-based sampling interval used to find a bracket.
			float advance = MathF.Max( step, MathF.Abs( previous ) / MathF.Max( heightChangeBound, 0.000001f ) );
			float next = MathF.Min( t + advance, last );
			// Avoid repeating an unchanged parameter when the step is below float precision.
			if ( next <= t )
				break;
			float depth = WaveDistance( Vector3.Lerp( start, end, next ) );
			if ( (previous < 0) != (depth < 0) )
			{
				float low = t, high = next;
				for ( int i = 0; i < 12; i++ )
				{
					float middle = (low + high) * 0.5f;
					float sampled = WaveDistance( Vector3.Lerp( start, end, middle ) );
					if ( (sampled < 0) == (previous < 0) )
						low = middle;
					else
						high = middle;
				}
				fraction = (low + high) * 0.5f;
				position = Vector3.Lerp( start, end, fraction );
				return true;
			}
			t = next;
			previous = depth;
		}
		return false;

		bool Clip( float from, float to, float minimum, float maximum )
		{
			float delta = to - from;
			if ( MathF.Abs( delta ) < 0.000001f )
				return from >= minimum && from <= maximum;
			float enter = (minimum - from) / delta;
			float exit = (maximum - from) / delta;
			first = MathF.Max( first, MathF.Min( enter, exit ) );
			last = MathF.Min( last, MathF.Max( enter, exit ) );
			return first < last;
		}
	}

	internal static void CreateBulletSplash( Scene scene, Vector3 start, Vector3 end )
	{
		WaterBody hit = null;
		Vector3 hitPosition = default;
		float nearest = float.PositiveInfinity;
		foreach ( var water in scene.GetAll<WaterBody>() )
		{
			if ( !water.Active || !water.Effects || !water.TraceSurface( start, end, out var position, out float fraction ) )
				continue;

			if ( fraction > nearest || (fraction == nearest && hit.IsValid() && (!hit.Ocean || water.Ocean)) )
				continue;

			hit = water;
			hitPosition = position;
			nearest = fraction;
		}
		if ( hit.IsValid() )
			hit.CreateBulletSplash( hitPosition );
	}
}
