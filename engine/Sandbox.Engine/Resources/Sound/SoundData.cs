using System.IO;
using System.Text;

namespace Sandbox;

/// <summary>
/// Raw sound data, kind of like a bitmap but for sounds
/// </summary>
internal class SoundData
{
	public ushort Format { get; private set; }
	public ushort Channels { get; private set; }
	public uint SampleRate { get; private set; }
	public ushort BitsPerSample { get; private set; }
	public uint SampleCount { get; private set; }
	public float Duration { get; private set; }
	public byte[] PCMData { get; private set; }

	public static SoundData FromWav( Span<byte> data )
	{
		using var stream = new MemoryStream( data.ToArray() );
		using var reader = new BinaryReader( stream );

		Span<byte> header = stackalloc byte[4];
		if ( stream.Length < 12 ||
			reader.Read( header ) != 4 || !header.SequenceEqual( RIFF ) ||
			reader.ReadInt32() != (stream.Length - 8) ||
			reader.Read( header ) != 4 || !header.SequenceEqual( WAVE ) )
		{
			throw new ArgumentException( "Invalid WAV file format" );
		}

		Span<byte> fmtChunk = default;
		Span<byte> dataChunk = default;

		uint fmtSize = 0;
		uint dataSize = 0;

		while ( stream.Position + 8 <= stream.Length )
		{
			reader.Read( header );
			var chunkSize = reader.ReadUInt32();
			var chunkStart = stream.Position;

			if ( chunkStart + chunkSize > stream.Length )
				throw new ArgumentException( "Invalid chunk size in WAV file" );

			if ( header.SequenceEqual( FMT ) )
			{
				if ( chunkSize < 16 )
					throw new ArgumentException( "Format chunk size too small" );

				fmtChunk = data.Slice( (int)chunkStart, (int)chunkSize );
				fmtSize = chunkSize;
			}
			else if ( header.SequenceEqual( DATA ) )
			{
				dataChunk = data.Slice( (int)chunkStart, (int)chunkSize );
				dataSize = chunkSize;
			}

			stream.Position = chunkStart + chunkSize;
			if ( chunkSize % 2 != 0 )
				stream.Position++;
		}

		if ( fmtChunk.IsEmpty )
			throw new ArgumentException( "Missing required FMT chunks" );

		if ( dataChunk.IsEmpty )
			throw new ArgumentException( "Missing required DATA chunks" );

		var channels = BitConverter.ToUInt16( fmtChunk[2..] );
		var bitsPerSample = BitConverter.ToUInt16( fmtChunk[14..] );
		var bytesPerSample = (uint)bitsPerSample / 8;
		var sampleSize = bytesPerSample * channels;

		if ( dataSize % sampleSize != 0 )
			throw new ArgumentException( "Data chunk size is not a multiple of sample size" );

		var format = BitConverter.ToUInt16( fmtChunk );
		var sampleRate = BitConverter.ToUInt32( fmtChunk[4..] );
		var sampleCount = dataSize / sampleSize;
		var duration = sampleRate > 0 ? (float)sampleCount / sampleRate : 0.0f;
		var pcmData = dataChunk[..(int)dataSize].ToArray();

		return new SoundData
		{
			Format = format,
			Channels = channels,
			SampleRate = sampleRate,
			BitsPerSample = bitsPerSample,
			SampleCount = sampleCount,
			Duration = duration,
			PCMData = pcmData
		};
	}

	private static readonly byte[] RIFF = Encoding.ASCII.GetBytes( "RIFF" );
	private static readonly byte[] WAVE = Encoding.ASCII.GetBytes( "WAVE" );
	private static readonly byte[] FMT = Encoding.ASCII.GetBytes( "fmt " );
	private static readonly byte[] DATA = Encoding.ASCII.GetBytes( "data" );

	private static readonly int[,] Mp3SampleRates =
	{
		{ 11025, 12000, 8000 },
		{ 0, 0, 0 },
		{ 22050, 24000, 16000 },
		{ 44100, 48000, 32000 }
	};

	private static readonly int[,,] Mp3Bitrates =
	{
		{
			{ 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 },
			{ 0, 32, 40, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320, 0 },
			{ 0, 32, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320, 384, 0 },
			{ 0, 32, 64, 96, 128, 160, 192, 224, 256, 288, 320, 352, 384, 416, 448, 0 }
		},
		{
			{ 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 },
			{ 0, 8, 16, 24, 32, 40, 48, 56, 64, 80, 96, 112, 128, 144, 160, 0 },
			{ 0, 8, 16, 24, 32, 40, 48, 56, 64, 80, 96, 112, 128, 144, 160, 0 },
			{ 0, 32, 48, 56, 64, 80, 96, 112, 128, 144, 160, 176, 192, 224, 256, 0 }
		}
	};

	private static readonly int[,] Mp3SamplesPerFrame =
	{
		{ 0, 1152, 1152, 384 },
		{ 0, 576, 1152, 384 }
	};

	public static SoundData FromMp3( Span<byte> data )
	{
		int offset = 0;
		int audioEnd = data.Length;

		if ( data.Length >= 10 && data[0] == 'I' && data[1] == 'D' && data[2] == '3' )
		{
			int id3Size = ((data[6] & 0x7F) << 21) | ((data[7] & 0x7F) << 14) | ((data[8] & 0x7F) << 7) | (data[9] & 0x7F);
			offset = 10 + id3Size;
		}

		if ( data.Length >= 128 && data[^128] == 'T' && data[^127] == 'A' && data[^126] == 'G' )
			audioEnd = data.Length - 128;

		while ( offset < audioEnd - 4 && !(data[offset] == 0xFF && (data[offset + 1] & 0xE0) == 0xE0) )
			offset++;

		if ( offset >= audioEnd - 4 )
			throw new ArgumentException( "Invalid MP3: no frame sync found" );

		int version = (data[offset + 1] >> 3) & 0x03;
		int layer = (data[offset + 1] >> 1) & 0x03;
		int channelMode = (data[offset + 3] >> 6) & 0x03;

		if ( version == 1 || layer == 0 )
			throw new ArgumentException( "Invalid MP3: reserved version or layer" );

		int versionType = version == 3 ? 0 : 1;
		int samplesPerFrame = Mp3SamplesPerFrame[versionType, layer];
		ushort channels = (ushort)(channelMode == 3 ? 1 : 2);

		int frameCount = 0;
		int pos = offset;

		while ( pos < audioEnd - 4 && frameCount < 10000 )
		{
			if ( data[pos] != 0xFF || (data[pos + 1] & 0xE0) != 0xE0 )
				break;

			int v = (data[pos + 1] >> 3) & 0x03;
			int l = (data[pos + 1] >> 1) & 0x03;
			int brIdx = (data[pos + 2] >> 4) & 0x0F;
			int srIdx = (data[pos + 2] >> 2) & 0x03;
			int padding = (data[pos + 2] >> 1) & 0x01;

			if ( v == 1 || l == 0 )
				break;

			int vt = v == 3 ? 0 : 1;
			int sr = Mp3SampleRates[v, srIdx];
			int br = Mp3Bitrates[vt, l, brIdx];

			if ( sr == 0 || br == 0 )
				break;

			int spf = Mp3SamplesPerFrame[vt, l];
			int frameSize = l == 3
				? (12 * br * 1000 / sr + padding) * 4
				: spf / 8 * br * 1000 / sr + padding;

			if ( frameSize <= 0 )
				break;

			frameCount++;
			pos += frameSize;
		}

		if ( frameCount >= 10000 && pos > offset )
		{
			int avgFrameSize = (pos - offset) / frameCount;
			frameCount = (audioEnd - offset) / avgFrameSize;
		}

		int bitrateIdx = (data[offset + 2] >> 4) & 0x0F;
		int sampleIdx = (data[offset + 2] >> 2) & 0x03;
		int sampleRate = Mp3SampleRates[version, sampleIdx];
		int bitrate = Mp3Bitrates[versionType, layer, bitrateIdx];

		if ( sampleRate == 0 || bitrate == 0 )
			throw new ArgumentException( "Invalid MP3: bad sample rate or bitrate" );

		float duration = frameCount * samplesPerFrame / (float)sampleRate;
		uint sampleCount = (uint)(frameCount * samplesPerFrame);

		return new SoundData
		{
			Format = 2,
			Channels = channels,
			SampleRate = (uint)sampleRate,
			BitsPerSample = 16,
			SampleCount = sampleCount,
			Duration = duration,
			PCMData = null
		};
	}
}
