namespace Sandbox
{
	/// <summary>
	/// How a sound's volume falls off with distance.
	/// </summary>
	public enum SoundFalloffMode
	{
		/// <summary>
		/// Inverse distance: full volume within the min distance, half the volume every time the distance doubles beyond it,
		/// faded out towards the max distance.
		/// </summary>
		Physical,

		/// <summary>
		/// The falloff curve, evaluated from the source (0) to the max distance (1).
		/// </summary>
		Curve,
	}
}

namespace Sandbox.Audio
{
	internal static class SoundFalloff
	{
		/// <summary>
		/// The stock falloff curve every sound had before <see cref="SoundFalloffMode.Physical"/>. Sounds still using it get the physical model.
		/// </summary>
		public static Curve ClassicCurve => new( new( 0, 1, 0, -1.8f ), new( 0.05f, 0.22f, 3.5f, -3.5f ), new( 0.2f, 0.04f, 0.16f, -0.16f ), new( 1, 0 ) );

		// An unset min distance is a 70th of the max distance (37 dB down there before the fade), which keeps sounds mixed against the
		// classic curve close to how they were.
		const float AutoMinDistanceFraction = 1f / 70f;

		// Physical sounds fade out over the last half of their range, gently enough that sounds near their end trail off
		// rather than cut, while the max distance leaves the near and mid falloff alone.
		const float FadeStart = 0.5f;

		public static float MinDistance( float minDistance, float maxDistance )
			=> minDistance > 0f ? minDistance : MathF.Max( maxDistance, 1f ) * AutoMinDistanceFraction;

		public static float RangeFade( float distance, float maxDistance )
		{
			float t = (distance / MathF.Max( maxDistance, 1f ) - FadeStart) / (1f - FadeStart);
			if ( t <= 0f ) return 1f;
			if ( t >= 1f ) return 0f;
			return 1f - t * t * (3f - 2f * t);
		}

		/// <summary>
		/// Gain of the direct sound at <paramref name="distance"/> units from the source.
		/// </summary>
		public static float Gain( SoundFalloffMode mode, in Curve falloff, float minDistance, float maxDistance, float distance )
		{
			if ( mode == SoundFalloffMode.Curve )
				return falloff.Evaluate( MathX.Clamp( distance / MathF.Max( maxDistance, 1f ), 0f, 1f ) );

			float near = MinDistance( minDistance, maxDistance );
			float gain = distance <= near ? 1f : near / distance;
			return gain * RangeFade( distance, maxDistance );
		}

		public static bool IsClassic( in Curve curve )
		{
			var frames = curve.Frames;
			var classic = ClassicCurve.Frames;
			if ( frames.IsDefault || frames.Length != classic.Length ) return false;

			for ( int i = 0; i < frames.Length; i++ )
			{
				if ( !Same( frames[i].Time, classic[i].Time ) || !Same( frames[i].Value, classic[i].Value )
					|| !Same( frames[i].In, classic[i].In ) || !Same( frames[i].Out, classic[i].Out ) ) return false;
			}

			return true;
		}

		static bool Same( float a, float b ) => MathF.Abs( a - b ) < 1e-4f;
	}
}
