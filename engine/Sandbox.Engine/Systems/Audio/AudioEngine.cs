
using NativeEngine;

namespace Sandbox.Audio;

/// <summary>
/// A mix frame size is 512 samples
/// A second is made up of 44100 samples
/// This means one frame is about 11.6ms
/// So as long as mixing takes less than 10ms we're okay
/// </summary>
internal static partial class AudioEngine
{
	public static bool IsValid => g_pAudioDevice.IsValid();

	/// <summary>
	/// How many output channels do we have? Generally 2, but if they have a 7.1 setup it can be more.
	/// </summary>
	public static int ChannelCount { get; private set; } = 2;

	/// <summary>
	/// How many seconds one sample lasts
	/// </summary>
	public static float SecondsPerSample => 1.0f / SamplingRate;

	/// <summary>
	/// The engine's output sampling rate. This doesn't change.
	/// </summary>
	public static float SamplingRate => 44100.0f; // MIX_DEFAULT_SAMPLING_RATE

	/// <summary>
	/// The size of one 
	/// </summary>
	public static int MixBufferSize => 512; // MIX_BUFFER_SIZE


	[ConVar( "voice_loopback", ConVarFlags.Protected )]
	public static bool VoiceLoopback { get; set; } = false;

	static AudioEngine()
	{
		if ( Application.IsUnitTest )
			return;

		if ( IsValid )
		{
			ChannelCount = (int)g_pAudioDevice.ChannelCount();
		}

		DspFactory.CreateBuiltIn();
	}

	public static void ClearBuffer()
	{
		if ( !IsValid )
			return;

		g_pAudioDevice.ClearBuffer();
	}

	[ConVar( "snd_mute", ConVarFlags.Saved )]
	public static bool Mute { get; set; }

	[ConVar( "snd_mute_losefocus", ConVarFlags.Saved )]
	public static bool MuteLoseFocus { get; set; }

	/// <summary>
	/// Called every frame. 
	/// </summary>
	internal static void Tick()
	{
		if ( !g_pAudioDevice.IsValid() )
			return;

		// calling g_pAudioDevice.MuteDevice actually starts the audio system for the first time.
		// if we never call it, the audio system will never start.

		var mute = Mute || (MuteLoseFocus && !InputSystem.IsAppActive());
		g_pAudioDevice.MuteDevice( mute );

		Game.Music.Tick();
	}
}
