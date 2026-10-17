using Sandbox.Network;
using System.Threading;

namespace NetSim;

/// <summary>
/// Runs async continuations on the peer that started them, during that peer's frame.
/// </summary>
internal sealed class SimSyncContext : SynchronizationContext
{
	readonly Queue<(SendOrPostCallback Callback, object State)> _queue = new();

	public override void Post( SendOrPostCallback d, object state )
	{
		lock ( _queue ) _queue.Enqueue( (d, state) );
	}

	public override void Send( SendOrPostCallback d, object state ) => d( state );

	public override SynchronizationContext CreateCopy() => this;

	public int Pending
	{
		get { lock ( _queue ) return _queue.Count; }
	}

	public void Drain()
	{
		while ( true )
		{
			(SendOrPostCallback Callback, object State) item;
			lock ( _queue )
			{
				if ( !_queue.TryDequeue( out item ) )
					return;
			}

			item.Callback( item.State );
		}
	}
}

/// <summary>
/// One game instance in the simulation: its own network system, scene network system and scene.
/// Networking state is process wide, so a peer is entered before anything runs on its behalf.
/// </summary>
internal sealed class SimPeer
{
	public string Name { get; }
	public bool IsHost { get; }
	public NetworkSystem System { get; set; }
	public SceneNetworkSystem SceneSystem { get; set; }
	public Scene Scene { get; set; }
	public Connection Local { get; set; }
	public SimSocket Socket { get; } = new();
	public SimSyncContext Sync { get; } = new();
	public SimTrafficStats Sent { get; } = new();

	/// <summary>
	/// The connection this peer uses to reach the host. Null on the host.
	/// </summary>
	public SimConnection ToHost { get; set; }

	/// <summary>
	/// The frame this peer finished joining.
	/// </summary>
	public int JoinedFrame;

	/// <summary>
	/// Skips its frames while set, like a hitch or a stalled process.
	/// </summary>
	public bool Paused;

	public long FrameTicks;
	public long FrameAllocatedBytes;

	public SimPeer( string name, bool isHost, Guid id )
	{
		Name = name;
		IsHost = isHost;
		Local = new LocalConnection( id );
	}

	public bool IsConnected => IsHost || Local.State == Connection.ChannelState.Connected;

	public PeerScope Enter() => new( this );

	public override string ToString() => Name;

	internal static SimPeer Current { get; private set; }

	internal struct PeerScope : IDisposable
	{
		readonly SimPeer _peer;
		readonly SimPeer _previousPeer;
		readonly NetworkSystem _previousSystem;
		readonly SceneNetworkSystem _previousSceneSystem;
		readonly Connection _previousLocal;
		readonly SynchronizationContext _previousSync;
		readonly Scene _previousScene;
		ScenePushScope _scene;

		public PeerScope( SimPeer peer )
		{
			_peer = peer;
			_previousPeer = Current;
			_previousSystem = Networking.System;
			_previousSceneSystem = SceneNetworkSystem.Instance;
			_previousLocal = Connection.Local;
			_previousSync = SynchronizationContext.Current;
			_previousScene = Game.ActiveScene;

			Current = peer;
			Networking.System = peer.System;
			SceneNetworkSystem.Instance = peer.SceneSystem;
			Connection.Local = peer.Local;
			SynchronizationContext.SetSynchronizationContext( peer.Sync );
			_scene = peer.Scene is null ? default : peer.Scene.PushScope();
		}

		public void Dispose()
		{
			// Loading a snapshot replaces the active scene
			var active = Game.ActiveScene;
			if ( active.IsValid() && active != _peer.Scene )
			{
				_peer.Scene = active;
				Game.ActiveScene = _previousScene;
			}

			_scene.Dispose();

			// The handshake replaces the local connection with the id the host assigned
			_peer.Local = Connection.Local;

			Current = _previousPeer;
			Networking.System = _previousSystem;
			SceneNetworkSystem.Instance = _previousSceneSystem;
			Connection.Local = _previousLocal;
			SynchronizationContext.SetSynchronizationContext( _previousSync );
		}
	}
}
