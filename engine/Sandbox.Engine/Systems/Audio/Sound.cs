namespace Sandbox;

/// <summary>
/// Single source for creating sounds
/// </summary>
public static partial class Sound
{
	/// <summary>
	/// Sound listener of the active scene.
	/// </summary>
	public static Transform Listener
	{
		get => Game.ActiveScene?.Listener?.Transform ?? Transform.Zero;
		set
		{
			if ( Game.ActiveScene is not Scene scene )
				return;

			scene.Listener ??= new( scene );
			scene.Listener.Transform = value;
		}
	}

	/// <summary>
	/// The user's preference for their master volume.
	/// </summary>
	[ConVar( "volume", ConVarFlags.Protected | ConVarFlags.Saved, Help = "Global volume output", Min = 0.0f )]
	public static float MasterVolume { get; internal set; } = 1.0f;

	[System.Obsolete]
	public static void SetEffect( string name, float value, float velocity = 10.0f, float fadeOut = -1 )
	{

	}

	internal static void Clear()
	{
		Audio.Listener.Clear();
	}


	/// <summary>
	/// Precaches sound files associated with given sound event by name.
	/// This helps avoid stutters on first load of each sound file.
	/// </summary>
	public static void Preload( string eventName )
	{
		var se = SoundEvent.Find( eventName );
		if ( se is not null )
		{
			foreach ( var soundFile in se.Sounds )
			{
				if ( soundFile is not null )
				{
					soundFile.Preload();
				}
			}
		}
	}

	/// <summary>
	/// Get a list of available DSP names
	/// </summary>
	public static string[] DspNames { get; internal set; }


	/// <summary>
	/// Uncompress the voice data
	/// </summary>
	public static unsafe void UncompressVoiceData( byte[] buffer, Action<Memory<short>> ondata )
	{
		VoiceManager.Uncompress( buffer, ondata );
	}

	/// <summary>
	/// The sample rate for voice data
	/// </summary>
	public static int VoiceSampleRate => VoiceManager.SampleRate;

}
