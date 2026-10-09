using System;

namespace RasLib;

// MP2 ships most sounds as MS ADPCM (format tag 2). s&box's FromWav assumes PCM,
// so decode ADPCM to PCM16 here and hand back raw little-endian samples.
public static class MaxPayneWav
{
	public class Pcm
	{
		public byte[] Data;
		public int Channels;
		public int SampleRate;
		public int BitsPerSample;
	}

	static readonly int[] AdaptationTable =
	[
		230, 230, 230, 230, 307, 409, 512, 614,
		768, 614, 512, 409, 307, 230, 230, 230
	];
	static readonly int[] CoeffTable1 = [256, 512, 0, 192, 240, 460, 392];
	static readonly int[] CoeffTable2 = [0, -256, 0, 64, 0, -208, -232];

	// Returns null for PCM (caller should use the WAV as-is) or unsupported formats.
	public static Pcm DecodeIfAdpcm( byte[] wav )
	{
		if ( wav.Length < 44 || wav[0] != 'R' || wav[1] != 'I' || wav[2] != 'F' || wav[3] != 'F' )
			return null;

		var pos = 12;
		var fmtPos = -1;
		var dataPos = -1;
		var dataSize = 0;

		while ( pos + 8 <= wav.Length )
		{
			var id = BitConverter.ToUInt32( wav, pos );
			var size = (int)BitConverter.ToUInt32( wav, pos + 4 );
			var body = pos + 8;

			if ( id == 0x20746D66 ) fmtPos = body; // "fmt "
			else if ( id == 0x61746164 ) { dataPos = body; dataSize = size; } // "data"

			pos = body + size + (size & 1);
		}

		if ( fmtPos < 0 || dataPos < 0 )
			return null;

		var format = BitConverter.ToUInt16( wav, fmtPos );
		if ( format != 2 )
			return null; // PCM (1) or other - let the engine handle it

		var channels = BitConverter.ToUInt16( wav, fmtPos + 2 );
		var sampleRate = (int)BitConverter.ToUInt32( wav, fmtPos + 4 );
		var blockAlign = BitConverter.ToUInt16( wav, fmtPos + 12 );

		return DecodeMsAdpcm( wav, dataPos, dataSize, channels, sampleRate, blockAlign );
	}

	static Pcm DecodeMsAdpcm( byte[] src, int dataPos, int dataSize, int channels, int sampleRate, int blockAlign )
	{
		using var stream = new System.IO.MemoryStream();
		var end = dataPos + dataSize;

		for ( var block = dataPos; block < end; block += blockAlign )
		{
			var available = Math.Min( blockAlign, end - block );
			if ( available < 7 * channels )
				break;

			DecodeBlock( src, block, available, channels, stream );
		}

		return new Pcm
		{
			Data = stream.ToArray(),
			Channels = channels,
			SampleRate = sampleRate,
			BitsPerSample = 16,
		};
	}

	static void DecodeBlock( byte[] src, int pos, int length, int channels, System.IO.MemoryStream output )
	{
		Span<int> predictor = stackalloc int[2];
		Span<int> delta = stackalloc int[2];
		Span<int> sample1 = stackalloc int[2];
		Span<int> sample2 = stackalloc int[2];
		Span<int> coeff1 = stackalloc int[2];
		Span<int> coeff2 = stackalloc int[2];

		var p = pos;

		for ( var c = 0; c < channels; c++ )
		{
			predictor[c] = Math.Min( src[p++], (byte)6 );
			coeff1[c] = CoeffTable1[predictor[c]];
			coeff2[c] = CoeffTable2[predictor[c]];
		}

		for ( var c = 0; c < channels; c++ ) { delta[c] = BitConverter.ToInt16( src, p ); p += 2; }
		for ( var c = 0; c < channels; c++ ) { sample1[c] = BitConverter.ToInt16( src, p ); p += 2; }
		for ( var c = 0; c < channels; c++ ) { sample2[c] = BitConverter.ToInt16( src, p ); p += 2; }

		Span<byte> pair = stackalloc byte[2];
		for ( var c = 0; c < channels; c++ ) WriteSample( output, sample2[c], pair );
		for ( var c = 0; c < channels; c++ ) WriteSample( output, sample1[c], pair );

		var end = pos + length;
		while ( p < end )
		{
			var b = src[p++];
			DecodeNibble( b >> 4, 0, predictor, delta, sample1, sample2, coeff1, coeff2, channels, output, pair );
			DecodeNibble( b & 0x0F, channels == 1 ? 0 : 1, predictor, delta, sample1, sample2, coeff1, coeff2, channels, output, pair );
		}
	}

	static void DecodeNibble( int nibble, int c, Span<int> predictor, Span<int> delta, Span<int> sample1, Span<int> sample2, Span<int> coeff1, Span<int> coeff2, int channels, System.IO.MemoryStream output, Span<byte> pair )
	{
		var signed = nibble >= 8 ? nibble - 16 : nibble;
		var predict = (sample1[c] * coeff1[c] + sample2[c] * coeff2[c]) / 256;
		predict += signed * delta[c];
		predict = Math.Clamp( predict, short.MinValue, short.MaxValue );

		sample2[c] = sample1[c];
		sample1[c] = predict;

		delta[c] = AdaptationTable[nibble] * delta[c] / 256;
		if ( delta[c] < 16 ) delta[c] = 16;

		WriteSample( output, predict, pair );
	}

	static void WriteSample( System.IO.MemoryStream output, int sample, Span<byte> pair )
	{
		pair[0] = (byte)(sample & 0xFF);
		pair[1] = (byte)((sample >> 8) & 0xFF);
		output.Write( pair );
	}
}
