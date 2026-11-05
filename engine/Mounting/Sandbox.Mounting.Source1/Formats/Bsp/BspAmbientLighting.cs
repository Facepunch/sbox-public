using System;
using System.IO;

namespace Sandbox.Mounting.Source1;

internal sealed class BspAmbientLighting
{
	internal readonly record struct Sample( Vector3 Position, Vector3[] Cube );
	private readonly Sample[][] samples;
	public BspAmbientLighting( BBox[] bounds, byte[] leafData, int leafVersion, byte[] indices, byte[] lighting, int lightingVersion )
	{
		samples = new Sample[bounds.Length][];
		if ( lighting.Length == 0 && indices.Length == 0 && leafVersion != 0 )
		{
			Array.Fill( samples, Array.Empty<Sample>() );
			return;
		}

		if ( indices.Length == 0 )
		{
			int stride = lighting.Length == 0 ? 56 : 24;
			var data = lighting.Length == 0 ? leafData : lighting;
			if ( data.Length != checked(bounds.Length * stride) || lighting.Length > 0 && lightingVersion != 0 )
				throw new InvalidDataException( "BSP leaf ambient lighting requires a matching per-leaf cube or sample index." );
			for ( int leaf = 0; leaf < bounds.Length; leaf++ )
				samples[leaf] = [new Sample( bounds[leaf].Center, DecodeCube( data.AsSpan( leaf * stride + (stride == 56 ? 30 : 0), 24 ) ) )];
			return;
		}

		if ( indices.Length != checked(bounds.Length * 4) || lighting.Length % 28 != 0 || lighting.Length > 0 && lightingVersion != 1 )
			throw new InvalidDataException( "BSP leaf ambient indices or samples have invalid dimensions or version." );
		var aliases = new int[bounds.Length];
		Array.Fill( aliases, -1 );
		for ( int leaf = 0; leaf < bounds.Length; leaf++ )
		{
			int count = BitConverter.ToUInt16( indices, leaf * 4 );
			int first = BitConverter.ToUInt16( indices, leaf * 4 + 2 );
			if ( count == 0 )
			{
				if ( first >= bounds.Length )
					throw new InvalidDataException( $"BSP ambient leaf {leaf} references invalid leaf {first}." );
				aliases[leaf] = first;
				continue;
			}

			if ( first > lighting.Length / 28 || count > lighting.Length / 28 - first )
				throw new InvalidDataException( $"BSP ambient leaf {leaf} has an invalid sample range." );
			samples[leaf] = new Sample[count];
			for ( int sample = 0; sample < count; sample++ )
			{
				var data = lighting.AsSpan( (first + sample) * 28, 28 );
				var position = bounds[leaf].Mins + bounds[leaf].Size * new Vector3( data[24], data[25], data[26] ) / 255f;
				samples[leaf][sample] = new Sample( position, DecodeCube( data[..24] ) );
			}
		}

		var states = new byte[bounds.Length];
		var chain = new List<int>();
		for ( int leaf = 0; leaf < bounds.Length; leaf++ )
		{
			int current = leaf;
			chain.Clear();
			while ( samples[current] is null )
			{
				if ( aliases[current] == current )
				{
					samples[current] = [];
					break;
				}

				if ( states[current] == 1 )
					throw new InvalidDataException( "BSP ambient leaf references contain a cycle." );
				states[current] = 1;
				chain.Add( current );
				current = aliases[current];
			}

			foreach ( int referenced in chain )
			{
				samples[referenced] = samples[current];
				states[referenced] = 2;
			}
		}
	}

	public ReadOnlySpan<Sample> Samples( int leaf ) => samples[leaf];
	public void Evaluate( int leaf, Vector3 position, Span<Vector3> cube )
	{
		if ( cube.Length != 6 )
			throw new ArgumentException( "An ambient cube needs six directions.", nameof( cube ) );
		cube.Clear();
		float total = 0;
		foreach ( var sample in samples[leaf] )
		{
			float weight = 1f / ((sample.Position - position).LengthSquared + 1f);
			total += weight;
			for ( int side = 0; side < 6; side++ )
				cube[side] += sample.Cube[side] * weight;
		}

		if ( total > 0 )
			for ( int side = 0; side < 6; side++ )
				cube[side] /= total;
	}

	private static Vector3[] DecodeCube( ReadOnlySpan<byte> data )
	{
		var cube = new Vector3[6];
		for ( int side = 0; side < 6; side++ )
		{
			var color = LightmapCodec.Decode( data.Slice( side * 4, 4 ) );
			cube[side] = new Vector3( color.x, color.y, color.z ) * 255f;
			if ( !cube[side].IsFinite )
				throw new InvalidDataException( "BSP ambient cube has non-finite RGBExp32 lighting." );
		}

		return cube;
	}
}
