using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using static Sandbox.BytePack;

namespace Sandbox.Internal;

public partial class TypeLibrary
{
	BytePack bytePack;

	/// <summary>
	/// Prepare an independent serializer on the owning thread. It has no callbacks into the
	/// library, so a worker can use it even while the library is being rebuilt by hotload.
	/// Callers must supply every runtime type and only serialize detached data.
	/// </summary>
	internal BytePack CreateDetachedSerializer( params Type[] types )
	{
		var result = new BytePack();
		foreach ( var type in types )
		{
			result.Add( TryCreatePackerFor( type ) ?? throw new NotSupportedException( $"Unhandled type {type}" ) );
		}
		return result;
	}

	void InitBytePack()
	{
		bytePack?.Dispose();

		bytePack = new BytePack();
		bytePack.OnCreatePackerFromType = TryCreatePackerFor;
		bytePack.OnCreatePackerFromIdentifier = TryCreatePackerFor;
	}

	/// <summary>
	/// Serialize this value to bytes, where possible
	/// </summary>
	public byte[] ToBytes<T>( T value )
	{
		return bytePack.Serialize( value );
	}

	/// <summary>
	/// Serialize this value to bytes, where possible
	/// </summary>
	public void ToBytes<T>( T value, ref ByteStream bs )
	{
		bytePack.SerializeTo( ref bs, value );
	}

	/// <summary>
	/// Deserialize this from bytes. 
	/// If the type is unknown, T can be an object.
	/// </summary>
	public T FromBytes<T>( byte[] data )
	{
		return (T)bytePack.Deserialize( data );
	}

	/// <summary>
	/// Deserialize this from bytes. 
	/// If the type is unknown, T can be an object.
	/// </summary>
	public T FromBytes<T>( ReadOnlySpan<byte> data )
	{
		return (T)bytePack.Deserialize( data );
	}

	/// <summary>
	/// Deserialize this from bytes. 
	/// If the type is unknown, T can be an object.
	/// </summary>
	public T FromBytes<T>( ref ByteStream bs )
	{
		return (T)bytePack.Deserialize( ref bs );
	}

	private BytePack.Packer TryCreatePackerFor( Type type )
	{
		var t = GetType( type );
		if ( t is null ) return null;

		var pod = SandboxedUnsafe.IsAcceptablePod( type );

		if ( pod )
		{
			var packerType = typeof( ValuePacker<> ).MakeGenericType( type );
			return (BytePack.Packer)Activator.CreateInstance( packerType, new object[] { t.Identity } );
		}
		else
		{
			if ( type.IsAssignableTo( typeof( ISerializer ) ) )
			{
				var packerType = typeof( SerializerPacker<> ).MakeGenericType( type );
				return (BytePack.Packer)Activator.CreateInstance( packerType, new object[] { t } );
			}
			else
			{
				var packerType = typeof( TypePacker<> ).MakeGenericType( type );
				return (BytePack.Packer)Activator.CreateInstance( packerType, new object[] { t } );
			}
		}
	}

	private BytePack.Packer TryCreatePackerFor( int type )
	{
		var t = GetTypeByIdent( type );
		if ( t is null )
		{
			log.Warning( $"GetTypeByIdent - type {type} not found!" );
			return null;
		}

		return TryCreatePackerFor( t.TargetType );
	}
}

file class ValuePacker<T> : Packer where T : unmanaged
{
	public override Type TargetType => typeof( T );
	internal override Identifier Header => Identifier.Runtime;
	internal override int TypeIdentifier => ident;

	int ident;

	public ValuePacker( int ident )
	{
		this.ident = ident;
	}

	public override void WriteTypeIdentifier( ref ByteStream bs, Type targetType )
	{
		bs.Write( Header );
		bs.Write( TypeIdentifier );
	}

	public override void Write( ref ByteStream bs, object obj )
	{
		bs.Write( (T)obj );
	}

	public override object Read( ref ByteStream bs )
	{
		return bs.Read<T>();
	}
}

/// <summary>
/// A packer that handles serialization and deserialization for implementations of <see cref="ISerializer"/>.
/// </summary>
/// <typeparam name="T"></typeparam>
file class SerializerPacker<T> : Packer where T : ISerializer
{
	static Logger log = new Logger( $"SerializerPacker<{typeof( T )}>" );

	public override Type TargetType => typeof( T );
	internal override Identifier Header => Identifier.Runtime;
	internal override int TypeIdentifier { get; }

	public SerializerPacker( TypeDescription ts )
	{
		TypeIdentifier = ts.Identity;
	}

	public override void WriteTypeIdentifier( ref ByteStream bs, Type targetType )
	{
		bs.Write( Header );
		bs.Write( TypeIdentifier );
	}

	/// <summary>
	/// Write an object to the <see cref="ByteStream"/> through the implementation of <see cref="ISerializer.BytePackWrite"/> for this type.
	/// </summary>
	/// <param name="bs"></param>
	/// <param name="value"></param>
	public override void Write( ref ByteStream bs, object value )
	{
		try
		{
			T.BytePackWrite( value, ref bs );
		}
		catch ( System.Exception e )
		{
			log.Error( e );
		}
	}

	/// <summary>
	/// Read an object from the <see cref="ByteStream"/> through the implementation of <see cref="ISerializer.BytePackRead"/> for this type.
	/// </summary>
	/// <param name="bs"></param>
	/// <returns></returns>
	public override object Read( ref ByteStream bs )
	{
		try
		{
			return (T)T.BytePackRead( ref bs, TargetType );
		}
		catch ( System.Exception e )
		{
			log.Error( e );
		}

		return null;
	}
}

file class TypePacker<T> : Packer where T : new()
{
	public override Type TargetType => typeof( T );
	internal override Identifier Header => Identifier.Runtime;
	internal override int TypeIdentifier => ident;

	int ident;

	MemberInfo[] members;

	public TypePacker( TypeDescription ts )
	{
		this.ident = ts.Identity;

		if ( ts.IsValueType )
		{
			// For structs, we exactly only want to serialize fields since that's where all the
			// actual data is stored.

			// Have to get the fields directly, because we don't include property backing fields in TypeLibrary

			var bFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

			members = ts.TargetType.GetFields( bFlags )
				.OrderByDescending( x => x.Name )
				.ThenBy( x => x.DeclaringType!.Name )
				.Cast<MemberInfo>()
				.ToArray();
		}
		else
		{
			// For reference types, we want the same rules as json, kind of
			// TODO: do we want to use JsonTypeInfo here?

			members = ts.Members
				.Where( x => x is PropertyDescription { IsPublic: true, IsStatic: false } )
				.Where( x => !x.HasAttribute<JsonIgnoreAttribute>() ) // must not have [JsonIgnore]
				.Select( x => x.MemberInfo as PropertyInfo )
				.Where( x => x.SetMethod is not null ) // we must have a setter
				.OrderByDescending( x => x.Name )
				.ThenBy( x => x.DeclaringType!.Name )
				.ToArray<MemberInfo>();
		}
	}

	public override void WriteTypeIdentifier( ref ByteStream bs, Type targetType )
	{
		bs.Write( Header );
		bs.Write( TypeIdentifier );
	}

	public override void Write( ref ByteStream bs, object obj )
	{
		var visited = _visited.Value!;
		if ( !visited.Add( obj ) )
			throw new SerializationException( $"Cycle detected when serializing {typeof( T ).Name}" );

		try
		{
			foreach ( var member in members )
			{
				var value = member switch
				{
					PropertyInfo pi => pi.GetValue( obj ),
					FieldInfo fi => fi.GetValue( obj ),
					_ => throw new NotImplementedException()
				};

				Serialize( ref bs, value );
			}
		}
		finally
		{
			visited.Remove( obj );
		}
	}

	public override object Read( ref ByteStream bs )
	{
		object t = new T();

		foreach ( var member in members )
		{
			var value = Deserialize( ref bs );

			switch ( member )
			{
				case PropertyInfo pi:
					pi.SetValue( t, value );
					continue;

				case FieldInfo fi:
					fi.SetValue( t, value );
					continue;

				default:
					throw new System.NotImplementedException();
			}
		}

		return t;
	}
}
