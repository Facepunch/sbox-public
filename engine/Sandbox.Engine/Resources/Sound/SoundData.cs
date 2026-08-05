using System.IO;
using System.Text;

namespace Sandbox;

/// <summary>
/// Raw PCM sound data, kind of like a bitmap but for sounds
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

	/// <summary>
	/// Start sample of the loop region declared by the file's "cue " chunk, or -1 if it has none.
	/// </summary>
	public int LoopStart { get; private set; } = -1;

	/// <summary>
	/// End sample of the loop region, or 0 when the loop runs to the end of the sound.
	/// </summary>
	public int LoopEnd { get; private set; }

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

		// Offsets of the "cue " chunk and of the first "LIST" chunk following it, which together
		// describe the loop region. See below.
		var cueStart = -1;
		var listStart = -1;

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
			else if ( header.SequenceEqual( CUE ) && cueStart < 0 )
			{
				cueStart = (int)chunkStart - 8;
			}
			else if ( header.SequenceEqual( LIST ) && cueStart >= 0 && listStart < 0 )
			{
				listStart = (int)chunkStart - 8;
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

		var (loopStart, loopEnd) = ReadLoop( data, cueStart, listStart );

		return new SoundData
		{
			Format = format,
			Channels = channels,
			SampleRate = sampleRate,
			BitsPerSample = bitsPerSample,
			SampleCount = sampleCount,
			Duration = duration,
			PCMData = pcmData,
			LoopStart = loopStart,
			LoopEnd = loopEnd
		};
	}

	/// <summary>
	/// The loop region declared by a WAV's "cue " chunk. This is how sound editors mark up a loop,
	/// so it is worth honouring rather than making every caller specify one.
	///
	/// The first cue point's sample offset sits 32 bytes into the chunk. A "LIST" chunk after it
	/// whose bytes at +28 read "mark" carries the loop length at +24, which ends the loop early.
	/// </summary>
	private static (int LoopStart, int LoopEnd) ReadLoop( Span<byte> data, int cueStart, int listStart )
	{
		if ( cueStart < 0 || cueStart + 36 > data.Length )
			return (-1, 0);

		var loopStart = BitConverter.ToInt32( data[(cueStart + 32)..] );
		if ( loopStart < 0 )
			return (-1, 0);

		if ( listStart < 0 || listStart + 32 > data.Length || !data.Slice( listStart + 28, 4 ).SequenceEqual( MARK ) )
			return (loopStart, 0);

		return (loopStart, loopStart + BitConverter.ToInt32( data[(listStart + 24)..] ));
	}

	private static readonly byte[] RIFF = Encoding.ASCII.GetBytes( "RIFF" );
	private static readonly byte[] WAVE = Encoding.ASCII.GetBytes( "WAVE" );
	private static readonly byte[] FMT = Encoding.ASCII.GetBytes( "fmt " );
	private static readonly byte[] DATA = Encoding.ASCII.GetBytes( "data" );
	private static readonly byte[] CUE = Encoding.ASCII.GetBytes( "cue " );
	private static readonly byte[] LIST = Encoding.ASCII.GetBytes( "LIST" );
	private static readonly byte[] MARK = Encoding.ASCII.GetBytes( "mark" );

	public static unsafe SoundData FromMP3( Span<byte> data )
	{
		if ( data.Length <= 0 )
			throw new ArgumentException( null, nameof( data ) );

		using var pcm = CSimplePCMWaveData.Create();

		fixed ( byte* p = data )
		{
			pcm.ParseMP3File( (IntPtr)p, data.Length );
		}

		int size = pcm.GetPCMSize();
		if ( size <= 0 )
			throw new ArgumentException( "Invalid MP3" );

		var buffer = new byte[size];

		fixed ( byte* dst = buffer )
		{
			Buffer.MemoryCopy( (void*)pcm.GetPCMData(), dst, size, size );
		}

		return new SoundData
		{
			Format = pcm.m_nBits == 32 ? (ushort)3 : (ushort)1,
			Channels = (ushort)pcm.m_nChannels,
			SampleRate = pcm.m_nSampleRate,
			BitsPerSample = (ushort)pcm.m_nBits,
			SampleCount = pcm.m_nSampleCount,
			Duration = pcm.m_flDuration,
			PCMData = buffer
		};
	}

	public static unsafe SoundData FromOGG( Span<byte> data )
	{
		if ( data.Length <= 0 )
			throw new ArgumentException( null, nameof( data ) );

		using var pcm = CSimplePCMWaveData.Create();
		fixed ( byte* p = data )
		{
			if ( !pcm.ParseOGGFile( (IntPtr)p, data.Length ) )
				throw new ArgumentException( "Invalid OGG" );
		}

		int size = pcm.GetPCMSize();
		var buffer = new byte[size];
		fixed ( byte* dst = buffer )
		{
			Buffer.MemoryCopy( (void*)pcm.GetPCMData(), dst, size, size );
		}

		return new SoundData
		{
			Format = pcm.m_nBits == 32 ? (ushort)3 : (ushort)1,
			Channels = (ushort)pcm.m_nChannels,
			SampleRate = pcm.m_nSampleRate,
			BitsPerSample = (ushort)pcm.m_nBits,
			SampleCount = pcm.m_nSampleCount,
			Duration = pcm.m_flDuration,
			PCMData = buffer
		};
	}
}
