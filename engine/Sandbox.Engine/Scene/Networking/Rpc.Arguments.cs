using Sandbox.Internal;

namespace Sandbox;

/// <summary>
/// The arguments of an RPC call, written into its message without boxing them. Generated RPC wrappers pass typed
/// arguments (<see cref="RpcArgs{T1}"/> and friends), anything else comes as an <see cref="object"/> array.
/// </summary>
internal interface IRpcArguments
{
	int Count { get; }
	void Write( TypeLibrary library, ref ByteStream bs );
}

internal readonly struct ObjectRpcArgs( object[] args ) : IRpcArguments
{
	public int Count => args?.Length ?? 0;

	public void Write( TypeLibrary library, ref ByteStream bs )
	{
		if ( args is null ) return;

		foreach ( var arg in args )
			library.ToBytes( arg, ref bs );
	}
}

internal readonly struct RpcArgs : IRpcArguments
{
	public int Count => 0;
	public void Write( TypeLibrary library, ref ByteStream bs ) { }
}

internal readonly struct RpcArgs<T1>( T1 a1 ) : IRpcArguments
{
	public int Count => 1;

	public void Write( TypeLibrary library, ref ByteStream bs )
	{
		library.ToBytes( a1, ref bs );
	}
}

internal readonly struct RpcArgs<T1, T2>( T1 a1, T2 a2 ) : IRpcArguments
{
	public int Count => 2;

	public void Write( TypeLibrary library, ref ByteStream bs )
	{
		library.ToBytes( a1, ref bs );
		library.ToBytes( a2, ref bs );
	}
}

internal readonly struct RpcArgs<T1, T2, T3>( T1 a1, T2 a2, T3 a3 ) : IRpcArguments
{
	public int Count => 3;

	public void Write( TypeLibrary library, ref ByteStream bs )
	{
		library.ToBytes( a1, ref bs );
		library.ToBytes( a2, ref bs );
		library.ToBytes( a3, ref bs );
	}
}

internal readonly struct RpcArgs<T1, T2, T3, T4>( T1 a1, T2 a2, T3 a3, T4 a4 ) : IRpcArguments
{
	public int Count => 4;

	public void Write( TypeLibrary library, ref ByteStream bs )
	{
		library.ToBytes( a1, ref bs );
		library.ToBytes( a2, ref bs );
		library.ToBytes( a3, ref bs );
		library.ToBytes( a4, ref bs );
	}
}

internal readonly struct RpcArgs<T1, T2, T3, T4, T5>( T1 a1, T2 a2, T3 a3, T4 a4, T5 a5 ) : IRpcArguments
{
	public int Count => 5;

	public void Write( TypeLibrary library, ref ByteStream bs )
	{
		library.ToBytes( a1, ref bs );
		library.ToBytes( a2, ref bs );
		library.ToBytes( a3, ref bs );
		library.ToBytes( a4, ref bs );
		library.ToBytes( a5, ref bs );
	}
}

internal readonly struct RpcArgs<T1, T2, T3, T4, T5, T6>( T1 a1, T2 a2, T3 a3, T4 a4, T5 a5, T6 a6 ) : IRpcArguments
{
	public int Count => 6;

	public void Write( TypeLibrary library, ref ByteStream bs )
	{
		library.ToBytes( a1, ref bs );
		library.ToBytes( a2, ref bs );
		library.ToBytes( a3, ref bs );
		library.ToBytes( a4, ref bs );
		library.ToBytes( a5, ref bs );
		library.ToBytes( a6, ref bs );
	}
}
