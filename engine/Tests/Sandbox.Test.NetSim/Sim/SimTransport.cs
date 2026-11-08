using Sandbox.Network;

namespace NetSim;

/// <summary>
/// Latency and loss of the simulated network. Reliable messages are never lost and arrive in order.
/// </summary>
internal record SimLinkSettings
{
	public float LatencySeconds { get; init; } = 0.03f;
	public float Loss { get; init; }

	/// <summary>
	/// Each packet takes up to this much longer, unreliable ones can arrive out of order.
	/// </summary>
	public float JitterSeconds { get; init; }
}

/// <summary>
/// Traffic counted per sending peer and message type.
/// </summary>
internal sealed class SimTrafficStats
{
	public long Bytes;
	public long Messages;

	/// <summary>
	/// Messages whose bytes this peer already sent to someone else in the same frame.
	/// </summary>
	public long Repeats;
	public readonly HashSet<ulong> SentThisFrame = new();
	public long Dropped;
	public readonly Dictionary<InternalMessageType, (long Bytes, long Messages, long Uncompressed)> ByType = new();

	public void Add( InternalMessageType type, int bytes, int uncompressed )
	{
		Bytes += bytes;
		Messages++;
		ByType.TryGetValue( type, out var t );
		ByType[type] = (t.Bytes + bytes, t.Messages + 1, t.Uncompressed + uncompressed);
	}

	public void Clear()
	{
		Bytes = Messages = Dropped = 0;
		ByType.Clear();
	}
}

/// <summary>
/// One end of a simulated link: the connection a peer's network system uses to talk to one remote peer.
/// Sends are scheduled on the sim's transport and land in <see cref="Remote"/>'s inbox.
/// </summary>
internal sealed class SimConnection : Connection
{
	public SimPeer Owner { get; }
	public SimPeer RemotePeer { get; }
	public SimConnection Remote { get; set; }

	readonly SimTransport _transport;
	readonly Queue<(byte[] Data, int Length)> _inbox = new();

	public override string Address => $"sim:{RemotePeer.Name}";

	public SimConnection( SimTransport transport, SimPeer owner, SimPeer remote, Guid id = default )
	{
		_transport = transport;
		Owner = owner;
		RemotePeer = remote;
		Id = id;
	}

	internal void Deliver( byte[] data, int length ) => _inbox.Enqueue( (data, length) );

	internal override void InternalSend( NetPacket packet, NetFlags flags ) => _transport.Send( this, packet, flags );

	internal override void InternalRecv( NetworkSystem.MessageHandler handler )
	{
		while ( _inbox.TryDequeue( out var packet ) )
		{
			OnRawPacketReceived( packet.Data.AsSpan( 0, packet.Length ), handler );
			SimTransport.Pool.Return( packet.Data );
		}
	}

	internal override void InternalClose( int closeCode, string closeReason ) { }

	public override string ToString() => $"{Owner.Name}->{RemotePeer.Name}";
}

/// <summary>
/// Accepts simulated connections for a peer, the host's equivalent of a lobby or server socket.
/// </summary>
internal sealed class SimSocket : NetworkSocket
{
	readonly List<SimConnection> _connections = new();

	public void Accept( SimConnection connection )
	{
		_connections.Add( connection );
		OnClientConnect?.Invoke( connection );
	}

	internal override void GetIncomingMessages( NetworkSystem.MessageHandler handler )
	{
		for ( var i = 0; i < _connections.Count; i++ )
		{
			_connections[i].GetIncomingMessages( handler );
		}
	}

	internal override void ProcessMessagesInThread() { }
	internal override void Dispose() { }
}

/// <summary>
/// Moves packets between peers with latency and loss, on the sim's clock.
/// </summary>
internal sealed class SimTransport
{
	readonly record struct InFlight( double DeliverAt, long Order, SimConnection Target, NetPacket Packet );

	/// <summary>
	/// Thousands of packets can be in flight, more than the shared pool keeps, and its misses would count as the sender's allocations.
	/// </summary>
	internal static readonly global::System.Buffers.ArrayPool<byte> Pool = global::System.Buffers.ArrayPool<byte>.Create( 1024 * 1024, 100000 );

	readonly NetSimulation _sim;
	readonly Random _random;
	readonly List<InFlight> _inFlight = new();
	readonly Dictionary<SimConnection, double> _lastReliable = new();
	long _order;

	public SimLinkSettings Settings { get; set; } = new();

	/// <summary>
	/// When a link is down until a later time: unreliable sends are lost, reliable ones arrive once it's back.
	/// </summary>
	public Func<SimConnection, double> DownUntil { get; set; }

	/// <summary>
	/// When set, every send is decoded to count it by message type. Costs time, which is kept out of peer timings.
	/// </summary>
	public bool CountByType { get; set; } = true;

	public long TransportTicks { get; private set; }

	/// <summary>
	/// When set, every send is recorded as frame, sender, receiver, size and whether it was lost.
	/// </summary>
	public List<string> SendLog { get; set; }

	public SimTransport( NetSimulation sim, int seed )
	{
		_sim = sim;
		_random = new Random( seed );
	}

	public void Send( SimConnection from, NetPacket packet, NetFlags flags )
	{
		var data = packet.Span;
		var t0 = System.Diagnostics.Stopwatch.GetTimestamp();

		var stats = from.Owner.Sent;
		var uncompressed = data.Length;
		var type = CountByType ? Classify( data, out uncompressed ) : default;

		var reliable = (flags & NetFlags.Reliable) != 0;
		var lossRoll = Settings.Loss > 0 ? _random.NextDouble() : 1;
		var downUntil = DownUntil?.Invoke( from ) ?? 0;
		var down = downUntil > _sim.Now;
		if ( !reliable && (down || lossRoll < Settings.Loss) )
		{
			SendLog?.Add( $"{_sim.Frame} {from} {type} {uncompressed} lost" );
			stats.Dropped++;
			TransportTicks += System.Diagnostics.Stopwatch.GetTimestamp() - t0;
			return;
		}

		SendLog?.Add( $"{_sim.Frame} {from} {type} {uncompressed}" );
		stats.Add( type, data.Length, uncompressed );
		if ( !stats.SentThisFrame.Add( Sandbox.Hashing.XxHash3.HashToUInt64( data ) ) )
			stats.Repeats++;

		var deliverAt = _sim.Now + Settings.LatencySeconds + _random.NextDouble() * Settings.JitterSeconds;
		if ( down ) deliverAt = Math.Max( deliverAt, downUntil + Settings.LatencySeconds );

		// Reliable messages keep their order on a link
		if ( reliable )
		{
			if ( _lastReliable.TryGetValue( from, out var last ) && last > deliverAt )
				deliverAt = last;

			_lastReliable[from] = deliverAt;
		}

		// Held until delivery like a queued transport would, so releasing it early corrupts what arrives
		packet.AddReference();
		_inFlight.Add( new InFlight( deliverAt, _order++, from.Remote, packet ) );

		TransportTicks += System.Diagnostics.Stopwatch.GetTimestamp() - t0;
	}

	/// <summary>
	/// Hand everything that's due to the receiving connections, in send order.
	/// </summary>
	public void DeliverDue()
	{
		if ( _inFlight.Count == 0 )
			return;

		_inFlight.Sort( static ( a, b ) => a.DeliverAt != b.DeliverAt ? a.DeliverAt.CompareTo( b.DeliverAt ) : a.Order.CompareTo( b.Order ) );

		var due = 0;
		while ( due < _inFlight.Count && _inFlight[due].DeliverAt <= _sim.Now + 1e-9 )
		{
			var packet = _inFlight[due].Packet;

			// Pooled, the copy is the transport's and shouldn't count as the receiver's allocation
			var copy = Pool.Rent( packet.Length );
			packet.Span.CopyTo( copy );
			_inFlight[due].Target.Deliver( copy, packet.Length );
			packet.Release();
			due++;
		}

		_inFlight.RemoveRange( 0, due );
	}

	public int InFlightCount => _inFlight.Count;

	byte[] _classifyBuffer = new byte[1024];

	// Not Connection.Decode: that shares one buffer with the receive path, which may be mid message right now
	InternalMessageType Classify( ReadOnlySpan<byte> data, out int uncompressed )
	{
		uncompressed = data.Length;

		if ( data.Length < 2 || data[0] == Connection.FlagChunk )
			return default;

		if ( data[0] == Connection.FlagRaw )
			return (InternalMessageType)data[1];

		var length = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian( data[1..] );
		uncompressed = length + 1;
		if ( _classifyBuffer.Length < length )
			_classifyBuffer = new byte[length];

		Sandbox.Compression.LZ4.DecompressBlock( data[5..], _classifyBuffer );
		return (InternalMessageType)_classifyBuffer[0];
	}
}
