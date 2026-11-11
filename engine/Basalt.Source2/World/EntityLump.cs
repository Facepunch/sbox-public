using Basalt.Source2.Models;
using Basalt.Source2.Resources;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;

namespace Basalt.Source2.World;

/// <summary>
/// A compiled map's entity lump (<c>.vents_c</c>, KV3 <c>DATA</c>): its entities' key values and the lumps under it
/// (<c>m_childLumps</c>). Each entity's key values are a binary blob (<c>m_keyValuesData</c>): a version, a count of
/// keys stored by hash and a count stored with their names, then per key its hash (MurmurHash2 of the lower-case name,
/// seed <c>0x31415926</c>), its name for the second kind, its field type and its value. Newer lumps may keep them as
/// KV3 (<c>m_keyValues3Data</c>) instead.
/// </summary>
public sealed class EntityLump
{
	/// <summary>The lump's path, <c>maps/construct_25/entities/default_ents.vents</c>.</summary>
	public string Name { get; init; }

	/// <summary>Its entities.</summary>
	public IReadOnlyList<MapEntity> Entities { get; init; }

	/// <summary>The lumps under it, by path.</summary>
	public IReadOnlyList<string> ChildLumps { get; init; }

	/// <summary>Read a lump from <paramref name="content"/>.</summary>
	public static EntityLump Load( ContentFiles content, string path )
	{
		var root = Kv3.Read( ResourceFile.Read( content.Read( path ) ).Block( "DATA" ) ) as Dictionary<string, object>
			?? throw new InvalidDataException( $"{path}: DATA isn't an object" );

		var entities = new List<MapEntity>();
		foreach ( var entry in Kv.Objects( root.Get( "m_entityKeyValues" ) ) )
		{
			if ( entry.Get( "m_keyValuesData" ) is byte[] { Length: > 0 } data ) entities.Add( MapEntity.Read( data ) );
			else if ( entry.Get( "m_keyValues3Data" ) is Dictionary<string, object> kv3 ) entities.Add( MapEntity.FromKv3( kv3 ) );
		}

		return new EntityLump { Name = path, Entities = entities, ChildLumps = Kv.Strings( root.Get( "m_childLumps" ) ) };
	}
}

/// <summary>
/// One entity's key values, looked up by name (hashed as the compiler hashed them). Values are as stored: strings,
/// floats, ints, bools, vectors (<see cref="Vector3"/>), colours (four bytes) and so on; the typed getters also read
/// the string forms Hammer writes (<c>"1 2 3"</c>).
/// </summary>
public sealed class MapEntity
{
	readonly Dictionary<uint, object> values;

	MapEntity( Dictionary<uint, object> values ) => this.values = values;

	/// <summary>How many key values it has.</summary>
	public int Count => values.Count;

	/// <summary>Its class, <c>light_spot</c>.</summary>
	public string ClassName => String( "classname" );

	/// <summary>A key's value, or null.</summary>
	public object this[string key] => values.TryGetValue( Hash( key ), out var value ) ? value : null;

	/// <summary>A key's value as a string, or <paramref name="fallback"/>.</summary>
	public string String( string key, string fallback = null ) => this[key] switch
	{
		null => fallback,
		string s => s,
		object o => System.Convert.ToString( o, System.Globalization.CultureInfo.InvariantCulture ),
	};

	/// <summary>A key's value as a number, or <paramref name="fallback"/>.</summary>
	public float Float( string key, float fallback = 0 ) => this[key] switch
	{
		float f => f,
		double d => (float)d,
		int i => i,
		uint u => u,
		long l => l,
		ulong ul => ul,
		short s => s,
		byte b => b,
		bool b => b ? 1 : 0,
		string s when float.TryParse( s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var f ) => f,
		_ => fallback,
	};

	/// <summary>A key's value as a bool: true, or a non-zero number.</summary>
	public bool Bool( string key, bool fallback = false ) => this[key] switch
	{
		null => fallback,
		bool b => b,
		string s when bool.TryParse( s, out var b ) => b,
		_ => Float( key, fallback ? 1 : 0 ) != 0,
	};

	/// <summary>A key's value as three numbers (a vector, angles), or <paramref name="fallback"/>.</summary>
	public Vector3 Vector( string key, Vector3 fallback = default )
	{
		switch ( this[key] )
		{
			case Vector3 v: return v;
			case Vector4 v: return new Vector3( v.X, v.Y, v.Z );
			case string s:
				var parts = s.Split( ' ', StringSplitOptions.RemoveEmptyEntries );
				if ( parts.Length < 3 ) return fallback;
				float P( int i ) => float.Parse( parts[i], System.Globalization.CultureInfo.InvariantCulture );
				return new Vector3( P( 0 ), P( 1 ), P( 2 ) );
			default: return fallback;
		}
	}

	/// <summary>A colour as four 0-255 components (alpha 255 when it has three), or <paramref name="fallback"/>.</summary>
	public Vector4 Color( string key, Vector4 fallback = default )
	{
		switch ( this[key] )
		{
			case byte[] { Length: >= 4 } c: return new Vector4( c[0], c[1], c[2], c[3] );
			case Vector3 v: return new Vector4( v, 255 );
			case Vector4 v: return v;
			case string s:
				var parts = s.Split( ' ', StringSplitOptions.RemoveEmptyEntries ).Select( x => float.Parse( x, System.Globalization.CultureInfo.InvariantCulture ) ).ToArray();
				return parts.Length >= 3 ? new Vector4( parts[0], parts[1], parts[2], parts.Length > 3 ? parts[3] : 255 ) : fallback;
			default: return fallback;
		}
	}

	/// <summary>Its position (<c>origin</c>).</summary>
	public Vector3 Origin => Vector( "origin" );

	/// <summary>Its pitch, yaw and roll in degrees (<c>angles</c>).</summary>
	public Vector3 Angles => Vector( "angles" );

	/// <summary>The hash a key is stored by: MurmurHash2 of its lower-case name, seed <c>0x31415926</c>.</summary>
	public static uint Hash( string key )
	{
		const uint M = 0x5bd1e995;
		var data = Encoding.UTF8.GetBytes( key.ToLowerInvariant() );
		var length = data.Length;
		var h = 0x31415926u ^ (uint)length;
		var i = 0;
		for ( ; length >= 4; i += 4, length -= 4 )
		{
			var k = BinaryPrimitives.ReadUInt32LittleEndian( data.AsSpan( i ) ) * M;
			k ^= k >> 24;
			k *= M;
			h = h * M ^ k;
		}

		if ( length == 3 ) h ^= (uint)data[i + 2] << 16;
		if ( length >= 2 ) h ^= (uint)data[i + 1] << 8;
		if ( length >= 1 ) h = (h ^ data[i]) * M;

		h ^= h >> 13;
		h *= M;
		h ^= h >> 15;
		return h;
	}

	internal static MapEntity Read( byte[] data )
	{
		var reader = new ByteReader( data );
		reader.UInt();
		var hashed = (int)reader.UInt();
		var named = (int)reader.UInt();
		var values = new Dictionary<uint, object>( hashed + named );

		for ( int i = 0; i < hashed; i++ )
		{
			var hash = reader.UInt();
			values[hash] = Value( reader, reader.UInt() );
		}

		for ( int i = 0; i < named; i++ )
		{
			reader.UInt();
			var name = reader.Terminated();
			values[Hash( name )] = Value( reader, reader.UInt() );
		}

		return new MapEntity( values );
	}

	internal static MapEntity FromKv3( Dictionary<string, object> kv3 )
	{
		var source = kv3.Get( "values" ) as Dictionary<string, object> ?? kv3;
		return new MapEntity( source.ToDictionary( x => Hash( x.Key ), x => x.Value is object[] a && a.All( v => v is double or float or long or int ) ? Vector3Of( a ) : x.Value ) );
	}

	static object Vector3Of( object[] a ) => a.Length switch
	{
		3 => new Vector3( (float)Kv.Number( a[0] ), (float)Kv.Number( a[1] ), (float)Kv.Number( a[2] ) ),
		4 => new Vector4( (float)Kv.Number( a[0] ), (float)Kv.Number( a[1] ), (float)Kv.Number( a[2] ), (float)Kv.Number( a[3] ) ),
		_ => a,
	};

	/// <summary>A value by its field type (<c>fieldtype_t</c>).</summary>
	static object Value( ByteReader reader, uint type ) => type switch
	{
		1 => reader.Float(), // float
		2 or 17 or 28 or 30 or 31 or 53 => reader.Terminated(), // string, sound name, resource, C string, script, utl string
		3 or 14 or 39 or 45 or 46 => new Vector3( reader.Float(), reader.Float(), reader.Float() ), // vector, position, angles, direction
		4 or 27 => new Vector4( reader.Float(), reader.Float(), reader.Float(), reader.Float() ), // quaternion, vector4
		5 => reader.Int(), // int
		6 => reader.Byte() != 0, // bool
		7 => reader.Short(),
		8 => reader.Byte(), // char
		9 => reader.Bytes( 4 ), // colour, four bytes
		13 or 15 or 16 or 37 or 38 => reader.UInt(), // handle, time, tick, uint, token
		25 => new Vector2( reader.Float(), reader.Float() ),
		26 => reader.Long(), // int64
		33 => reader.ULong(), // uint64
		34 => BitConverter.Int64BitsToDouble( reader.Long() ), // double
		20 or 21 => reader.Bytes( 64 ), // matrices
		22 => reader.Bytes( 48 ),
		_ => throw new InvalidDataException( $"Entity key value type {type} isn't read" ),
	};
}
