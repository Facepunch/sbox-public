using System.ComponentModel;

namespace Sandbox;

public static partial class Rpc
{
	/// <summary>
	/// Called when a static RPC is called with a single argument of an array type.
	/// </summary>
	[EditorBrowsable( EditorBrowsableState.Never )]
	public static void OnCallRpc<T>( WrappedMethod m, T[] argument )
	{
		OnCallRpcInternal( m, new RpcArgs<T[]>( argument ) );
	}

	/// <summary>
	/// Called when a static RPC is called with object parameters.
	/// </summary>
	[EditorBrowsable( EditorBrowsableState.Never )]
	public static void OnCallRpc( WrappedMethod m, params object[] argumentList )
	{
		OnCallRpcInternal( m, new ObjectRpcArgs( argumentList ) );
	}

	// Generated static RPC wrappers bind to these, so arguments are written without boxing them

	[EditorBrowsable( EditorBrowsableState.Never )]
	public static void OnCallRpc( WrappedMethod m ) => OnCallRpcInternal( m, new RpcArgs() );

	[EditorBrowsable( EditorBrowsableState.Never )]
	public static void OnCallRpc<T1>( WrappedMethod m, T1 a1 ) => OnCallRpcInternal( m, new RpcArgs<T1>( a1 ) );

	[EditorBrowsable( EditorBrowsableState.Never )]
	public static void OnCallRpc<T1, T2>( WrappedMethod m, T1 a1, T2 a2 ) => OnCallRpcInternal( m, new RpcArgs<T1, T2>( a1, a2 ) );

	[EditorBrowsable( EditorBrowsableState.Never )]
	public static void OnCallRpc<T1, T2, T3>( WrappedMethod m, T1 a1, T2 a2, T3 a3 ) => OnCallRpcInternal( m, new RpcArgs<T1, T2, T3>( a1, a2, a3 ) );

	[EditorBrowsable( EditorBrowsableState.Never )]
	public static void OnCallRpc<T1, T2, T3, T4>( WrappedMethod m, T1 a1, T2 a2, T3 a3, T4 a4 ) => OnCallRpcInternal( m, new RpcArgs<T1, T2, T3, T4>( a1, a2, a3, a4 ) );

	[EditorBrowsable( EditorBrowsableState.Never )]
	public static void OnCallRpc<T1, T2, T3, T4, T5>( WrappedMethod m, T1 a1, T2 a2, T3 a3, T4 a4, T5 a5 ) => OnCallRpcInternal( m, new RpcArgs<T1, T2, T3, T4, T5>( a1, a2, a3, a4, a5 ) );

	[EditorBrowsable( EditorBrowsableState.Never )]
	public static void OnCallRpc<T1, T2, T3, T4, T5, T6>( WrappedMethod m, T1 a1, T2 a2, T3 a3, T4 a4, T5 a5, T6 a6 ) => OnCallRpcInternal( m, new RpcArgs<T1, T2, T3, T4, T5, T6>( a1, a2, a3, a4, a5, a6 ) );

	static void OnCallRpcInternal<TArgs>( WrappedMethod m, in TArgs args ) where TArgs : struct, IRpcArguments
	{
		var attribute = m.GetAttribute<RpcAttribute>();
		if ( attribute is null ) return;

		//
		// Send over network
		//
		if ( !Calling && Networking.IsActive )
		{
			SendStaticRpc( m, args, attribute );
		}
		// Was filtered out
		if ( Filter.HasValue && !Filter.Value.IsRecipient( Connection.Local ) ) return;

		// Was not included in the filter
		if ( attribute.Mode == RpcMode.Owner && !Networking.IsHost ) return;
		if ( attribute.Mode == RpcMode.Host && !Networking.IsHost ) return;

		PreCall();

		// Can they even call this shit
		if ( !HasStaticPermission( Caller ?? Connection.Local, attribute.Flags ) ) return;

		Resume( m );
	}

	/// <summary>
	/// Does the current caller have permission to invoke the RPC?
	/// </summary>
	static bool HasStaticPermission( Connection caller, NetFlags permission )
	{
		if ( (permission.Contains( NetFlags.HostOnly ) || permission.Contains( NetFlags.OwnerOnly )) && !caller.IsHost )
			return false;

		return true;
	}

	/// <summary>
	/// Do the actual send of the static RPC.
	/// </summary>
	private static void SendStaticRpc<TArgs>( WrappedMethod m, in TArgs args, RpcAttribute attribute ) where TArgs : struct, IRpcArguments
	{
		// To the host (statics can't have an owner)
		if ( attribute.Mode is RpcMode.Owner or RpcMode.Host && Networking.IsHost )
			return; // don't send to ourselves

		var bs = BeginMessage( RpcTarget.Static );

		try
		{
			FinishMessage( ref bs, m, args );
			SendMessage( ref bs, m, attribute, Connection.Host?.Id ?? Guid.Empty );
		}
		finally
		{
			bs.Dispose();
		}
	}
}
