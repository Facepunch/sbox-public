using System;
using System.IO;

namespace Sandbox.Mounting.Source1;

internal sealed class BspProbeTextureData
{
	private static readonly int[] HammerSides = [0, 2, 4, 1, 3, 5];
	public byte[] Data { get; }

	private readonly int probes;
	public BspProbeTextureData( Vector3Int counts )
	{
		if ( counts.x is < 1 or > 32 || counts.y is < 1 or > 32 || counts.z is < 1 or > 32 )
			throw new InvalidDataException( "Source BSP probe dimensions must be between 1 and 32." );
		Data = new byte[checked(counts.x * counts.y * counts.z * 60)];
		probes = counts.x * counts.y * counts.z;
		Data.AsSpan( probes * 48, probes * 4 ).Fill( 255 );
	}

	public void Write( int probe, ReadOnlySpan<Vector3> cube, ReadOnlySpan<int> indices, ReadOnlySpan<float> strengths )
	{
		if ( (uint)probe >= probes || cube.Length != 6 || indices.Length != 4 || strengths.Length != 4 )
			throw new ArgumentException( "Invalid BSP probe texture sample." );
		for ( int layer = 0; layer < 6; layer++ )
		{
			var color = cube[HammerSides[layer]];
			int offset = (layer * probes + probe) * 8;
			WriteHalf( offset, color.x );
			WriteHalf( offset + 2, color.y );
			WriteHalf( offset + 4, color.z );
			WriteHalf( offset + 6, 1 );
		}

		for ( int channel = 0; channel < 4; channel++ )
		{
			int index = indices[channel];
			if ( index is < -1 or > 254 || !float.IsFinite( strengths[channel] ) || strengths[channel] < 0 )
				throw new InvalidDataException( "Invalid BSP probe direct-light index or strength." );
			Data[probes * 48 + probe * 4 + channel] = index < 0 ? (byte)255 : (byte)index;
			WriteHalf( probes * 52 + probe * 8 + channel * 2, index < 0 ? 0 : MathF.Sqrt( strengths[channel] ) );
		}
	}

	private void WriteHalf( int offset, float value )
	{
		if ( !float.IsFinite( value ) || value < 0 || value > 65504 )
			throw new InvalidDataException( "BSP probe lighting is outside the finite half-float texture range." );
		BitConverter.TryWriteBytes( Data.AsSpan( offset, 2 ), (Half)value );
	}
}
