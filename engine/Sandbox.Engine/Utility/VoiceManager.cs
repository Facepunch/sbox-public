using NativeEngine;
using Sandbox.Utility;
using System.Buffers;
using System.Buffers.Binary;

namespace Sandbox;

/// <summary>
/// Captures the microphone through SDL and encodes it with Opus. The device is only open while
/// something wants to record. A packet is a run of Opus frames, each prefixed with a ushort length.
/// </summary>
internal static class VoiceManager
{
	const int Rate = 48000;
	const int FrameSamples = Rate / 50; // 20ms
	const int MaxFrameSamples = Rate * 120 / 1000; // largest frame Opus can decode
	const int Bitrate = 32000;

	/// <summary>
	/// Keep capturing for a bit after a stop, people let go of push to talk early
	/// </summary>
	const float TailTime = 0.2f;

	/// <summary>
	/// Keep sending for a bit after it goes quiet, so we don't clip the end of words
	/// </summary>
	const float GateHoldTime = 0.3f;

	public static int SampleRate => Rate;

	/// <summary>
	/// Most samples a single packet can decode to
	/// </summary>
	internal static int MaxPacketSamples => Rate;

	public static Action<Memory<byte>> OnCompressedVoiceData { get; set; }

	static IntPtr capture;
	static string captureDevice;
	static IntPtr encoder;
	static IntPtr sharedDecoder;

	// 200ms is the most we'll read in one tick; native drops anything older than that
	static float[] captureBuffer = new float[Rate / 5];
	static int captureCount;
	static byte[] packetBuffer = new byte[1024 * 32];

	static RealTimeSince timeSinceLastHear = 10;
	static RealTimeSince timeSinceStopped = 10;
	static RealTimeSince timeSinceLoud = 10;

	/// <summary>
	/// Mic that wouldn't open. Not tried again until a device comes or goes, or another is picked.
	/// </summary>
	static string failedDevice;

	public static bool IsValid => !Application.IsHeadless;
	public static bool IsListening { get; private set; }
	public static bool IsRecording => timeSinceLastHear < 0.2f;

	public static bool StartRecording()
	{
		if ( !IsValid ) return false;

		IsListening = true;
		return true;
	}

	public static bool StopRecording()
	{
		if ( !IsValid ) return false;

		if ( IsListening )
			timeSinceStopped = 0;

		IsListening = false;
		return true;
	}

	/// <summary>
	/// Names of the microphones we can record from
	/// </summary>
	public static IEnumerable<string> GetRecordingDevices()
	{
		var count = VoiceGlue.GetRecordingDeviceCount();
		for ( int i = 0; i < count; i++ )
		{
			yield return VoiceGlue.GetRecordingDeviceName( i );
		}
	}

	/// <summary>
	/// Name of the microphone the system is using as its default right now, empty if there isn't one
	/// </summary>
	public static string GetDefaultRecordingDevice() => VoiceGlue.GetDefaultRecordingDeviceName() ?? "";

	static Superluminal _voiceTick = new Superluminal( "VoiceTick", Color.Yellow );

	static RealTimeSince timeSinceLastTick = 0;
	public static void Tick()
	{
		using ( _voiceTick.Start() )
		{
			if ( timeSinceLastTick < (1.0f / 30.0f) )
				return;

			timeSinceLastTick = 0;

			var sending = IsListening || timeSinceStopped < TailTime;
			var testing = timeSinceTestRequested < 0.5f;

			// Nobody wants the mic, let it go
			if ( !sending && !testing )
			{
				CloseCapture();
				return;
			}

			// Picked a different mic in the settings. A test listens to the one it asked for,
			// unless we're actually talking.
			var device = (sending ? Preferences.VoiceDevice : testDevice) ?? "";
			if ( capture != IntPtr.Zero && captureDevice != device )
				CloseCapture();

			if ( capture == IntPtr.Zero && !OpenCapture( device ) )
				return;

			ReadVoice( sending );
		}
	}

	/// <summary>
	/// Quietest level we report, in decibels. What <see cref="Level"/> reads when the mic is closed.
	/// </summary>
	public const float MinLevel = -96.0f;

	/// <summary>
	/// How loud the microphone was over the last tick, in decibels. Only meaningful while
	/// something is recording or testing.
	/// </summary>
	public static float Level { get; private set; } = MinLevel;

	static RealTimeSince timeSinceTestRequested = 10;
	static string testDevice;

	/// <summary>
	/// Keep this microphone open so <see cref="Level"/> can be shown, without sending anything.
	/// Call it every frame while testing, the mic closes shortly after you stop.
	/// </summary>
	public static void TestMicrophone( string device )
	{
		if ( !IsValid ) return;

		testDevice = device;
		timeSinceTestRequested = 0;
	}

	static bool OpenCapture( string device )
	{
		if ( device == failedDevice )
			return false;

		encoder = VoiceGlue.CreateEncoder( Rate, Bitrate );
		capture = VoiceGlue.OpenCapture( device, Rate );
		if ( capture == IntPtr.Zero || encoder == IntPtr.Zero )
		{
			CloseCapture();
			failedDevice = device;
			return false;
		}

		captureDevice = device;
		return true;
	}

	/// <summary>
	/// SDL says a device was plugged in or out. It keeps an unplugged mic's stream open but
	/// silent, so close it ourselves. Either way, let the next tick try opening again by name.
	/// </summary>
	internal static void OnAudioDevicesChanged()
	{
		failedDevice = null;

		// The default device follows whatever the system picks, SDL handles that
		if ( capture == IntPtr.Zero || string.IsNullOrEmpty( captureDevice ) )
			return;

		if ( !GetRecordingDevices().Contains( captureDevice ) )
			CloseCapture();
	}

	static void CloseCapture()
	{
		VoiceGlue.DestroyEncoder( encoder );
		encoder = IntPtr.Zero;

		if ( capture == IntPtr.Zero )
			return;

		VoiceGlue.CloseCapture( capture );
		capture = IntPtr.Zero;
		captureCount = 0;
		timeSinceLoud = 10;
		Level = MinLevel;
	}

	static unsafe void ReadVoice( bool send )
	{
		fixed ( float* samples = captureBuffer )
		{
			captureCount += VoiceGlue.ReadCapture( capture, (IntPtr)(samples + captureCount), captureBuffer.Length - captureCount );
		}

		int packetLength = 0;
		int offset = 0;
		float loudest = MinLevel;

		for ( ; captureCount - offset >= FrameSamples; offset += FrameSamples )
		{
			var frame = captureBuffer.AsSpan( offset, FrameSamples );
			var level = FrameLevel( frame );
			loudest = MathF.Max( loudest, level );

			if ( !send )
				continue;

			// Only send when it's louder than the threshold, Steam used to do this for us
			if ( level > Preferences.VoiceThreshold )
				timeSinceLoud = 0;

			if ( timeSinceLoud > GateHoldTime )
				continue;

			// Encode straight into the packet after a ushort length prefix
			int bytes;
			fixed ( float* samples = frame )
			fixed ( byte* dest = packetBuffer )
			{
				bytes = VoiceGlue.Encode( encoder, (IntPtr)samples, FrameSamples, (IntPtr)(dest + packetLength + 2), packetBuffer.Length - packetLength - 2 );
			}

			if ( bytes <= 0 )
				continue;

			BinaryPrimitives.WriteUInt16LittleEndian( packetBuffer.AsSpan( packetLength ), (ushort)bytes );
			packetLength += 2 + bytes;
		}

		// Keep the partial frame for next tick
		captureBuffer.AsSpan( offset, captureCount - offset ).CopyTo( captureBuffer );
		captureCount -= offset;

		// No whole frame this tick, keep showing the last one
		if ( offset > 0 )
			Level = loudest;

		if ( packetLength == 0 )
			return;

		timeSinceLastHear = 0;
		OnCompressedVoiceData?.Invoke( new Memory<byte>( packetBuffer, 0, packetLength ) );
	}

	/// <summary>
	/// RMS loudness of a frame in decibels, 0 being full scale
	/// </summary>
	static float FrameLevel( ReadOnlySpan<float> frame )
	{
		float sum = 0;
		foreach ( var s in frame )
			sum += s * s;

		var db = 20.0f * MathF.Log10( MathF.Sqrt( sum / frame.Length ) + 1e-9f );
		return MathF.Max( db, MinLevel );
	}

	internal static IntPtr CreateDecoder() => VoiceGlue.CreateDecoder( Rate );
	internal static void DestroyDecoder( IntPtr decoder ) => VoiceGlue.DestroyDecoder( decoder );

	/// <summary>
	/// Decode a packet into output, returns the number of samples written. Opus keeps state
	/// between frames, so each speaker needs their own decoder.
	/// </summary>
	internal static unsafe int Decode( IntPtr decoder, ReadOnlySpan<byte> packet, Span<short> output )
	{
		if ( decoder == IntPtr.Zero )
			return 0;

		int samples = 0;
		int offset = 0;

		while ( offset + 2 <= packet.Length && output.Length - samples >= MaxFrameSamples )
		{
			int length = BinaryPrimitives.ReadUInt16LittleEndian( packet.Slice( offset ) );
			offset += 2;

			if ( length == 0 || offset + length > packet.Length )
				break;

			fixed ( byte* data = packet.Slice( offset, length ) )
			fixed ( short* dest = output.Slice( samples ) )
			{
				var decoded = VoiceGlue.Decode( decoder, (IntPtr)data, length, (IntPtr)dest, output.Length - samples );
				if ( decoded > 0 )
					samples += decoded;
			}

			offset += length;
		}

		return samples;
	}

	/// <summary>
	/// Uncompress a voice buffer and call ondata with the result. Shares one decoder between
	/// everyone, so prefer <see cref="SoundStream.WriteVoiceData"/> which has one per stream.
	/// </summary>
	public static void Uncompress( byte[] buffer, Action<Memory<short>> ondata )
	{
		if ( sharedDecoder == IntPtr.Zero )
			sharedDecoder = CreateDecoder();

		var output = ArrayPool<short>.Shared.Rent( MaxPacketSamples );

		var samples = Decode( sharedDecoder, buffer, output );
		if ( samples > 0 )
			ondata?.Invoke( new Memory<short>( output, 0, samples ) );

		ArrayPool<short>.Shared.Return( output );
	}
}
