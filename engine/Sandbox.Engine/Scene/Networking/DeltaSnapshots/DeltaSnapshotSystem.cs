using Sandbox.Hashing;
using System.Buffers.Binary;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Sandbox.Network;

/// <summary>
/// Sends the synced state of networked objects to connections as deltas, and applies the deltas others send us.
/// <para>
/// Sending: every value change is numbered (<see cref="LocalSnapshotState.Entry.Change"/>). For each connection and
/// object we keep, per entry, the newest change it acknowledged and the newest one on its way, so deciding what a
/// connection needs is a comparison. Values that aren't acknowledged within about a round trip go again.
/// </para>
/// <para>
/// Snapshots carry when the sender took them, so receivers interpolate on the sender's timeline rather than by when
/// packets happened to arrive.
/// </para>
/// </summary>
internal class DeltaSnapshotSystem
{
	private static readonly GuidUlongComparer GuidComparer = new();

	/// <summary>
	/// How many seconds we wait for a snapshot acknowledgement before sending the values again, until we know the
	/// connection's round trip time.
	/// </summary>
	private const float DefaultResendTime = 0.25f;

	private GameNetworkSystem System { get; set; }

	public DeltaSnapshotSystem( GameNetworkSystem system )
	{
		System = system;
	}

	internal class GuidUlongComparer : IEqualityComparer<Guid>
	{
		[MethodImpl( MethodImplOptions.AggressiveInlining )]
		public bool Equals( Guid x, Guid y ) => x == y;

		[MethodImpl( MethodImplOptions.AggressiveInlining )]
		public int GetHashCode( Guid guid )
		{
			var span = MemoryMarshal.Cast<Guid, long>( MemoryMarshal.CreateSpan( ref guid, 1 ) );
			return (int)(span[0] ^ span[1]);
		}
	}

	/// <summary>
	/// What one connection has of one object, by <see cref="LocalSnapshotState.Entry.Index"/>.
	/// </summary>
	internal sealed class ObjectState
	{
		public LocalSnapshotState Local;
		public int ClearGeneration;

		/// <summary>
		/// Unique number of this start, acknowledgements of sends from before a reset no longer count.
		/// </summary>
		public int Resets;

		static int NextReset;

		public long[] Acked = [];
		public long[] Sent = [];
		public float[] SentAt = [];

		/// <summary>
		/// The <see cref="LocalSnapshotState.LastChange"/> at which every entry was acknowledged.
		/// </summary>
		public long CleanAt = -1;

		public void Reset( LocalSnapshotState local )
		{
			Local = local;
			ClearGeneration = local.ClearGeneration;
			Resets = ++NextReset;
			CleanAt = -1;
			Array.Clear( Acked );
			Array.Clear( Sent );
		}

		public void EnsureSize( int count )
		{
			if ( Acked.Length >= count )
				return;

			var size = Math.Max( count, Math.Max( 8, Acked.Length * 2 ) );
			Array.Resize( ref Acked, size );
			Array.Resize( ref Sent, size );
			Array.Resize( ref SentAt, size );
		}
	}

	/// <summary>
	/// A value we sent, applied to <see cref="ObjectState.Acked"/> when its packet is acknowledged. No references,
	/// thousands are in flight.
	/// </summary>
	internal readonly record struct SentValue( int NetIndex, int Resets, int Index, long Change, uint SnapshotId, ushort Version );

	internal sealed class SentPacket
	{
		public readonly List<SentValue> Values;
		public float SentTime;

		// By capacity in powers of two, so a packet is rented big enough and its list never grows
		static readonly Stack<SentPacket>[] Pools = Enumerable.Range( 0, 32 ).Select( _ => new Stack<SentPacket>() ).ToArray();

		SentPacket( int capacity ) => Values = new( capacity );

		/// <summary>
		/// A packet with room for <paramref name="count"/> values.
		/// </summary>
		public static SentPacket Rent( float time, int count )
		{
			var size = (int)BitOperations.RoundUpToPowerOf2( (uint)Math.Max( count, 1 ) );
			var packet = Pools[BitOperations.Log2( (uint)size )].TryPop( out var p ) ? p : new SentPacket( size );
			packet.SentTime = time;
			return packet;
		}

		public void Return()
		{
			Values.Clear();
			Pools[BitOperations.Log2( (uint)Values.Capacity )].Push( this );
		}
	}

	internal class ConnectionData
	{
		public Dictionary<ushort, SentPacket> SentClusters { get; } = new();
		public Dictionary<(Guid Object, uint SnapshotId), SentPacket> SentSnapshots { get; } = new();
		public Dictionary<Guid, RemoteSnapshotState> RemoteSnapshotStates { get; set; } = new( GuidComparer );
		public Connection Connection { get; private set; }

		/// <summary>
		/// By <see cref="LocalSnapshotState.NetIndex"/>.
		/// </summary>
		public ObjectState[] Objects = new ObjectState[64];

		public ConnectionData( Connection connection )
		{
			Connection = connection;
		}

		// Smoothed round trip time and its variation, like TCP
		float _roundTrip = -1;
		float _roundTripVariation;

		/// <summary>
		/// How long to wait for an acknowledgement before sending values again.
		/// </summary>
		public float ResendTime { get; private set; } = DefaultResendTime;

		public void AddRoundTrip( float sample, float minimum )
		{
			if ( _roundTrip < 0 )
			{
				_roundTrip = sample;
				_roundTripVariation = sample / 2;
			}
			else
			{
				_roundTripVariation = 0.75f * _roundTripVariation + 0.25f * MathF.Abs( _roundTrip - sample );
				_roundTrip = 0.875f * _roundTrip + 0.125f * sample;
			}

			ResendTime = MathF.Max( minimum, _roundTrip + 4 * _roundTripVariation );
		}

		double _clockOffset = double.NaN;

		/// <summary>
		/// The sender's time on our clock, plus this sender's interpolation delay less <see cref="Networking.InterpolationTime"/>.
		/// The offset is the quickest delivery seen, so jitter only ever makes a snapshot late, never early. It creeps up
		/// for slow drift and jumps when the clocks or the route change a lot.
		/// </summary>
		public double ToLocalTime( double sentAt )
		{
			var now = Sandbox.Time.NowDouble;
			var sample = now - sentAt;

			if ( double.IsNaN( _clockOffset ) || sample < _clockOffset || sample - _clockOffset > Networking.InterpolationTime )
				_clockOffset = sample;
			else
				_clockOffset += (sample - _clockOffset) * 0.01;

			var time = sentAt + _clockOffset;
			return time + UpdateDelay( time, now ) - Networking.InterpolationTime;
		}

		double _newest = double.NaN;
		double _delay = double.NaN;
		double _delayTarget;
		double _holdUntil;
		double _lastReceived;

		// The longest recent wait for an update after the one before, at most 100 ms or two updates
		double UpdateDelay( double time, double now )
		{
			if ( Networking.InterpolationTimeOverride > 0 )
				return Networking.InterpolationTime;

			var max = Math.Max( Networking.InterpolationTime, 0.1 );
			var dt = now - _lastReceived;
			_lastReceived = now;

			if ( double.IsNaN( _delay ) )
				_delay = _delayTarget = max;

			if ( !double.IsNaN( _newest ) && time > _newest )
			{
				var wait = now - _newest + 0.002;

				// A longer silence is the sender having nothing to send
				if ( wait < max * 2 )
				{
					if ( wait >= _delayTarget )
					{
						_delayTarget = wait;
						_holdUntil = now + 3;
					}
					else if ( now >= _holdUntil )
					{
						_delayTarget += (wait - _delayTarget) * (1 - Math.Exp( -dt / 1.5 ));
					}
				}
			}

			if ( double.IsNaN( _newest ) || time > _newest )
				_newest = time;

			var goal = Math.Min( _delayTarget, max );
			_delay = goal > _delay ? Math.Min( goal, _delay + dt * 0.2 ) : Math.Max( goal, _delay - dt * 0.05 );

			return _delay;
		}

		/// <summary>
		/// Acknowledgements of clusters this connection sent us, sent back together once incoming messages are handled.
		/// </summary>
		public byte[] PendingAcks = new byte[64];
		public int PendingAckLength;
		public int PendingAckCount;

		public void AddPendingAck( ushort clusterId, HashSet<uint> invalid )
		{
			var size = 4 + invalid.Count * 4;

			if ( PendingAckLength + size > PendingAcks.Length )
				Array.Resize( ref PendingAcks, Math.Max( PendingAckLength + size, PendingAcks.Length * 2 ) );

			var span = PendingAcks.AsSpan( PendingAckLength );
			BinaryPrimitives.WriteUInt16LittleEndian( span, clusterId );
			BinaryPrimitives.WriteUInt16LittleEndian( span[2..], (ushort)invalid.Count );

			var offset = 4;
			foreach ( var id in invalid )
			{
				BinaryPrimitives.WriteUInt32LittleEndian( span[offset..], id );
				offset += 4;
			}

			PendingAckLength += size;
			PendingAckCount++;
		}

		public ObjectState Get( LocalSnapshotState local )
		{
			var index = local.NetIndex;

			if ( index >= Objects.Length )
				Array.Resize( ref Objects, Math.Max( index + 1, Objects.Length * 2 ) );

			var state = Objects[index] ??= new ObjectState();

			if ( state.Local != local || state.ClearGeneration != local.ClearGeneration )
				state.Reset( local );

			state.EnsureSize( local.IndexCount );
			return state;
		}

		/// <summary>
		/// Clean up any data about a removed networked object.
		/// </summary>
		public void RemoveNetworkObject( NetworkObject nwo )
		{
			RemoteSnapshotStates.Remove( nwo.Id );
		}

		/// <summary>
		/// How long we keep what we sent waiting for an acknowledgement.
		/// </summary>
		private const float MaxAckWait = 5f;

		private float _nextPruneTime;

		/// <summary>
		/// Clear this connection and clean up.
		/// </summary>
		public void Clear()
		{
			RemoteSnapshotStates.Clear();
			PendingAckLength = 0;
			PendingAckCount = 0;

			foreach ( var sent in SentSnapshots.Values )
				sent.Return();

			foreach ( var sent in SentClusters.Values )
				sent.Return();

			SentSnapshots.Clear();
			SentClusters.Clear();
			Array.Clear( Objects );
		}

		/// <summary>
		/// Tick the connection and clear any out-of-date data.
		/// </summary>
		public void Tick( float time )
		{
			if ( time < _nextPruneTime )
				return;

			foreach ( var (id, sent) in SentClusters )
			{
				if ( time - sent.SentTime <= MaxAckWait )
					continue;

				SentClusters.Remove( id );
				sent.Return();
			}

			foreach ( var (key, sent) in SentSnapshots )
			{
				if ( time - sent.SentTime <= MaxAckWait )
					continue;

				SentSnapshots.Remove( key );
				sent.Return();
			}

			_nextPruneTime = time + 1f;
		}
	}

	private Dictionary<Guid, ConnectionData> Connections { get; set; } = new( GuidComparer );
	private Dictionary<Guid, uint> LastSentSnapshotIds { get; set; } = new( GuidComparer );
	internal float Time { get; set; }

	/// <summary>
	/// Objects by <see cref="LocalSnapshotState.NetIndex"/>.
	/// </summary>
	private readonly List<LocalSnapshotState> _netObjects = new();
	private readonly Stack<int> _freeNetIndices = new();

	internal delegate void SendHandler( Connection target, InternalMessageType type, ReadOnlySpan<byte> data, NetFlags flags );

	/// <summary>
	/// Tests replace the transport and the scene lookup so the snapshot system runs without an engine.
	/// </summary>
	internal SendHandler SendOverride { get; set; }

	/// <inheritdoc cref="SendOverride"/>
	internal Func<Guid, IDeltaSnapshot> FindSnapshotterOverride { get; set; }

	private void SendMessage( Connection target, InternalMessageType type, ReadOnlySpan<byte> data, NetFlags flags )
	{
		if ( SendOverride is not null )
		{
			SendOverride( target, type, data, flags );
			return;
		}

		System.Send( target, type, data, flags );
	}

	private IDeltaSnapshot FindSnapshotter( Scene scene, Guid objectId )
	{
		if ( FindSnapshotterOverride is not null )
			return FindSnapshotterOverride( objectId );

		IDeltaSnapshot snapshotter = scene.Directory.FindSystemByGuid( objectId );

		if ( snapshotter is null )
		{
			var go = scene.Directory.FindByGuid( objectId );
			if ( go.IsValid() )
				snapshotter = go._net;
		}

		return snapshotter;
	}

	/// <summary>
	/// Remove a connection from the snapshot system.
	/// </summary>
	public void RemoveConnection( Connection target )
	{
		if ( Connections.Remove( target.Id, out var data ) )
		{
			data.Clear();
			_connectionSet++;
		}
	}

	/// <summary>
	/// Reset all connection data for the snapshot system. This might happen when a hotload
	/// occurs, or the host changes.
	/// </summary>
	internal void Reset()
	{
		LastSentSnapshotIds.Clear();

		foreach ( var connection in Connections.Values )
		{
			connection.Clear();
		}

		Connections.Clear();
		_connectionSet++;

		foreach ( var local in _netObjects )
		{
			if ( local is null ) continue;
			local.NetIndex = -1;
			local.AckedByConnections = -1;
		}

		_netObjects.Clear();
		_freeNetIndices.Clear();
	}

	/// <summary>
	/// Locally clear any stored snapshot information about a networked object.
	/// </summary>
	internal void ClearNetworkObject( NetworkObject nwo )
	{
		foreach ( var c in Connections.Values )
		{
			c.RemoveNetworkObject( nwo );
		}

		LastSentSnapshotIds.Remove( nwo.Id );
		ReleaseNetIndex( nwo.LocalSnapshotState );
	}

	/// <summary>
	/// Forget what <paramref name="target"/> has of this object, so everything is sent to it again.
	/// </summary>
	internal void ForgetObject( LocalSnapshotState local, Connection target )
	{
		var index = local.NetIndex;
		if ( index < 0 || !Connections.TryGetValue( target.Id, out var data ) || index >= data.Objects.Length )
			return;

		if ( data.Objects[index] is { } state && state.Local == local )
			state.Reset( local );

		local.AckedByConnections = -1;
	}

	void AssignNetIndex( LocalSnapshotState local )
	{
		if ( local.NetIndex >= 0 )
			return;

		if ( _freeNetIndices.TryPop( out var index ) )
		{
			_netObjects[index] = local;
		}
		else
		{
			index = _netObjects.Count;
			_netObjects.Add( local );
		}

		local.NetIndex = index;
	}

	void ReleaseNetIndex( LocalSnapshotState local )
	{
		var index = local.NetIndex;
		if ( index < 0 || index >= _netObjects.Count || _netObjects[index] != local )
			return;

		foreach ( var c in Connections.Values )
		{
			if ( index < c.Objects.Length )
				c.Objects[index] = null;
		}

		_netObjects[index] = null;
		_freeNetIndices.Push( index );
		local.NetIndex = -1;
		local.AckedByConnections = -1;
	}

	/// <summary>
	/// Packets sent to all connections that are still waiting for an acknowledgement.
	/// </summary>
	internal int AwaitingAckCount => Connections.Values.Sum( c => c.SentClusters.Count + c.SentSnapshots.Count );

	internal ConnectionData GetConnection( Connection connection )
	{
		if ( !Connections.TryGetValue( connection.Id, out var data ) )
		{
			data = Connections[connection.Id] = new( connection );

			// Starts with nothing, objects everyone else has need sending to it
			_connectionSet++;
		}

		return data;
	}

	private readonly List<LocalSnapshotState.Entry> _sendWithChanges = new();

	/// <summary>
	/// Collect the entries of <paramref name="local"/> that <paramref name="state"/> doesn't have and that aren't
	/// on their way. Returns whether everything is acknowledged.
	/// </summary>
	bool CollectNeeded( LocalSnapshotState local, ObjectState state, List<LocalSnapshotState.Entry> needed, bool all, float resendTime )
	{
		var acknowledged = true;
		var changes = local.ChangeByIndex.AsSpan( 0, local.IndexCount );
		var acked = state.Acked;

		for ( var index = 0; index < changes.Length; index++ )
		{
			var change = changes[index];

			// Unused index, or they have it
			if ( change == 0 || (!all && change <= acked[index]) )
				continue;

			acknowledged = false;

			if ( !all && state.Sent[index] == change && Time - state.SentAt[index] <= resendTime )
			{
				if ( local.EntryByIndex[index].SendWithChanges )
					_sendWithChanges.Add( local.EntryByIndex[index] );

				continue;
			}

			needed.Add( local.EntryByIndex[index] );
		}

		// Values sent without the keys they belong to would be acknowledged but unreadable
		if ( needed.Count > 0 )
			needed.AddRange( _sendWithChanges );

		_sendWithChanges.Clear();
		return acknowledged;
	}

	// The objects in the cluster being written, marked sent when it goes out, so a cluster that turns out too big can
	// hand its last ones to the next
	readonly List<(ObjectState State, int Start, int FirstEntry, int EntryCount)> _clusterObjects = new();
	readonly List<LocalSnapshotState.Entry> _clusterEntries = new();
	byte[] _carried = new byte[4096];

	static int ClusterLimit => DeltaSnapshotCluster.MaxSize;

	// Measuring is what costs, so it waits until the raw size where the last measurement's compression says the cluster
	// will be over. That only decides when to measure: a cluster is always cut at the most objects that fit.
	int _measureAt;
	int _measuredAt;
	float _rawPerEncoded = 1;

	// Measuring encodes the cluster as it would be sent, so if it's sent at the last size that fit, that's the packet
	NetPacket _fitPacket;
	int _fitEnd = -1;

	int Measure( ref ByteStream writer, int end, int objectCount )
	{
		// Encoding adds at most 4 bytes, and without an engine nothing is encoded, see SendOverride
		if ( end + 4 <= ClusterLimit || SendOverride is not null ) return end;
		if ( end == _fitEnd ) return _fitPacket.Length;

		var position = writer.Position;
		WriteClusterCounts( ref writer, end, objectCount );
		writer.Position = position;

		var encoded = Connection.EncodePacket( writer.ToSpan()[..end] );
		var length = encoded.Length;
		_rawPerEncoded = end / (float)length;

		if ( length > ClusterLimit )
		{
			encoded.Release();
			return length;
		}

		_fitPacket?.Release();
		_fitPacket = encoded;
		_fitEnd = end;
		return length;
	}

	NetPacket TakeFitPacket( int end )
	{
		if ( end != _fitEnd ) return null;

		var packet = _fitPacket;
		_fitPacket = null;
		_fitEnd = -1;
		return packet;
	}

	void ReleaseFitPacket()
	{
		_fitPacket?.Release();
		_fitPacket = null;
		_fitEnd = -1;
	}

	/// <summary>
	/// Cut the cluster being written until what's left fits in one packet once encoded.
	/// </summary>
	void Fit( ConnectionData target, ref ByteStream writer, ushort firstClusterId, ref int clusters )
	{
		while ( true )
		{
			var end = writer.Position;
			if ( end == _measuredAt ) return;

			var encoded = Measure( ref writer, end, _clusterObjects.Count );
			if ( encoded <= ClusterLimit || _clusterObjects.Count == 1 )
			{
				_measuredAt = end;
				_measureAt = end + Math.Max( 0, (int)((ClusterLimit - encoded) * _rawPerEncoded) );
				return;
			}

			var keep = _clusterObjects.Count - 1;
			while ( keep > 1 && Measure( ref writer, _clusterObjects[keep].Start, keep ) > ClusterLimit )
				keep--;

			Carry( target, ref writer, (ushort)(firstClusterId + clusters), keep );
			clusters++;
		}
	}

	/// <summary>
	/// Send the first <paramref name="keep"/> objects of the cluster being written and start the next with the rest.
	/// </summary>
	void Carry( ConnectionData target, ref ByteStream writer, ushort clusterId, int keep )
	{
		var cut = _clusterObjects[keep].Start;
		var end = writer.Position;
		var length = end - cut;

		if ( _carried.Length < length )
			_carried = new byte[length * 2];

		writer.ToSpan()[cut..end].CopyTo( _carried );
		writer.Position = cut;
		Close( target, ref writer, clusterId, keep );

		BeginCluster( ref writer, (ushort)(clusterId + 1) );

		var shift = writer.Position - cut;
		writer.Write<byte>( _carried.AsSpan( 0, length ) );

		for ( var i = 0; i < _clusterObjects.Count; i++ )
			_clusterObjects[i] = _clusterObjects[i] with { Start = _clusterObjects[i].Start + shift };
	}

	/// <summary>
	/// Send the first <paramref name="count"/> objects of the cluster being written, which ends at the writer's position.
	/// </summary>
	void Close( ConnectionData target, ref ByteStream writer, ushort clusterId, int count )
	{
		var entries = CollectionsMarshal.AsSpan( _clusterEntries );
		var used = count < _clusterObjects.Count ? _clusterObjects[count].FirstEntry : _clusterEntries.Count;
		var packet = SentPacket.Rent( Time, used );

		for ( var i = 0; i < count; i++ )
		{
			var (state, _, first, entryCount) = _clusterObjects[i];
			MarkSent( state, entries.Slice( first, entryCount ), packet.Values );
		}

		FlushCluster( target, ref writer, packet, clusterId, count );

		_clusterObjects.RemoveRange( 0, count );
		_clusterEntries.RemoveRange( 0, used );

		for ( var i = 0; i < _clusterObjects.Count; i++ )
			_clusterObjects[i] = _clusterObjects[i] with { FirstEntry = _clusterObjects[i].FirstEntry - used };
	}

	void WriteObject( ref ByteStream writer, LocalSnapshotState local, List<LocalSnapshotState.Entry> entries )
	{
		writer.Write( local.Version );
		writer.Write( local.SnapshotId );
		writer.Write( local.ObjectId );
		WriteEntries( ref writer, entries );
	}

	static void WriteEntries( ref ByteStream writer, List<LocalSnapshotState.Entry> entries )
	{
		writer.Write( (ushort)entries.Count );

		foreach ( var entry in entries )
		{
			writer.Write( entry.Slot );
			writer.WriteArray( entry.Value );
		}
	}

	void MarkSent( ObjectState state, ReadOnlySpan<LocalSnapshotState.Entry> entries, List<SentValue> record )
	{
		var local = state.Local;

		foreach ( var entry in entries )
		{
			state.Sent[entry.Index] = entry.Change;
			state.SentAt[entry.Index] = Time;
			record.Add( new SentValue( local.NetIndex, state.Resets, entry.Index, entry.Change, local.SnapshotId, local.Version ) );
		}
	}

	void Acknowledge( ConnectionData target, SentPacket sent, HashSet<uint> rejected )
	{
		// Never quicker than our own sends, values only go out on those
		target.AddRoundTrip( Time - sent.SentTime, Game.ActiveScene?.NetworkRate ?? 0 );

		var objects = target.Objects;

		foreach ( var value in sent.Values )
		{
			// Did the client reject this snapshot? Maybe the game object didn't exist yet.
			if ( rejected is not null && rejected.Contains( value.SnapshotId ) )
				continue;

			if ( value.NetIndex >= objects.Length || objects[value.NetIndex] is not { } state )
				continue;

			if ( state.Resets != value.Resets || state.Local.Version != value.Version )
				continue;

			if ( value.Change > state.Acked[value.Index] )
				state.Acked[value.Index] = value.Change;
		}

		sent.Return();
	}

	private readonly HashSet<uint> _invalidSnapshotIds = new( 32 );

	/// <summary>
	/// Acknowledgements of the clusters we sent, several in one message: a count, then for each the cluster id and
	/// the snapshots in it that were rejected.
	/// </summary>
	public void OnDeltaSnapshotClusterAck( Connection source, ByteStream message )
	{
		var connectionData = GetConnection( source );
		var count = message.Read<ushort>();

		for ( var a = 0; a < count; a++ )
		{
			var clusterId = message.Read<ushort>();
			var invalidSnapshotCount = message.Read<ushort>();

			_invalidSnapshotIds.Clear();

			for ( var i = 0; i < invalidSnapshotCount; i++ )
			{
				_invalidSnapshotIds.Add( message.Read<uint>() );
			}

			if ( connectionData.SentClusters.Remove( clusterId, out var sent ) )
				Acknowledge( connectionData, sent, _invalidSnapshotIds );
		}
	}

	/// <summary>
	/// Send the acknowledgements of the clusters received since the last call, one message per connection.
	/// </summary>
	public void FlushAcks()
	{
		foreach ( var data in Connections.Values )
		{
			if ( data.PendingAckCount == 0 )
				continue;

			var bs = ByteStream.Create( data.PendingAckLength + 2 );
			bs.Write( (ushort)data.PendingAckCount );
			bs.Write( data.PendingAcks.AsSpan( 0, data.PendingAckLength ) );

			SendMessage( data.Connection, InternalMessageType.DeltaSnapshotClusterAck, bs.ToSpan(),
				NetFlags.Unreliable | NetFlags.DiscardOnDelay );

			bs.Dispose();
			data.PendingAckLength = 0;
			data.PendingAckCount = 0;
		}
	}

	/// <summary>
	/// Copy what's left of <paramref name="reader"/> into a pooled buffer. Received values point into it, so it's
	/// returned once the message has been handled.
	/// </summary>
	static byte[] CopyMessage( ByteStream reader, out int length )
	{
		var remaining = reader.GetRemainingBytes();
		var buffer = global::System.Buffers.ArrayPool<byte>.Shared.Rent( Math.Max( 1, remaining.Length ) );
		remaining.CopyTo( buffer );
		length = remaining.Length;
		return buffer;
	}

	/// <summary>
	/// Read one object's slots into a pooled snapshot, leaving out values older than ones we already have.
	/// </summary>
	static DeltaSnapshot ReadSnapshot( ref ByteStream reader, byte[] buffer, ConnectionData connectionData, Guid objectId, uint snapshotId, double time, bool accept )
	{
		var dataCount = reader.Read<ushort>();
		var snapshot = accept ? DeltaSnapshot.Pool.Rent() : null;
		RemoteSnapshotState state = null;

		if ( accept && !connectionData.RemoteSnapshotStates.TryGetValue( objectId, out state ) )
			state = connectionData.RemoteSnapshotStates[objectId] = new RemoteSnapshotState { ObjectId = objectId };

		for ( var j = 0; j < dataCount; j++ )
		{
			var slot = reader.Read<int>();
			var length = reader.ReadArraySpan<byte>( 1024 * 1024 * 16 ).Length;

			if ( accept && state.Accept( slot, snapshotId ) )
				snapshot.AddSerialized( slot, buffer.AsMemory( reader.Position - length, length ) );
		}

		if ( snapshot is not null )
			snapshot.Time = time;

		return snapshot;
	}

	public void OnDeltaSnapshotCluster( Connection source, ByteStream message )
	{
		var scene = Game.ActiveScene;
		if ( FindSnapshotterOverride is null && !scene.IsValid() ) return;

		var buffer = CopyMessage( message, out var length );
		var reader = ByteStream.CreateReader( buffer.AsSpan( 0, length ) );

		try
		{
			var clusterId = reader.Read<ushort>();
			var connectionData = GetConnection( source );
			var count = (int)reader.Read<ushort>();
			var time = connectionData.ToLocalTime( reader.Read<double>() );

			_invalidSnapshotIds.Clear();

			for ( var i = 0; i < count; i++ )
			{
				var version = reader.Read<ushort>();
				var snapshotId = reader.Read<uint>();
				var objectId = reader.Read<Guid>();

				var snapshotter = FindSnapshotter( scene, objectId );
				var valid = snapshotter is not null && snapshotter.SnapshotVersion == version;

				var snapshot = ReadSnapshot( ref reader, buffer, connectionData, objectId, snapshotId, time, valid );

				if ( !valid )
				{
					_invalidSnapshotIds.Add( snapshotId );
					continue;
				}

				if ( !snapshotter.OnSnapshot( source, snapshot ) )
				{
					_invalidSnapshotIds.Add( snapshotId );
				}

				snapshot.Release();
			}

			// Sent with the others from this connection, see FlushAcks
			connectionData.AddPendingAck( clusterId, _invalidSnapshotIds );
		}
		finally
		{
			reader.Dispose();
			global::System.Buffers.ArrayPool<byte>.Shared.Return( buffer );
		}
	}

	public void OnDeltaSnapshotAck( Connection source, ByteStream message )
	{
		var objectId = message.Read<Guid>();
		var snapshotId = message.Read<uint>();
		var connectionData = GetConnection( source );

		if ( !connectionData.SentSnapshots.Remove( (objectId, snapshotId), out var sent ) )
			return;

		Acknowledge( connectionData, sent, null );
	}

	public void OnDeltaSnapshot( Connection source, ByteStream message )
	{
		var scene = Game.ActiveScene;
		if ( FindSnapshotterOverride is null && !scene.IsValid() ) return;

		var connectionData = GetConnection( source );
		var buffer = CopyMessage( message, out var length );
		var reader = ByteStream.CreateReader( buffer.AsSpan( 0, length ) );

		try
		{
			var objectId = reader.Read<Guid>();
			var version = reader.Read<ushort>();
			var snapshotId = reader.Read<uint>();
			var time = connectionData.ToLocalTime( reader.Read<double>() );

			var snapshotter = FindSnapshotter( scene, objectId );
			if ( snapshotter is null || snapshotter.SnapshotVersion != version )
				return;

			var snapshot = ReadSnapshot( ref reader, buffer, connectionData, objectId, snapshotId, time, true );
			var applied = snapshotter.OnSnapshot( source, snapshot );
			snapshot.Release();

			if ( !applied )
				return;

			var ackBs = ByteStream.Create( 32 );
			ackBs.Write( objectId );
			ackBs.Write( snapshotId );

			SendMessage( source, InternalMessageType.DeltaSnapshotAck, ackBs.ToSpan(),
				NetFlags.Unreliable | NetFlags.DiscardOnDelay );

			ackBs.Dispose();
		}
		finally
		{
			reader.Dispose();
			global::System.Buffers.ArrayPool<byte>.Shared.Return( buffer );
		}
	}

	/// <summary>
	/// Create a new snapshot id for the provided <see cref="NetworkObject"/>.
	/// </summary>
	public uint CreateSnapshotId( Guid objectId )
	{
		ref var id = ref CollectionsMarshal.GetValueRefOrAddDefault( LastSentSnapshotIds, objectId, out bool exists );

		if ( exists )
			id++;

		return id;
	}

	/// <summary>
	/// Update the cached real time value that is used internally in the Delta Snapshot System.
	/// </summary>
	internal void UpdateTime()
	{
		// We cache the real time here for use within the Delta Snapshot System. This is because
		// it can be expensive when called a lot of times.
		Time = RealTime.Now;
		SendTime = Sandbox.Time.NowDouble;
	}

	/// <summary>
	/// Game time written into what we send, see <see cref="ConnectionData.ToLocalTime"/>.
	/// </summary>
	internal double SendTime { get; set; }

	private readonly record struct PendingEmptyAck( ConnectionData Target, ushort ClusterId, RealTimeSince TimeSince );
	private readonly List<PendingEmptyAck> _pendingEmptyAcks = new();

	/// <summary>
	/// Tick the snapshot system and clear any out-of-date data.
	/// </summary>
	public void Tick()
	{
		foreach ( var c in Connections.Values )
		{
			c.Tick( Time );
		}

		ProcessPendingEmptyAcks();
	}

	private void ProcessPendingEmptyAcks()
	{
		for ( var i = _pendingEmptyAcks.Count - 1; i >= 0; i-- )
		{
			var ack = _pendingEmptyAcks[i];

			if ( ack.TimeSince < 0.05f ) continue;

			_pendingEmptyAcks.RemoveAt( i );

			if ( ack.Target.SentClusters.Remove( ack.ClusterId, out var sent ) )
				Acknowledge( ack.Target, sent, null );
		}
	}

	/// <summary>
	/// Bumped whenever the connections we send to change, objects acknowledged by everyone before need checking again.
	/// </summary>
	private int _connectionSet;
	private long _connectionSetKey;

	void UpdateConnectionSet( Connection[] connections )
	{
		long key = connections.Length;

		foreach ( var connection in connections )
			key += connection.Id.GetHashCode() * unchecked((long)0x9E3779B97F4A7C15UL);

		if ( key == _connectionSetKey )
			return;

		_connectionSetKey = key;
		_connectionSet++;
	}

	private readonly List<(IDeltaSnapshot Source, LocalSnapshotState Local, uint Revision)> _pending = new();
	private readonly List<int> _cleanCounts = new();
	private readonly List<LocalSnapshotState.Entry> _needed = new();
	private ushort _nextClusterId;

	/// <summary>
	/// Send a delta snapshot for a set of networked objects to the specified connections.
	/// </summary>
	public void Send( ReadOnlySpan<IDeltaSnapshot> objects, Connection[] connections )
	{
		_pending.Clear();
		_cleanCounts.Clear();
		UpdateConnectionSet( connections );

		foreach ( var nwo in objects )
		{
			var revision = nwo.DirtyRevision;
			nwo.IsFullyUpdated = false;

			// Don't send updates about objects we don't own. The host can always send updates though
			// because there may be FromHost sync vars.
			if ( nwo.IsProxy && !Networking.IsHost )
			{
				nwo.IsFullyUpdated = true;
				continue;
			}

			if ( connections.Length == 0 )
			{
				// Discard reliable collection deltas even without recipients: future peers get a full baseline.
				nwo.SendNetworkUpdate( true );
				nwo.IsFullyUpdated = revision == nwo.DirtyRevision;
				continue;
			}

			var isAnyVisible = nwo.UpdateTransmitState( connections );

			// No point doing anything else if no connections can even see this object.
			if ( !isAnyVisible )
			{
				nwo.SendNetworkUpdate( true );
				continue;
			}

			var local = nwo.WriteSnapshotState();
			nwo.SendNetworkUpdate();

			ClearRemovedSlots( local );

			// Do we even need to send this to anybody?
			if ( local.AckedByConnections == _connectionSet || local.Size == 0 )
			{
				nwo.IsFullyUpdated = revision == nwo.DirtyRevision;
				continue;
			}

			AssignNetIndex( local );
			_pending.Add( (nwo, local, revision) );
			_cleanCounts.Add( 0 );
		}

		if ( _pending.Count == 0 )
			return;

		var firstClusterId = _nextClusterId;
		var clustersUsed = 0;

		try
		{
			foreach ( var connection in connections )
			{
				var used = SendClusters( GetConnection( connection ), firstClusterId );
				clustersUsed = Math.Max( clustersUsed, used );
			}
		}
		finally
		{
			FlushShared();
		}

		_nextClusterId = (ushort)(firstClusterId + clustersUsed);

		for ( var i = 0; i < _pending.Count; i++ )
		{
			var (source, local, revision) = _pending[i];
			var isFullyUpdated = _cleanCounts[i] == connections.Length;
			local.AckedByConnections = isFullyUpdated ? _connectionSet : -1;
			source.IsFullyUpdated = isFullyUpdated && revision == source.DirtyRevision;
		}
	}

	/// <summary>
	/// Pack what this connection needs of the pending objects into clusters and send them. Clusters are numbered
	/// from <paramref name="firstClusterId"/> for every connection, so connections that need the same get the same bytes.
	/// </summary>
	int SendClusters( ConnectionData target, ushort firstClusterId )
	{
		var connection = target.Connection;
		var writer = new ByteStream( DeltaSnapshotCluster.MaxSize * 4 );
		var clusters = 0;

		_clusterObjects.Clear();
		_clusterEntries.Clear();

		try
		{
			for ( var i = 0; i < _pending.Count; i++ )
			{
				var (source, local, _) = _pending[i];

				// Culled for this connection: nothing to send and nothing to wait for
				if ( !source.ShouldTransmit( connection ) )
				{
					_cleanCounts[i]++;
					continue;
				}

				var state = target.Get( local );

				if ( state.CleanAt == local.LastChange )
				{
					_cleanCounts[i]++;
					continue;
				}

				_needed.Clear();

				if ( CollectNeeded( local, state, _needed, false, target.ResendTime ) )
				{
					state.CleanAt = local.LastChange;
					_cleanCounts[i]++;
					continue;
				}

				if ( _needed.Count == 0 )
					continue;

				if ( _clusterObjects.Count == 0 )
					BeginCluster( ref writer, (ushort)(firstClusterId + clusters) );

				_clusterObjects.Add( (state, writer.Position, _clusterEntries.Count, _needed.Count) );
				_clusterEntries.AddRange( _needed );
				WriteObject( ref writer, local, _needed );

				if ( writer.Position > _measureAt )
					Fit( target, ref writer, firstClusterId, ref clusters );
			}

			if ( _clusterObjects.Count > 0 )
			{
				Fit( target, ref writer, firstClusterId, ref clusters );
				Close( target, ref writer, (ushort)(firstClusterId + clusters), _clusterObjects.Count );
				clusters++;
			}
		}
		finally
		{
			ReleaseFitPacket();
			writer.Dispose();
		}

		return clusters;
	}

	// The whole message is written, so identical ones can share one encoding. Lengths are filled in when flushing.
	const int ClusterCountOffset = 1 + 4 + 2;

	void BeginCluster( ref ByteStream writer, ushort clusterId )
	{
		ReleaseFitPacket();
		_measureAt = Math.Max( ClusterLimit, (int)(ClusterLimit * _rawPerEncoded) );
		_measuredAt = -1;
		writer.Position = 0;
		writer.Write( InternalMessageType.DeltaSnapshotCluster );
		writer.Write( 0 );
		writer.Write( clusterId );
		writer.Write( (ushort)0 );
		writer.Write( SendTime );
	}

	// The length and object count, filled in once the cluster's contents are known
	static void WriteClusterCounts( ref ByteStream writer, int end, int objectCount )
	{
		writer.Position = 1;
		writer.Write( end - 5 );
		writer.Position = ClusterCountOffset;
		writer.Write( (ushort)objectCount );
	}

	void FlushCluster( ConnectionData target, ref ByteStream writer, SentPacket packet, ushort clusterId, int objectCount )
	{
		var end = writer.Position;
		WriteClusterCounts( ref writer, end, objectCount );
		writer.Position = end;

		var message = writer.ToSpan().Slice( 0, end );
		var connection = target.Connection;

		if ( SendOverride is not null || (connection is not MockConnection && System.NetworkSystem.FindConnection( connection.Id ) != connection) )
		{
			SendMessage( connection, InternalMessageType.DeltaSnapshotCluster, message[5..], ClusterFlags );
		}
		else
		{
			ref var shared = ref Share( message );

			// Peers we only reach through the host go up together, see FlushShared
			if ( connection.RelayFor( ClusterFlags ) )
				(shared.RelayTargets ??= _targetLists.TryPop( out var list ) ? list : new()).Add( connection );
			else
				connection.Send( shared.Encoded ??= TakeFitPacket( end ) ?? Connection.EncodePacket( message ), ClusterFlags );
		}

		// Cluster ids wrap, so in theory an old unacknowledged one could still be here
		if ( target.SentClusters.Remove( clusterId, out var replaced ) )
			replaced.Return();

		target.SentClusters[clusterId] = packet;

		// For empty connections, we still want to "receive" acknowledgements for benchmarking purposes
		if ( connection is EmptyConnection )
		{
			_pendingEmptyAcks.Add( new PendingEmptyAck( target, clusterId, 0f ) );
		}
	}

	const NetFlags ClusterFlags = NetFlags.Unreliable | NetFlags.SendImmediate;

	/// <summary>
	/// A cluster message several connections need byte for byte: compressed once for the direct ones, sent up to the
	/// host once for the ones behind it.
	/// </summary>
	struct SharedMessage
	{
		public ulong Hash;
		public byte[] Bytes;
		public int Length;
		public NetPacket Encoded;
		public List<Connection> RelayTargets;
	}

	private readonly List<SharedMessage> _shared = new();
	private readonly Stack<List<Connection>> _targetLists = new();

	ref SharedMessage Share( ReadOnlySpan<byte> message )
	{
		var hash = XxHash3.HashToUInt64( message );
		var shared = CollectionsMarshal.AsSpan( _shared );

		for ( var i = 0; i < shared.Length; i++ )
		{
			if ( shared[i].Hash == hash && message.SequenceEqual( shared[i].Bytes.AsSpan( 0, shared[i].Length ) ) )
				return ref shared[i];
		}

		var copy = global::System.Buffers.ArrayPool<byte>.Shared.Rent( message.Length );
		message.CopyTo( copy );
		_shared.Add( new SharedMessage { Hash = hash, Bytes = copy, Length = message.Length } );

		return ref CollectionsMarshal.AsSpan( _shared )[^1];
	}

	/// <summary>
	/// Send the clusters for peers behind the host, one multicast for each set of peers that need the same bytes, and
	/// let go of this frame's shared messages.
	/// </summary>
	void FlushShared()
	{
		foreach ( var shared in _shared )
		{
			if ( shared.RelayTargets is not null )
			{
				System.NetworkSystem.SendMulticast( shared.RelayTargets, shared.Bytes.AsSpan( 0, shared.Length ), ClusterFlags );
				shared.RelayTargets.Clear();
				_targetLists.Push( shared.RelayTargets );
			}

			shared.Encoded?.Release();
			global::System.Buffers.ArrayPool<byte>.Shared.Return( shared.Bytes );
		}

		_shared.Clear();
	}

	readonly List<Connection> _singleSendConnections = new();

	/// <summary>
	/// Send everything this object has to every connection, reliably, like when its owner changes.
	/// </summary>
	public void SendFull( IDeltaSnapshot snapshotter )
	{
		if ( snapshotter is null ) return;

		var local = snapshotter.WriteSnapshotState();
		snapshotter.SendNetworkUpdate();

		ClearRemovedSlots( local );

		if ( local.Size == 0 )
			return;

		AssignNetIndex( local );

		System.CollectConnections( _singleSendConnections, Connection.ChannelState.Snapshot );

		foreach ( var connection in _singleSendConnections )
		{
			var target = GetConnection( connection );
			var state = target.Get( local );

			_needed.Clear();
			CollectNeeded( local, state, _needed, true, target.ResendTime );

			if ( _needed.Count == 0 )
				continue;

			using var writer = WriteSnapshot( local, _needed );
			SendMessage( connection, InternalMessageType.DeltaSnapshot, writer.ToSpan(), NetFlags.Reliable );

			var packet = SentPacket.Rent( Time, _needed.Count );
			MarkSent( state, CollectionsMarshal.AsSpan( _needed ), packet.Values );

			if ( target.SentSnapshots.Remove( (local.ObjectId, local.SnapshotId), out var replaced ) )
				replaced.Return();

			target.SentSnapshots[(local.ObjectId, local.SnapshotId)] = packet;
		}
	}

	/// <summary>
	/// Clear all <see cref="RemoteSnapshotState"/> of removed slots from a <see cref="LocalSnapshotState"/>.
	/// </summary>
	void ClearRemovedSlots( LocalSnapshotState localState )
	{
		if ( localState.RemovedSlots.Count == 0 )
			return;

		foreach ( var connection in Connections.Values )
		{
			if ( !connection.RemoteSnapshotStates.TryGetValue( localState.ObjectId, out var state ) )
				continue;

			foreach ( var slot in localState.RemovedSlots )
			{
				state.Remove( slot );
			}
		}

		localState.RemovedSlots.Clear();
	}

	/// <summary>
	/// Get a full serialized data update for a local snapshot state.
	/// </summary>
	public byte[] GetFullSnapshotData( LocalSnapshotState state )
	{
		using var bs = WriteSnapshot( state, state.Entries );
		return bs.ToArray();
	}

	/// <summary>
	/// The object's snapshot version in <see cref="GetFullSnapshotData"/>, after its id.
	/// </summary>
	internal static ushort ReadVersion( byte[] snapshotData ) => BinaryPrimitives.ReadUInt16LittleEndian( snapshotData.AsSpan( 16 ) );

	/// <summary>
	/// A <see cref="InternalMessageType.DeltaSnapshot"/> of these entries.
	/// </summary>
	ByteStream WriteSnapshot( LocalSnapshotState local, List<LocalSnapshotState.Entry> entries )
	{
		// Object id, version, snapshot id, send time, count, then a slot and a length prefixed value each
		var size = 16 + 2 + 4 + 8 + 2;
		foreach ( var entry in entries )
			size += 4 + 4 + entry.Value.Length;

		var writer = ByteStream.Create( size );
		writer.Write( local.ObjectId );
		writer.Write( local.Version );
		writer.Write( local.SnapshotId );
		writer.Write( SendTime );
		WriteEntries( ref writer, entries );

		return writer;
	}
}
