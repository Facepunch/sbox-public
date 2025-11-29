using System.Runtime.CompilerServices;
using System.Threading;
using Sandbox.Network;

namespace Sandbox;

/// <summary>
/// A connection, usually to a server or a client.
/// </summary>
[Expose, ActionGraphIgnore]
public abstract partial class Connection
{
	internal abstract void InternalSend( ByteStream stream, NetFlags flags );
	internal abstract void InternalRecv( NetworkSystem.MessageHandler handler );
	internal abstract void InternalClose( int closeCode, string closeReason );

	/// <summary>
	/// Called when we receive <see cref="ServerInfo"/> from this <see cref="Connection"/>.
	/// </summary>
	/// <param name="userInfo">Our outgoing <see cref="UserInfo"/> data.</param>
	/// <param name="serverInfo"></param>
	/// <returns>Whether or not we want to continue to connect.</returns>
	internal virtual bool OnReceiveServerInfo( ref UserInfo userInfo, ServerInfo serverInfo )
	{
		return true;
	}

	/// <summary>
	/// Called when we receive <see cref="UserInfo"/> from this <see cref="Connection"/>.
	/// </summary>
	/// <param name="info"></param>
	/// <returns>Whether or not we want to allow this connection</returns>
	internal virtual bool OnReceiveUserInfo( UserInfo info )
	{
		return true;
	}

	/// <summary>
	/// Get whether this connection has a specific permission.
	/// </summary>
	public virtual bool HasPermission( string permission )
	{
		// The host has every permission by default.
		return IsHost;
	}

	/// <summary>
	/// This connection's unique identifier.
	/// </summary>
	[ActionGraphInclude]
	public Guid Id { get; protected set; }

	/// <summary>
	/// A unique identifier that is set when the connection starts handshaking. This identifier will
	/// be passed into all handshake messages, so that if a new handshaking process starts while one
	/// is already active, old handshake messages will be ignored.
	/// </summary>
	internal Guid HandshakeId { get; set; }

	/// <summary>
	/// The <see cref="NetworkSystem"/> this connection belongs to.
	/// </summary>
	internal NetworkSystem System { get; set; }

	/// <summary>
	/// An array of Pvs sources from this connection.
	/// </summary>
	internal Vector3[] VisibilityOrigins = [];

	/// <summary>
	/// Calculate the closest distance (squared) to a position based on the Pvs sources from
	/// this <see cref="Connection"/>.
	/// </summary>
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public float DistanceSquared( Vector3 position )
	{
		if ( VisibilityOrigins == null || VisibilityOrigins.Length == 0 )
			return float.PositiveInfinity;

		var minSq = float.PositiveInfinity;
		var sources = VisibilityOrigins;
		var count = sources.Length;

		for ( var i = 0; i < count; i++ )
		{
			var source = VisibilityOrigins[i];
			var distance = source.DistanceSquared( position );

			if ( distance > minSq )
				continue;

			if ( distance == 0f )
				return 0f;

			minSq = distance;
		}

		return minSq;
	}

	/// <summary>
	/// Calculate the closest distance to a position based on the Pvs sources from
	/// this <see cref="Connection"/>.
	/// </summary>
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	internal float Distance( Vector3 position )
	{
		return MathF.Sqrt( DistanceSquared( position ) );
	}

	/// <summary>
	/// Can this connection spawn networked objects?
	/// </summary>
	public bool CanSpawnObjects
	{
		get => IsHost || (Info?.CanSpawnObjects ?? true);
		set
		{
			Assert.True( Networking.IsHost );

			var info = Info;
			if ( info is null ) return;
			if ( info.CanSpawnObjects == value ) return;

			info.CanSpawnObjects = value;
			info.UpdateStringTable();
		}
	}

	/// <summary>
	/// Can this connection refresh networked objects that they own?
	/// </summary>
	public bool CanRefreshObjects
	{
		get => IsHost || (Info?.CanRefreshObjects ?? true);
		set
		{
			Assert.True( Networking.IsHost );

			var info = Info;
			if ( info is null ) return;
			if ( info.CanRefreshObjects == value ) return;

			info.CanRefreshObjects = value;
			info.UpdateStringTable();
		}
	}

	/// <summary>
	/// Can this connection destroy networked objects they own?
	/// </summary>
	public bool CanDestroyObjects
	{
		get => IsHost || (Info?.CanDestroyObjects ?? true);
		set
		{
			Assert.True( Networking.IsHost );

			var info = Info;
			if ( info is null ) return;
			if ( info.CanDestroyObjects == value ) return;

			info.CanDestroyObjects = value;
			info.UpdateStringTable();
		}
	}

	public virtual float Latency => 0;

	[ActionGraphInclude]
	public virtual string Name => "Unnammed";

	[ActionGraphInclude]
	public virtual float Time => 0.0f;

	[ActionGraphInclude]
	public virtual string Address => "unknown";

	[ActionGraphInclude]
	public virtual bool IsHost => false;

	/// <summary>
	/// True if this channel is still currently connecting.
	/// </summary>
	[ActionGraphInclude]
	public bool IsConnecting => State <= ChannelState.Snapshot;

	/// <summary>
	/// True if this channel is fully connnected and fully logged on.
	/// </summary>
	[ActionGraphInclude]
	public bool IsActive => State == ChannelState.Connected;

	private int _messagesSent;
	private int _messagesReceived;

	/// <summary>
	/// How many messages have been sent to this connection?
	/// </summary>
	public int MessagesSent
	{
		get => Interlocked.CompareExchange( ref _messagesSent, 0, 0 );
		internal set => Interlocked.Exchange( ref _messagesSent, value );
	}

	/// <summary>
	/// How many messages have been received from this connection?
	/// </summary>
	public int MessagesRecieved
	{
		get => Interlocked.CompareExchange( ref _messagesReceived, 0, 0 );
		internal set => Interlocked.Exchange( ref _messagesReceived, value );
	}

	/// <summary>
	/// Kick this <see cref="Connection"/> from the server. Only the host can kick clients.
	/// </summary>
	/// <param name="reason">The reason to display to this client.</param>
	public virtual void Kick( string reason )
	{
		Assert.NotNull( System );

		if ( !System.IsHost )
			return;

		if ( string.IsNullOrWhiteSpace( reason ) )
			reason = "Kicked";

		// TODO: do we immediately close this connection locally
		// instead of waiting for a disconnection message?

		SendMessage( new KickMsg
		{
			Reason = reason
		} );
	}

	/// <summary>
	/// Log a message to the console for this connection.
	/// </summary>
	public void SendLog( LogLevel level, string message )
	{
		if ( Local == this || System is null )
		{
			switch ( level )
			{
				case LogLevel.Info:
					Log.Info( message );
					break;
				case LogLevel.Warn:
					Log.Warning( message );
					break;
				case LogLevel.Error:
					Log.Error( message );
					break;
			}

			return;
		}

		var msg = new LogMsg { Level = (byte)level, Message = message };
		SendMessage( msg, NetFlags.Reliable );
	}

	internal void InitializeSystem( NetworkSystem parent )
	{
		System = parent;
	}

	internal void SendMessage<T>( InternalMessageType type, T t )
	{
		Assert.NotNull( System );

		var msg = ByteStream.Create( 256 );
		msg.Write( type );

		System.Serialize( t, ref msg );

		SendRawMessage( msg );
		msg.Dispose();
	}

	/// <summary>
	/// Get stats about this connection such as bandwidth usage and how many packets are being
	/// sent and received.
	/// </summary>
	public virtual ConnectionStats Stats => default;

	/// <summary>
	/// Send a message to this connection.
	/// </summary>
	public void SendMessage<T>( T t )
	{
		SendMessage( t, NetFlags.Reliable );
	}

	internal void SendMessage<T>( T t, NetFlags flags )
	{
		Assert.NotNull( System );

		var msg = ByteStream.Create( 256 );
		msg.Write( InternalMessageType.Packed );

		System.Serialize( t, ref msg );

		SendRawMessage( msg, flags );
		msg.Dispose();
	}

	internal virtual void SendRawMessage( ByteStream stream, NetFlags flags = NetFlags.Reliable )
	{
		// Note: this is basically quater of k_cbMaxSteamNetworkingSocketsMessageSizeSend
		var maxChunkSize = 128 * 1024;
		var isReliableMessage = (flags & NetFlags.Reliable) != 0;

		if ( !isReliableMessage || stream.Length < maxChunkSize )
		{
			InternalSend( stream, flags );
			return;
		}

		//
		// Split messages into multiple parts, this should hardly ever happen.
		//

		var chunkHeader = 32;
		var chunks = (stream.Length / (float)maxChunkSize).CeilToInt();

		Log.Trace( $"splitting {stream.Length} bytes into {chunks} {maxChunkSize}b chunks" );

		for ( int i = 0; i < chunks; i++ )
		{
			using ByteStream chunkMessage = ByteStream.Create( maxChunkSize + chunkHeader );
			chunkMessage.Write( InternalMessageType.Chunk );
			chunkMessage.Write( (uint)i );
			chunkMessage.Write( (uint)chunks );
			chunkMessage.Write( stream, i * maxChunkSize, maxChunkSize );

			Log.Trace( $"Chunk {i + 1} is {chunkMessage.Length}b" );

			InternalSend( chunkMessage, flags );

			chunkMessage.Dispose();
		}
	}

	/// <summary>
	/// This is called on a worker thread and should handle any threaded processing of messages.
	/// </summary>
	internal virtual void ProcessMessagesInThread()
	{

	}

	internal void GetIncomingMessages( NetworkSystem.MessageHandler handler )
	{
		InternalRecv( handler );
	}

	internal void Close( int reasonCode, string reasonString )
	{
		InternalClose( reasonCode, reasonString );
	}

	ChannelState InternalState { get; set; }

	/// <summary>
	/// Current internal progression of this connection.
	/// </summary>
	internal ChannelState State
	{
		get
		{
			return Info?.State ?? InternalState;
		}
		set
		{
			InternalState = value;

			var info = Info;
			if ( info is null ) return;
			if ( info.State == value ) return;

			info.State = value;
			info.UpdateStringTable();
		}
	}

	internal enum ChannelState
	{
		Unconnected,
		LoadingServerInformation,
		Welcome,
		MountVPKs,
		Snapshot,
		Connected,
	}

	public override string ToString() => Name;

	/// <summary>
	/// Generate an ID for this connection. This is called by the server to allocate
	/// the connection an identifier. We're avoiding sequential, allocated ids because
	/// who needs to deal with that bullshit.
	/// </summary>
	internal void GenerateConnectionId()
	{
		Id = Guid.NewGuid();
	}

	/// <summary>
	/// Update this channel's info. Usually called from the host.
	/// </summary>
	internal void UpdateFrom( ChannelInfo host )
	{
		Id = host.Id;
	}

	/// <summary>
	/// Called once a second.
	/// </summary>
	internal virtual void Tick( NetworkSystem parent )
	{

	}

	/// <summary>
	/// The ping of this connection (in milliseconds.)
	/// </summary>
	[ActionGraphInclude]
	public float Ping => Info?.Ping ?? 0f;

	/// <summary>
	/// The server has worked out the round trip time on a connection using the heartbeat.
	/// We want to keep a sliding window of this timing and use it to predict the latency.
	/// </summary>
	internal void UpdateRtt( float rtt )
	{
		Info?.UpdatePing( (rtt * 1000f * 0.5f).CeilToInt() );
	}

	ConnectionInfo Info => FindConnectionInfo( Id ) ?? PreInfo;

	/// <summary>
	/// The connection info before connection is added
	/// </summary>
	internal ConnectionInfo PreInfo { get; set; }

	[ActionGraphInclude]
	public string DisplayName => Info?.DisplayName ?? "Unknown Player";

	[ActionGraphInclude]
	public SteamId SteamId => Info?.SteamId ?? default;

	/// <summary>
	/// The Id of the party that this user is a part of. This can be used to compare to other users to 
	/// group them into parties.
	/// </summary>
	[ActionGraphInclude]
	public SteamId PartyId => Info?.PartyId ?? default;

	public DateTimeOffset ConnectionTime => Info?.ConnectionTime ?? default;

	[ActionGraphInclude]
	public string GetUserData( string key ) => Info?.GetUserData( key ) ?? default;

	/// <summary>
	/// New, updated UserInfo data arrived. Replace our old data with this.
	/// </summary>
	internal void UpdateUserData( Dictionary<string, string> userData )
	{
		Info?.UpdateUserData( userData );
	}

	/// <summary>
	/// Set or update an individual UserInfo data key.
	/// </summary>
	/// <param name="key"></param>
	/// <param name="value"></param>
	internal void SetUserData( string key, string value )
	{
		Info?.SetUserData( key, value );
	}
}
