using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox;
using Sandbox.Network;

namespace NetworkTests;

/// <summary>
/// A networked object for the delta snapshot simulation. The host copy owns values per slot, client copies
/// receive them. Mirrors what NetworkObject does with its LocalSnapshotState.
/// </summary>
internal sealed class SimObject : IDeltaSnapshot
{
	public Guid Id { get; }
	public bool IsProxy => false;
	public ushort SnapshotVersion => Local.Version;

	public readonly LocalSnapshotState Local = new();
	public readonly int[] Slots;
	public readonly byte[][] Values;
	public readonly bool[] Removed;
	readonly byte[][] _written;
	public readonly Dictionary<int, byte[]> Received = new();
	public readonly HashSet<Guid> Culled = new();

	readonly DeltaSnapshotSystem _system;

	public SimObject( DeltaSnapshotSystem system, Guid id, int[] slots )
	{
		_system = system;
		Id = id;
		Slots = slots;
		Values = new byte[slots.Length][];
		Removed = new bool[slots.Length];
		_written = new byte[slots.Length][];
	}

	public bool OnSnapshot( Connection source, DeltaSnapshot snapshot )
	{
		foreach ( var entry in snapshot.Entries )
			Received[entry.Slot] = entry.Value.ToArray();

		return true;
	}

	public bool UpdateTransmitState( Connection[] targets )
	{
		var any = false;

		foreach ( var target in targets )
		{
			if ( !Culled.Contains( target.Id ) )
				any = true;
		}

		return any;
	}

	public bool ShouldTransmit( Connection target ) => !Culled.Contains( target.Id );

	/// <summary>
	/// Becoming visible to a connection again forgets what it had, like NetworkObject does.
	/// </summary>
	public void SetCulled( Connection target, bool culled )
	{
		if ( culled )
		{
			Culled.Add( target.Id );
			return;
		}

		if ( Culled.Remove( target.Id ) )
			_system.ForgetObject( Local, target );
	}

	LocalSnapshotState IDeltaSnapshot.WriteSnapshotState()
	{
		Local.Begin();
		Local.SnapshotId = _system.CreateSnapshotId( Id );
		Local.ObjectId = Id;

		for ( var i = 0; i < Slots.Length; i++ )
		{
			if ( Removed[i] )
			{
				Local.Remove( Slots[i] );
				_written[i] = null;
			}
			else if ( !ReferenceEquals( _written[i], Values[i] ) )
			{
				// Unchanged values aren't re-added, like SnapshotValueCache in the engine
				Local.AddSerialized( Slots[i], Values[i] );
				_written[i] = Values[i];
			}
		}

		return Local;
	}

	void IDeltaSnapshot.SendNetworkUpdate( bool queryValues ) { }
}

/// <summary>
/// Settings for a <see cref="DeltaSnapshotSim"/> run.
/// </summary>
internal sealed record SimSettings
{
	public int Objects { get; init; } = 200;
	public int SlotsPerObject { get; init; } = 8;
	public int ValueBytes { get; init; } = 12;
	public int Clients { get; init; } = 4;

	/// <summary>Fraction of objects that change some slots each tick.</summary>
	public float ChangeRate { get; init; } = 0.3f;
	public int SlotsChangedPerObject { get; init; } = 2;

	/// <summary>Ticks between sending a message and it arriving.</summary>
	public int LatencyTicks { get; init; } = 2;
	public float Loss { get; init; }
	public float TickSeconds { get; init; } = 1f / 30f;
	public int Seed { get; init; } = 1234;

	/// <summary>Per tick chance that an object toggles culling for a random client.</summary>
	public float CullToggleRate { get; init; }

	/// <summary>Per tick chance that an object removes or re-adds a random slot.</summary>
	public float SlotToggleRate { get; init; }
}

/// <summary>
/// Runs a host <see cref="DeltaSnapshotSystem"/> against client systems with the real wire format, through
/// a simulated transport with latency and loss. No engine, no scene.
/// </summary>
internal sealed class DeltaSnapshotSim
{
	readonly record struct Message( int DeliverTick, int Client, bool ToHost, InternalMessageType Type, byte[] Data, int Length );

	public readonly SimSettings Settings;
	public readonly DeltaSnapshotSystem Host;
	public readonly DeltaSnapshotSystem[] ClientSystems;
	public readonly List<SimObject> HostObjects = new();
	public readonly Dictionary<Guid, SimObject> HostLookup = new();
	public readonly Dictionary<Guid, SimObject>[] ClientObjects;
	public readonly Connection[] ClientConnections;
	public readonly Connection HostConnection;

	readonly Random _random;
	readonly List<Message> _inFlight = new();
	readonly byte[][][] _valuePool;
	int _tick;

	public long HostBytesSent { get; private set; }
	public int HostMessagesSent { get; private set; }
	public long BytesSentThisTick { get; private set; }

	/// <summary>
	/// Every host to client payload, cluster ids zeroed, hashed in order. Equal hashes mean the same bytes went out.
	/// </summary>
	public ulong WireHash { get; private set; } = 1469598103934665603UL;

	public DeltaSnapshotSim( SimSettings settings )
	{
		Settings = settings;
		_random = new Random( settings.Seed );

		Host = new DeltaSnapshotSystem( null );
		HostConnection = new MockConnection( GuidFor( 0xFFFF, 0 ) );

		ClientConnections = new Connection[settings.Clients];
		ClientSystems = new DeltaSnapshotSystem[settings.Clients];
		ClientObjects = new Dictionary<Guid, SimObject>[settings.Clients];

		Host.FindSnapshotterOverride = id => HostLookup.GetValueOrDefault( id );
		Host.SendOverride = ( target, type, data, flags ) => HostSend( target, type, data );

		for ( var c = 0; c < settings.Clients; c++ )
		{
			var client = c;
			ClientConnections[c] = new MockConnection( GuidFor( 0xEEEE, c ) );
			ClientObjects[c] = new Dictionary<Guid, SimObject>();
			ClientSystems[c] = new DeltaSnapshotSystem( null );
			ClientSystems[c].FindSnapshotterOverride = id => ClientObjects[client].GetValueOrDefault( id );
			ClientSystems[c].SendOverride = ( target, type, data, flags ) => Enqueue( client, true, type, data );
		}

		// A fixed pool of values per slot so changing values doesn't allocate during measured ticks
		_valuePool = new byte[settings.SlotsPerObject][][];
		for ( var s = 0; s < settings.SlotsPerObject; s++ )
		{
			_valuePool[s] = new byte[64][];
			for ( var v = 0; v < 64; v++ )
			{
				var bytes = new byte[settings.ValueBytes];
				_random.NextBytes( bytes );
				_valuePool[s][v] = bytes;
			}
		}

		for ( var i = 0; i < settings.Objects; i++ )
			AddObject();
	}

	static Guid GuidFor( int kind, int index ) => new( index, (short)kind, 0, 1, 2, 3, 4, 5, 6, 7, 8 );

	int _nextObjectIndex;

	public SimObject AddObject()
	{
		var index = _nextObjectIndex++;
		var id = GuidFor( 1, index );
		var slots = Enumerable.Range( 0, Settings.SlotsPerObject ).Select( s => 1000 + s * 7919 ).ToArray();

		var obj = new SimObject( Host, id, slots );
		for ( var s = 0; s < slots.Length; s++ )
			obj.Values[s] = _valuePool[s][_random.Next( 64 )];

		HostObjects.Add( obj );
		HostLookup[id] = obj;

		for ( var c = 0; c < Settings.Clients; c++ )
			ClientObjects[c][id] = new SimObject( ClientSystems[c], id, slots );

		return obj;
	}

	public void RemoveObject( SimObject obj )
	{
		HostObjects.Remove( obj );
		HostLookup.Remove( obj.Id );

		for ( var c = 0; c < Settings.Clients; c++ )
			ClientObjects[c].Remove( obj.Id );
	}

	// Time spent in the simulated transport, taken out of the host's measured time
	long _transportTicks;

	void HostSend( Connection target, InternalMessageType type, ReadOnlySpan<byte> data )
	{
		var t0 = System.Diagnostics.Stopwatch.GetTimestamp();
		HostSendUntimed( target, type, data );
		_transportTicks += System.Diagnostics.Stopwatch.GetTimestamp() - t0;
	}

	void HostSendUntimed( Connection target, InternalMessageType type, ReadOnlySpan<byte> data )
	{
		HostMessagesSent++;
		HostBytesSent += data.Length;
		BytesSentThisTick += data.Length;

		var client = Array.IndexOf( ClientConnections, target );

		// Cluster ids come from a process wide counter, leave them out so runs compare
		var start = type == InternalMessageType.DeltaSnapshotCluster ? 2 : 0;
		var hash = WireHash;
		hash = (hash ^ (ulong)client) * 1099511628211UL;
		hash = (hash ^ (ulong)type) * 1099511628211UL;
		for ( var i = start; i < data.Length; i++ )
			hash = (hash ^ data[i]) * 1099511628211UL;
		WireHash = hash;

		Enqueue( client, false, type, data );
	}

	/// <summary>
	/// Optional trace of every message: tick, client, direction, type, payload, dropped.
	/// </summary>
	public Action<int, int, bool, InternalMessageType, byte[], bool> Trace { get; set; }

	public int CurrentTick => _tick;

	void Enqueue( int client, bool toHost, InternalMessageType type, ReadOnlySpan<byte> data )
	{
		var dropped = Settings.Loss > 0 && _random.NextDouble() < Settings.Loss;
		Trace?.Invoke( _tick, client, toHost, type, data.ToArray(), dropped );

		if ( dropped )
			return;

		// Pooled so the transport's copy doesn't count as the host's allocation
		var buffer = System.Buffers.ArrayPool<byte>.Shared.Rent( data.Length );
		data.CopyTo( buffer );
		_inFlight.Add( new Message( _tick + Settings.LatencyTicks, client, toHost, type, buffer, data.Length ) );
	}

	/// <summary>
	/// Change values, cull state and slots on the host according to the settings.
	/// </summary>
	public void Mutate()
	{
		foreach ( var obj in HostObjects )
		{
			if ( _random.NextDouble() < Settings.ChangeRate )
			{
				for ( var k = 0; k < Settings.SlotsChangedPerObject; k++ )
				{
					var s = _random.Next( obj.Slots.Length );
					obj.Values[s] = _valuePool[s][_random.Next( 64 )];
				}
			}

			if ( Settings.CullToggleRate > 0 && _random.NextDouble() < Settings.CullToggleRate )
			{
				var c = ClientConnections[_random.Next( ClientConnections.Length )];
				obj.SetCulled( c, !obj.Culled.Contains( c.Id ) );
			}

			if ( Settings.SlotToggleRate > 0 && _random.NextDouble() < Settings.SlotToggleRate )
			{
				var s = _random.Next( obj.Slots.Length );
				obj.Removed[s] = !obj.Removed[s];
			}
		}
	}

	public long LastSendTicks { get; private set; }
	public long LastHostReceiveTicks { get; private set; }
	public long LastHostAllocatedBytes { get; private set; }
	public long LastClientReceiveTicks { get; private set; }
	public long LastClientAllocatedBytes { get; private set; }

	/// <summary>
	/// One network tick: host sends, then everything due arrives. Host work is timed and its allocations counted.
	/// </summary>
	public void Tick( bool mutate = true )
	{
		if ( mutate )
			Mutate();

		BytesSentThisTick = 0;
		var now = _tick * Settings.TickSeconds;
		Host.Time = now;
		foreach ( var c in ClientSystems )
			c.Time = now;

		var objects = HostObjects.Cast<IDeltaSnapshot>().ToArray();

		_transportTicks = 0;
		var allocBefore = GC.GetAllocatedBytesForCurrentThread();
		var t0 = System.Diagnostics.Stopwatch.GetTimestamp();
		Host.Send( objects, ClientConnections );
		var t1 = System.Diagnostics.Stopwatch.GetTimestamp();
		var allocHost = GC.GetAllocatedBytesForCurrentThread() - allocBefore;
		LastSendTicks = t1 - t0 - _transportTicks;

		long hostReceive = 0, clientReceive = 0, clientAlloc = 0;

		for ( var i = 0; i < _inFlight.Count; i++ )
		{
			var m = _inFlight[i];
			if ( m.DeliverTick > _tick ) continue;

			_inFlight.RemoveAt( i-- );

			using var reader = ByteStream.CreateReader( m.Data.AsSpan( 0, m.Length ) );

			if ( m.ToHost )
			{
				var a0 = GC.GetAllocatedBytesForCurrentThread();
				var r0 = System.Diagnostics.Stopwatch.GetTimestamp();
				if ( m.Type == InternalMessageType.DeltaSnapshotClusterAck )
					Host.OnDeltaSnapshotClusterAck( ClientConnections[m.Client], reader );
				else if ( m.Type == InternalMessageType.DeltaSnapshotAck )
					Host.OnDeltaSnapshotAck( ClientConnections[m.Client], reader );
				hostReceive += System.Diagnostics.Stopwatch.GetTimestamp() - r0;
				allocHost += GC.GetAllocatedBytesForCurrentThread() - a0;
				System.Buffers.ArrayPool<byte>.Shared.Return( m.Data );
				continue;
			}

			var client = ClientSystems[m.Client];
			var ca = GC.GetAllocatedBytesForCurrentThread();
			var c0 = System.Diagnostics.Stopwatch.GetTimestamp();
			if ( m.Type == InternalMessageType.DeltaSnapshotCluster )
				client.OnDeltaSnapshotCluster( HostConnection, reader );
			else if ( m.Type == InternalMessageType.DeltaSnapshot )
				client.OnDeltaSnapshot( HostConnection, reader );
			clientReceive += System.Diagnostics.Stopwatch.GetTimestamp() - c0;
			clientAlloc += GC.GetAllocatedBytesForCurrentThread() - ca;

			System.Buffers.ArrayPool<byte>.Shared.Return( m.Data );
		}

		// Like SceneNetworkSystem after incoming messages each frame
		foreach ( var c in ClientSystems )
			c.FlushAcks();

		var tickStart = System.Diagnostics.Stopwatch.GetTimestamp();
		var a1 = GC.GetAllocatedBytesForCurrentThread();
		Host.Tick();
		hostReceive += System.Diagnostics.Stopwatch.GetTimestamp() - tickStart;
		allocHost += GC.GetAllocatedBytesForCurrentThread() - a1;

		LastHostReceiveTicks = hostReceive;
		LastHostAllocatedBytes = allocHost;
		LastClientReceiveTicks = clientReceive;
		LastClientAllocatedBytes = clientAlloc;

		_tick++;
	}

	public bool HasMessagesInFlight => _inFlight.Count > 0;

	/// <summary>
	/// A client drops and joins again with the same id, starting from nothing.
	/// </summary>
	public void ReconnectClient( int c )
	{
		var connection = ClientConnections[c];
		Host.RemoveConnection( connection );

		ClientSystems[c].Reset();
		_inFlight.RemoveAll( m => m.Client == c );

		foreach ( var obj in ClientObjects[c].Values )
			obj.Received.Clear();
	}

	/// <summary>
	/// Slots where a client that can see an object doesn't hold the host's current value.
	/// </summary>
	public List<string> Mismatches()
	{
		var result = new List<string>();

		foreach ( var obj in HostObjects )
		{
			for ( var c = 0; c < Settings.Clients; c++ )
			{
				if ( obj.Culled.Contains( ClientConnections[c].Id ) )
					continue;

				var client = ClientObjects[c][obj.Id];

				for ( var s = 0; s < obj.Slots.Length; s++ )
				{
					if ( obj.Removed[s] )
						continue;

					if ( !client.Received.TryGetValue( obj.Slots[s], out var got ) || !got.AsSpan().SequenceEqual( obj.Values[s] ) )
						result.Add( $"object {obj.Id} slot {obj.Slots[s]} client {c}" );
				}
			}
		}

		return result;
	}

	/// <summary>
	/// Stop changing things and tick until the network goes quiet, up to a limit.
	/// </summary>
	public int Settle( int maxTicks = 300 )
	{
		// Lost sends are only retried once their prediction expires (0.25 s), so quiet has to outlast that
		var needQuiet = (int)Math.Ceiling( 0.5f / Settings.TickSeconds ) + Settings.LatencyTicks * 2;
		var quietTicks = 0;

		for ( var i = 0; i < maxTicks; i++ )
		{
			Tick( mutate: false );

			quietTicks = BytesSentThisTick == 0 && !HasMessagesInFlight ? quietTicks + 1 : 0;
			if ( quietTicks >= needQuiet )
				return i;
		}

		return -1;
	}
}
