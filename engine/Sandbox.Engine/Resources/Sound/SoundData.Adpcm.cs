using System.Buffers.Binary;
using System.IO;

namespace Sandbox;

internal partial class SoundData
{
	private static ReadOnlySpan<int> AdpcmAdaptation => [230, 230, 230, 230, 307, 409, 512, 614, 768, 614, 512, 409, 307, 230, 230, 230];

	private struct AdpcmChannel
	{
		public int Delta;
		public int Sample1;
		public int Sample2;
		public short Coefficient1;
		public short Coefficient2;

		public short Decode( int nibble )
		{
			var predicted = ((long)Sample1 * Coefficient1 + (long)Sample2 * Coefficient2) / 256;
			var sample = (short)Math.Clamp( predicted + (nibble < 8 ? nibble : nibble - 16) * (long)Delta, short.MinValue, short.MaxValue );
			Sample2 = Sample1;
			Sample1 = sample;
			Delta = (int)Math.Clamp( (long)AdpcmAdaptation[nibble] * Delta / 256, 16, int.MaxValue );
			return sample;
		}
	}

	private static (byte[] Data, uint Samples) DecodeAdpcm( ReadOnlySpan<byte> format, ReadOnlySpan<byte> data, uint? factSamples )
	{
		if ( format.Length < 22 )
			throw new InvalidDataException( "Incomplete ADPCM format chunk." );
		var channels = BinaryPrimitives.ReadUInt16LittleEndian( format[2..] );
		var blockSize = BinaryPrimitives.ReadUInt16LittleEndian( format[12..] );
		var bits = BinaryPrimitives.ReadUInt16LittleEndian( format[14..] );
		var extraSize = BinaryPrimitives.ReadUInt16LittleEndian( format[16..] );
		var samplesPerBlock = BinaryPrimitives.ReadUInt16LittleEndian( format[18..] );
		var coefficientCount = BinaryPrimitives.ReadUInt16LittleEndian( format[20..] );
		if ( channels is < 1 or > 2 || bits != 4 || blockSize < 7 * channels
			|| coefficientCount is < 1 or > 256 || extraSize < 4 + coefficientCount * 4
			|| extraSize > format.Length - 18 || samplesPerBlock != 2 + (blockSize - 7 * channels) * 2 / channels )
			throw new InvalidDataException( "Invalid ADPCM block format." );
		var tailSize = data.Length % blockSize;
		if ( tailSize > 0 && tailSize < 7 * channels )
			throw new InvalidDataException( "Truncated ADPCM block header." );
		var availableSamples = (long)(data.Length / blockSize) * samplesPerBlock
			+ (tailSize == 0 ? 0 : 2 + (tailSize - 7 * channels) * 2 / channels);
		var sampleCount = factSamples.HasValue ? factSamples.Value : availableSamples;
		if ( sampleCount <= 0 || sampleCount > availableSamples || sampleCount > Array.MaxLength / (channels * 2) )
			throw new InvalidDataException( "Invalid ADPCM sample count." );
		var output = new byte[(int)sampleCount * channels * 2];
		var samples = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, short>( output.AsSpan() );
		Span<AdpcmChannel> states = stackalloc AdpcmChannel[2];
		var written = 0;
		for ( var offset = 0; offset < data.Length && written < samples.Length; offset += blockSize )
		{
			var block = data.Slice( offset, Math.Min( blockSize, data.Length - offset ) );
			for ( var channel = 0; channel < channels; channel++ )
			{
				var predictor = block[channel];
				if ( predictor >= coefficientCount )
					throw new InvalidDataException( "Invalid ADPCM predictor." );
				var coefficients = format[(22 + predictor * 4)..];
				states[channel] = new AdpcmChannel
				{
					Delta = BinaryPrimitives.ReadUInt16LittleEndian( block[(channels + channel * 2)..] ),
					Sample1 = BinaryPrimitives.ReadInt16LittleEndian( block[(channels * 3 + channel * 2)..] ),
					Sample2 = BinaryPrimitives.ReadInt16LittleEndian( block[(channels * 5 + channel * 2)..] ),
					Coefficient1 = BinaryPrimitives.ReadInt16LittleEndian( coefficients ),
					Coefficient2 = BinaryPrimitives.ReadInt16LittleEndian( coefficients[2..] )
				};
				if ( states[channel].Delta == 0 )
					throw new InvalidDataException( "Invalid ADPCM initial delta." );
			}
			for ( var frame = 0; frame < 2 && written < samples.Length; frame++ )
				for ( var channel = 0; channel < channels; channel++ )
					samples[written++] = (short)(frame == 0 ? states[channel].Sample2 : states[channel].Sample1);
			for ( var i = channels * 7; i < block.Length && written < samples.Length; i++ )
			{
				samples[written++] = states[0].Decode( block[i] >> 4 );
				if ( written < samples.Length )
					samples[written++] = states[channels - 1].Decode( block[i] & 15 );
			}
		}
		return (output, (uint)sampleCount);
	}
}
