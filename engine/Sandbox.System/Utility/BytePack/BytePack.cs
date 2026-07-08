namespace Sandbox;

/// <summary>
/// A class that can serialize and deserialize whole objects to and from byte streams, 
/// without needing to know the type beforehand.
/// 
/// https://docs.facepunch.com/doc/bytepack-FdTR6ZUoOa
///
/// </summary>
internal partial class BytePack
{
	readonly Dictionary<Type, Packer> types = new();
	readonly Dictionary<Identifier, Packer> handlers = new();
	readonly Dictionary<int, Packer> typeHandler = new();

	// Cap recursion so a deeply nested payload can't overflow the stack (uncatchable). Thread-local.
	[ThreadStatic] static int _depth;
	const int MaxDepth = 500;

	internal Func<Type, Packer> OnCreatePackerFromType { get; set; }
	internal Func<int, Packer> OnCreatePackerFromIdentifier { get; set; }

	public BytePack()
	{
		InstallPodCommon();

		Add( new ObjectPacker() );
		Add( new StringPacker() );
		Add( new ValueArrayPacker() );
		Add( new ObjectArrayPacker() );
		Add( new ListPacker() );
		Add( new DictionaryPacker() );
	}

	public void Dispose()
	{
		types.Clear();
		handlers.Clear();
		typeHandler.Clear();

		OnCreatePackerFromType = default;
		OnCreatePackerFromIdentifier = default;
	}

	void Add( Packer ti )
	{
		ti.Init( this );
	}

	public byte[] Serialize<T>( T obj )
	{
		ByteStream bs = ByteStream.Create( 1024 );

		try
		{
			Serialize( ref bs, obj );
			return bs.ToArray();
		}
		finally
		{
			bs.Dispose();
		}
	}

	public void SerializeTo<T>( ref ByteStream bs, T obj )
	{
		Serialize( ref bs, obj );
	}

	public object Deserialize( byte[] memory ) => Deserialize( new ReadOnlySpan<byte>( memory ) );
	public object Deserialize( ReadOnlySpan<byte> data )
	{
		var bs = ByteStream.CreateReader( data );

		try
		{
			return Deserialize( ref bs );
		}
		finally
		{
			bs.Dispose();
		}
	}

	public object Deserialize( ref ByteStream data )
	{
		_depth++;

		try
		{
			if ( _depth > MaxDepth )
				throw new System.Exception( $"BytePack recursion depth exceeded ({MaxDepth}) - possible malicious payload" );

			var h = data.Read<Identifier>();

			if ( h == Identifier.Runtime )
			{
				int typeIdent = data.Read<int>();
				var p = GetOrCreatePacker( typeIdent );

				if ( p is not null )
				{
					return p.Read( ref data );
				}

				throw new System.Exception( $"Unhandled runtime ident {typeIdent}" );
			}

			if ( handlers.TryGetValue( h, out var typeInfo ) )
			{
				return typeInfo.Read( ref data );
			}


			if ( h == Identifier.Null ) return null;
			throw new System.Exception( $"Unhandled header {h}" );
		}
		finally
		{
			_depth--;
		}
	}

	void Serialize<T>( ref ByteStream bs, T obj )
	{
		if ( obj is null )
		{
			bs.Write( Identifier.Null );
			return;
		}

		if ( obj is Array array )
		{
			var element = array.GetType().GetElementType();

			if ( SandboxedUnsafe.IsAcceptablePod( element ) )
			{
				bs.Write( Identifier.ArrayValue );
				Serialize( Identifier.ArrayValue, ref bs, array );
				return;
			}

			bs.Write( Identifier.Array );
			Serialize( Identifier.Array, ref bs, array );
			return;
		}

		var t = obj.GetType();

		if ( t.IsBasedOnGenericType( typeof( List<> ) ) )
		{
			bs.Write( Identifier.List );
			Serialize( Identifier.List, ref bs, obj );
			return;
		}

		if ( t.IsBasedOnGenericType( typeof( Dictionary<,> ) ) )
		{
			bs.Write( Identifier.Dictionary );
			Serialize( Identifier.Dictionary, ref bs, obj );
			return;
		}

		if ( GetOrCreatePacker( t ) is Packer packer )
		{
			packer.WriteTypeIdentifier( ref bs, t );
			packer.Write( ref bs, obj );
			return;
		}

		throw new System.NotSupportedException( $"Unhandled type {t}" );
	}

	private Packer GetOrCreatePacker( Type type )
	{
		if ( types.TryGetValue( type, out var packer ) )
			return packer;

		if ( OnCreatePackerFromType is null )
			return null;

		packer = OnCreatePackerFromType( type );
		if ( packer is null ) return null;

		packer.Init( this );
		return packer;
	}


	private Packer GetOrCreatePacker( int typeIdentifier )
	{
		if ( typeHandler.TryGetValue( typeIdentifier, out var packer ) )
			return packer;

		if ( OnCreatePackerFromIdentifier is null )
			return null;

		packer = OnCreatePackerFromIdentifier( typeIdentifier );
		if ( packer is null )
			return null;

		packer.Init( this );
		return packer;
	}

	void Serialize( Identifier ident, ref ByteStream bs, object obj )
	{
		handlers[ident].Write( ref bs, obj );
	}
}
