using Sandbox;
using System;
using System.Text;

record struct SampleInformation(
	SoundBankLoader.SoundFormat Format,
	long InfoOffset,
	long DataOffset,
	long Length = 0
);

class SoundBankLoader( string bankPath, SampleInformation info ) : ResourceLoader<GameMount>
{
	private string BankPath { get; init; } = bankPath;
	private SampleInformation Info { get; init; } = info;

	public enum SoundFormat
	{
		None,
		PCM8,
		PCM16,
		PCM24,
		PCM32,
		PCMFLOAT,
		GCADPCM,
		IMAADPCM,
		VAG,
		HEVAG,
		XMA,
		MPEG,
		CELT,
		AT9,
		XWMA,
		VORBIS
	};

	static ulong Bits( ulong val, int start, int len )
	{
		var stop = start + len;
		var r = val & ((1UL << stop) - 1);
		return r >> start;
	}

	static int Frequency( ulong raw )
	{
		return raw switch
		{
			1 => 8000,
			2 => 11000,
			3 => 11025,
			4 => 16000,
			5 => 22050,
			6 => 24000,
			7 => 32000,
			8 => 44100,
			9 => 48000,
			_ => throw new InvalidDataException()
		};
	}

	public static void AddSoundsFromBank( MountContext context, string bankPath, string relPath )
	{
		using var fs = File.OpenRead( bankPath );
		using var br = new BinaryReader( fs );

		// read and verify magic number
		if ( Encoding.ASCII.GetString( br.ReadBytes( 4 ) ) != "FSB5" ) return;

		// read bank header, notably contains the sound format used in the entire bank
		var version = br.ReadUInt32();
		var numSamples = br.ReadUInt32();
		var sampleHeaderSize = br.ReadUInt32();
		var nameTableSize = br.ReadUInt32();
		var dataSize = br.ReadUInt32();
		var mode = (SoundFormat)br.ReadUInt32();
		fs.Seek( 32, SeekOrigin.Current ); // skip Zero, Hash, Dummy

		// now there's a table of information for each sample
		// figure out where information is stored, but leave reading the info for later
		var samples = new List<SampleInformation>();
		for ( var i = 0; i < numSamples; i++ )
		{
			var infoOffset = fs.Position;

			var raw = br.ReadUInt64();
			var nextChunk = Bits( raw, 0, 1 );
			var dataOffset = (uint)Bits( raw, 1 + 4 + 1, 28 ) * 16;

			while ( nextChunk != 0 )
			{
				var rawi = br.ReadUInt32();
				nextChunk = Bits( rawi, 0, 1 );
				var chunkSize = Bits( rawi, 1, 24 );
				fs.Seek( (long)chunkSize, SeekOrigin.Current );
			}

			var sample = new SampleInformation(
				mode,
				infoOffset,
				dataOffset
			);

			samples.Add( sample );
		}

		// before we continue reading, let's figure out how long each sample is
		for ( var i = 0; i < numSamples; i++ )
		{
			var dataStart = samples[i].DataOffset;
			long dataEnd;
			if ( i < numSamples - 1 )
			{
				// the sample ends at the offset for the next sample
				dataEnd = samples[i + 1].DataOffset;
			}
			else
			{
				// the sample ends at the end of the bank file data
				dataEnd = sampleHeaderSize + nameTableSize + dataSize;
			}
			samples[i] = samples[i] with { Length = dataEnd - dataStart };
		}

		// finally, there's a table which has the name for each sample
		// first there's a list of offsets which we skip because we're reading each name anyway
		fs.Seek( 4 * numSamples, SeekOrigin.Current );
		/* this is how you might want to read the name offsets:
		var startOfNameTable = fs.Position;
		var sampleNameOffsets = new List<long>();
		for ( var i = 0; i < numSamples; i++ )
		{
			sampleNameOffsets.Add( br.ReadUInt32() );
		}
		*/

		// now read each name in sequence and add them to the MountContext
		for ( var i = 0; i < numSamples; i++ )
		{
			var nameBuilder = new StringBuilder();
			var b = br.ReadChar();
			do
			{
				nameBuilder.Append( b );
				b = br.ReadChar();
			} while ( b != 0 );

			var path = $"{relPath}/{nameBuilder}";

			context.Add( ResourceType.Sound, path, new SoundBankLoader( bankPath, samples[i] ) );
		}
	}

	protected override object Load()
	{
		using var fs = File.OpenRead( BankPath );
		using var br = new BinaryReader( fs );

		// read sample information
		fs.Seek( Info.InfoOffset, SeekOrigin.Begin );

		var raw = br.ReadUInt64();
		var nextChunk = Bits( raw, 0, 1 );
		var frequency = Frequency( Bits( raw, 1, 4 ) );
		var channels = (int)(Bits( raw, 1 + 4, 1 ) + 1);
		// var samples = (int)Bits( raw, 1 + 4 + 1 + 28, 30 ); - unused

		while ( nextChunk != 0 )
		{
			var rawi = br.ReadUInt32();
			nextChunk = Bits( rawi, 0, 1 );
			var chunkSize = Bits( rawi, 1, 24 );
			var chunkType = Bits( rawi, 1 + 24, 7 );

			switch ( chunkType )
			{
				case 1: // CHANNELS
					channels = br.ReadChar();
					break;
				case 2: // FREQUENCY
					frequency = (int)br.ReadUInt32();
					break;
				default:
					// skip this
					fs.Seek( (long)chunkSize, SeekOrigin.Current );
					break;
			}
		}

		// read file data
		byte[] fileBytes = new byte[Info.Length];
		fs.Seek( Info.DataOffset, SeekOrigin.Begin );
		fs.ReadExactly( fileBytes );

		if ( Info.Format == SoundFormat.MPEG )
		{
			return SoundFile.FromMp3( Path, fileBytes, false );
		}

		if ( Info.Format == SoundFormat.PCM16 )
		{
			return SoundFile.FromPcm( Path, fileBytes, channels, (uint)frequency, 16, false );
		}

		throw new NotImplementedException(); // NS2 only uses PCM16 (wav) and MPEG (mp3) samples.
	}
}
