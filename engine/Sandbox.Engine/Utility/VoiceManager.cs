using Sandbox.Utility;
using System.Buffers;

namespace Sandbox;

internal static class VoiceManager
{
	static ISteamUser steamUser;
	static byte[] compressedBuffer = new byte[1024 * 32];
	static Memory<byte> compressedMemory = Memory<byte>.Empty;

	public static Action<Memory<byte>> OnCompressedVoiceData { get; set; }

	static VoiceManager()
	{
		steamUser = NativeEngine.Steam.SteamUser();
	}

	static RealTimeSince timeSinceLastHear = 10;

	public static int SampleRate => 44100;
	public static bool IsValid => steamUser.IsValid;
	public static bool IsListening { get; private set; }
	public static bool IsRecording => timeSinceLastHear < 0.2f;
	private static bool _listenActive;

	public static bool StartRecording()
	{
		if ( !IsValid ) return false;

		IsListening = true;
		_listenActive = true;
		steamUser.StartVoiceRecording();
		return true;
	}

	public static bool StopRecording()
	{
		if ( !IsValid ) return false;

		IsListening = false;
		steamUser.StopVoiceRecording();
		return true;
	}

	static Superluminal _voiceTick = new Superluminal( "VoiceTick", Color.Yellow );

	static RealTimeSince timeSinceLastTick = 0;
	public static void Tick()
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
		if ( !_listenActive || steamUser.GetAvailableVoice( out var _ ) != 0 )
		{
			if ( _listenActive && !IsListening && !IsRecording )
				_listenActive = false;
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
			OnCompressedVoiceData?.Invoke( compressedMemory );
		}
	}

	/// <summary>
	/// Uncompress a voice buffer and call ondata with the result
	/// </summary>
	public static unsafe void Uncompress( byte[] buffer, Action<Memory<short>> ondata )
	{
		var shortSize = sizeof( short );
		var outBuffer = ArrayPool<short>.Shared.Rent( Math.Max( 20 * 1024, buffer.Length * 4 ) / shortSize );
		uint byteSize = 0;

		fixed ( byte* inPtr = buffer )
		fixed ( short* outPtr = outBuffer )
		{
			if ( steamUser.DecompressVoice( (IntPtr)inPtr, (uint)buffer.Length, (IntPtr)outPtr, (uint)(outBuffer.Length * shortSize), out byteSize, (uint)VoiceManager.SampleRate ) == 0 )
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
