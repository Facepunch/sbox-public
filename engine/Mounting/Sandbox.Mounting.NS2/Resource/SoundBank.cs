using ExCSS;
using Sandbox;
using System;
using System.Text;

record struct SampleInformation(
	int Frequency,
	int Channels,
	uint DataOffset,
	int Samples,
	SoundBankLoader.SoundFormat Format,
	uint Length = 0
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

	static int WidthForFormat( SoundFormat format ) => format switch
	{
		SoundFormat.PCM8 => 1,
		SoundFormat.PCM16 => 2,
		SoundFormat.PCM32 => 4,
		_ => throw new InvalidDataException(),
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

	// CS4012: Parameters of type 'MountContext' cannot be declared in async methods
	public static void AddSoundsFromBank( MountContext context, string bankPath, string relPath )
	{
		var ms = File.OpenRead( bankPath );
		using var br = new BinaryReader( ms );
		if ( Encoding.ASCII.GetString( br.ReadBytes( 4 ) ) != "FSB5" ) return;

		var version = br.ReadUInt32();
		var numSamples = br.ReadUInt32();
		var sampleHeaderSize = br.ReadUInt32();
		var nameTableSize = br.ReadUInt32();
		var dataSize = br.ReadUInt32();
		var mode = (SoundFormat)br.ReadUInt32();

		ms.Seek( 32, SeekOrigin.Current ); // skip Zero, Hash, Dummy

		var headerSize = ms.Position;
		var startOfData = (ulong)(headerSize + sampleHeaderSize + nameTableSize);

		var incompleteSamples = new List<SampleInformation>();

		for ( var i = 0; i < numSamples; i++ )
		{
			var raw = br.ReadUInt64();
			var nextChunk = Bits( raw, 0, 1 );
			var frequency = Frequency( Bits( raw, 1, 4 ) );
			var channels = (int)(Bits( raw, 1 + 4, 1 ) + 1);
			var dataOffset = (uint)Bits( raw, 1 + 4 + 1, 28 ) * 16;
			var samples = (int)Bits( raw, 1 + 4 + 1 + 28, 30 );

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
						ms.Seek( (long)chunkSize, SeekOrigin.Current );
						break;
				}
			}

			var sample = new SampleInformation(
				frequency,
				channels,
				dataOffset,
				samples,
				mode
			);

			incompleteSamples.Add( sample );
		}

		var startOfNameTable = ms.Position;
		var sampleNameOffsets = new List<long>();
		for ( var i = 0; i < numSamples; i++ )
		{
			sampleNameOffsets.Add( br.ReadUInt32() );
		}

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
			var dataStart = incompleteSamples[i].DataOffset;
			var dataEnd = sampleHeaderSize + nameTableSize + dataSize;
			if ( i < numSamples - 1 )
			{
				dataEnd = incompleteSamples[i + 1].DataOffset;
			}
			var sample = incompleteSamples[i] with
			{
				DataOffset = dataStart,
				Length = dataEnd - dataStart
			};

			context.Add( ResourceType.Sound, path, new SoundBankLoader( bankPath, sample ) );
		}
	}

	protected override object Load()
	{
		using var bf = File.OpenRead( BankPath );
		byte[] fileBytes = new byte[Info.Length];
		bf.Seek( Info.DataOffset, SeekOrigin.Begin );
		bf.ReadExactly( fileBytes );

		if ( Info.Format == SoundFormat.MPEG )
		{
			var tempfname = $"ns2_{Guid.NewGuid()}.mp3";
			File.WriteAllBytes( tempfname, fileBytes );
			Log.Info( $"Wrote <a href=\"{tempfname}\">{tempfname}</a>" );
			return null;
		}

		if ( Info.Format != SoundFormat.PCM16 )
		{
			Log.Info( $"Can't handle format {Info.Format}" );
			return null;
		}

		// how do I make a sound from just samples??
		// for now, generate a temporary .wav and then load it

		var tempf = new MemoryStream();

		{
			using var wb = new BinaryWriter( tempf );
			var width = WidthForFormat( Info.Format );

			wb.Write( 'R' );
			wb.Write( 'I' );
			wb.Write( 'F' );
			wb.Write( 'F' );
			wb.Write( 0 ); // filesize, will be filled in later
			wb.Write( 'W' );
			wb.Write( 'A' );
			wb.Write( 'V' );
			wb.Write( 'E' );
			wb.Write( 'f' );
			wb.Write( 'm' );
			wb.Write( 't' );
			wb.Write( ' ' );
			wb.Write( 16 );
			wb.Write( (short)1 );  // formatTag: PCM
			wb.Write( (short)Info.Channels );
			wb.Write( Info.Frequency );
			var bytePerBloc = Info.Channels * width;
			var bytePerSec = bytePerBloc * Info.Frequency;
			wb.Write( bytePerSec );
			wb.Write( (short)bytePerBloc );
			wb.Write( (short)(width * 8) );
			wb.Write( 'd' );
			wb.Write( 'a' );
			wb.Write( 't' );
			wb.Write( 'a' );
			wb.Write( fileBytes.Length );
			wb.Write( fileBytes );
			// go back and fill in size
			var size = (int)tempf.Position - 8;
			tempf.Seek( 4, SeekOrigin.Begin );
			wb.Write( size );
		}

		return SoundFile.FromWav( Path, tempf.ToArray(), false );
	}
}
