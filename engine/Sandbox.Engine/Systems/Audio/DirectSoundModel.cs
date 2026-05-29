namespace Sandbox.Audio;

internal sealed class DirectSoundModel : IDisposable
{
	// ISO 9613-1 air absorption coefficients (20°C, 50% RH), nepers/m. World units are inches.
	const float InchesToMeters = 0.0254f;
	const float AirAbsLow = 0.000024f;
	const float AirAbsMid = 0.000084f;
	const float AirAbsHigh = 0.00012f;

	CDirectEffect _native;

	public Transform Transform { get; private set; }

	public void Dispose()
	{
		if ( !_native.IsNull )
		{
			MainThread.QueueDispose( _native );
			_native = default;
		}

		GC.SuppressFinalize( this );
	}

	~DirectSoundModel() => Dispose();

	FrequencyBands? _transmission;
	FrequencyBands _targetTransmission = FrequencyBands.One;

	FrequencyBands? _smoothedDiffraction;
	FrequencyBands _diffractionTarget = FrequencyBands.One;

	bool _firstTraceApplied;

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
		if ( !_firstTraceApplied )
			_smoothedDiffraction = value;
		_firstTraceApplied = true;
	}

	public void Update( Transform transform )
	{
		Transform = transform;

		var dt = RealTime.Delta;
		if ( Occlusion && !ListenLocal && GlobalOcclusionEnabled && _firstTraceApplied )
		{
			_transmission = _transmission.Value.Decay( _targetTransmission, 0.15f, dt );
			_smoothedDiffraction = _smoothedDiffraction.Value.Decay( _diffractionTarget, 0.8f, dt );
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
	public Curve Falloff { get; set; } = new(
		new( 0, 1, 0, -1.8f ),
		new( 0.05f, 0.22f, 3.5f, -3.5f ),
		new( 0.2f, 0.04f, 0.16f, -0.16f ),
		new( 1, 0 ) );

	internal DirectSoundParams GetParams() => new()
	{
		Position = Transform.Position,
		Distance = Distance,
		Falloff = Falloff,
		TransmissionBands = _transmission.HasValue && _smoothedDiffraction.HasValue
			? FrequencyBands.Max( _transmission.Value, _smoothedDiffraction.Value )
			: _transmission ?? _smoothedDiffraction ?? FrequencyBands.One,
		DistanceAttenuation = DistanceAttenuation,
		OcclusionEnabled = Occlusion,
		AirAbsorption = AirAbsorption,
		ReverbAmount = ReverbAmount,
	};

	public void Apply( in Listener listener, MultiChannelBuffer input, MultiChannelBuffer output,
		float occlusionMultiplier, float inputGain, in DirectSoundParams p )
	{
		var listenerPos = listener.MixTransform.Position;
		var distInUnits = p.Position.Distance( listenerPos );
		var distInMeters = distInUnits * InchesToMeters;

		var distAtten = p.DistanceAttenuation
			? p.Falloff.Evaluate( MathX.Clamp( distInUnits / p.Distance, 0f, 1f ) )
			: 1f;

		var airLow = p.AirAbsorption ? MathF.Exp( -AirAbsLow * distInMeters ) : 1f;
		var airMid = p.AirAbsorption ? MathF.Exp( -AirAbsMid * distInMeters ) : 1f;
		var airHigh = p.AirAbsorption ? MathF.Exp( -AirAbsHigh * distInMeters ) : 1f;

		var occlusion = 1f;
		var txLow = 1f; var txMid = 1f; var txHigh = 1f;

		if ( p.OcclusionEnabled )
		{
			var mul = occlusionMultiplier.Clamp( 0f, 1f );
			var tx = p.TransmissionBands;

			txLow = tx.Low;
			txMid = tx.Mid;
			txHigh = tx.High;

			// SA formula: output = occlusion + (1-occlusion)*tx. We pass occlusion=1-mul so
			// mul=1 (fully occluded) → SA sees occlusion=0 and applies transmission only.
			occlusion = 1f - mul;
		}

		// Always call _native.Apply to keep SA's internal gain interpolator state warm.
		// Skipping it freezes the interpolator, causing a pop when the sound becomes audible again.
		_native = _native.IsNull ? CDirectEffect.Create() : _native;
		output.Silence();
		// Fold inputGain into distAtten so SA's GainEffect interpolates the combined gain
		// per-sample rather than applying a coarse block-level scale that would click at frame boundaries.
		_native.Apply( distAtten * inputGain, airLow, airMid, airHigh, occlusion, txLow, txMid, txHigh, input._native, output._native );
	}
}
