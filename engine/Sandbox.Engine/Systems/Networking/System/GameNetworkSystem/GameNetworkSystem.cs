using Sandbox.Internal;

namespace Sandbox.Network;

/// <summary>
/// An instance of this is created by the NetworkSystem when a server is joined, or created.
/// You should not try to create this manually.
/// </summary>
public abstract partial class GameNetworkSystem : IDisposable
{
	internal TypeLibrary Library { get; set; }
	internal NetworkSystem NetworkSystem { get; set; }

	public GameNetworkSystem()
	{
	}

	public virtual void Dispose()
	{

	}

	/// <summary>
	/// Called on the host to decide whether to accept a <see cref="Connection"/>.
	/// </summary>
	/// <param name="channel"></param>
	/// <param name="reason">The reason to display to the client.</param>
	public virtual bool AcceptConnection( Connection channel, ref string reason )
	{
		return true;
	}

	public virtual void GetMountedVPKs( Connection source, ref MountedVPKsResponse msg ) { }

	public virtual void GetSnapshot( Connection source, ref SnapshotMsg msg ) { }

	internal virtual SnapshotCapture CaptureSnapshot( Connection source, bool handoff = false, SnapshotCapture shared = null ) => null;

	internal SnapshotCapture SendSnapshot<T>( Connection target, Func<SnapshotMsg, T> envelope, bool handoff = false, SnapshotCapture shared = null,
		NetFlags flags = NetFlags.Reliable )
	{
		var capture = CaptureSnapshot( target, handoff, shared );
		if ( capture is not null )
		{
			target.SendSnapshot( capture, envelope, flags );
		}
		else
		{
			var snapshot = SnapshotMsg.Create();
			if ( handoff )
			{
				GetHandoffSnapshot( ref snapshot );
			}
			else
			{
				GetSnapshot( target, ref snapshot );
			}
			target.SendMessage( envelope( snapshot ), flags );
		}
		return capture;
	}

	/// <summary>
	/// Snapshot for the player taking over as host: nothing culled, local objects included.
	/// </summary>
	internal virtual void GetHandoffSnapshot( ref SnapshotMsg msg ) => GetSnapshot( null, ref msg );

	public virtual Task SetSnapshotAsync( SnapshotMsg data ) => Task.CompletedTask;

	public virtual Task MountVPKs( Connection source, MountedVPKsResponse msg ) => Task.CompletedTask;

	/// <summary>
	/// Called when the network system should handle initialization.
	/// </summary>
	public virtual void OnInitialize()
	{

	}

	/// <summary>
	/// A client has connected to the server but hasn't fully finished joining yet.
	/// </summary>
	public virtual void OnConnected( Connection client ) { }

	/// <summary>
	/// Fully joined the server. Can be called when changing the map too. The game should usually create
	/// some object for the player to control here.
	/// </summary>
	public virtual void OnJoined( Connection client ) { } // TODO rename OnActive

	/// <summary>
	/// A client has disconnected from the server.
	/// </summary>
	public virtual void OnLeave( Connection client ) { }

	/// <summary>
	/// Whether a snapshot of the game can be taken right now.
	/// </summary>
	internal virtual bool CanSnapshot => true;

	/// <summary>
	/// We're already the host; load the previous host's snapshot.
	/// </summary>
	public virtual Task BecomeHostAsync( Connection previousHost, SnapshotMsg snapshot )
	{
		OnBecameHost( previousHost );
		return Task.CompletedTask;
	}

	/// <summary>
	/// Rebuild the game from the new host's snapshot.
	/// </summary>
	public virtual Task ResyncFromHostAsync( Connection previousHost, Connection newHost, SnapshotMsg snapshot )
	{
		OnHostChanged( previousHost, newHost );
		return Task.CompletedTask;
	}

	/// <summary>
	/// Legacy host-change callback. Override BecomeHostAsync to apply the handoff snapshot.
	/// The default BecomeHostAsync implementation calls this for existing subclasses.
	/// </summary>
	public virtual void OnBecameHost( Connection previousHost ) { }

	/// <summary>
	/// Legacy host-change callback. Override ResyncFromHostAsync to apply the new host's snapshot.
	/// The default ResyncFromHostAsync implementation calls this for existing subclasses.
	/// </summary>
	public virtual void OnHostChanged( Connection previousHost, Connection newHost ) { }

	internal void BroadcastRaw( ByteStream msg, Connection.Filter? filter, NetFlags flags )
	{
		NetworkSystem.Broadcast( msg, Connection.ChannelState.Snapshot, filter, flags );
	}

	internal IEnumerable<Connection> GetFilteredConnections( Connection.ChannelState minimumState = Connection.ChannelState.Snapshot, Connection.Filter? filter = null )
	{
		return NetworkSystem.GetFilteredConnections( minimumState, filter );
	}

	public void BroadcastRaw( ByteStream msg, Connection.Filter? filter = null )
	{
		BroadcastRaw( msg, filter, NetFlags.Reliable );
	}

	internal void Broadcast<T>( T obj, Connection.Filter? filter, NetFlags flags )
	{
		var bs = ByteStream.Create( 512 );
		bs.Write( InternalMessageType.Packed );

		try
		{
			Library.ToBytes( obj, ref bs );
		}
		catch ( Exception e )
		{
			Log.Warning( e, $"Error when trying to network serialize object: {e.Message}" );
		}

		BroadcastRaw( bs, filter, flags );
		bs.Dispose();
	}

	public void Broadcast<T>( T obj, Connection.Filter? filter = null )
	{
		Broadcast( obj, filter, NetFlags.Reliable );
	}

	internal void Send( Connection connection, InternalMessageType type, ReadOnlySpan<byte> data, NetFlags flags )
	{
		using var bs = ByteStream.Create( 512 );
		bs.Write( type );
		bs.Write( data.Length );
		bs.Write( data );

		// Do we really have a valid connection to this?
		var targetConnection = NetworkSystem.FindConnection( connection.Id );

		if ( targetConnection is null )
		{
			if ( NetworkSystem.Connection is null )
				return;

			var wrapper = new TargetedInternalMessage
			{
				SenderId = Connection.Local.Id,
				TargetId = connection.Id,
				Data = bs.ToArray(),
				Flags = (byte)flags
			};

			NetworkSystem.Connection.SendMessage( wrapper, flags );
		}
		else
		{
			targetConnection.SendStream( bs, flags );
		}
	}

	internal void Send( Connection connection, InternalMessageType type, byte[] data, NetFlags flags )
	{
		Send( connection, type, data.AsSpan(), flags );
	}

	internal void Send<T>( Connection connection, T obj, NetFlags flags )
	{
		var bs = ByteStream.Create( 512 );
		bs.Write( InternalMessageType.Packed );

		try
		{
			Library.ToBytes( obj, ref bs );
		}
		catch ( Exception e )
		{
			Log.Warning( e, $"Error when trying to network serialize object: {e.Message}" );
		}

		connection.SendStream( bs, flags );
		bs.Dispose();
	}

	internal void Send<T>( Guid connectionId, T obj, NetFlags flags )
	{
		var connection = NetworkSystem.FindConnection( connectionId );

		if ( connection is not null )
		{
			var bs = ByteStream.Create( 512 );
			bs.Write( InternalMessageType.Packed );

			try
			{
				Library.ToBytes( obj, ref bs );
			}
			catch ( Exception e )
			{
				Log.Warning( e, $"Error when trying to network serialize object: {e.Message}" );
			}

			connection.SendStream( bs, flags );

			bs.Dispose();
		}
		else if ( NetworkSystem.Connection is not null )
		{
			var wrapper = new TargetedMessage
			{
				SenderId = Connection.Local.Id,
				TargetId = connectionId,
				Message = obj,
				Flags = (byte)flags
			};

			NetworkSystem.Connection.SendMessage( wrapper, flags );
		}
	}

	public void Send<T>( Guid connectionId, T obj )
	{
		Send( connectionId, obj, NetFlags.Reliable );
	}

	/// <summary>
	/// Allows to push some kind of scope when reading network messages. This is useful if you
	/// need to adjust Time.Now etc.
	/// </summary>
	public virtual IDisposable Push() => null;

	public void AddHandler<T>( Action<T, Connection, Guid> handler )
	{
		NetworkSystem.AddHandler<T>( handler );
	}

	public void AddHandler<T>( Func<T, Connection, Guid, Task> handler )
	{
		NetworkSystem.AddHandler<T>( handler );
	}

	public void AddHandler<T>( Action<T, Connection> handler )
	{
		AddHandler<T>( ( t, c, g ) => handler( t, c ) );
	}

	internal void TickInternal()
	{
		Tick();
	}

	/// <summary>
	/// Called every frame
	/// </summary>
	protected virtual void Tick()
	{

	}

	/// <summary>
	/// A heartbeat has been received from the host. We should make sure our times are in sync.
	/// </summary>
	internal virtual void OnHeartbeat( double serverGameTime )
	{

	}

	/// <summary>
	/// We've received a cull state change for a networked object.
	/// </summary>
	internal virtual void OnCullStateChangeMessage( ByteStream data, Connection source )
	{

	}

	/// <summary>
	/// A delta snapshot message has been received from another connection.
	/// </summary>
	internal virtual void OnDeltaSnapshotMessage( InternalMessageType type, ByteStream data, Connection source )
	{

	}
}
