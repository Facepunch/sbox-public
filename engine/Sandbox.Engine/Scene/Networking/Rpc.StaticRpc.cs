using System.ComponentModel;
using System.Reflection;

namespace Sandbox;

public static partial class Rpc
{
	/// <summary>
	/// A static RPC call is incoming from the network. Look up the method and call it.
	/// </summary>
	internal static void IncomingStaticRpcMsg( StaticRpcMsg message, Connection source )
	{
		NetworkDebugSystem.Current?.Record( NetworkDebugSystem.MessageType.Rpc, message );

		if ( Game.TypeLibrary.GetMemberByIdent( message.MethodIdentity ) is not MethodDescription method )
		{
			throw new( $"Unknown Static RPC type for method with identity '{message.MethodIdentity}'" );
		}

		if ( !method.HasAttribute<RpcAttribute>() )
		{
			source.Kick( "Unauthorized RPC" );
			return;
		}

		NetworkDebugSystem.Current?.Track( $"{method.TypeDescription.FullName}.{method.Name}", message );

		using ( WithCaller( source ) )
		{
			try
			{
				if ( message.GenericArguments is not null )
				{
					var methodInfo = method.MemberInfo as MethodInfo;
					var genericTypes = Game.TypeLibrary.FromIdentities( message.GenericArguments );
					var genericMethod = methodInfo.MakeGenericMethod( genericTypes );

					genericMethod.Invoke( null, message.Arguments );
				}
				else
				{
					method.Invoke( null, message.Arguments );
				}
			}
			catch ( Exception e )
			{
				Log.Error( e, $"Error calling RPC '{method.TypeDescription.FullName}.{method.Name}' - {e.Message}" );
			}
		}
	}

	/// <summary>
	/// Called when a static RPC is called
	/// </summary>
	[EditorBrowsable( EditorBrowsableState.Never )]
	public static void OnCallRpc( WrappedMethod m, params object[] argumentList )
	{
		var attribute = m.GetAttribute<RpcAttribute>();
		if ( attribute is null ) return;

		//
		// Send over network
		//
		if ( !Calling && Networking.IsActive )
		{
			SendStaticRpc( m, argumentList, attribute );
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
	private static void SendStaticRpc( WrappedMethod m, object[] argumentList, RpcAttribute attribute )
	{
		var networkSystem = SceneNetworkSystem.Instance;
		if ( networkSystem is null )
		{
			Log.Warning( "SceneNetworkSystem.Instance is null when sending RPC" );
			return;
		}

		//
		// To everyone
		//
		if ( attribute.Mode == RpcMode.Broadcast )
		{
			var msg = new StaticRpcMsg
			{
				MethodIdentity = m.MethodIdentity,
				Arguments = argumentList,
				GenericArguments = Game.TypeLibrary.ToIdentities( m.GenericArguments )
			};

			networkSystem.Broadcast( msg, Filter, attribute.Flags );
			return;
		}

		//
		// To the host (statics can't have an owner)
		//
		if ( attribute.Mode is RpcMode.Owner or RpcMode.Host )
		{
			if ( Networking.IsHost ) return; // don't send to ourselves

			var targetId = Connection.Host?.Id ?? Guid.Empty;
			if ( targetId == Connection.Local.Id ) return; // don't send to ourselves
			if ( targetId == Guid.Empty ) return; // don't send to no-one

			var msg = new StaticRpcMsg
			{
				MethodIdentity = m.MethodIdentity,
				Arguments = argumentList,
				GenericArguments = Game.TypeLibrary.ToIdentities( m.GenericArguments )
			};

			networkSystem.Send( targetId, msg, attribute.Flags );
		}
	}
}
