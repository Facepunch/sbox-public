using System;
using System.IO;
using System.Text;
using static Sandbox.Mounting.Source1.BspGeometry;

namespace Sandbox.Mounting.Source1;

internal partial class BspFile
{
	public MapProp[] StaticProps { get; private set; } = [];

	internal static MapProp[] ReadStaticProps( Stream stream, long fileStart, (int offset, int length, int version, int uncompressedSize) lump )
	{
		ValidateLump( lump, 0 );
		if ( lump.length == 0 )
			return [];
		if ( lump.uncompressedSize != 0 )
			throw new NotSupportedException( "Compressed outer BSP game lumps are unsupported; Source compresses individual game lumps." );
		var data = ReadRaw( stream, fileStart, lump );
		ValidateRange( 0, 4, data.Length, "Game lump count" );
		var count = BitConverter.ToInt32( data, 0 );
		ValidateRange( 4, (long)count * 16, data.Length, "Game lump directory" );
		var directoryEnd = 4 + count * 16;
		var selected = -1;
		for ( var i = 0; i < count; i++ )
		{
			var entry = 4 + i * 16;
			if ( BitConverter.ToInt32( data, entry ) != 0x73707270 )
				continue;
			if ( selected != -1 )
				throw new InvalidDataException( "Duplicate BSP static-prop game lump." );
			selected = entry;
		}

		if ( selected == -1 )
			return [];
		var flags = BitConverter.ToUInt16( data, selected + 4 );
		var version = BitConverter.ToUInt16( data, selected + 6 );
		var absoluteOffset = BitConverter.ToInt32( data, selected + 8 );
		var length = BitConverter.ToInt32( data, selected + 12 );
		if ( length < 0 )
			throw new InvalidDataException( "Negative BSP static-prop lump length." );
		if ( length == 0 )
			return [];
		if ( (flags & ~1) != 0 )
			throw new NotSupportedException( $"Unsupported BSP static-prop lump flags {flags}." );
		var relativeOffset = (long)absoluteOffset - lump.offset;
		ValidateRange( relativeOffset, 0, data.Length, "Static-prop game lump offset" );
		var start = (int)relativeOffset;
		if ( start < directoryEnd )
			throw new InvalidDataException( "Static-prop game lump overlaps its directory." );
		var end = data.Length;
		for ( var i = 0; i < count; i++ )
		{
			var next = (long)BitConverter.ToInt32( data, 4 + i * 16 + 8 ) - lump.offset;
			if ( next > start && next < end )
				end = (int)next;
		}

		byte[] payload;
		if ( (flags & 1) != 0 )
		{
			ValidateRange( start, 17, end, "Static-prop LZMA header" );
			var compressedSize = BitConverter.ToUInt32( data, start + 8 );
			ValidateRange( start, 17L + compressedSize, end, "Compressed static props" );
			payload = DecodeLump( data.AsSpan( start, (int)(17L + compressedSize) ).ToArray(), length );
		}
		else
		{
			ValidateRange( start, length, end, "Static-prop game lump" );
			payload = data.AsSpan( start, length ).ToArray();
		}

		return ParseStaticProps( payload, version );
	}

	internal static MapProp[] ParseStaticProps( byte[] data, int version )
	{
		if ( version is not (4 or 5 or 6 or 10) )
		{
			Log.Warning( $"BSP static-prop version {version} is unsupported; static props were omitted." );
			return [];
		}

		var position = 0;
		var modelCount = ReadStaticPropCount( data, ref position, 128, "Static-prop models" );
		var names = new string[modelCount];
		for ( var i = 0; i < names.Length; i++ )
		{
			var name = data.AsSpan( position, 128 );
			var terminator = name.IndexOf( (byte)0 );
			if ( terminator <= 0 )
				throw new InvalidDataException( "Static-prop model name is empty or unterminated." );
			names[i] = Encoding.UTF8.GetString( name[..terminator] );
			position += 128;
		}

		var leafCount = ReadStaticPropCount( data, ref position, 2, "Static-prop leaf references" );
		var propLeafIndices = new ushort[leafCount];
		for ( var i = 0; i < leafCount; i++ )
			propLeafIndices[i] = BitConverter.ToUInt16( data, position + i * 2 );
		position += leafCount * 2;
		var propCount = ReadStaticPropCount( data, ref position, 0, "Static-prop records" );
		var remaining = data.Length - position;
		if ( propCount == 0 )
		{
			if ( remaining != 0 )
				throw new InvalidDataException( "Unexpected data after empty static props." );
			return [];
		}

		if ( remaining % propCount != 0 || remaining / propCount < 56 )
			throw new InvalidDataException( "Truncated BSP static-prop records." );
		var stride = remaining / propCount;
		var expected = version switch
		{
			4 => 56,
			5 => 60,
			6 => 64,
			10 => 72,
			_ => 0
		};
		if ( stride < expected )
			throw new InvalidDataException( "Truncated BSP static-prop records." );
		if ( stride != expected )
		{
			Log.Warning( $"BSP static-prop layout v{version}/{stride} bytes is unsupported; {propCount} static props were omitted." );
			return [];
		}

		var props = new MapProp[propCount];
		for ( var i = 0; i < props.Length; i++, position += stride )
		{
			var modelIndex = BitConverter.ToUInt16( data, position + 24 );
			ValidateRange( modelIndex, 1, names.Length, "Static-prop model index" );
			var firstLeaf = BitConverter.ToUInt16( data, position + 26 );
			var propLeaves = BitConverter.ToUInt16( data, position + 28 );
			ValidateRange( firstLeaf, propLeaves, leafCount, "Static-prop leaf range" );
			var origin = ReadStaticPropVector( data, position );
			var angles = ReadStaticPropVector( data, position + 12 );
			var skin = BitConverter.ToInt32( data, position + 32 );
			if ( skin < 0 )
				throw new InvalidDataException( "Static prop has a negative skin." );
			var flags = version == 10 ? BitConverter.ToUInt32( data, position + 64 ) : data[position + 31];
			props[i] = new MapProp( names[modelIndex], $"prop_static_{i}", origin, new Angles( angles.x, angles.y, angles.z ), 1, skin, data[position + 30], Color.White, StaticPropIndex: i, StaticPropFlags: flags, LightingOrigin: (flags & 2) != 0 ? ReadStaticPropVector( data, position + 44 ) : origin, Leaves: propLeafIndices.AsSpan( firstLeaf, propLeaves ).ToArray() );
		}

		return props;
	}

	internal static int ReadStaticPropCount( byte[] data, ref int position, int size, string name )
	{
		ValidateRange( position, 4, data.Length, name );
		var count = BitConverter.ToInt32( data, position );
		position += 4;
		if ( count < 0 )
			throw new InvalidDataException( $"{name} count is negative." );
		ValidateRange( position, (long)count * size, data.Length, name );
		return count;
	}

	internal static Vector3 ReadStaticPropVector( byte[] data, int position )
	{
		var value = new Vector3( BitConverter.ToSingle( data, position ), BitConverter.ToSingle( data, position + 4 ), BitConverter.ToSingle( data, position + 8 ) );
		if ( !IsFinite( value ) )
			throw new InvalidDataException( "Static prop has a non-finite transform." );
		return value;
	}
}
