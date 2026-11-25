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
		return bytePack.Deserialize<T>( data );
	}

	/// <summary>
	/// Deserialize this from bytes. 
	/// If the type is unknown, T can be an object.
	/// </summary>
	public T FromBytes<T>( ReadOnlySpan<byte> data )
	{
		return bytePack.Deserialize<T>( data );
	}

	/// <summary>
	/// Deserialize this from bytes. 
	/// If the type is unknown, T can be an object.
	/// </summary>
	public T FromBytes<T>( ref ByteStream bs )
	{
		return bytePack.Deserialize<T>( ref bs );
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
				var packerType = typeof( SerializerPacker<,> ).MakeGenericType( type, GetSerializerReadType( type ) );
				return (BytePack.Packer)Activator.CreateInstance( packerType, new object[] { t } );
			}
			else if ( StructPacker.CanPack( type ) )
			{
				var packerType = typeof( StructPacker<> ).MakeGenericType( type );
				return (BytePack.Packer)Activator.CreateInstance( packerType, new object[] { t } );
			}
			else
			{
				var packerType = typeof( TypePacker<> ).MakeGenericType( type );
				return (BytePack.Packer)Activator.CreateInstance( packerType, new object[] { t } );
			}
		}
	}

	private static Type GetSerializerReadType( Type type )
	{
		// Use the actual reader implementation, including explicit reimplementations.
		// An inherited reader can resolve any instance of the type that declares it.
		var map = type.GetInterfaceMap( typeof( ISerializer ) );
		for ( var i = 0; i < map.InterfaceMethods.Length; i++ )
		{
			if ( map.InterfaceMethods[i].Name == nameof( ISerializer.BytePackRead ) )
			{
				return map.TargetMethods[i].DeclaringType;
			}
		}

		throw new InvalidOperationException( $"No BytePack reader found for {type}." );
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

file class ValuePacker<T> : Packer, IValuePacker<T> where T : unmanaged
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

	void IValuePacker<T>.Write( ref ByteStream bs, T value )
	{
		bs.Write( value );
	}

	public override object Read( ref ByteStream bs, int depth )
	{
		return bs.Read<T>();
	}

	T IValuePacker<T>.Read( ref ByteStream bs ) => bs.Read<T>();
}

/// <summary>
/// A packer that handles serialization and deserialization for implementations of <see cref="ISerializer"/>.
/// </summary>
/// <typeparam name="T">The serialized runtime type.</typeparam>
/// <typeparam name="TResult">The type declaring the reader implementation.</typeparam>
file class SerializerPacker<T, TResult> : Packer where T : ISerializer
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
	/// <param name="depth"></param>
	/// <returns></returns>
	public override object Read( ref ByteStream bs, int depth )
	{
		try
		{
			return (TResult)T.BytePackRead( ref bs, TargetType );
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

	public override object Read( ref ByteStream bs, int depth )
	{
		object t = new T();

		foreach ( var member in members )
		{
			var value = Deserialize( ref bs, depth + 1 );

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

file static class StructPacker
{
	public const BindingFlags Fields = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

	/// <summary>
	/// A struct whose fields can all be generic arguments. Pointers and ref structs stay on the reflection packer.
	/// </summary>
	public static bool CanPack( Type type )
	{
		if ( !type.IsValueType || type.IsByRefLike || Nullable.GetUnderlyingType( type ) is not null )
			return false;

		return type.GetFields( Fields ).All( x => !x.FieldType.IsPointer && !x.FieldType.IsFunctionPointer && !x.FieldType.IsByRefLike && !x.FieldType.IsByRef );
	}
}

/// <summary>
/// Packs a struct field by field like <see cref="TypePacker{T}"/>, with the same bytes, but through compiled
/// accessors so neither the struct nor its fields are boxed.
/// </summary>
file sealed class StructPacker<T> : Packer, IValuePacker<T> where T : struct
{
	public override Type TargetType => typeof( T );
	internal override Identifier Header => Identifier.Runtime;
	internal override int TypeIdentifier => ident;

	readonly int ident;
	readonly StructField<T>[] fields;

	public StructPacker( TypeDescription ts )
	{
		ident = ts.Identity;
		fields = StructFields<T>.All;
	}

	public override void WriteTypeIdentifier( ref ByteStream bs, Type targetType )
	{
		bs.Write( Header );
		bs.Write( TypeIdentifier );
	}

	public override void Write( ref ByteStream bs, object obj )
	{
		// A struct can only lead back to itself through a box
		var visited = _visited.Value!;
		if ( !visited.Add( obj ) )
			throw new SerializationException( $"Cycle detected when serializing {typeof( T ).Name}" );

		try
		{
			var value = (T)obj;
			WriteFields( ref bs, ref value );
		}
		finally
		{
			visited.Remove( obj );
		}
	}

	void IValuePacker<T>.Write( ref ByteStream bs, T value ) => WriteFields( ref bs, ref value );

	void WriteFields( ref ByteStream bs, ref T value )
	{
		foreach ( var field in fields )
		{
			field.Write( this, ref bs, ref value );
		}
	}

	public override object Read( ref ByteStream bs, int depth ) => ReadFields( ref bs, depth );

	T IValuePacker<T>.Read( ref ByteStream bs ) => ReadFields( ref bs, 0 );

	T IValuePacker<T>.Read( ref ByteStream bs, int depth ) => ReadFields( ref bs, depth );

	T ReadFields( ref ByteStream bs, int depth )
	{
		var value = new T();

		foreach ( var field in fields )
		{
			field.Read( this, ref bs, ref value, depth + 1 );
		}

		return value;
	}
}

/// <summary>
/// A struct's compiled field accessors. They only depend on the type, so every BytePack rebuild reuses them.
/// </summary>
file static class StructFields<T> where T : struct
{
	public static readonly StructField<T>[] All = typeof( T ).GetFields( StructPacker.Fields )
		.OrderByDescending( x => x.Name )
		.ThenBy( x => x.DeclaringType!.Name )
		.Select( StructField<T>.Create )
		.ToArray();
}

file abstract class StructField<T> where T : struct
{
	public abstract void Write( Packer packer, ref ByteStream bs, ref T value );
	public abstract void Read( Packer packer, ref ByteStream bs, ref T value, int depth );

	public static StructField<T> Create( FieldInfo field )
	{
		var type = typeof( StructField<,> ).MakeGenericType( typeof( T ), field.FieldType );
		return (StructField<T>)Activator.CreateInstance( type, field );
	}
}

file sealed class StructField<T, TField> : StructField<T> where T : struct
{
	delegate TField Getter( ref T target );
	delegate void Setter( ref T target, TField value );

	readonly Getter _get;
	readonly Setter _set;

	public StructField( FieldInfo field )
	{
		// IL rather than expression trees, which can't assign readonly fields
		var get = new System.Reflection.Emit.DynamicMethod( $"get_{field.Name}", typeof( TField ), [typeof( T ).MakeByRefType()], true );
		var il = get.GetILGenerator();
		il.Emit( System.Reflection.Emit.OpCodes.Ldarg_0 );
		il.Emit( System.Reflection.Emit.OpCodes.Ldfld, field );
		il.Emit( System.Reflection.Emit.OpCodes.Ret );
		_get = get.CreateDelegate<Getter>();

		var set = new System.Reflection.Emit.DynamicMethod( $"set_{field.Name}", typeof( void ), [typeof( T ).MakeByRefType(), typeof( TField )], true );
		il = set.GetILGenerator();
		il.Emit( System.Reflection.Emit.OpCodes.Ldarg_0 );
		il.Emit( System.Reflection.Emit.OpCodes.Ldarg_1 );
		il.Emit( System.Reflection.Emit.OpCodes.Stfld, field );
		il.Emit( System.Reflection.Emit.OpCodes.Ret );
		_set = set.CreateDelegate<Setter>();
	}

	public override void Write( Packer packer, ref ByteStream bs, ref T value ) => packer.SerializeValue( ref bs, _get( ref value ) );

	public override void Read( Packer packer, ref ByteStream bs, ref T value, int depth ) => _set( ref value, packer.DeserializeValue<TField>( ref bs, depth ) );
}
