using Sandbox.Engine;
using Sandbox.Internal;
using System.IO;

namespace Sandbox.Network;

/// <summary>
/// A network system is a bunch of connections that people can send messages 
/// over. Right now it can be a dedicated server, a listen server, a pure client,
/// or a p2p system.
/// </summary>
internal partial class NetworkSystem
{
	internal delegate void MessageHandler( NetworkMessage msg );
	internal delegate void TypedMessageHandler( InternalMessageType type, NetworkMessage msg );
	internal delegate void TypdMessageHandler( object t, Connection msg, Guid guid );
	internal delegate void TypedMessageHandler<T>( T message, Connection msg, Guid guid );
	internal delegate Task TypedMessageHandlerAsync<T>( T message, Connection source, Guid guid );

	public ref struct NetworkMessage
	{
		public Connection Source;
		public ByteStream Data;
	}

	readonly Dictionary<InternalMessageType, TypedMessageHandler> messageHandlers = new();
	readonly Dictionary<Type, TypdMessageHandler> typeMessageHandlers = new();

	internal void AddHandler( InternalMessageType message, TypedMessageHandler handler )
	{
		messageHandlers[message] = handler;
	}

	internal void AddHandler<T>( Action<T, Connection, Guid> handler )
	{
		typeMessageHandlers[(typeof( T ))] = ( o, channel, g ) => handler( (T)o, channel, g );
	}

	internal void AddHandler<T>( Func<T, Connection, Guid, Task> handler )
	{
		typeMessageHandlers[(typeof( T ))] = ( o, channel, g ) =>
		{
			_ = ExceptionWrapAsync( async () => await handler( (T)o, channel, g ) );
		};
	}

	async Task ExceptionWrapAsync( Func<Task> method )
	{
		try
		{
			await method();
		}
		catch ( Exception e )
		{
			IGameInstanceDll.Current?.Disconnect();
			Log.Warning( e );
			Log.Warning( "Disconnected - Connection has crashed!" );
		}
	}

	/// <summary>
	/// Process any incoming or outgoing messages. This would usually be called on a worker thread unless
	/// threaded networking is disabled.
	/// </summary>
	internal void ProcessMessagesInThread()
	{
		foreach ( var socket in Sockets )
		{
			socket?.ProcessMessagesInThread();
		}

		if ( Connection is null )
			return;

		lock ( Connection )
		{
			Connection.ProcessMessagesInThread();
		}
	}

	void HandleIncomingMessages()
	{
		Assert.NotNull( sockets, "Socket list is null" ); // should be impossible

		// This network system only exists in the game.
		using var gameScope = GameSystem?.Push();

		foreach ( var socket in sockets )
		{
			socket?.GetIncomingMessages( HandleIncomingMessage );
		}

		Connection?.GetIncomingMessages( HandleIncomingMessage );
	}

	MemoryStream chunkStream;

	void HandleIncomingMessage( NetworkMessage msg )
	{
		// Conna: If this message is not from the host and we're still connecting, ignore it.
		if ( !IsHost && !msg.Source.IsHost && Connection.Local.IsConnecting )
		{
			return;
		}

		var type = msg.Data.Read<InternalMessageType>();

		if ( type == InternalMessageType.HeartbeatPing )
		{
			OnHeartbeatPingMessage( msg.Data, msg.Source );
			return;
		}

		if ( type is InternalMessageType.DeltaSnapshot
			or InternalMessageType.DeltaSnapshotAck
			or InternalMessageType.DeltaSnapshotCluster
			or InternalMessageType.DeltaSnapshotClusterAck )
		{
			var dataCount = msg.Data.Read<int>();
			var bs = msg.Data.ReadByteStream( dataCount );
			OnDeltaSnapshotMessage( type, bs, msg.Source );
			bs.Dispose();

			return;
		}

		if ( type == InternalMessageType.ClientTick )
		{
			OnReceiveClientTick( msg.Data, msg.Source );
			return;
		}

		if ( type == InternalMessageType.SetCullState )
		{
			OnReceiveCullStateChange( msg.Data, msg.Source );
			return;
		}

		if ( type == InternalMessageType.HeartbeatPong )
		{
			OnHeartbeatPongMessage( msg.Data, msg.Source );
			return;
		}

		if ( type == InternalMessageType.Chunk )
		{
			var index = msg.Data.Read<uint>();
			var total = msg.Data.Read<uint>();

			if ( index < 0 ) throw new InvalidDataException();
			if ( index + 1 > total ) throw new InvalidDataException();
			if ( total <= 1 ) throw new InvalidDataException();
			if ( total > 1024 ) throw new InvalidDataException();

			if ( index == 0 )
			{
				chunkStream = new MemoryStream();
			}

			unsafe
			{
				Log.Trace( $"Reading Chunk {index + 1} of {total} (chunk is {msg.Data.ReadRemaining}b)" );

				//
				// This can happen when leaving a lobby (usually during connect), and then rejoining it..
				// getting packets sennt during previous connection. Maybe need smarter headers to avoid it.
				//
				Assert.NotNull( chunkStream, $"Reading chunk {index + 1} but not started a chunk!" );

				chunkStream.Write( msg.Data.GetRemainingBytes() );
				Log.Trace( $"Total chuunk stream is now {chunkStream.Length}b long" );
			}

			if ( index + 1 == total )
			{
				var constructedMessage = new NetworkMessage();
				constructedMessage.Source = msg.Source;
				constructedMessage.Data = ByteStream.CreateReader( chunkStream.ToArray() ); //todo make suck less

				chunkStream = null;

				HandleIncomingMessage( constructedMessage );

				constructedMessage.Data.Dispose();
			}

			return;
		}

		var responseTo = Guid.Empty;
		var requestGuid = Guid.Empty;

		if ( type == InternalMessageType.Request )
		{
			requestGuid = msg.Data.Read<Guid>();
			type = msg.Data.Read<InternalMessageType>();
		}

		if ( type == InternalMessageType.Response )
		{
			responseTo = msg.Data.Read<Guid>();
			type = msg.Data.Read<InternalMessageType>();
		}

		if ( type == InternalMessageType.Packed )
		{
			object obj = default;

			//
			// This can error if we're getting a message containing types that are
			// defined in an assemblly we didn't recieve yet (because we're connecting)
			// so just ignore these exceptions, but output an error for now so we know it's happening.
			//
			try
			{
				obj = TypeLibrary.FromBytes<object>( ref msg.Data );
			}
			catch ( Exception e )
			{
				Log.Warning( e, $"Skipping message from {msg.Source}, deserialize error ({e.Message})!" );
			}

			if ( obj is null )
			{
				Log.Warning( $"Got packed null message from {msg.Source}!" );
				return;
			}

			if ( responseTo != Guid.Empty )
			{
				msg.Source.OnResponse( responseTo, obj );
				return;
			}

			if ( typeMessageHandlers.TryGetValue( obj.GetType(), out var h ) )
			{
				try
				{
					h( obj, msg.Source, requestGuid );
				}
				catch ( Exception e )
				{
					IGameInstanceDll.Current?.Disconnect();
					Log.Warning( e );
					Log.Warning( "Disconnected - Connection has crashed!" );
				}

				return;
			}

			if ( Networking.Debug )
			{
				// Conna: let's only print this warning if we have network debugging enabled.
				// It's possible we receive some unreliably sent messages before we've loaded assemblies.
				Log.Warning( $"Unhandled packed message type {obj.GetType()} from {msg.Source}!" );
			}

			return;
		}

		if ( messageHandlers.TryGetValue( type, out var handler ) )
		{
			try
			{
				handler( type, msg );
			}
			catch ( Exception e )
			{
				Log.Warning( e );
				OnConnectionCrashed( e.Message );
			}

			return;
		}

		Log.Info( $"Unhandled message type: {type} from {msg.Source}" );
	}

	void OnConnectionCrashed( string message )
	{
		Log.Warning( $"TODO: Disconnect on error ({message})" );
	}

	private void OnDeltaSnapshotMessage( InternalMessageType type, ByteStream data, Connection source )
	{
		GameSystem?.OnDeltaSnapshotMessage( type, data, source );
	}

	private void OnReceiveCullStateChange( ByteStream data, Connection source )
	{
		GameSystem?.OnCullStateChangeMessage( data, source );
	}

	internal void OnReceiveClientTick( ByteStream data, Connection source )
	{
		NetworkDebugSystem.Current?.Record( NetworkDebugSystem.MessageType.UserCommands, data.Length );

		// Read and apply visibility origins from the client
		{
			var count = data.Read<char>();

			if ( count == 0 )
			{
				if ( source.VisibilityOrigins.Length > 0 )
					source.VisibilityOrigins = [];
			}
			else
			{

				if ( source.VisibilityOrigins.Length != count )
					source.VisibilityOrigins = new Vector3[count];

				for ( var i = 0; i < count; i++ )
				{
					var x = data.Read<float>();
					var y = data.Read<float>();
					var z = data.Read<float>();
					source.VisibilityOrigins[i] = new Vector3( x, y, z );
				}
			}
		}

		// Read and apply the user command from this client
		{
			if ( data.ReadRemaining == 0 )
				return;

			// We should reject user commands from clients if we're not the host
			if ( !Networking.IsHost )
				return;

			// This is a user command directly from another client
			UserCommand cmd = default;
			cmd.Deserialize( ref data );
			source.Input.ApplyUserCommand( cmd );
		}
	}

	private void OnHeartbeatPingMessage( ByteStream data, Connection source )
	{
		if ( IsHost ) return; // Only the host is allowed to send out pings
		if ( !source.IsHost ) return; // Ignore this heartbeat

		var serverRealTime = data.Read<float>();
		var serverGameTime = data.Read<float>();

		// Echo it back to the server, so they can work out our ping
		{
			ByteStream bs = ByteStream.Create( 512 );
			bs.Write( InternalMessageType.HeartbeatPong );
			bs.Write( serverRealTime ); // the time they sent
			source.SendRawMessage( bs );
			bs.Dispose();
		}

		// Tell the game about the new time etc
		GameSystem?.OnHeartbeat( serverGameTime );
	}

	private void OnHeartbeatPongMessage( ByteStream data, Connection source )
	{
		if ( !IsHost ) return; // Only the host accepts replies from pings

		var requestTime = data.Read<float>();
		var rtt = RealTime.Now - requestTime;

		if ( rtt < 0 )
		{
			OnConnectionCrashed( "Rtt error" );
			return;
		}

		// Cap the max, because it could get silly
		if ( rtt > 2.0f ) rtt = 2.0f;

		source.UpdateRtt( rtt );
	}
}
