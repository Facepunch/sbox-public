using System.Buffers;
using System.Buffers.Binary;

namespace Nav;

/// <summary>
/// The bake: finished tiles, with no links, no volumes and no rebuild cache. A tile is written as its
/// polygons and the cells its vertices sit on; everything else a tile holds is made again at load. The
/// bytes are small numbers meant for a general codec such as LZ4 on top. Version 3 is a mesh of one
/// agent type, version 4 one of several; a mesh of one type always writes version 3.
///   Header   version 3: "NAVB", 3, the settings field by field, tile count, all four bytes wide
///            version 4: "NAVB", 4, the number of agent types, then per type the settings field by
///            field and its tile count, all four bytes wide
///   Tiles    per agent type in order, its tiles in the order of y, then x. Numbers are varints, seven
///            bits a byte with the low bits first, and a signed one keeps its sign in the lowest bit.
///            Per tile:
///              x and y, as differences to the tile before
///              polygon count times two, plus one when a volume-set hash follows
///              vertex count
///              the volume-set hash, eight bytes, when there is one
///              a byte per polygon: vertex count less three in the low two bits, the area above
///              the vertex index of every polygon corner, as the difference to the corner before
///              x of every vertex, then y of every vertex, then z of every vertex, each as the
///              difference to the vertex before: x and y in cells from the tile's corner, z in cell
///              heights from zero
/// </summary>
public sealed partial class NavMesh
{
	const uint BakeMagic = 0x4256414E, OneTypeVersion = 3, TypesVersion = 4;
	const int OneTypeHeader = 8 + MeshAgentType.SettingsBytes + 4, TypesHeader = 12, TypeHeader = MeshAgentType.SettingsBytes + 4;

	/// <summary>
	/// Writes the bake. False, with nothing written, when it could exceed 2 GB. Any thread, also while
	/// the owner commits. The tiles written are those of one Commit, each with the volume set it was
	/// built with or, when the save overlaps a Commit, one it had before, never the set of a later build.
	/// A load keeps a tile only under the set written with it, so loaded under the volumes it was built
	/// with, a tile is kept or rebuilt, never kept out of date. The writer has to hand out the header as
	/// one span and each tile as one span.
	/// A vertex is written as the cell it sits on, which is where the builder puts every vertex, so a
	/// loaded tile is the built one bit for bit.
	/// </summary>
	public bool Save( IBufferWriter<byte> output )
	{
		int n = types.Length;
		var states = new MeshState[n];
		var hashes = new (int Y, int X, ulong Hash)[n][];
		// The hashes before the state: the owner publishes them after it. With several types, every
		// type as of one commit.
		for ( var spin = new SpinWait(); ; spin.SpinOnce() )
		{
			int before = Volatile.Read( ref publishing );
			if ( (before & 1) != 0 ) continue;
			for ( int k = 0; k < n; k++ ) hashes[k] = types[k].TileHashes;
			for ( int k = 0; k < n; k++ ) states[k] = types[k].State;
			if ( n == 1 || Volatile.Read( ref publishing ) == before ) break;
		}

		long most = n == 1 ? OneTypeHeader : TypesHeader + (long)TypeHeader * n;
		var tiles = new Tile[n][];
		for ( int k = 0; k < n; k++ )
		{
			tiles[k] = MeshAgentType.SortedTiles( states[k], out long bytes );
			most += bytes;
		}
		if ( most > int.MaxValue ) return false;

		int headerBytes = n == 1 ? OneTypeHeader : TypesHeader + TypeHeader * n;
		var head = output.GetSpan( headerBytes )[..headerBytes];
		BinaryPrimitives.WriteUInt32LittleEndian( head, BakeMagic );
		if ( n == 1 )
		{
			BinaryPrimitives.WriteUInt32LittleEndian( head[4..], OneTypeVersion );
			MeshAgentType.WriteSettings( head[8..], types[0].Settings );
			BinaryPrimitives.WriteInt32LittleEndian( head[(8 + MeshAgentType.SettingsBytes)..], tiles[0].Length );
		}
		else
		{
			BinaryPrimitives.WriteUInt32LittleEndian( head[4..], TypesVersion );
			BinaryPrimitives.WriteInt32LittleEndian( head[8..], n );
			for ( int k = 0; k < n; k++ )
			{
				var at = head[(TypesHeader + TypeHeader * k)..];
				MeshAgentType.WriteSettings( at, types[k].Settings );
				BinaryPrimitives.WriteInt32LittleEndian( at[MeshAgentType.SettingsBytes..], tiles[k].Length );
			}
		}
		output.Advance( headerBytes );
		for ( int k = 0; k < n; k++ ) types[k].WriteTiles( output, tiles[k], hashes[k] );
		return true;
	}

	/// <summary>
	/// The settings of a bake of a mesh of one agent type. False when the data is not such a bake (a bake
	/// of several types included) or its settings are not valid.
	/// </summary>
	public static bool TryReadSettings( ReadOnlySpan<byte> data, out NavSettings settings )
	{
		settings = default;
		if ( FormatVersion( data ) != OneTypeVersion || data.Length < OneTypeHeader ) return false;
		settings = MeshAgentType.ReadSettings( data[8..] );
		return settings.IsValid( out _ );
	}

	/// <summary>
	/// Appends the settings of every agent type of a bake, in order, one for a mesh of one type. False,
	/// with nothing appended, when the data is not a bake of a format version this library reads or a
	/// type's settings are not valid.
	/// </summary>
	public static bool TryReadSettings( ReadOnlySpan<byte> data, List<NavSettings> agentTypes )
	{
		if ( !ReadHeader( data, out int count, out _ ) ) return false;
		for ( int k = 0; k < count; k++ ) agentTypes.Add( SettingsOf( data, count, k ) );
		return true;
	}

	static uint FormatVersion( ReadOnlySpan<byte> data ) => data.Length >= 8 && BinaryPrimitives.ReadUInt32LittleEndian( data ) == BakeMagic ? BinaryPrimitives.ReadUInt32LittleEndian( data[4..] ) : 0;

	static NavSettings SettingsOf( ReadOnlySpan<byte> data, int count, int type ) => MeshAgentType.ReadSettings( count == 1 && FormatVersion( data ) == OneTypeVersion ? data[8..] : data[(TypesHeader + TypeHeader * type)..] );

	static int TileCountOf( ReadOnlySpan<byte> data, int count, int type ) => BinaryPrimitives.ReadInt32LittleEndian( count == 1 && FormatVersion( data ) == OneTypeVersion ? data[(8 + MeshAgentType.SettingsBytes)..] : data[(TypesHeader + TypeHeader * type + MeshAgentType.SettingsBytes)..] );

	// The number of agent types and the header's length, for a bake whose header and settings are good.
	static bool ReadHeader( ReadOnlySpan<byte> data, out int count, out int headerBytes )
	{
		count = headerBytes = 0;
		uint version = FormatVersion( data );
		if ( version == OneTypeVersion )
		{
			count = 1;
			headerBytes = OneTypeHeader;
		}
		else if ( version == TypesVersion && data.Length >= TypesHeader )
		{
			count = BinaryPrimitives.ReadInt32LittleEndian( data[8..] );
			// A bake of several types never has one.
			if ( count < 2 || count > MaxAgentTypes ) return false;
			headerBytes = TypesHeader + TypeHeader * count;
		}
		else return false;
		if ( data.Length < headerBytes ) return false;
		for ( int k = 0; k < count; k++ )
		{
			if ( !SettingsOf( data, count, k ).IsValid( out _ ) ) return false;
		}
		return true;
	}

	/// <summary>
	/// Makes a mesh of one agent type from its bake. Null when anything in the data is wrong, when it is
	/// a bake of several types, or when the link settings are not valid for the bake's settings: every
	/// count, index and coordinate is checked before it is used, and what is not in the data is built
	/// here, so it cannot be wrong. Keeps nothing of data.
	/// A bake holds neither volumes nor links: add those of the scene to the new mesh before its first
	/// TakeJobs. That call compares each tile's volumes with the ones it was baked with and issues a job,
	/// which asks for geometry, for every tile where they differ. Generated links are not in a bake
	/// either: a loaded tile has none until it is built from triangles again.
	/// </summary>
	public static NavMesh? Load( ReadOnlySpan<byte> data, in NavMeshOptions options )
	{
		if ( FormatVersion( data ) != OneTypeVersion ) return null;
		return Load( data, new ReadOnlySpan<NavMeshOptions>( in options ) );
	}

	/// <summary>
	/// Load for a bake of any number of agent types, with the options of each type in order: as many as
	/// the bake has types. Null when they are not as many, or as Load says.
	/// </summary>
	public static NavMesh? Load( ReadOnlySpan<byte> data, ReadOnlySpan<NavMeshOptions> options )
	{
		if ( !ReadHeader( data, out int count, out int headerBytes ) || options.Length != count ) return null;
		var descs = new NavAgentTypeDesc[count];
		long least = headerBytes;
		for ( int k = 0; k < count; k++ )
		{
			var settings = SettingsOf( data, count, k );
			int tiles = TileCountOf( data, count, k );
			if ( !options[k].Links.IsValid( settings, out _ ) || tiles < 0 || tiles > PolyRef.MaxTiles( settings.PolyBits ) ) return null;
			least += (long)tiles * MeshAgentType.MinTileBytes;
			descs[k] = new NavAgentTypeDesc { Settings = settings, Options = options[k] };
		}
		if ( least > data.Length ) return null;

		var reader = new MeshAgentType.BakeReader( data, headerBytes );
		var read = new (Tile[] Tiles, ulong[] Hashes)[count];
		for ( int k = 0; k < count; k++ )
		{
			var tiles = MeshAgentType.ReadTiles( ref reader, descs[k].Settings, TileCountOf( data, count, k ), out var hashes );
			if ( tiles == null ) return null;
			read[k] = (tiles, hashes);
		}
		if ( reader.Left != 0 ) return null;

		var mesh = count == 1 ? new NavMesh( descs[0].Settings, descs[0].Options ) : new NavMesh( descs );
		mesh.counted = false;
		for ( int k = 0; k < count; k++ ) mesh.types[k].Loaded( read[k].Tiles, read[k].Hashes );
		return mesh;
	}
}
