using Sandbox.Engine;
using Sandbox.Internal;
using Sandbox.Network;
using Sandbox.Utility;
using System.Reflection;
using NetworkLog = Sandbox.Diagnostics.DevTools.NetworkLog;

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

		var generics = GlobalContext.Current.TypeLibrary.ToIdentities( m.GenericArguments );
		bs.Write( (byte)(generics?.Length ?? 0) );

		if ( generics is not null )
		{
			foreach ( var id in generics )
				bs.Write( id );
		}

		if ( args.Count > byte.MaxValue )
			throw new ArgumentException( $"RPC {m.TypeName}.{m.MethodName} has too many arguments" );

		bs.Write( (byte)args.Count );
		args.Write( GlobalContext.Current.TypeLibrary, ref bs );
	}

	/// <summary>
	/// Send an RPC message by its mode: to everyone, or to the one connection that should run it.
	/// </summary>
	static void SendMessage<TArgs>( ref ByteStream bs, in WrappedMethod m, RpcAttribute attribute, Guid targetId, object owner, in TArgs args ) where TArgs : struct, IRpcArguments
	{
		var networkSystem = SceneNetworkSystem.Instance;
		if ( networkSystem is null )
		{
			Log.Warning( "SceneNetworkSystem.Instance is null when sending RPC" );
			return;
		}

		var broadcast = attribute.Mode == RpcMode.Broadcast;
		if ( !broadcast && (targetId == Connection.Local.Id || targetId == Guid.Empty) )
		{
			return;
		}

		List<Connection> filtered = null;

		if ( NetworkDebugSystem.Current is { TracksCalls: true } debug )
		{
			var name = $"{m.TypeName}.{m.MethodName}";
			if ( broadcast && Filter is { } filter && NetworkLog.LogsMessages )
			{
				filtered = new List<Connection>();
				networkSystem.CollectConnections( filtered, Connection.ChannelState.Snapshot, filter );
				debug.TrackFilteredRpc( name, bs.Length, filtered, owner, DescribeArguments( m, args ) );
			}
			else
			{
				var peer = NetworkLog.LogsMessages && !broadcast ? Connection.Find( targetId ) : null;
				debug.TrackRpc( name, bs.Length, outbound: true, peer, owner, DescribeArguments( m, args ) );
			}
		}

		if ( filtered is not null )
		{
			networkSystem.SendToRecipients( filtered, bs, attribute.Flags );
			return;
		}

		if ( broadcast )
		{
			networkSystem.BroadcastRaw( bs, Filter, attribute.Flags );
			return;
		}

		networkSystem.SendRaw( targetId, bs, attribute.Flags );
	}

	static string DescribeArguments<TArgs>( in WrappedMethod m, in TArgs args ) where TArgs : struct, IRpcArguments
	{
		if ( !NetworkLog.LogsMessages )
		{
			return null;
		}

		var parameters = (GlobalContext.Current.TypeLibrary.GetMemberByIdent( m.MethodIdentity ) as MethodDescription)?.Parameters;
		return NetworkLog.FormatRpcArgs( args.Values(), parameters );
	}

	/// <summary>
	/// Logs an incoming RPC with the arguments the call reads, so they aren't read twice.
	/// </summary>
	sealed class IncomingArgumentLog( NetworkDebugSystem debug, string name, int size, Connection source, object owner, MethodDescription member )
	{
		bool _logged;

		public void Arguments( object[] args )
		{
			_logged = true;
			debug.TrackRpc( name, size, outbound: false, source, owner, NetworkLog.FormatRpcArgs( args, member.Parameters ) );
		}

		/// <summary>
		/// Logs the call without its values if reading them failed.
		/// </summary>
		public void Unread( int count )
		{
			if ( _logged ) return;
			_logged = true;
			debug.TrackRpc( name, size, outbound: false, source, owner, count == 1 ? "1 arg" : $"{count} args" );
		}
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

			typeDesc = GlobalContext.Current.TypeLibrary.GetType( instance.GetType() );
		}

		var member = GlobalContext.Current.TypeLibrary.GetMemberByIdent( methodIdentity ) as MethodDescription;
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

		IncomingArgumentLog argumentLog = null;
		if ( NetworkDebugSystem.Current is { TracksCalls: true } debug )
		{
			var name = $"{(typeDesc ?? member.TypeDescription).FullName}.{method.Name}";
			if ( NetworkLog.LogsMessages )
			{
				argumentLog = new IncomingArgumentLog( debug, name, bs.Length, source, instance, member );
			}
			else
			{
				debug.TrackRpc( name, bs.Length, outbound: false, source, instance, null );
			}
		}

		var observe = argumentLog is null ? null : (Action<object[]>)argumentLog.Arguments;

		using ( WithCaller( source ) )
		{
			try
			{
				if ( generics is not null )
				{
					var genericMethod = method.MakeGenericMethod( GlobalContext.Current.TypeLibrary.FromIdentities( generics ) );
					var args = ReadArguments( ref bs, argCount );
					observe?.Invoke( args );
					genericMethod.Invoke( instance, args );
				}
				else if ( RpcInvoker.Get( method ) is { } invoker && invoker.ParameterCount == argCount )
				{
					invoker.Invoke( instance, GlobalContext.Current.TypeLibrary, ref bs, observe );
				}
				else
				{
					var args = ReadArguments( ref bs, argCount );
					observe?.Invoke( args );
					method.Invoke( instance, args );
				}
			}
			catch ( Exception e )
			{
				if ( e is TargetInvocationException { InnerException: { } inner } )
					e = inner;

				Log.Error( e, $"Error calling RPC '{(typeDesc ?? member.TypeDescription).Name}.{method.Name}' - {e.Message}" );
			}

			argumentLog?.Unread( argCount );
		}
	}

	static object[] ReadArguments( ref ByteStream bs, int count )
	{
		var args = new object[count];

		for ( var i = 0; i < count; i++ )
			args[i] = GlobalContext.Current.TypeLibrary.FromBytes<object>( ref bs );

		return args;
	}

	static string MethodName( int identity ) => (GlobalContext.Current.TypeLibrary.GetMemberByIdent( identity )?.MemberInfo as MethodInfo)?.Name ?? "Unknown";

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
