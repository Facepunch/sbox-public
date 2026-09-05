using Sandbox.Utility;
using System.Buffers;

namespace Sandbox;

/// <summary>
/// Access to the local user's microphone.
/// </summary>
public static class Microphone
{
	static ISteamUser steamUser;
	static byte[] compressedBuffer = new byte[1024 * 32];
	static Memory<byte> compressedMemory = Memory<byte>.Empty;

	static Microphone()
	{
		steamUser = NativeEngine.Steam.SteamUser();
	}

	static RealTimeSince timeSinceLastHear = 10;

	/// <summary>
	/// The sample rate (in Hz) of the 16-bit PCM samples produced by <see cref="Decompress"/>.
	/// </summary>
	public static int SampleRate => 44100;

	/// <summary>
	/// Whether voice capture is available at all. This is <c>false</c> if Steam isn't running.
	/// </summary>
	public static bool IsAvailable => steamUser.IsValid;

	/// <summary>
	/// Are we currently recording from the microphone? Becomes <c>true</c> after a successful
	/// <see cref="StartRecording"/> and <c>false</c> after <see cref="StopRecording"/>.
	/// </summary>
	public static bool IsRecording { get; private set; }

	/// <summary>
	/// Are we recording <b>and</b> actually capturing audible sound right now? This goes <c>false</c>
	/// shortly after the microphone falls silent, even while <see cref="IsRecording"/> stays <c>true</c>.
	/// </summary>
	public static bool IsActive => timeSinceLastHear < 0.2f;

	/// <summary>
	/// State of the on-screen microphone indicator (the "mic icon").
	/// </summary>
	public enum IndicatorState
	{
		/// <summary>Not recording - icon hidden.</summary>
		Hidden,
		/// <summary>Recording, but currently silent - icon shown.</summary>
		Open,
		/// <summary>Recording and capturing audible sound - icon shown as "hot".</summary>
		Capturing
	}

	/// <summary>
	/// Authoritative state for the on-screen microphone indicator (the "mic icon"), owned here next to
	/// the recording logic on purpose. Voice data can only reach a game via <see cref="OnCompressedData"/>,
	/// which only fires after <see cref="StartRecording"/> has set <see cref="IsRecording"/> - so any capture
	/// of the local user's mic necessarily drives this on. The indicator is the main protection against a game
	/// covertly recording the user (e.g. recording then silencing their voice), so this mapping must stay
	/// bound to the real recording state and must not be overridable by game code.
	/// </summary>
	public static IndicatorState Indicator =>
		!IsRecording ? IndicatorState.Hidden :
		IsActive ? IndicatorState.Capturing :
		IndicatorState.Open;

	/// <summary>
	/// Called on the main thread each time a chunk of compressed voice data is captured from the
	/// microphone (only while <see cref="IsRecording"/>). The data is Steam-compressed - send it to
	/// other players however you like, then decode it on the receiving end with <see cref="Decompress"/>.
	/// This is cleared automatically when the game closes.
	/// </summary>
	/// <remarks>
	/// The supplied memory is a view into a reused internal buffer. Copy it (e.g. with <c>.ToArray()</c>)
	/// if you need to keep it around beyond the callback.
	/// </remarks>
	public static Action<ReadOnlyMemory<byte>> OnCompressedData { get; set; }

	/// <summary>
	/// Same as <see cref="OnCompressedData"/> but for engine systems (party voice, voice components),
	/// so it survives <see cref="Reset"/> when the game closes.
	/// </summary>
	internal static event Action<ReadOnlyMemory<byte>> OnCompressedDataInternal;

	/// <summary>
	/// Start recording from the microphone. While recording, captured voice data is delivered via
	/// <see cref="OnCompressedData"/>. Returns <c>false</c> if voice capture isn't <see cref="IsAvailable">available</see>.
	/// </summary>
	/// <remarks>
	/// This sets <see cref="IsRecording"/>, which drives the on-screen mic indicator via <see cref="Indicator"/>.
	/// That coupling is deliberate and is the user's protection against covert recording - don't add a way to
	/// capture voice data without going through here.
	/// </remarks>
	public static bool StartRecording()
	{
		if ( !IsAvailable ) return false;

		IsRecording = true;
		steamUser.StartVoiceRecording();
		return true;
	}

	/// <summary>
	/// Stop recording from the microphone. Returns <c>false</c> if voice capture isn't <see cref="IsAvailable">available</see>.
	/// </summary>
	public static bool StopRecording()
	{
		if ( !IsAvailable ) return false;

		IsRecording = false;
		steamUser.StopVoiceRecording();
		return true;
	}

	/// <summary>
	/// Decode compressed voice data (as received via <see cref="OnCompressedData"/>) back into raw
	/// 16-bit PCM samples at <see cref="SampleRate"/>. The callback is invoked with the decoded samples.
	/// </summary>
	/// <remarks>
	/// The decoded samples are a view into a pooled buffer that's returned once the callback ends -
	/// copy them if you need to keep them.
	/// </remarks>
	/// <param name="data">The compressed voice data to decode.</param>
	/// <param name="onSamples">Receives the decoded 16-bit PCM samples.</param>
	public static void Decompress( byte[] data, Action<ReadOnlyMemory<short>> onSamples )
	{
		Uncompress( data, samples => onSamples( samples ) );
	}

	/// <summary>
	/// Called when the game closes. Clears the game's <see cref="OnCompressedData"/> callback and,
	/// if no engine system is listening either, stops recording.
	/// </summary>
	internal static void Reset()
	{
		OnCompressedData = null;

		if ( IsRecording && OnCompressedDataInternal is null )
		{
			StopRecording();
		}
	}

	static Superluminal _voiceTick = new Superluminal( "VoiceTick", Color.Yellow );
	static RealTimeSince timeSinceLastTick = 0;

	/// <summary>
	/// Pump Steam for any newly captured voice data. Called once per frame by the engine loop.
	/// </summary>
	internal static void Tick()
	{
		using ( _voiceTick.Start() )
		{
			if ( timeSinceLastTick < (1.0f / 30.0f) )
				return;

			timeSinceLastTick = 0;
			ReadVoice();
		}
	}

	static unsafe void ReadVoice()
	{
		uint dataSize = 0;

		// Check if there's any avaliable first, the subsequent call takes 0.1ms even if you're not recording..
		if ( steamUser.GetAvailableVoice( out var _ ) != 0 )
		{
			dataSize = 0;
			compressedMemory = Memory<byte>.Empty;
			return;
		}

		fixed ( byte* inPtr = compressedBuffer )
		{
			if ( steamUser.GetVoice( true, (IntPtr)inPtr, (uint)compressedBuffer.Length, out dataSize ) != 0 )
			{
				dataSize = 0;
				compressedMemory = Memory<byte>.Empty;
				return;
			}

			timeSinceLastHear = 0;
			compressedMemory = new Memory<byte>( compressedBuffer, 0, (int)dataSize );
			OnCompressedDataInternal?.Invoke( compressedMemory );
			OnCompressedData?.Invoke( compressedMemory );
		}
	}

	/// <summary>
	/// Uncompress a voice buffer and call ondata with the result
	/// </summary>
	internal static unsafe void Uncompress( byte[] buffer, Action<Memory<short>> ondata )
	{
		var shortSize = sizeof( short );
		var outBuffer = ArrayPool<short>.Shared.Rent( Math.Max( 20 * 1024, buffer.Length * 4 ) / shortSize );
		uint byteSize = 0;

		fixed ( byte* inPtr = buffer )
		fixed ( short* outPtr = outBuffer )
		{
			if ( steamUser.DecompressVoice( (IntPtr)inPtr, (uint)buffer.Length, (IntPtr)outPtr, (uint)(outBuffer.Length * shortSize), out byteSize, (uint)SampleRate ) == 0 )
			{
				if ( byteSize >= 2 && byteSize % 2 == 0 )
				{
					var memory = new Memory<short>( outBuffer, 0, (int)(byteSize / shortSize) );
					ondata?.Invoke( memory );
				}
			}
		}

		ArrayPool<short>.Shared.Return( outBuffer );
	}
}
