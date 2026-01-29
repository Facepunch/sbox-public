namespace Sandbox.Audio;

/// <summary>
/// A source for the "direct" group of effects. This gets added to the SteamAudio scene and
/// is simulated for things like occlusion.
/// </summary>
class SteamAudioSource : IDisposable
{
	private DirectSource _direct;
	private BinauralEffect _binauralEffect;

	internal SteamAudioSource()
	{
		_direct = new DirectSource();
		_binauralEffect = new BinauralEffect();
	}

	~SteamAudioSource()
	{
		Dispose();
	}

	public void Dispose()
	{
		GC.SuppressFinalize( this );

		_direct?.Dispose();
		_direct = default;

		_binauralEffect?.Dispose();
		_binauralEffect = default;
	}

	/// <summary>
	/// Buffers should be mono in, mono out
	/// </summary>
	public void ApplyDirectMix( in Listener listener, MultiChannelBuffer input, MultiChannelBuffer output, float occlusionMultiplier, float inputGain )
	{
		_direct.Apply( listener, input, output, occlusionMultiplier, inputGain );
	}

	/// <summary>
	/// Buffers should be mono in, stereo out
	/// </summary>
	public void ApplyBinauralMix( Vector3 direction, float spatialBlend, bool useNearestInterpolation, MultiChannelBuffer input, MultiChannelBuffer output )
	{
		_binauralEffect.Apply( direction, spatialBlend, useNearestInterpolation, input, output );
	}

	/// <summary>
	/// Called by the sound handle at the appropriate times to update the native source
	/// </summary>
	internal void UpdateFrom( SoundHandle handle, PhysicsWorld world = default, Vector3 listenerPos = default )
	{
		_direct.ListenLocal = handle.ListenLocal;
		_direct.Distance = handle.Distance;
		_direct.Falloff = handle.Falloff;
		_direct.DistanceAttenuation = handle.DistanceAttenuation;
		_direct.AirAbsorption = handle.AirAbsorption;
		_direct.Transmission = handle.Transmission;
		_direct.Occlusion = handle.Occlusion;
		_direct.OcclusionSize = handle.OcclusionRadius;

		_direct.Update( handle.Transform, listenerPos, handle.TargetMixer ?? Mixer.Default, world );
	}

	/// <summary>
	/// Time tracking for occlusion updates, managed by SoundOcclusionSystem
	/// </summary>
	internal RealTimeUntil TimeUntilNextOcclusionCalc
	{
		get => _direct.TimeUntilNextOcclusionCalc;
		set => _direct.TimeUntilNextOcclusionCalc = value;
	}

	/// <summary>
	/// Set the target occlusion value. Called by SoundOcclusionSystem after computing occlusion.
	/// </summary>
	internal void SetTargetOcclusion( float value )
	{
		_direct.SetTargetOcclusion( value );
	}
}
