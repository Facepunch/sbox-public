using System;
using System.Buffers.Binary;
using System.Text;

namespace SeriousEngine3;

public enum DataTypeTag : uint
{
	Primitive = 0,
	Enum = 1,
	Pointer = 2,
	Array = 4,
	Struct = 5,
	StaticArray = 6,
	StaticStackArray = 7,
	DynamicContainer = 8,
	Ptr = 11,
	Handle = 12,
	TypeDef = 13,
}

public abstract class DataTypeType
{
	public abstract DataTypeTag Tag { get; }

	public sealed class PrimitiveT : DataTypeType { public uint Bytes; public uint LBE; public override DataTypeTag Tag => DataTypeTag.Primitive; }
	public sealed class EnumT : DataTypeType { public uint Bytes; public override DataTypeTag Tag => DataTypeTag.Enum; }
	public sealed class PointerT : DataTypeType { public uint To; public override DataTypeTag Tag => DataTypeTag.Pointer; }
	public sealed class ArrayT : DataTypeType { public uint Of; public uint Length; public override DataTypeTag Tag => DataTypeTag.Array; }
	public sealed class StaticArrayT : DataTypeType { public uint Of; public override DataTypeTag Tag => DataTypeTag.StaticArray; }
	public sealed class StaticStackArrayT : DataTypeType { public uint Of; public override DataTypeTag Tag => DataTypeTag.StaticStackArray; }
	public sealed class DynamicContainerT : DataTypeType { public uint Of; public override DataTypeTag Tag => DataTypeTag.DynamicContainer; }
	public sealed class PtrT : DataTypeType { public uint Param; public override DataTypeTag Tag => DataTypeTag.Ptr; }
	public sealed class HandleT : DataTypeType { public uint Param; public override DataTypeTag Tag => DataTypeTag.Handle; }
	public sealed class TypeDefT : DataTypeType { public uint For; public override DataTypeTag Tag => DataTypeTag.TypeDef; }

	public sealed class StructT : DataTypeType
	{
		public int Base;
		public List<StructMember> Members = [];
		public Dictionary<string, int> MemberIndex;
		public override DataTypeTag Tag => DataTypeTag.Struct;

		public void BuildIndex()
		{
			MemberIndex = new Dictionary<string, int>( Members.Count );
			for ( int i = 0; i < Members.Count; i++ )
				MemberIndex.TryAdd( Members[i].Name, i );
		}
	}

	public static DataTypeType Read( MetaReader r )
	{
		var tag = (DataTypeTag)r.U32();
		switch ( tag )
		{
			case DataTypeTag.Primitive:
				return new PrimitiveT { Bytes = r.U32(), LBE = r.U32() };
			case DataTypeTag.Enum:
				return new EnumT { Bytes = r.U32() };
			case DataTypeTag.Pointer:
				return new PointerT { To = r.U32() };
			case DataTypeTag.Array:
				{
					var of = r.U32();
					r.Expect( "ADIM" );
					var dimCount = r.U32();
					uint length = dimCount > 0 ? 1u : 0u;
					for ( int d = 0; d < dimCount; d++ ) length *= r.U32();
					return new ArrayT { Of = of, Length = length };
				}
			case DataTypeTag.Struct:
				{
					var baseId = r.I32();
					r.Expect( "STMB" );
					var count = checked((int)r.U32());
					var members = new List<StructMember>( count );
					for ( int i = 0; i < count; i++ )
						members.Add( new StructMember { Name = r.PascalString(), Type = r.U32() } );
					var st = new StructT { Base = baseId, Members = members };
					st.BuildIndex();
					return st;
				}
			case DataTypeTag.StaticArray:
				return new StaticArrayT { Of = r.U32() };
			case DataTypeTag.StaticStackArray:
				return new StaticStackArrayT { Of = r.U32() };
			case DataTypeTag.DynamicContainer:
				return new DynamicContainerT { Of = r.U32() };
			case DataTypeTag.Ptr:
				return new PtrT { Param = r.U32() };
			case DataTypeTag.Handle:
				return new HandleT { Param = r.U32() };
			case DataTypeTag.TypeDef:
				return new TypeDefT { For = r.U32() };
			default:
				throw new InvalidDataException( $"Unknown DataType tag: {(uint)tag}" );
		}
	}
}

public sealed class StructMember
{
	public string Name;
	public uint Type;
}

public sealed class DataType
{
	public uint Id;
	public string Name;
	public uint Format;
	public DataTypeType Type;
}

public sealed class Ident
{
	public uint Id;
	public string Name;
}

public sealed class ResourceFile
{
	public uint Id;
	public uint Flags;
	public string Path;
}

public sealed class ExternalType
{
	public uint Type;
	public string Name;
}

public sealed class ExternalObject
{
	public uint Id;
	public uint ResourceId;
	public uint Unknown;
	public uint ObjectId;
	public uint TypeId;
}

public enum ValueKind
{
	Pointer,
	CString,
	IDENT,
	UBYTE,
	ULONG,
	SLONG,
	UQUAD,
	SQUAD,
	FLOAT,
	Primitive,
	SLONGEnum,
	Enum,
	Array,
	Struct,
	CSyncedSLONG,
	StaticStackArray,
	StaticArray,
	DynamicContainer,
	Ptr,
	Handle,
}

public sealed class Value
{
	public ValueKind Kind;
	public int Pointer;
	public string CString;
	public uint IDENT;
	public byte UBYTE;
	public uint ULONG;
	public int SLONG;
	public ulong UQUAD;
	public long SQUAD;
	public float FLOAT;
	public byte[] Primitive;
	public int SLONGEnum;
	public byte[] EnumBytes;
	public List<Value> Array;
	public (Value Base, List<Value> Members) Struct;
	public int CSyncedSLONG;
	public List<Value> Elements;
	public byte[] BulkBytes;
	public float[] BulkFloats;
	public int[] BulkInts;
	public List<uint> Container;
}

public sealed class InternalObject
{
	public uint Object;
	public uint Type;
	public Value Value;
}

public sealed class CTSEMeta
{
	public const string Magic = "CTSEMETA";
	public const uint Cookie = 0x1234ABCD;

	public uint Version { get; private set; }
	public string VersionString { get; private set; }

	public List<ResourceFile> ResourceFiles { get; private set; }
	public List<Ident> Idents { get; private set; }
	public List<ExternalType> ExternalTypes { get; private set; }
	public List<DataType> Types { get; private set; }
	public List<ExternalObject> ExternalObjects { get; private set; }
	public List<InternalObject> Objects { get; private set; }

	public GameMount Host { get; set; }

	Dictionary<uint, DataType> _typeById;
	Dictionary<string, uint> _typeIdByName;
	Dictionary<uint, InternalObject> _objectById;
	Dictionary<uint, List<InternalObject>> _objectsByType;
	Dictionary<string, CTSEMeta> _externalCache;

	CTSEMeta() { }

	public static CTSEMeta Read( byte[] data )
	{
		var r = new MetaReader( data );

		if ( !r.Magic8( Magic ) ) throw new InvalidDataException( "Bad header magic" );
		var cookie = r.U32();
		if ( cookie != Cookie ) throw new InvalidDataException( $"Bad endianness cookie: 0x{cookie:X8}" );

		var meta = new CTSEMeta { Version = r.U32() };
		if ( meta.Version >= 2 ) meta.VersionString = r.PascalString();

		r.Expect( "MSGS" ); r.ExpectEmpty();
		r.Expect( "INFO" ); r.Skip( 5 * 4 );

		r.Expect( "RFIL" );
		meta.ResourceFiles = r.Vec( x => new ResourceFile { Id = x.U32(), Flags = x.U32(), Path = x.PascalString() } );

		r.Expect( "IDNT" );
		meta.Idents = r.Vec( x => new Ident { Id = x.U32(), Name = x.PascalString() } );

		r.Expect( "EXTY" );
		meta.ExternalTypes = r.Vec( x => new ExternalType { Type = x.U32(), Name = x.PascalString() } );

		r.Expect( "INTY" );
		meta.Types = r.Vec( x =>
		{
			x.Expect( "DTTY" );
			return new DataType { Id = x.U32(), Name = x.PascalString(), Format = x.U32(), Type = DataTypeType.Read( x ) };
		} );

		r.Expect( "EXOB" );
		meta.ExternalObjects = r.Vec( x => new ExternalObject
		{
			Id = x.U32(),
			ResourceId = x.U32(),
			Unknown = x.U32(),
			ObjectId = x.U32(),
			TypeId = x.U32()
		} );

		meta.BuildTypeMaps();

		r.Expect( "OBTY" );
		var objectTypes = r.Vec( x => (Object: x.U32(), Type: x.U32()) );

		r.Expect( "EDTY" );
		r.Vec( x => (x.U32(), x.U32()) );

		r.Expect( "OBJS" );
		var values = new ValueReader( meta._typeById );
		meta.Objects = r.Vec( x =>
		{
			var obj = new InternalObject { Object = x.U32(), Type = x.U32() };
			obj.Value = values.Read( x, obj.Type );
			return obj;
		} );

		r.Expect( "EDOB" );
		if ( r.U32() == 0 && !r.Magic8( "METAEND " ) )
			throw new InvalidDataException( "Missing METAEND" );

		meta.BuildObjectMaps( objectTypes );
		return meta;
	}

	void BuildTypeMaps()
	{
		_typeById = new Dictionary<uint, DataType>( Types.Count );
		_typeIdByName = new Dictionary<string, uint>( Types.Count );
		foreach ( var t in Types )
		{
			_typeById[t.Id] = t;
			_typeIdByName.TryAdd( t.Name, t.Id );
		}
	}

	void BuildObjectMaps( List<(uint Object, uint Type)> objectTypes )
	{
		_objectById = new Dictionary<uint, InternalObject>( Objects.Count );
		foreach ( var o in Objects ) _objectById[o.Object] = o;

		_objectsByType = [];
		foreach ( var (objId, typeId) in objectTypes )
		{
			if ( !_objectById.TryGetValue( objId, out var obj ) ) continue;
			if ( !_objectsByType.TryGetValue( typeId, out var list ) )
				_objectsByType[typeId] = list = [];
			list.Add( obj );
		}
	}

	public IEnumerable<string> TypeNames => _typeIdByName.Keys;

	public bool HasType( string typeName ) => _typeIdByName.ContainsKey( typeName );

	public uint GetTypeId( string typeName ) => _typeIdByName.TryGetValue( typeName, out var id )
		? id : throw new InvalidOperationException( $"Type '{typeName}' not found in INTY." );

	public DataType GetType( uint typeId ) => _typeById.TryGetValue( typeId, out var t )
		? t : throw new KeyNotFoundException( $"DataType {typeId} not found." );

	public InternalObject GetObject( uint objectId ) => _objectById.TryGetValue( objectId, out var o )
		? o : throw new KeyNotFoundException( $"Object {objectId} not found." );

	public bool TryGetObject( uint objectId, out InternalObject obj ) => _objectById.TryGetValue( objectId, out obj );

	public IReadOnlyList<InternalObject> GetObjectsOfType( string typeName )
	{
		if ( !_typeIdByName.TryGetValue( typeName, out var id ) ) return [];
		return _objectsByType.TryGetValue( id, out var list ) ? list : (IReadOnlyList<InternalObject>)[];
	}

	public InternalObject GetFirstObjectOfType( string typeName )
	{
		var list = GetObjectsOfType( typeName );
		if ( list.Count == 0 ) throw new InvalidOperationException( $"No object with type '{typeName}' found." );
		return list[0];
	}

	public bool TryGetFirstObjectOfType( string typeName, out InternalObject obj )
	{
		var list = GetObjectsOfType( typeName );
		obj = list.Count > 0 ? list[0] : null;
		return obj != null;
	}

	public MetaValue Get( string typeName )
		=> GetOrNull( typeName ) ?? throw new InvalidOperationException( $"No object with type '{typeName}' found (internal or external)." );

	public MetaValue GetOrNull( string typeName )
	{
		if ( TryGetFirstObjectOfType( typeName, out var obj ) )
			return new MetaValue( obj.Value, obj.Type, this );

		var external = ResolveExternal( typeName );
		var direct = external?.GetOrNull( typeName );
		if ( direct != null ) return direct;

		if ( _externalCache != null )
		{
			foreach ( var cached in _externalCache.Values )
			{
				var hit = cached?.GetOrNull( typeName );
				if ( hit != null ) return hit;
			}
		}

		if ( Host != null )
		{
			foreach ( var extType in ExternalTypes )
			{
				if ( extType.Name == typeName ) continue;
				var hit = ResolveExternal( extType.Name )?.GetOrNull( typeName );
				if ( hit != null ) return hit;
			}
		}

		return null;
	}

	public IReadOnlyList<MetaValue> GetAll( string typeName )
	{
		var objects = GetObjectsOfType( typeName );
		var result = new List<MetaValue>( objects.Count );
		foreach ( var obj in objects ) result.Add( new MetaValue( obj.Value, obj.Type, this ) );
		return result;
	}

	public MetaValue Wrap( InternalObject obj ) => new( obj.Value, obj.Type, this );

	public MetaValue WrapById( uint objectId ) => TryGetObject( objectId, out var obj )
		? new MetaValue( obj.Value, obj.Type, this ) : null;

	public string GetExternalResourcePath( int objectId )
	{
		foreach ( var e in ExternalObjects )
		{
			if ( e.Id != objectId ) continue;
			return e.ResourceId < ResourceFiles.Count ? ResourceFiles[(int)e.ResourceId].Path : null;
		}
		return null;
	}

	public string GetExternalResourcePath( string externalTypeName )
	{
		uint typeId = 0;
		var found = false;
		foreach ( var t in ExternalTypes )
		{
			if ( t.Name != externalTypeName ) continue;
			typeId = t.Type; found = true; break;
		}
		if ( !found ) return null;

		foreach ( var e in ExternalObjects )
		{
			if ( e.TypeId != typeId ) continue;
			return e.ResourceId < ResourceFiles.Count ? ResourceFiles[(int)e.ResourceId].Path : null;
		}
		return null;
	}

	CTSEMeta ResolveExternal( string typeName )
	{
		if ( Host == null ) return null;

		var path = GetExternalResourcePath( typeName );
		if ( path == null ) return null;

		_externalCache ??= [];
		if ( !_externalCache.TryGetValue( path, out var cached ) )
		{
			try
			{
				cached = Read( Host.GetBytes( path ) );
				cached.Host = Host;
			}
			catch
			{
				cached = null;
			}
			_externalCache[path] = cached;
		}
		return cached;
	}
}

public sealed class ValueReader
{
	readonly Dictionary<uint, DataType> _types;

	public ValueReader( Dictionary<uint, DataType> types ) => _types = types;

	public Value Read( MetaReader r, uint typeId )
	{
		if ( !_types.TryGetValue( typeId, out var dt ) )
			throw new InvalidDataException( $"Tried to read external type {typeId}" );

		switch ( dt.Type )
		{
			case DataTypeType.PrimitiveT p:
				return dt.Name switch
				{
					"CString" => new Value { Kind = ValueKind.CString, CString = r.PascalString() },
					"IDENT" => new Value { Kind = ValueKind.IDENT, IDENT = r.U32() },
					"UBYTE" => new Value { Kind = ValueKind.UBYTE, UBYTE = r.U8() },
					"ULONG" => new Value { Kind = ValueKind.ULONG, ULONG = r.U32() },
					"SLONG" => new Value { Kind = ValueKind.SLONG, SLONG = r.I32() },
					"UQUAD" => new Value { Kind = ValueKind.UQUAD, UQUAD = r.U64() },
					"SQUAD" => new Value { Kind = ValueKind.SQUAD, SQUAD = r.I64() },
					"FLOAT" => new Value { Kind = ValueKind.FLOAT, FLOAT = r.F32() },
					_ => new Value { Kind = ValueKind.Primitive, Primitive = r.Bytes( checked((int)p.Bytes) ) },
				};

			case DataTypeType.EnumT e:
				return e.Bytes == 4
					? new Value { Kind = ValueKind.SLONGEnum, SLONGEnum = r.I32() }
					: new Value { Kind = ValueKind.Enum, EnumBytes = r.Bytes( checked((int)e.Bytes) ) };

			case DataTypeType.PointerT:
				return new Value { Kind = ValueKind.Pointer, Pointer = r.I32() };
			case DataTypeType.PtrT:
				return new Value { Kind = ValueKind.Ptr, Pointer = r.I32() };
			case DataTypeType.HandleT:
				return new Value { Kind = ValueKind.Handle, Pointer = r.I32() };

			case DataTypeType.ArrayT a:
				{
					var list = new List<Value>( checked((int)a.Length) );
					for ( int i = 0; i < a.Length; i++ ) list.Add( Read( r, a.Of ) );
					return new Value { Kind = ValueKind.Array, Array = list };
				}

			case DataTypeType.StructT s:
				{
					if ( dt.Name == "CSyncedSLONG" && s.Members.Count == 0 )
						return new Value { Kind = ValueKind.CSyncedSLONG, CSyncedSLONG = r.I32() };

					if ( dt.Name == "CMetaHandle" )
						return new Value { Kind = ValueKind.Handle, Pointer = r.I32() };

					if ( dt.Name == "CTransString" )
						r.Expect( "DTRS" );

					var baseVal = s.Base != -1 ? Read( r, (uint)s.Base ) : null;
					var members = new List<Value>( s.Members.Count );
					foreach ( var m in s.Members ) members.Add( Read( r, m.Type ) );
					return new Value { Kind = ValueKind.Struct, Struct = (baseVal, members) };
				}

			case DataTypeType.StaticStackArrayT ssa:
				return ReadBulkArray( r, "SSAR", ssa.Of, ValueKind.StaticStackArray );

			case DataTypeType.StaticArrayT sa:
				return ReadBulkArray( r, "STAR", sa.Of, ValueKind.StaticArray );

			case DataTypeType.DynamicContainerT:
				{
					r.Expect( "DCON" );
					var count = checked((int)r.U32());
					var ids = new List<uint>( count );
					for ( int i = 0; i < count; i++ ) ids.Add( r.U32() );
					return new Value { Kind = ValueKind.DynamicContainer, Container = ids };
				}

			case DataTypeType.TypeDefT td:
				return Read( r, td.For );

			default:
				throw new NotSupportedException( $"{dt.Type} not handled" );
		}
	}

	Value ReadBulkArray( MetaReader r, string magic, uint elementType, ValueKind kind )
	{
		r.Expect( magic );
		var count = checked((int)r.U32());

		if ( ResolvesToPrimitive( elementType, "UBYTE" ) )
			return new Value { Kind = kind, BulkBytes = r.Bytes( count ) };

		if ( ResolvesToPrimitive( elementType, "FLOAT" ) )
		{
			var floats = new float[count];
			for ( int i = 0; i < count; i++ ) floats[i] = r.F32();
			return new Value { Kind = kind, BulkFloats = floats };
		}

		if ( ResolvesToPrimitive( elementType, "SLONG" ) )
		{
			var ints = new int[count];
			for ( int i = 0; i < count; i++ ) ints[i] = r.I32();
			return new Value { Kind = kind, BulkInts = ints };
		}

		var list = new List<Value>( count );
		for ( int i = 0; i < count; i++ ) list.Add( Read( r, elementType ) );
		return new Value { Kind = kind, Elements = list };
	}

	bool ResolvesToPrimitive( uint typeId, string name )
	{
		if ( !_types.TryGetValue( typeId, out var dt ) ) return false;
		if ( dt.Type is DataTypeType.PrimitiveT && dt.Name == name ) return true;
		if ( dt.Type is DataTypeType.TypeDefT td ) return ResolvesToPrimitive( td.For, name );
		return false;
	}
}

public sealed class MetaReader
{
	readonly byte[] _data;
	int _pos;

	public MetaReader( byte[] data ) => _data = data;

	public int Position => _pos;

	ReadOnlySpan<byte> Take( int n )
	{
		if ( (uint)(_pos + n) > (uint)_data.Length ) throw new EndOfStreamException();
		var span = _data.AsSpan( _pos, n );
		_pos += n;
		return span;
	}

	public void Skip( int n )
	{
		if ( (uint)(_pos + n) > (uint)_data.Length ) throw new EndOfStreamException();
		_pos += n;
	}

	public byte U8() => Take( 1 )[0];
	public uint U32() => BinaryPrimitives.ReadUInt32LittleEndian( Take( 4 ) );
	public int I32() => BinaryPrimitives.ReadInt32LittleEndian( Take( 4 ) );
	public ulong U64() => BinaryPrimitives.ReadUInt64LittleEndian( Take( 8 ) );
	public long I64() => BinaryPrimitives.ReadInt64LittleEndian( Take( 8 ) );
	public float F32() => BinaryPrimitives.ReadSingleLittleEndian( Take( 4 ) );
	public byte[] Bytes( int n ) => Take( n ).ToArray();

	public string PascalString()
	{
		var len = checked((int)U32());
		return len == 0 ? string.Empty : Encoding.UTF8.GetString( Take( len ) );
	}

	public bool Magic8( string magic )
	{
		var span = Take( 8 );
		for ( int i = 0; i < 8; i++ ) if ( span[i] != magic[i] ) return false;
		return true;
	}

	public void Expect( string fourcc )
	{
		var span = Take( 4 );
		if ( span[0] != fourcc[0] || span[1] != fourcc[1] || span[2] != fourcc[2] || span[3] != fourcc[3] )
			throw new InvalidDataException( $"Expected '{fourcc}' at offset {_pos - 4}" );
	}

	public void ExpectEmpty()
	{
		if ( U32() != 0 ) throw new InvalidDataException( "Expected empty section" );
	}

	public List<T> Vec<T>( Func<MetaReader, T> read )
	{
		var count = checked((int)U32());
		var list = new List<T>( count );
		for ( int i = 0; i < count; i++ ) list.Add( read( this ) );
		return list;
	}
}

public sealed class MetaValue
{
	public Value Raw { get; }
	public DataType Type { get; }
	public CTSEMeta Meta { get; }

	internal MetaValue( Value value, DataType type, CTSEMeta meta )
	{
		Raw = value;
		Type = type;
		Meta = meta;
	}

	internal MetaValue( Value value, uint typeId, CTSEMeta meta )
		: this( value, meta.GetType( typeId ), meta ) { }

	public ValueKind Kind => Raw.Kind;

	public MetaValue this[string memberName]
	{
		get
		{
			if ( Raw.Kind != ValueKind.Struct )
				throw new InvalidOperationException( $"Cannot access member '{memberName}' on {Raw.Kind} (type '{Type.Name}')." );

			return ResolveMember( Type, Raw, memberName )
				?? throw new KeyNotFoundException( $"Member '{memberName}' not found on type '{Type.Name}'." );
		}
	}

	public MetaValue GetOrNull( string memberName )
		=> Raw.Kind == ValueKind.Struct ? ResolveMember( Type, Raw, memberName ) : null;

	public MetaValue this[int index]
	{
		get
		{
			if ( Raw.Kind == ValueKind.DynamicContainer && Type.Type is DataTypeType.DynamicContainerT )
			{
				var ids = Raw.Container;
				if ( (uint)index >= (uint)ids.Count ) throw new ArgumentOutOfRangeException( nameof( index ) );
				return Meta.WrapById( ids[index] );
			}

			var (list, elementType) = ListAndElementType();
			if ( (uint)index >= (uint)list.Count )
				throw new ArgumentOutOfRangeException( nameof( index ), $"Index {index} out of range for {Raw.Kind} with {list.Count} elements." );
			return new MetaValue( list[index], elementType, Meta );
		}
	}

	public int Count
	{
		get
		{
			if ( Raw.Kind == ValueKind.DynamicContainer ) return Raw.Container?.Count ?? 0;
			if ( Raw.BulkBytes != null ) return Raw.BulkBytes.Length;
			if ( Raw.BulkFloats != null ) return Raw.BulkFloats.Length;
			if ( Raw.BulkInts != null ) return Raw.BulkInts.Length;
			return ListAndElementType().List.Count;
		}
	}

	public int MemberCount => Type.Type is DataTypeType.StructT st ? st.Members.Count : 0;

	public MetaValue Member( int index )
	{
		if ( Raw.Kind != ValueKind.Struct )
			throw new InvalidOperationException( $"Cannot access member by index on {Raw.Kind}." );
		if ( Type.Type is not DataTypeType.StructT st )
			throw new InvalidOperationException( $"Type '{Type.Name}' is not a struct." );
		if ( (uint)index >= (uint)st.Members.Count )
			throw new ArgumentOutOfRangeException( nameof( index ), $"Struct '{Type.Name}' has {st.Members.Count} members, index {index} is out of range." );

		return new MetaValue( Raw.Struct.Members[index], st.Members[index].Type, Meta );
	}

	public MetaValue Base
	{
		get
		{
			if ( Raw.Kind != ValueKind.Struct )
				throw new InvalidOperationException( $"Cannot access Base on {Raw.Kind}." );
			if ( Type.Type is not DataTypeType.StructT st || st.Base == -1 || Raw.Struct.Base == null )
				return null;
			return new MetaValue( Raw.Struct.Base, (uint)st.Base, Meta );
		}
	}

	public IEnumerable<(string Name, MetaValue Value)> Members()
	{
		if ( Raw.Kind != ValueKind.Struct || Type.Type is not DataTypeType.StructT st ) yield break;
		for ( int i = 0; i < st.Members.Count; i++ )
			yield return (st.Members[i].Name, new MetaValue( Raw.Struct.Members[i], st.Members[i].Type, Meta ));
	}

	public IEnumerable<MetaValue> Elements()
	{
		var (list, elementType) = ListAndElementType();
		for ( int i = 0; i < list.Count; i++ )
			yield return new MetaValue( list[i], elementType, Meta );
	}

	public int AsInt() => Raw.Kind switch
	{
		ValueKind.SLONG => Raw.SLONG,
		ValueKind.CSyncedSLONG => Raw.CSyncedSLONG,
		_ => throw new InvalidOperationException( $"Cannot read as int: value is {Raw.Kind}." ),
	};

	public uint AsUInt() => Raw.Kind == ValueKind.ULONG ? Raw.ULONG
		: throw new InvalidOperationException( $"Cannot read as uint: value is {Raw.Kind}." );

	public float AsFloat() => Raw.Kind == ValueKind.FLOAT ? Raw.FLOAT
		: throw new InvalidOperationException( $"Cannot read as float: value is {Raw.Kind}." );

	public byte AsByte() => Raw.Kind == ValueKind.UBYTE ? Raw.UBYTE
		: throw new InvalidOperationException( $"Cannot read as byte: value is {Raw.Kind}." );

	public ushort AsUShort() => Raw.Kind switch
	{
		ValueKind.Primitive when Raw.Primitive.Length == 2 => BitConverter.ToUInt16( Raw.Primitive, 0 ),
		ValueKind.ULONG => (ushort)Raw.ULONG,
		_ => throw new InvalidOperationException( $"Cannot read as ushort: value is {Raw.Kind}." ),
	};

	public string AsString() => Raw.Kind == ValueKind.CString ? Raw.CString
		: throw new InvalidOperationException( $"Cannot read as string: value is {Raw.Kind}." );

	public uint AsIdent() => Raw.Kind == ValueKind.IDENT ? Raw.IDENT
		: throw new InvalidOperationException( $"Cannot read as IDENT: value is {Raw.Kind}." );

	public string AsIdentName()
	{
		var id = AsIdent();
		if ( id >= (uint)Meta.Idents.Count )
			throw new InvalidOperationException( $"IDENT index {id} out of range (max {Meta.Idents.Count})." );
		return Meta.Idents[(int)id].Name;
	}

	public int AsEnum() => Raw.Kind == ValueKind.SLONGEnum ? Raw.SLONGEnum
		: throw new InvalidOperationException( $"Cannot read as enum: value is {Raw.Kind}." );

	public ulong AsULong64() => Raw.Kind == ValueKind.UQUAD ? Raw.UQUAD
		: throw new InvalidOperationException( $"Cannot read as UQUAD: value is {Raw.Kind}." );

	public long AsLong64() => Raw.Kind == ValueKind.SQUAD ? Raw.SQUAD
		: throw new InvalidOperationException( $"Cannot read as SQUAD: value is {Raw.Kind}." );

	public int AsPointer() => Raw.Kind switch
	{
		ValueKind.Pointer or ValueKind.Ptr or ValueKind.Handle => Raw.Pointer,
		_ => throw new InvalidOperationException( $"Cannot read as pointer: value is {Raw.Kind}." ),
	};

	public MetaValue Deref()
	{
		var ptr = AsPointer();
		if ( ptr <= 0 ) return null;
		var obj = Meta.GetObject( (uint)ptr );
		return new MetaValue( obj.Value, obj.Type, Meta );
	}

	public byte[] AsBytes() => Raw.Kind switch
	{
		ValueKind.Primitive => Raw.Primitive,
		ValueKind.Enum => Raw.EnumBytes,
		_ => throw new InvalidOperationException( $"Cannot read as bytes: value is {Raw.Kind}." ),
	};

	public Vector3 AsVector3()
	{
		if ( Raw.Kind != ValueKind.Struct )
			throw new InvalidOperationException( $"Cannot read as Vector3: value is {Raw.Kind}." );
		var m = Raw.Struct.Members;
		return new Vector3( m[0].FLOAT, m[1].FLOAT, m[2].FLOAT );
	}

	public Rotation AsQuaternion()
	{
		if ( Raw.Kind != ValueKind.Struct )
			throw new InvalidOperationException( $"Cannot read as Quaternion: value is {Raw.Kind}." );
		var m = Raw.Struct.Members;
		return new Rotation( m[0].FLOAT, m[1].FLOAT, m[2].FLOAT, m[3].FLOAT );
	}

	public byte[] AsByteArray()
	{
		if ( Raw.Kind is not (ValueKind.StaticStackArray or ValueKind.StaticArray) )
			throw new InvalidOperationException( $"Cannot read as byte array: value is {Raw.Kind}." );
		if ( Raw.BulkBytes != null ) return Raw.BulkBytes;

		var list = Raw.Elements;
		var bytes = new byte[list.Count];
		for ( int i = 0; i < list.Count; i++ ) bytes[i] = list[i].UBYTE;
		return bytes;
	}

	public float[] AsFloatArray()
	{
		if ( Raw.Kind is not (ValueKind.StaticStackArray or ValueKind.StaticArray) )
			throw new InvalidOperationException( $"Cannot read as float array: value is {Raw.Kind}." );
		if ( Raw.BulkFloats != null ) return Raw.BulkFloats;

		var list = Raw.Elements;
		var floats = new float[list.Count];
		for ( int i = 0; i < list.Count; i++ ) floats[i] = list[i].FLOAT;
		return floats;
	}

	public int[] AsIntArray()
	{
		if ( Raw.Kind is not (ValueKind.StaticStackArray or ValueKind.StaticArray) )
			throw new InvalidOperationException( $"Cannot read as int array: value is {Raw.Kind}." );
		if ( Raw.BulkInts != null ) return Raw.BulkInts;

		var list = Raw.Elements;
		var ints = new int[list.Count];
		for ( int i = 0; i < list.Count; i++ ) ints[i] = list[i].SLONG;
		return ints;
	}

	public override string ToString() => Raw.Kind switch
	{
		ValueKind.SLONG => Raw.SLONG.ToString(),
		ValueKind.ULONG => Raw.ULONG.ToString(),
		ValueKind.FLOAT => Raw.FLOAT.ToString(),
		ValueKind.CString => Raw.CString,
		ValueKind.UBYTE => Raw.UBYTE.ToString(),
		ValueKind.IDENT => $"IDENT({Raw.IDENT})",
		ValueKind.Pointer or ValueKind.Ptr or ValueKind.Handle => $"Ptr({Raw.Pointer})",
		ValueKind.Struct => $"Struct({Type.Name})",
		ValueKind.StaticStackArray or ValueKind.StaticArray => $"Array[{Count}]",
		ValueKind.Array => $"Array[{Raw.Array.Count}]",
		_ => $"{Raw.Kind}",
	};

	MetaValue ResolveMember( DataType type, Value value, string memberName )
	{
		if ( type.Type is not DataTypeType.StructT st ) return null;

		if ( st.MemberIndex != null && st.MemberIndex.TryGetValue( memberName, out int idx ) )
			return new MetaValue( value.Struct.Members[idx], st.Members[idx].Type, Meta );

		if ( st.Base != -1 && value.Struct.Base != null )
			return ResolveMember( Meta.GetType( (uint)st.Base ), value.Struct.Base, memberName );

		return null;
	}

	(List<Value> List, uint ElementType) ListAndElementType() => Raw.Kind switch
	{
		ValueKind.StaticStackArray when Type.Type is DataTypeType.StaticStackArrayT ssa => (Raw.Elements, ssa.Of),
		ValueKind.StaticArray when Type.Type is DataTypeType.StaticArrayT sa => (Raw.Elements, sa.Of),
		ValueKind.Array when Type.Type is DataTypeType.ArrayT a => (Raw.Array, a.Of),
		_ => throw new InvalidOperationException( $"Cannot index into {Raw.Kind} (type '{Type.Name}', schema tag '{Type.Type.Tag}')." ),
	};
}
