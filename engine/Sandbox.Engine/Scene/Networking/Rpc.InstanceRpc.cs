using System.ComponentModel;

namespace Sandbox;

public static partial class Rpc
{
	/// <summary>
	/// Does the current caller have permission to invoke the RPC?
	/// </summary>
	static bool HasHostInstancePermission( Connection caller, NetFlags permission )
	{
		if ( permission.Contains( NetFlags.HostOnly ) && !caller.IsHost )
			return false;

		return !permission.Contains( NetFlags.OwnerOnly ) || caller.IsHost;
	}

	/// <summary>
	/// Does the current caller have permission to invoke the RPC?
	/// </summary>
	static bool HasInstancePermission( Connection caller, GameObject go, NetFlags permission )
	{
		if ( permission.Contains( NetFlags.HostOnly ) && !caller.IsHost )
			return false;

		if ( permission.Contains( NetFlags.OwnerOnly ) )
		{
			if ( !go.IsValid() )
			{
				// Conna: if the RPC attribute permission is OwnerOnly but the game object is invalid,
				// then we can't be sure that we are the owner or have permission. So assume we don't.
				return false;
			}

			var hasOwner = go.Network.OwnerId != Guid.Empty;

			if ( (hasOwner && go.Network.OwnerId != caller.Id) || (!hasOwner && !caller.IsHost) )
				return false;
		}

		return true;
	}

	/// <summary>
	/// Called when an instance RPC is called for a <see cref="Scene"/> and <see cref="GameObjectSystem"/>.
	/// </summary>
	[EditorBrowsable( EditorBrowsableState.Never )]
	public static void OnCallInstanceRpc( in GameObjectSystem system, in WrappedMethod m, in object[] argumentList )
	{
		OnCallInstanceRpc( system, m, new ObjectRpcArgs( argumentList ) );
	}

	internal static void OnCallInstanceRpc<TArgs>( GameObjectSystem system, in WrappedMethod m, in TArgs args ) where TArgs : struct, IRpcArguments
	{
		var attribute = m.GetAttribute<RpcAttribute>();
		if ( attribute is null ) return;

		//
		// Send over network
		//
		if ( !Calling && Networking.IsActive )
		{
			SendInstanceRpc( system, m, args, attribute );
		}

		// Was filtered out
		if ( Filter.HasValue && !Filter.Value.IsRecipient( Connection.Local ) ) return;

		// We're the owner if we're the host
		var isOwner = Networking.IsHost;

		// Was not included in the filter
		if ( attribute.Mode == RpcMode.Owner && !isOwner ) return;
		if ( attribute.Mode == RpcMode.Host && !Networking.IsHost ) return;

		PreCall();

		if ( !HasHostInstancePermission( Caller ?? Connection.Local, attribute.Flags ) ) return;

		Resume( m );
	}

	/// <summary>
	/// Called when an instance RPC is called for a <see cref="GameObject"/> and <see cref="Component"/>.
	/// </summary>
	[EditorBrowsable( EditorBrowsableState.Never )]
	public static void OnCallInstanceRpc( in GameObject go, in Component component, in WrappedMethod m, in object[] argumentList )
	{
		OnCallInstanceRpc( go, component, m, new ObjectRpcArgs( argumentList ) );
	}

	internal static void OnCallInstanceRpc<TArgs>( GameObject go, Component component, in WrappedMethod m, in TArgs args ) where TArgs : struct, IRpcArguments
	{
		var attribute = m.GetAttribute<RpcAttribute>();
		if ( attribute is null ) return;

		//
		// Send over network
		//
		if ( !Calling && Networking.IsActive )
		{
			SendInstanceRpc( go, component, m, args, attribute );
		}

		// Was filtered out
		if ( Filter.HasValue && !Filter.Value.IsRecipient( Connection.Local ) ) return;

		// We're the owner if we are the owner or if it has no owner and we're the host
		var isOwner = go.Network.IsOwner || (go.Network.OwnerId == Guid.Empty && Networking.IsHost);

		// Was not included in the filter
		if ( attribute.Mode == RpcMode.Owner && !isOwner ) return;
		if ( attribute.Mode == RpcMode.Host && !Networking.IsHost ) return;

		PreCall();

		// Can they even call this shit
		if ( !HasInstancePermission( Caller ?? Connection.Local, go, attribute.Flags ) ) return;

		Resume( m );
	}

	/// <summary>
	/// Do the actual send of the instance RPC.
	/// </summary>
	private static void SendInstanceRpc<TArgs>( GameObjectSystem system, in WrappedMethod m, in TArgs args, RpcAttribute attribute ) where TArgs : struct, IRpcArguments
	{
		if ( !system.Scene.IsValid() ) return;

		var bs = BeginMessage( RpcTarget.System );

		try
		{
			bs.Write( system.Id );
			FinishMessage( ref bs, m, args );

			// Owner and host are both the host for a system
			SendMessage( ref bs, m, attribute, Connection.Host?.Id ?? Guid.Empty );
		}
		finally
		{
			bs.Dispose();
		}
	}

	/// <summary>
	/// Do the actual send of the instance RPC.
	/// </summary>
	private static void SendInstanceRpc<TArgs>( GameObject go, Component component, in WrappedMethod m, in TArgs args, RpcAttribute attribute ) where TArgs : struct, IRpcArguments
	{
		if ( !go.IsValid() ) return;

		var targetId = Connection.Host?.Id ?? Guid.Empty;

		// To the owner of the object, the host if it has none
		if ( attribute.Mode == RpcMode.Owner && go.Network.OwnerId != Guid.Empty )
			targetId = go.Network.OwnerId;

		var bs = BeginMessage( RpcTarget.Object );

		try
		{
			bs.Write( go.Id );
			bs.Write( component?.Id ?? Guid.Empty );
			FinishMessage( ref bs, m, args );
			SendMessage( ref bs, m, attribute, targetId );
		}
		finally
		{
			bs.Dispose();
		}
	}
}
