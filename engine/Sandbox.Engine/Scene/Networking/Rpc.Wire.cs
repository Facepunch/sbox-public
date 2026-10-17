using Sandbox.Internal;
using Sandbox.Network;
using Sandbox.Utility;
using System.Reflection;

namespace Sandbox;

/// <summary>
/// RPCs travel as <see cref="InternalMessageType.Rpc"/>: what kind of target, its ids, the method identity, generic
/// argument identities, then each argument as BytePack writes it. Read straight from the message into a typed
/// <see cref="RpcInvoker"/> where possible.
/// </summary>
public static partial class Rpc
{
	internal enum RpcTarget : byte
	{
		/// <summary>A component, or the game object itself when the component id is empty.</summary>
		Object,
		/// <summary>A <see cref="GameObjectSystem"/>.</summary>
		System,
		Static
	}

	static readonly Superluminal _ph = new( "Rpc", Color.Cyan );

	static ByteStream BeginMessage( RpcTarget target )
	{
		var bs = ByteStream.Create( 128 );
		bs.Write( InternalMessageType.Rpc );
		bs.Write( target );
		return bs;
	}

	static void FinishMessage<TArgs>( ref ByteStream bs, in WrappedMethod m, in TArgs args ) where TArgs : struct, IRpcArguments
	{
		bs.Write( m.MethodIdentity );

		var generics = Game.TypeLibrary.ToIdentities( m.GenericArguments );
		bs.Write( (byte)(generics?.Length ?? 0) );

		if ( generics is not null )
		{
			foreach ( var id in generics )
				bs.Write( id );
		}

		if ( args.Count > byte.MaxValue )
			throw new ArgumentException( $"RPC {m.TypeName}.{m.MethodName} has too many arguments" );

		bs.Write( (byte)args.Count );
		args.Write( Game.TypeLibrary, ref bs );
	}

	/// <summary>
	/// Send an RPC message by its mode: to everyone, or to the one connection that should run it.
	/// </summary>
	static void SendMessage( ref ByteStream bs, in WrappedMethod m, RpcAttribute attribute, Guid targetId )
	{
		var networkSystem = SceneNetworkSystem.Instance;
		if ( networkSystem is null )
		{
			Log.Warning( "SceneNetworkSystem.Instance is null when sending RPC" );
			return;
		}

		if ( NetworkDebugSystem.Current is { TracksCalls: true } debug )
			debug.Track( $"{m.TypeName}.{m.MethodName}", bs.Length, outbound: true );

		if ( attribute.Mode == RpcMode.Broadcast )
		{
			networkSystem.BroadcastRaw( bs, Filter, attribute.Flags );
			return;
		}

		if ( targetId == Connection.Local.Id ) return; // don't send to ourselves
		if ( targetId == Guid.Empty ) return; // don't send to no-one

		networkSystem.SendRaw( targetId, bs, attribute.Flags );
	}

	/// <summary>
	/// An RPC message arrived. Find what it's for and call it.
	/// </summary>
	internal static void IncomingRpc( ByteStream bs, Connection source )
	{
		NetworkDebugSystem.Current?.Record( NetworkDebugSystem.MessageType.Rpc, bs.Length );

		var target = bs.Read<RpcTarget>();
		var objectId = target == RpcTarget.Static ? Guid.Empty : bs.Read<Guid>();
		var componentId = target == RpcTarget.Object ? bs.Read<Guid>() : Guid.Empty;
		var methodIdentity = bs.Read<int>();

		var genericCount = bs.Read<byte>();
		int[] generics = null;

		if ( genericCount > 0 )
		{
			generics = new int[genericCount];
			for ( var i = 0; i < genericCount; i++ )
				generics[i] = bs.Read<int>();
		}

		var argCount = bs.Read<byte>();

		object instance = null;
		TypeDescription typeDesc;

		if ( target == RpcTarget.Static )
		{
			typeDesc = null;
		}
		else
		{
			var scene = Game.ActiveScene;

			if ( scene is null )
			{
				Log.Warning( $"IncomingRpc: Invalid Scene for RPC {MethodName( methodIdentity )}" );
				return;
			}

			if ( target == RpcTarget.System )
			{
				instance = scene.Directory.FindSystemByGuid( objectId );

				if ( instance is null )
				{
					Log.Warning( $"IncomingRpc: Invalid GameObjectSystem for RPC {MethodName( methodIdentity )}" );
					return;
				}
			}
			else
			{
				if ( objectId == Guid.Empty )
				{
					Log.Warning( $"IncomingRpc: Failed to call RPC with identity '{methodIdentity}' for unknown GameObject" );
					return;
				}

				var gameObject = scene.Directory.FindByGuid( objectId );
				if ( gameObject is null )
				{
					Log.Warning( $"IncomingRpc: Unknown GameObject {objectId} for RPC {MethodName( methodIdentity )}" );
					return;
				}

				if ( componentId == Guid.Empty )
				{
					instance = gameObject;
				}
				else
				{
					instance = scene.Directory.FindComponentByGuid( componentId );

					if ( instance is null )
					{
						Log.Warning( $"IncomingRpc: Unknown Component {componentId} on {gameObject.Name} for RPC {MethodName( methodIdentity )}" );
						return;
					}
				}
			}

			typeDesc = Game.TypeLibrary.GetType( instance.GetType() );
		}

		var member = Game.TypeLibrary.GetMemberByIdent( methodIdentity ) as MethodDescription;
		var method = member?.MemberInfo as MethodInfo;

		// A method declared on a generic type is called on the constructed type of the target
		if ( typeDesc is not null && method is { DeclaringType.IsGenericTypeDefinition: true } )
			method = typeDesc.TargetType.GetInheritedConstructedGenericType( method.DeclaringType )?.GetMemberWithSameMetadataDefinitionAs( method ) as MethodInfo;

		if ( method is null )
		{
			if ( target == RpcTarget.Static )
				throw new( $"Unknown Static RPC type for method with identity '{methodIdentity}'" );

			Log.Error( $"Unknown RPC with identity '{methodIdentity}' on {typeDesc?.Name}" );
			return;
		}

		if ( !HasRpcAttribute( member ) )
		{
			source.Kick( "Unauthorized RPC" );
			return;
		}

		if ( method.IsStatic != (target == RpcTarget.Static) )
		{
			source.Kick( "Unauthorized RPC" );
			return;
		}

		using var profiler = _ph.Start( method.Name );

		if ( NetworkDebugSystem.Current is { TracksCalls: true } debug )
			debug.Track( $"{(typeDesc ?? member.TypeDescription).FullName}.{method.Name}", bs.Length, outbound: false, source );

		using ( WithCaller( source ) )
		{
			try
			{
				if ( generics is not null )
				{
					var genericMethod = method.MakeGenericMethod( Game.TypeLibrary.FromIdentities( generics ) );
					genericMethod.Invoke( instance, ReadArguments( ref bs, argCount ) );
				}
				else if ( RpcInvoker.Get( method ) is { } invoker && invoker.ParameterCount == argCount )
				{
					invoker.Invoke( instance, Game.TypeLibrary, ref bs );
				}
				else
				{
					method.Invoke( instance, ReadArguments( ref bs, argCount ) );
				}
			}
			catch ( Exception e )
			{
				if ( e is TargetInvocationException { InnerException: { } inner } )
					e = inner;

				Log.Error( e, $"Error calling RPC '{(typeDesc ?? member.TypeDescription).Name}.{method.Name}' - {e.Message}" );
			}
		}
	}

	static object[] ReadArguments( ref ByteStream bs, int count )
	{
		var args = new object[count];

		for ( var i = 0; i < count; i++ )
			args[i] = Game.TypeLibrary.FromBytes<object>( ref bs );

		return args;
	}

	static string MethodName( int identity ) => (Game.TypeLibrary.GetMemberByIdent( identity )?.MemberInfo as MethodInfo)?.Name ?? "Unknown";

	/// <summary>
	/// Whether the method is an RPC. The description caches its attributes, the MethodInfo would reflect them.
	/// </summary>
	static bool HasRpcAttribute( MethodDescription member )
	{
		foreach ( var attribute in member.Attributes )
		{
			if ( attribute is RpcAttribute )
				return true;
		}

		return false;
	}
}
