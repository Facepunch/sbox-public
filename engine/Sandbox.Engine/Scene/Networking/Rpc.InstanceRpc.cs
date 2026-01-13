using Sandbox.Utility;
using System.ComponentModel;
using System.Reflection;

namespace Sandbox;

public static partial class Rpc
{
	/// <summary>
	/// An instance RPC call is incoming from the network. Look up the method and call it.
	/// </summary>
	internal static void IncomingInstanceRpcMsg( ObjectRpcMsg rpc, Connection source )
	{
		NetworkDebugSystem.Current?.Record( NetworkDebugSystem.MessageType.Rpc, rpc );

		if ( rpc.Guid == Guid.Empty )
		{
			Log.Warning( $"OnObjectMessage: Failed to call RPC with identity '{rpc.MethodIdentity}' for unknown GameObject" );
			return;
		}

		if ( Game.ActiveScene is null )
		{
			var targetMethod = Game.TypeLibrary.GetMemberByIdent( rpc.MethodIdentity )?.MemberInfo as MethodInfo;
			Log.Warning( $"OnObjectMessage: Invalid Scene for RPC {targetMethod?.Name ?? "Unknown"}" );

			return;
		}

		var gameObject = Game.ActiveScene.Directory.FindByGuid( rpc.Guid );
		if ( gameObject is null )
		{
			var targetMethod = Game.TypeLibrary.GetMemberByIdent( rpc.MethodIdentity )?.MemberInfo as MethodInfo;
			Log.Warning( $"OnObjectMessage: Unknown GameObject {rpc.Guid} for RPC {targetMethod?.Name ?? "Unknown"}" );

			return;
		}

		// If we don't have a component, then we're calling a method on the GameObject itself
		if ( rpc.ComponentId == Guid.Empty )
		{
			InvokeInstanceRpc( rpc, Game.TypeLibrary.GetType( typeof( GameObject ) ), gameObject, source );
			return;
		}

		// Find target method on component
		var component = Game.ActiveScene.Directory.FindComponentByGuid( rpc.ComponentId );
		if ( component is null )
		{
			var targetMethod = Game.TypeLibrary.GetMemberByIdent( rpc.MethodIdentity )?.MemberInfo as MethodInfo;
			Log.Warning( $"OnObjectMessage: Unknown Component {rpc.ComponentId} on {gameObject.Name} for RPC {targetMethod?.Name ?? "Unknown"}" );
			return;
		}

		// Invoke on component
		var typeDesc = Game.TypeLibrary.GetType( component.GetType() );
		InvokeInstanceRpc( rpc, typeDesc, component, source );
	}

	/// <summary>
	/// An instance RPC call is incoming from the network. Look up the method and call it.
	/// </summary>
	internal static void IncomingInstanceRpcMsg( SceneRpcMsg message, Connection source )
	{
		NetworkDebugSystem.Current?.Record( NetworkDebugSystem.MessageType.Rpc, message );

		if ( message.Guid == Guid.Empty )
		{
			Log.Warning( $"OnObjectMessage: Failed to call RPC with identity '{message.MethodIdentity}' for unknown GameObject" );
			return;
		}

		if ( Game.ActiveScene is null )
		{
			var targetMethod = Game.TypeLibrary.GetMemberByIdent( message.MethodIdentity )?.MemberInfo as MethodInfo;
			Log.Warning( $"OnObjectMessage: Invalid Scene for RPC {targetMethod?.Name ?? "Unknown"}" );

			return;
		}

		var system = Game.ActiveScene.Directory.FindSystemByGuid( message.Guid );

		if ( system is null )
		{
			var targetMethod = Game.TypeLibrary.GetMemberByIdent( message.MethodIdentity )?.MemberInfo as MethodInfo;
			Log.Warning( $"OnObjectMessage: Invalid GameObjectSystem for RPC {targetMethod?.Name ?? "Unknown"}" );

			return;
		}

		// Invoke on GameObjectSystem
		InvokeInstanceRpc( message, system, source );
	}

	static Superluminal _ph = new Superluminal( "Rpc", Color.Cyan );

	static void InvokeInstanceRpc( in SceneRpcMsg rpc, in object targetObject, in Connection source )
	{
		var typeDesc = Game.TypeLibrary.GetType( targetObject.GetType() );
		var method = Game.TypeLibrary.GetMemberByIdent( rpc.MethodIdentity )?.MemberInfo as MethodInfo;

		if ( method is { DeclaringType.IsGenericTypeDefinition: true } )
		{
			// If called method was declared on a generic type, we need to find the right
			// generic instance type.
			method = typeDesc.TargetType.GetInheritedConstructedGenericType( method.DeclaringType )?.GetMemberWithSameMetadataDefinitionAs( method ) as MethodInfo;
		}

		if ( method is null )
		{
			Log.Error( $"Unknown RPC with identity '{rpc.MethodIdentity}' on {typeDesc.Name}" );
			return;
		}

		if ( !method.HasAttribute( typeof( RpcAttribute ) ) )
		{
			source.Kick( "Unauthorized RPC" );
			return;
		}

		var rpcName = $"{typeDesc.FullName}.{method.Name}";
		using var profiler = _ph.Start( rpcName );

		NetworkDebugSystem.Current?.Track( rpcName, rpc );

		using ( WithCaller( source ) )
		{
			try
			{
				if ( rpc.GenericArguments is not null )
				{
					var genericTypes = Game.TypeLibrary.FromIdentities( rpc.GenericArguments );
					var genericMethod = method.MakeGenericMethod( genericTypes );

					genericMethod.Invoke( targetObject, rpc.Arguments );
				}
				else
				{
					method.Invoke( targetObject, rpc.Arguments );
				}
			}
			catch ( Exception e )
			{
				Log.Error( e, $"Error calling RPC '{typeDesc.Name}.{method.Name}' - {e.Message}" );
			}
		}
	}

	static void InvokeInstanceRpc( in ObjectRpcMsg rpc, in TypeDescription typeDesc, in object targetObject, in Connection source )
	{
		var method = Game.TypeLibrary.GetMemberByIdent( rpc.MethodIdentity )?.MemberInfo as MethodInfo;

		if ( method is { DeclaringType.IsGenericTypeDefinition: true } )
		{
			// If called method was declared on a generic type, we need to find the right
			// generic instance type.
			method = typeDesc.TargetType.GetInheritedConstructedGenericType( method.DeclaringType )?.GetMemberWithSameMetadataDefinitionAs( method ) as MethodInfo;
		}

		if ( method is null )
		{
			Log.Error( $"Unknown RPC with identity '{rpc.MethodIdentity}' on {typeDesc.Name}" );
			return;
		}

		if ( !method.HasAttribute( typeof( RpcAttribute ) ) )
		{
			source.Kick( "Unauthorized RPC" );
			return;
		}

		var rpcName = $"{typeDesc.FullName}.{method.Name}";
		using var profiler = _ph.Start( rpcName );

		NetworkDebugSystem.Current?.Track( rpcName, rpc );

		using ( WithCaller( source ) )
		{
			try
			{
				if ( rpc.GenericArguments is not null )
				{
					var genericTypes = Game.TypeLibrary.FromIdentities( rpc.GenericArguments );
					var genericMethod = method.MakeGenericMethod( genericTypes );

					genericMethod.Invoke( targetObject, rpc.Arguments );
				}
				else
				{
					method.Invoke( targetObject, rpc.Arguments );
				}
			}
			catch ( Exception e )
			{
				Log.Error( e, $"Error calling RPC '{typeDesc.Name}.{method.Name}' - {e.Message}" );
			}
		}
	}

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
		var attribute = m.GetAttribute<RpcAttribute>();
		if ( attribute is null ) return;

		//
		// Send over network
		//
		if ( !Calling && Networking.IsActive )
		{
			SendInstanceRpc( system, m, argumentList, attribute );
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
		var attribute = m.GetAttribute<RpcAttribute>();
		if ( attribute is null ) return;

		//
		// Send over network
		//
		if ( !Calling && Networking.IsActive )
		{
			SendInstanceRpc( go, component, m, argumentList, attribute );
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
	private static void SendInstanceRpc( GameObjectSystem system, in WrappedMethod m, object[] argumentList, RpcAttribute attribute )
	{
		if ( !system.Scene.IsValid() ) return;

		var networkSystem = SceneNetworkSystem.Instance;
		if ( networkSystem is null )
		{
			Log.Warning( "SceneNetworkSystem.Instance is null when sending RPC" );
			return;
		}

		var msg = new SceneRpcMsg
		{
			Guid = system.Id,
			MethodIdentity = m.MethodIdentity,
			Arguments = argumentList,
			GenericArguments = Game.TypeLibrary.ToIdentities( m.GenericArguments )
		};

		//
		// To everyone
		//
		if ( attribute.Mode == RpcMode.Broadcast )
		{
			networkSystem.Broadcast( msg, Filter, attribute.Flags );
			return;
		}

		//
		// To the owner of the object
		//
		if ( attribute.Mode == RpcMode.Owner )
		{
			var targetId = Connection.Host?.Id ?? Guid.Empty;
			if ( targetId == Connection.Local.Id ) return; // don't send to ourselves
			if ( targetId == Guid.Empty ) return; // don't send to no-one

			networkSystem.Send( targetId, msg, attribute.Flags );
			return;
		}

		//
		// To the host of the server
		//
		if ( attribute.Mode == RpcMode.Host )
		{
			var targetId = Connection.Host?.Id ?? Guid.Empty;
			if ( targetId == Connection.Local.Id ) return; // don't send to ourselves
			if ( targetId == Guid.Empty ) return; // don't send to no-one

			networkSystem.Send( targetId, msg, attribute.Flags );
		}
	}

	/// <summary>
	/// Do the actual send of the instance RPC.
	/// </summary>
	private static void SendInstanceRpc( GameObject go, Component component, in WrappedMethod m, object[] argumentList, RpcAttribute attribute )
	{
		if ( !go.IsValid() ) return;

		var networkSystem = SceneNetworkSystem.Instance;
		if ( networkSystem is null )
		{
			Log.Warning( "SceneNetworkSystem.Instance is null when sending RPC" );
			return;
		}

		var msg = new ObjectRpcMsg
		{
			Guid = go.Id,
			ComponentId = component?.Id ?? Guid.Empty,
			MethodIdentity = m.MethodIdentity,
			Arguments = argumentList,
			GenericArguments = Game.TypeLibrary.ToIdentities( m.GenericArguments )
		};

		//
		// To everyone
		//
		if ( attribute.Mode == RpcMode.Broadcast )
		{
			networkSystem.Broadcast( msg, Filter, attribute.Flags );
			return;
		}

		//
		// To the owner of the object
		//
		if ( attribute.Mode == RpcMode.Owner )
		{
			var targetId = go.Network.OwnerId;
			if ( targetId == Guid.Empty ) targetId = Connection.Host?.Id ?? Guid.Empty; // host is the owner, fallback
			if ( targetId == Connection.Local.Id ) return; // don't send to ourselves
			if ( targetId == Guid.Empty ) return; // don't send to no-one

			networkSystem.Send( targetId, msg, attribute.Flags );
			return;
		}

		//
		// To the host of the server
		//
		if ( attribute.Mode == RpcMode.Host )
		{
			var targetId = Connection.Host?.Id ?? Guid.Empty;
			if ( targetId == Connection.Local.Id ) return; // don't send to ourselves
			if ( targetId == Guid.Empty ) return; // don't send to no-one

			networkSystem.Send( targetId, msg, attribute.Flags );
		}
	}
}
