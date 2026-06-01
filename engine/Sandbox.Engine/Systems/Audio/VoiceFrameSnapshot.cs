using System;
using System.Collections.Generic;

namespace Sandbox.Audio;

/// <summary>
/// A self-contained description of one mix frame.
/// Written once by the main thread; the mix thread reads inputs and writes only to Output* fields.
/// </summary>
sealed class VoiceFrameSnapshot
{
	/// <summary>
	/// Voice entries. SourceCount &gt; 0 is a full state (sampled and mixed).
	/// SourceCount == 0 is sample-only (sampled so one-shots can retire, but not mixed).
	/// </summary>
	public List<VoiceState> Voices = new();
	public List<ListenerState> Listeners = new();
	public List<Listener> RemovedListeners = new();
	public List<DirectSoundModel> AllModels = new();
	public List<BinauralEffect> AllBinaurals = new();
	public List<DirectSoundParams> AllParams = new();
	public float MasterVolume;

	/// <summary>
	/// Written by the mix thread as a release barrier after all Output* fields are set.
	/// Zero means not yet processed.
	/// </summary>
	public volatile int WritebacksReady;

	public void Reset()
	{
		Voices.Clear();
		Listeners.Clear();
		RemovedListeners.Clear();
		AllModels.Clear();
		AllBinaurals.Clear();
		AllParams.Clear();
		WritebacksReady = 0;
	}

	/// <summary>
	/// Nulls sampler/source refs before returning a snapshot to the pool, preventing
	/// use-after-free if the mix thread re-picks it before a fresh one is published.
	/// Preserves Output*/Handle fields so ApplyWritebacks can still read them via _completedSnapshot.
	/// </summary>
	public void ResetForPool()
	{
		var voices = System.Runtime.InteropServices.CollectionsMarshal.AsSpan( Voices );
		for ( var i = 0; i < voices.Length; i++ )
		{
			voices[i].Sampler = null;
			voices[i].SourceCount = 0;
			voices[i].Reverb = null;
		}

		AllModels.Clear();
		AllBinaurals.Clear();
		AllParams.Clear();
	}
}

/// <summary>
/// Everything the mix thread needs to sample, mix, and spatialize one voice.
/// All inputs are immutable once published. Output* fields are written by the mix thread
/// and applied back to the SoundHandle by the main thread in the next tick's sync step.
/// </summary>
internal struct VoiceState
{
	public AudioSampler Sampler;
	public float Pitch;

	public float Volume;
	public float FadeVolume;

	public bool IsFadingOut;
	public RealTimeUntil FadeOutTimer;
	public bool IsFadingIn;
	public RealTimeUntil FadeInTimer;

	public bool ListenLocal;
	public bool Loopback;
	public bool IsVoice;
	public float SpacialBlend;
	public Vector3 Position;
	public Scene Scene;

	public Mixer TargetMixer;
	public float CreatedTime;
	public int SourceOffset;
	public int SourceCount;

	public bool HasLipSync;
	public SoundHandle.LipSyncAccessor LipSync;
	public SoundHandle Handle;
	public NativeReverbEffect Reverb;
	public ReverbSnapshot ReverbRoom;

	public float OutputAmplitude;
	public bool OutputFadeInComplete;
	public bool OutputFinished;
}

internal struct ListenerState
{
	public Listener Listener;
	public Transform MixTransform;
	public Scene Scene;
}

internal struct DirectSoundParams
{
	public Vector3 Position;
	public float Distance;
	public Curve Falloff;
	public FrequencyBands TransmissionBands;
	public bool DistanceAttenuation;
	public bool OcclusionEnabled;
	public bool AirAbsorption;
	public float ReverbAmount;
}
