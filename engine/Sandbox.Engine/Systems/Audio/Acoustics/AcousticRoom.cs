namespace Sandbox.Audio;

/// <summary>
/// Room kernels: a few diffuse rays per update estimate openness, mean free path and wall absorption,
/// smoothed over time into a <see cref="ReverbSnapshot"/>.
/// </summary>
internal static class AcousticRoom
{
	public const int SourceRays = 1;
	public const float MaxSourceDist = 4096f;
	public const float ResetDist = 256f;
	public const float SeedDist = 128f;
	public const int MaxSourcesPerTick = 10;

	const int RayBounces = 5;
	const float MaxBounceLen = 8192f;
	const float UnitsPerMeter = 39.37f;
	const float AccumHalfLife = 0.6f;

	public struct Sample
	{
		public int RayCount;
		public float EscapedWeight;
		public float TotalDist;
		public FrequencyBands TotalRefl;
		public int BounceCount;
		public int SegmentCount;
	}

	public sealed class Estimator
	{
		public bool HasBurst;
		public Vector3 LastPos;
		public float LastUpdateTime = -1f;
		public int LastUpdateTick = -1;

		public float SmoothedOpenness = 1f;
		public float SmoothedDecayDist;
		public FrequencyBands SmoothedMaterialTone = FrequencyBands.One;

		public ReverbSnapshot Snapshot;

		public void SeedFrom( Estimator other )
		{
			HasBurst = true;
			LastUpdateTime = other.LastUpdateTime;
			SmoothedOpenness = other.SmoothedOpenness;
			SmoothedDecayDist = other.SmoothedDecayDist;
			SmoothedMaterialTone = other.SmoothedMaterialTone;
		}
	}

	public static float Priority( bool hasBurst, float secondsWaited, float dist )
		=> !hasBurst ? 1e6f - dist / 512f : secondsWaited / (1f + dist / 512f);

	public static int RayCount( float dist )
	{
		float distT = dist.Remap( 512f, MaxSourceDist, 0f, 1f ).Clamp( 0f, 1f );
		return Math.Max( 2, (int)(SourceRays * (1f - distT)) );
	}

	public static Vector3 RandomDirection( Random rng ) => rng.VectorInSphere().Normal;

	public static void TraceRays<T>( ref T tracer, Vector3 origin, int rayCount, Random rng, ref Sample s ) where T : IAcousticTracer, allows ref struct
	{
		s.RayCount += rayCount;

		for ( int r = 0; r < rayCount; r++ )
		{
			var pos = origin;
			var dir = RandomDirection( rng );
			int rayBounces = 0;

			for ( int b = 0; b < RayBounces; b++ )
			{
				if ( !tracer.Trace( pos, pos + dir * MaxBounceLen, AcousticIgnore.Source, out var hit ) )
				{
					s.EscapedWeight += EscapeWeight( rayBounces );
					break;
				}

				// The first segment starts inside the room rather than on a wall, so it isn't a free path.
				if ( rayBounces > 0 )
				{
					s.TotalDist += pos.Distance( hit.Position );
					s.SegmentCount++;
				}
				s.TotalRefl += AcousticMaterial.GetReflectivity( hit.Surface );
				s.BounceCount++;
				rayBounces++;

				dir = (hit.Normal + RandomDirection( rng )).Normal;
				pos = hit.Position + hit.Normal * 4f;
			}
		}
	}

	public static void ApplySample( Estimator est, in Sample s, float now )
	{
		float openness = s.EscapedWeight / s.RayCount;
		float decayDist = s.SegmentCount > 0 ? s.TotalDist / s.SegmentCount : 0f;
		// Eyring: the room's absorption is -ln of the mean reflectivity, not the mean of -ln (that over weights absorptive patches).
		var tone = s.BounceCount > 0 ? s.TotalRefl / s.BounceCount : FrequencyBands.One;

		float dt = est.LastUpdateTime < 0f ? 0f : now - est.LastUpdateTime;
		est.LastUpdateTime = now;

		if ( dt <= 0f )
		{
			est.SmoothedOpenness = openness;
			est.SmoothedDecayDist = decayDist;
			est.SmoothedMaterialTone = tone;
		}
		else
		{
			est.SmoothedOpenness = MathX.ExponentialDecay( est.SmoothedOpenness, openness, AccumHalfLife, dt );
			est.SmoothedDecayDist = MathX.ExponentialDecay( est.SmoothedDecayDist, decayDist, AccumHalfLife, dt );
			est.SmoothedMaterialTone = est.SmoothedMaterialTone.Decay( tone, AccumHalfLife, dt );
		}
	}

	/// <summary>
	/// Snapshot from the estimator. Sources pass how enclosed the listener is, which gates how much of their room's reverb
	/// reaches it; the listener's own room has no mix.
	/// </summary>
	public static ReverbSnapshot BuildSnapshot( Estimator est, bool source, float listenerEnclosure )
	{
		var tone = est.SmoothedMaterialTone;
		float toneLow = MathF.Max( tone.Low, 0.001f );
		float toneMid = MathF.Max( tone.Mid, 0.001f );
		float toneHigh = MathF.Max( tone.High, 0.001f );
		float absLow = -MathF.Log( toneLow );
		float absMid = -MathF.Log( toneMid );
		float absHigh = -MathF.Log( toneHigh );
		float absEff = -MathF.Log( MathF.Cbrt( toneLow * toneMid * toneHigh ) );
		float mfpMeters = est.SmoothedDecayDist / UnitsPerMeter;
		float decayTime = absEff > 1e-6f ? 0.040f * mfpMeters / absEff : 0f;
		float decayLow = mfpMeters > 0f && absLow > 0f ? 0.040f * mfpMeters / absLow : 0f;
		float decayMid = mfpMeters > 0f && absMid > 0f ? 0.040f * mfpMeters / absMid : 0f;
		float decayHigh = mfpMeters > 0f ? 0.040f * mfpMeters / (absHigh + 0.006f * mfpMeters) : 0f;

		var snapshot = new ReverbSnapshot
		{
			DecayTime = decayTime,
			MfpMeters = mfpMeters,
			Openness = est.SmoothedOpenness,
			MaterialTone = tone,
			DecayTimeLow = decayLow,
			DecayTimeMid = decayMid,
			DecayTimeHigh = decayHigh,
			CriticalDistanceMeters = mfpMeters * MathF.Sqrt( mfpMeters / MathF.Max( 90f * decayTime, 0.001f ) ),
		};

		// How loud the reverb is against the direct sound depends on distance, that's done per voice in the mixer.
		if ( source ) snapshot.Mix = (1f - est.SmoothedOpenness) * listenerEnclosure;

		return snapshot;
	}

	static float EscapeWeight( int bounces ) => bounces <= 1 ? 1f : MathF.Pow( 0.4f, bounces - 1 );
}
