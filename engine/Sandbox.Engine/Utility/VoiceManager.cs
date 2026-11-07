using NativeEngine;
using Sandbox.Utility;
using System.Buffers;
using System.Buffers.Binary;
using Sandbox.Platform;

namespace Sandbox;

/// <summary>
/// Captures the microphone through SDL and encodes it with Opus. The device is only open while
/// something wants to record, or between presses with <see cref="Preferences.StreamlinedPushToTalk"/>.
/// A packet is a run of Opus frames, each prefixed with a ushort length.
/// </summary>
internal static partial class VoiceManager
{
	const int Rate = 48000;
	const int FrameSamples = Rate / 50; // 20ms
	const int MaxFrameSamples = Rate * 120 / 1000; // largest frame Opus can decode
	const int Bitrate = 32000;

	internal const int MaxPacketLength = 1024 * 32;

	/// <summary>
	/// Keep capturing for a bit after a stop, people let go of push to talk early
	/// </summary>
	const float TailTime = 0.2f;

	/// <summary>
	/// Keep sending for a bit after it goes quiet, so we don't clip the end of words
	/// </summary>
	internal const float GateHoldTime = 0.3f;

	/// <summary>
	/// Frames from just before the gate opens that get sent with it, words start quieter than the threshold
	/// </summary>
	const int PreRollFrames = 3; // 60ms

	public static int SampleRate => Rate;

	/// <summary>
	/// Most samples a single packet can decode to
	/// </summary>
	internal static int MaxPacketSamples => Rate;

	static IntPtr capture;
	static string captureDevice;
	static IntPtr encoder;
	static IntPtr sharedDecoder;

	// 200ms is the most we'll read in one tick; native drops anything older than that
	static float[] captureBuffer = new float[Rate / 5];
	static int captureCount;
	static float[] captureBufferPreRoll = new float[FrameSamples * PreRollFrames];
	static int preRollCount;
	static byte[] packetBuffer = new byte[MaxPacketLength];

	static RealTimeSince timeSinceLastHear = 10;
	static RealTimeSince timeSinceStopped = 10;
	static RealTimeSince timeSinceLoud = 10;

	/// <summary>
	/// Mic that wouldn't open. Not tried again until a device comes or goes, or another is picked.
	/// </summary>
	static string failedDevice;

	public static bool IsValid => !Application.IsHeadless;

	/// <summary>
	/// Does voice chat want the mic? Worked out each tick in the game's scope, so the menu can read it too.
	/// </summary>
	public static bool IsListening { get; private set; }

	public static bool IsRecording => timeSinceLastHear < 0.2f;

	/// <inheritdoc cref="Preferences.StreamlinedPushToTalk"/>
	private static bool StreamlinedPushToTalk => Preferences.VoiceMode == VoiceMode.PushToTalk && Preferences.StreamlinedPushToTalk;

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
		if ( !IsValid ) return;

		using ( _voiceTick.Start() )
		{
			if ( timeSinceLastTick < (1.0f / 30.0f) )
				return;

			timeSinceLastTick = 0;

			IsListening = VoiceChat.IsListening;

			if ( IsListening )
			{
				timeSinceStopped = 0;
			}

			var sending = IsListening || timeSinceStopped < TailTime;
			var testing = !sending && timeSinceTestRequested < 0.5f;
			var wanted = sending || testing;

			// A test shows the AGC it asked for, unless we're actually talking
			levelGainControl = testing ? testGainControl : Preferences.VoiceAutomaticGainControl;

			// Nobody wants the mic, let it go
			if ( !wanted && !StreamlinedPushToTalk )
			{
				CloseCapture();
				return;
			}

			// Picked a different mic in the settings. A test listens to the one it asked for,
			// unless we're actually talking. Held open between presses, it's the one we'll talk on.
			var device = (testing ? testDevice : Preferences.VoiceDevice) ?? "";
			if ( capture != IntPtr.Zero && captureDevice != device )
				CloseCapture();

			if ( capture == IntPtr.Zero && !OpenCapture( device ) )
				return;

			// StreamlinedPushToTalk: held open between presses, throw away what it hears
			if ( !wanted )
			{
				DiscardCapture();
				return;
			}

			ReadVoice( sending );
		}
	}

	/// <summary>
	/// Quietest level we report, in decibels. What <see cref="Level"/> reads when the mic is closed.
	/// </summary>
	public const float MinLevel = -96.0f;

	/// <summary>
	/// How loud the microphone was over the last tick, in decibels, before AGC. Measured by
	/// <see cref="LevelMeter"/>, without the rumble, which is also what the gate compares against
	/// <see cref="Preferences.VoiceThreshold"/>. Only meaningful while something is recording or testing.
	/// </summary>
	public static float InputLevel { get; private set; } = MinLevel;

	/// <summary>
	/// Roughly how loud the microphone was over the last tick, in decibels, after AGC: <see cref="InputLevel"/>
	/// plus its gain, ignoring the limiter, so loud frames can read a little high. While testing, AGC is
	/// whatever the test asked for rather than <see cref="Preferences.VoiceAutomaticGainControl"/>.
	/// Only meaningful while something is recording or testing.
	/// </summary>
	public static float Level { get; private set; } = MinLevel;

	/// <summary>
	/// How loud AGC thinks the background noise is, in decibels, before any gain, measured the same way as
	/// <see cref="InputLevel"/>. <see cref="MinLevel"/> until it's heard anything. Kept across closes of the same mic.
	/// </summary>
	public static float NoiseLevel => gainControl.NoiseLevel;

	/// <summary>
	/// Does <see cref="Level"/> include AGC? Worked out each tick, from the test or the preference.
	/// </summary>
	static bool levelGainControl;

	static RealTimeSince timeSinceTestRequested = 10;
	static string testDevice;
	static bool testGainControl;

	/// <summary>
	/// Keep this microphone open so <see cref="Level"/> can be shown, without sending anything.
	/// Call it every frame while testing, the mic closes shortly after you stop.
	/// </summary>
	/// <param name="device">Microphone to listen to, empty for the system default</param>
	/// <param name="gainControl">Should <see cref="Level"/> include AGC, so a setting can be previewed before it's applied</param>
	public static void TestMicrophone( string device, bool gainControl )
	{
		if ( !IsValid ) return;

		testDevice = device;
		testGainControl = gainControl;
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

		// AGC should reset if we've changed device
		gainControl.SetDevice( string.IsNullOrEmpty( device ) ? GetDefaultRecordingDevice() : device );

		return true;
	}

	/// <summary>
	/// SDL says a device was plugged in or out. It keeps an unplugged mic's stream open but
	/// silent, so close it ourselves. Either way, let the next tick try opening again by name.
	/// </summary>
	internal static void OnAudioDevicesChanged()
	{
		failedDevice = null;

		if ( capture == IntPtr.Zero )
			return;

		// The default device follows whatever the system picks, SDL handles that.
		// AGC should still start over if it's now a different mic.
		if ( string.IsNullOrEmpty( captureDevice ) )
		{
			gainControl.SetDevice( GetDefaultRecordingDevice() );
			return;
		}

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
		preRollCount = 0;
		timeSinceLoud = 10;
		InputLevel = MinLevel;
		Level = MinLevel;
		levelMeter.Reset();
	}

	/// <summary>
	/// Drain an open mic nobody is listening to, so it doesn't pile up and get sent on the next press
	/// </summary>
	static unsafe void DiscardCapture()
	{
		fixed ( float* samples = captureBuffer )
		{
			VoiceGlue.ReadCapture( capture, (IntPtr)samples, captureBuffer.Length );
		}

		captureCount = 0;
		timeSinceLoud = 10;
		InputLevel = MinLevel;
		Level = MinLevel;

		// What's read next won't follow on from what we measured last
		levelMeter.Reset();
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
			var level = levelMeter.Measure( frame );

			// Even with AGC off, so it's already settled if it gets turned on
			gainControl.Learn( level );

			loudest = MathF.Max( loudest, level );

			if ( !send )
			{
				preRollCount = 0;
				continue;
			}

			// Only send when it's louder than the threshold, Steam used to do this for us
			if ( level > Preferences.VoiceThreshold )
				timeSinceLoud = 0;

			if ( timeSinceLoud > GateHoldTime )
			{
				HoldPreRoll( frame );
				continue;
			}

			// Gate just opened, send the quiet start of the word first
			for ( int i = 0; i < preRollCount; i++ )
				packetLength = ProcessAndEncodeFrame( captureBufferPreRoll.AsSpan( i * FrameSamples, FrameSamples ), packetLength );

			preRollCount = 0;

			packetLength = ProcessAndEncodeFrame( frame, packetLength );
		}

		// Keep the partial frame for next tick
		captureBuffer.AsSpan( offset, captureCount - offset ).CopyTo( captureBuffer );
		captureCount -= offset;

		// No whole frame this tick, keep showing the last one
		if ( offset > 0 )
		{
			InputLevel = loudest;

			// Near enough for a meter: the limiter only ever turns it down from here
			Level = levelGainControl && loudest > MinLevel ? MathF.Min( loudest + gainControl.Gain, 0.0f ) : loudest;
		}

		if ( packetLength == 0 )
			return;

		timeSinceLastHear = 0;

		VoiceChat.BroadcastPacket( packetBuffer.AsSpan( 0, packetLength ) );
	}

	/// <summary>
	/// Apply pre-processing like AGC in place, then encode the resulting frame.
	/// Encodes into the packet after a ushort length prefix, returns the new packet length.
	/// </summary>
	static unsafe int ProcessAndEncodeFrame( Span<float> frame, int packetLength )
	{
		// Note that AGC will gradually ease out when you turn it off
		gainControl.Apply( frame, Preferences.VoiceAutomaticGainControl );

		int bytes;
		fixed ( float* samples = frame )
		fixed ( byte* dest = packetBuffer )
		{
			bytes = VoiceGlue.Encode( encoder, (IntPtr)samples, FrameSamples, (IntPtr)(dest + packetLength + 2), packetBuffer.Length - packetLength - 2 );
		}

		if ( bytes <= 0 )
			return packetLength;

		BinaryPrimitives.WriteUInt16LittleEndian( packetBuffer.AsSpan( packetLength ), (ushort)bytes );
		return packetLength + 2 + bytes;
	}

	/// <summary>
	/// Keep the last few frames the gate held back, dropping the oldest
	/// </summary>
	static void HoldPreRoll( ReadOnlySpan<float> frame )
	{
		if ( preRollCount == PreRollFrames )
		{
			captureBufferPreRoll.AsSpan( FrameSamples ).CopyTo( captureBufferPreRoll );
			preRollCount--;
		}

		frame.CopyTo( captureBufferPreRoll.AsSpan( preRollCount * FrameSamples ) );
		preRollCount++;
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
