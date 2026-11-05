namespace Sandbox.Audio;

internal sealed class DirectSoundModel : IDisposable
{
	// ISO 9613-1 air absorption coefficients (20°C, 50% RH), nepers/m. World units are inches.
	const float InchesToMeters = 0.0254f;
	const float AirAbsLow = 0.000024f;
	const float AirAbsMid = 0.000084f;
	const float AirAbsHigh = 0.00012f;

	CDirectEffect _native;
	CDirectEffect _nativeReverb;

	public Transform Transform { get; private set; }

	public void Dispose()
	{
		if ( !_native.IsNull )
		{
			MainThread.QueueDispose( _native );
			_native = default;
		}

		if ( !_nativeReverb.IsNull )
		{
			MainThread.QueueDispose( _nativeReverb );
			_nativeReverb = default;
		}

		GC.SuppressFinalize( this );
	}

	~DirectSoundModel() => Dispose();

	FrequencyBands? _transmission;
	FrequencyBands _targetTransmission = FrequencyBands.One;

	FrequencyBands? _smoothedDiffraction;
	FrequencyBands _diffractionTarget = FrequencyBands.One;

	bool _firstTraceApplied;

	// Sim side state, only touched by the occlusion update for this source/listener pair.
	internal AcousticOcclusion.DiffractionPath DiffractionPath;

	internal static bool GlobalOcclusionEnabled { get; set; } = true;
	internal FrequencyBands? SmoothedTransmission => _transmission;
	internal FrequencyBands? SmoothedDiffraction => _smoothedDiffraction;
	internal bool HasFirstTrace => _firstTraceApplied;
	internal float AvgWalls { get; private set; }

	internal void SetTargetTransmission( FrequencyBands value, float avgWalls = 0f )
	{
		_targetTransmission = value;
		AvgWalls = avgWalls;
		if ( !_firstTraceApplied )
			_transmission = value;
	}

	internal int LastDiffractionProbes { get; private set; }
	internal int LastDiffractionRays { get; private set; }

	internal void SetTargetDiffraction( FrequencyBands value, int probesFound = 0, int probesTotal = 0 )
	{
		_diffractionTarget = value;
		LastDiffractionProbes = probesFound;
		LastDiffractionRays = probesTotal;
		if ( !_firstTraceApplied ) _smoothedDiffraction = value;
		_firstTraceApplied = true;
	}

	// Seed both bands before the first trace. GetParams uses Max, so seeding one alone gets masked.
	// markTraced=true counts it as already traced (a measured prime); false leaves it for the sim.
	internal void SetInitialOcclusion( FrequencyBands value, bool markTraced = false )
	{
		_targetTransmission = value;
		_transmission = value;
		_diffractionTarget = value;
		_smoothedDiffraction = value;
		if ( markTraced ) _firstTraceApplied = true;
	}

	public void Update( Transform transform ) => Update( transform, RealTime.Delta );

	internal void Update( Transform transform, float dt )
	{
		Transform = transform;

		if ( Occlusion && !ListenLocal && GlobalOcclusionEnabled && _firstTraceApplied )
		{
			_transmission = _transmission.Value.Decay( _targetTransmission, 0.15f, dt );
			_smoothedDiffraction = _smoothedDiffraction.Value.Decay( _diffractionTarget, 0.3f, dt );
		}
		else if ( Occlusion && !ListenLocal && GlobalOcclusionEnabled )
		{
			_transmission = _targetTransmission;
			_smoothedDiffraction = _diffractionTarget;
		}
		else
		{
			_firstTraceApplied = false;
			_targetTransmission = FrequencyBands.One;
			_transmission = _targetTransmission;
			_diffractionTarget = FrequencyBands.One;
			_smoothedDiffraction = _diffractionTarget;
		}
	}

	public bool ListenLocal { get; set; }
	public bool AirAbsorption { get; set; } = true;
	public bool Occlusion { get; set; } = true;
	public bool DistanceAttenuation { get; set; } = true;
	public float ReverbAmount { get; set; } = 1.0f;
	public float Distance { get; set; } = 15_000f;
	public Curve Falloff { get; set; } = SoundFalloff.ClassicCurve;
	public SoundFalloffMode FalloffMode { get; set; } = SoundFalloffMode.Physical;
	public float MinDistance { get; set; }

	internal DirectSoundParams GetParams() => new()
	{
		Position = Transform.Position,
		Distance = Distance,
		Falloff = Falloff,
		FalloffMode = FalloffMode,
		MinDistance = MinDistance,
		TransmissionBands = _transmission.HasValue && _smoothedDiffraction.HasValue
			? FrequencyBands.Max( _transmission.Value, _smoothedDiffraction.Value )
			: _transmission ?? _smoothedDiffraction ?? FrequencyBands.One,
		DistanceAttenuation = DistanceAttenuation,
		OcclusionEnabled = Occlusion,
		AirAbsorption = AirAbsorption,
		ReverbAmount = ReverbAmount,
	};

	// Below this the direct sound can't be heard (-80 dB).
	const float Inaudible = 1e-4f;
	bool _silent;

	/// <summary>
	/// True while the direct sound is too quiet to hear and its effects already settled on silence last buffer, so this
	/// one can skip them. Coming back, the direct effect ramps up from that silence.
	/// </summary>
	internal bool SkipSilent( in Listener listener, float inputGain, float occlusionMultiplier, in DirectSoundParams p )
	{
		var distInUnits = p.Position.Distance( listener.MixTransform.Position );
		var gain = inputGain * (p.DistanceAttenuation ? SoundFalloff.Gain( p.FalloffMode, p.Falloff, p.MinDistance, p.Distance, distInUnits ) : 1f);

		OcclusionParams( occlusionMultiplier, in p, out var occlusion, out var txLow, out var txMid, out var txHigh );
		gain *= occlusion + (1f - occlusion) * MathF.Max( txLow, MathF.Max( txMid, txHigh ) );

		bool silent = gain < Inaudible;
		bool skip = silent && _silent;
		_silent = silent;
		return skip;
	}

	public void Apply( in Listener listener, MultiChannelBuffer input, MultiChannelBuffer output,
		float occlusionMultiplier, float inputGain, in DirectSoundParams p )
	{
		var listenerPos = listener.MixTransform.Position;
		var distInUnits = p.Position.Distance( listenerPos );
		var distInMeters = distInUnits * InchesToMeters;

		var distAtten = p.DistanceAttenuation
			? SoundFalloff.Gain( p.FalloffMode, p.Falloff, p.MinDistance, p.Distance, distInUnits )
			: 1f;

		var airLow = p.AirAbsorption ? MathF.Exp( -AirAbsLow * distInMeters ) : 1f;
		var airMid = p.AirAbsorption ? MathF.Exp( -AirAbsMid * distInMeters ) : 1f;
		var airHigh = p.AirAbsorption ? MathF.Exp( -AirAbsHigh * distInMeters ) : 1f;

		OcclusionParams( occlusionMultiplier, in p, out var occlusion, out var txLow, out var txMid, out var txHigh );

		// Always call _native.Apply to keep SA's internal gain interpolator state warm.
		// Skipping it freezes the interpolator, causing a pop when the sound becomes audible again.
		_native = _native.IsNull ? CDirectEffect.Create() : _native;
		output.Silence();
		// Fold inputGain into distAtten so SA's GainEffect interpolates the combined gain
		// per-sample rather than applying a coarse block-level scale that would click at frame boundaries.
		_native.Apply( distAtten * inputGain, airLow, airMid, airHigh, occlusion, txLow, txMid, txHigh, input._native, output._native );
	}

	static void OcclusionParams( float occlusionMultiplier, in DirectSoundParams p, out float occlusion, out float txLow, out float txMid, out float txHigh )
	{
		occlusion = 1f;
		txLow = 1f; txMid = 1f; txHigh = 1f;

		if ( !p.OcclusionEnabled ) return;

		var mul = occlusionMultiplier.Clamp( 0f, 1f );
		var tx = p.TransmissionBands;

		txLow = tx.Low;
		txMid = tx.Mid;
		txHigh = tx.High;

		// SA formula: output = occlusion + (1-occlusion)*tx. We pass occlusion=1-mul so
		// mul=1 (fully occluded) → SA sees occlusion=0 and applies transmission only.
		occlusion = 1f - mul;
	}

	/// <summary>
	/// The source room's reverb reaches the listener the way the direct sound does: muffled per band by the walls in between
	/// or the path around them. Mono in, mono out, its own effect so its gain interpolation doesn't fight the direct path's.
	/// </summary>
	public void FilterReverbSend( MultiChannelBuffer input, MultiChannelBuffer output, float occlusionMultiplier, in DirectSoundParams p )
	{
		OcclusionParams( occlusionMultiplier, in p, out var occlusion, out var txLow, out var txMid, out var txHigh );

		_nativeReverb = _nativeReverb.IsNull ? CDirectEffect.Create() : _nativeReverb;
		output.Silence();
		_nativeReverb.Apply( 1f, 1f, 1f, 1f, occlusion, txLow, txMid, txHigh, input._native, output._native );
	}
}
