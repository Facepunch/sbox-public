using Sandbox.Engine;
using Sandbox.Internal;
using Sandbox.Network;

namespace NetSim;

internal record NetSimSettings
{
	public int Seed { get; init; } = 1;

	/// <summary>
	/// Frames per second every peer runs at.
	/// </summary>
	public float FrameRate { get; init; } = 60;

	/// <summary>
	/// How much each frame's length varies around the average, as a fraction of it, like a real frame rate.
	/// </summary>
	public float FrameTimeJitter { get; init; }

	public SimLinkSettings Link { get; init; } = new();

	/// <summary>
	/// Clients also connect to each other directly, like a Steam lobby. Otherwise everything goes through the
	/// host, like a dedicated server.
	/// </summary>
	public bool Mesh { get; init; }
}

/// <summary>
/// A host and any number of clients, each a full game network stack with its own scene, talking through a
/// simulated transport on a simulated clock. Clients join through the real handshake.
/// </summary>
internal sealed class NetSimulation : IDisposable
{
	public NetSimSettings Settings { get; }
	public SimTransport Transport { get; }
	public SimPeer Host { get; }
	public List<SimPeer> Clients { get; } = new();
	public SimGameInstance GameInstance { get; }
	public double Now { get; private set; } = 1000;
	public float FrameTime { get; private set; }
	public int Frame { get; private set; }

	internal static NetSimulation Current { get; private set; }

	public IEnumerable<SimPeer> Peers => Clients.Prepend( Host );

	// Ids end up in the traffic and change how it compresses, so they come from the seed like everything else
	readonly Random _ids;
	readonly Random _frames;

	public Guid NewId()
	{
		Span<byte> bytes = stackalloc byte[16];
		_ids.NextBytes( bytes );
		return new Guid( bytes );
	}

	readonly IGameInstanceDll _previousGame;
	readonly TypeLibrary _typeLibrary;

	public NetSimulation( NetSimSettings settings = null )
	{
		Settings = settings ?? new NetSimSettings();
		Transport = new SimTransport( this, Settings.Seed ) { Settings = Settings.Link };
		_ids = new Random( Settings.Seed );
		_frames = new Random( Settings.Seed );
		FrameTime = 1f / Settings.FrameRate;

		RealTime.SimulatedNow = Now;
		Current = this;

		_typeLibrary = Sandbox.Internal.GlobalGameNamespace.TypeLibrary;
		_previousGame = IGameInstanceDll.Current;
		GameInstance = new SimGameInstance( _typeLibrary );
		IGameInstanceDll.Current = GameInstance;

		Host = CreatePeer( "host", true );

		using ( Host.Enter() )
		{
			Host.System.AddSocket( Host.Socket );
			Host.System.InitializeHost();
		}
	}

	SimPeer CreatePeer( string name, bool isHost )
	{
		var peer = new SimPeer( name, isHost, NewId() );
		peer.Scene = new Scene();

		using ( peer.Enter() )
		{
			peer.System = new NetworkSystem( name, _typeLibrary );
			Networking.System = peer.System;

			peer.SceneSystem = new SceneNetworkSystem( _typeLibrary, peer.System );
			peer.System.GameSystem = peer.SceneSystem;

			if ( !isHost && Settings.Mesh )
				peer.System.AddSocket( peer.Socket );
		}

		return peer;
	}

	/// <summary>
	/// Start a client joining. It goes through the handshake over the following frames.
	/// </summary>
	public SimPeer AddClient()
	{
		var peer = CreatePeer( $"client{Clients.Count + 1}", false );

		var hostSide = new SimConnection( Transport, Host, peer );
		var clientSide = new SimConnection( Transport, peer, Host );
		hostSide.Remote = clientSide;
		clientSide.Remote = hostSide;
		peer.ToHost = clientSide;

		using ( peer.Enter() )
		{
			peer.System.Connect( clientSide );
		}

		using ( Host.Enter() )
		{
			Host.Socket.Accept( hostSide );
		}

		Clients.Add( peer );
		return peer;
	}

	/// <summary>
	/// Add a client and run frames until its handshake is done.
	/// </summary>
	public SimPeer Join( int maxFrames = 600 )
	{
		var peer = AddClient();

		for ( var i = 0; i < maxFrames && !peer.IsConnected; i++ )
			Step();

		peer.JoinedFrame = Frame;

		if ( !peer.IsConnected )
			throw new Exception( $"{peer} didn't finish joining: {peer.Local.State}. {string.Join( "; ", GameInstance.Disconnects )}" );

		if ( Settings.Mesh )
		{
			foreach ( var other in Clients )
			{
				if ( other != peer && other.IsConnected )
					LinkPeers( peer, other );
			}
		}

		return peer;
	}

	/// <summary>
	/// Connect two clients directly, each knowing the other by the id the host gave it.
	/// </summary>
	internal void LinkPeers( SimPeer a, SimPeer b )
	{
		var aToB = new SimConnection( Transport, a, b, b.Local.Id );
		var bToA = new SimConnection( Transport, b, a, a.Local.Id );
		aToB.Remote = bToA;
		bToA.Remote = aToB;

		As( a, () => a.Socket.Accept( aToB ) );
		As( b, () => b.Socket.Accept( bToA ) );
	}

	/// <summary>
	/// Advance the clock by one frame: deliver what's due, then run a frame on every peer.
	/// </summary>
	public void Step()
	{
		Frame++;
		FrameTime = 1f / Settings.FrameRate * (1 + Settings.FrameTimeJitter * (float)(_frames.NextDouble() * 2 - 1));
		Now += FrameTime;
		RealTime.SimulatedNow = Now;

		Transport.DeliverDue();

		if ( !Host.Paused )
			RunFrame( Host );

		foreach ( var client in Clients )
		{
			if ( !client.Paused )
				RunFrame( client );
		}
	}

	public void Run( int frames, Action<int> beforeFrame = null )
	{
		for ( var i = 0; i < frames; i++ )
		{
			beforeFrame?.Invoke( i );
			Step();
		}
	}

	public void RunSeconds( float seconds ) => Run( (int)MathF.Ceiling( seconds * Settings.FrameRate ) );

	/// <summary>
	/// What the engine does for a game instance each frame, minus Steam.
	/// </summary>
	void RunFrame( SimPeer peer )
	{
		using var scope = peer.Enter();

		peer.Sent.SentThisFrame.Clear();
		var transportBefore = Transport.TransportTicks;
		var allocBefore = GC.GetAllocatedBytesForCurrentThread();
		var t0 = System.Diagnostics.Stopwatch.GetTimestamp();

		peer.Sync.Drain();

		var system = peer.System;
		WaitForSnapshotEncoding( system );
		system.Tick();
		system.SendTableUpdates();
		system.SendHeartbeat();
		system.SendHostStats();

		// Joining replaces the scene with the host's snapshot
		Game.ActiveScene?.GameTick( FrameTime );

		system.SendTableUpdates();

		peer.FrameTicks += System.Diagnostics.Stopwatch.GetTimestamp() - t0 - (Transport.TransportTicks - transportBefore);
		peer.FrameAllocatedBytes += GC.GetAllocatedBytesForCurrentThread() - allocBefore;
	}

	static readonly System.Reflection.FieldInfo PendingSends = typeof( Connection ).GetField( "pendingSends", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic );

	// Snapshots encode on worker threads; finishing them here makes the frame they go out in the same every run
	static void WaitForSnapshotEncoding( NetworkSystem system )
	{
		foreach ( var connection in system.Connections )
		{
			if ( !connection.HasPendingSends ) continue;

			foreach ( var pending in (System.Collections.IEnumerable)PendingSends.GetValue( connection ) )
			{
				var task = (Task)pending.GetType().GetField( "Item1" ).GetValue( pending );
				((IAsyncResult)task).AsyncWaitHandle.WaitOne();
			}
		}
	}

	/// <summary>
	/// Run something as a peer, between frames.
	/// </summary>
	public T As<T>( SimPeer peer, Func<T> action )
	{
		using var scope = peer.Enter();
		return action();
	}

	public void As( SimPeer peer, Action action )
	{
		using var scope = peer.Enter();
		action();
	}

	public void ResetCounters()
	{
		foreach ( var peer in Peers )
		{
			peer.FrameTicks = 0;
			peer.FrameAllocatedBytes = 0;
			peer.Sent.Clear();
		}
	}

	public void Dispose()
	{
		foreach ( var peer in Peers.Reverse() )
		{
			using ( peer.Enter() )
			{
				peer.Scene?.Destroy();
			}
		}

		IGameInstanceDll.Current = _previousGame;
		if ( Current == this ) Current = null;
		RealTime.SimulatedNow = null;
	}
}
