using Sandbox.Engine;
using Sandbox.Network;
using Sandbox.Utility;
using System.Collections.Concurrent;
using System.Threading;

namespace Sandbox;

/// <summary>
/// A complete in-process game client - the engine side of the editor's "docked client" tabs.
/// Owns its own <see cref="NetworkSystem"/>, <see cref="SceneNetworkSystem"/>, local
/// <see cref="Connection"/> and client <see cref="Scene"/>, connected to the in-editor host
/// through an in-memory transport and driven through the REAL connection handshake
/// (ServerInfo → UserInfo → Welcome → Snapshot → Activate), so game code sees a genuine
/// multiplayer client.
///
/// The engine's networking state is global (Networking.System, SceneNetworkSystem.Instance,
/// Connection.Local, Game.ActiveScene) - the same pattern the integration tests multiplex with
/// ClientAndHost. Each session tick swaps those globals to this client's world, pumps its
/// networking and scene simulation, then swaps back, SAVING what the handlers wrote (the
/// handshake legitimately replaces Connection.Local and Game.ActiveScene mid-slice). Async
/// continuations started inside a slice are captured by a per-session SynchronizationContext
/// and pumped inside later slices, so they also run under this client's context.
///
/// Everything is main-thread only, in-process, sharing loaded assemblies and resources with
/// the host - which is what makes joining instant compared to spawning an sbox.exe.
/// </summary>
internal sealed class InProcessClientSession : IDisposable
{
	/// <summary>
	/// All live sessions, in creation order.
	/// </summary>
	public static List<InProcessClientSession> All { get; } = new();

	/// <summary>
	/// The "Client N" number - lowest free number at creation time.
	/// </summary>
	public int Number { get; }

	/// <summary>
	/// The fake player name this client presents to the host.
	/// </summary>
	public string PlayerName { get; }

	/// <summary>
	/// This client's network system. Distinct from the host's Networking.System.
	/// </summary>
	public NetworkSystem System { get; }

	/// <summary>
	/// This client's game scene. Null until the snapshot has been applied.
	/// </summary>
	public Scene Scene => _scene;

	/// <summary>
	/// True once the handshake completed and the client is live in the game.
	/// </summary>
	public bool IsConnected => !_disposed && _localConnection?.State == Connection.ChannelState.Connected;

	/// <summary>
	/// True when the session is dead (disposed, disconnected or kicked) and its tab should close.
	/// </summary>
	public bool IsDefunct => _disposed || System.IsDisconnected;

	/// <summary>
	/// This client's blank input context, pushed while the session ticks WITHOUT input focus,
	/// so its pawns get neutral input instead of mirroring the host player's keys.
	/// </summary>
	internal Input.Context InputContext { get; }

	/// <summary>
	/// The session that currently has input focus (its editor tab is focused). It ticks under
	/// the REAL per-frame input context, so its pawn is driven by the player's actual input,
	/// while the host's game tick is muted (see <see cref="MuteHostInput"/>). Null when the
	/// host has input as normal.
	/// </summary>
	public static InProcessClientSession Focused { get; set; }

	// A context that is never fed or flipped - all zeros, forever.
	static Input.Context _muteContext;

	/// <summary>
	/// Push a permanently-blank input context AND zero the analog statics. Used by the host's
	/// game tick while a docked client has input focus, so the host pawn stops responding
	/// while the player drives the focused client. Dispose restores everything.
	/// </summary>
	public static IDisposable MuteHostInput()
	{
		_muteContext ??= Input.Context.Create( "InProcessClientMute" );

		var contextScope = _muteContext.Push();
		var analogScope = ZeroAnalogInput();

		return new DisposeAction( () =>
		{
			analogScope.Dispose();
			contextScope.Dispose();
		} );
	}

	/// <summary>
	/// Neutralize the input statics that are NOT backed by the input context: AnalogMove and
	/// AnalogLook are recomputed by Input.Process once per frame as plain statics, so a blank
	/// context alone doesn't stop pawns from reading the player's WASD/mouse-look. Without
	/// this, every scene ticking in the process moves with the same input.
	/// </summary>
	static IDisposable ZeroAnalogInput()
	{
		var look = Input.AnalogLook;
		var move = Input.AnalogMove;

		Input.AnalogLook = default;
		Input.AnalogMove = default;

		return new DisposeAction( () =>
		{
			Input.AnalogLook = look;
			Input.AnalogMove = move;
		} );
	}

	// The tenant's view of the process-wide networking globals, saved between slices.
	Scene _scene;
	Connection _localConnection;
	SceneNetworkSystem _gameSystem;

	readonly InProcessConnection _clientEndpoint;
	readonly InProcessConnection _hostEndpoint;
	readonly InProcessSocket _hostSocket;
	readonly QueuedSyncContext _syncContext = new();

	bool _insideScope;
	bool _disposed;
	bool _warnedDirtyInput;

	// Offset well past external -joinlocal instances (they use BaseFakeSteamId + instanceid).
	const ulong FakeSteamIdBase = 1000;

	/// <summary>
	/// Create an in-process client and start its handshake with the current host.
	/// Must be called on the main thread while hosting is active.
	/// </summary>
	public static InProcessClientSession Create( string name = null )
	{
		ThreadSafe.AssertIsMainThread();

		var host = Networking.System;
		Assert.NotNull( host, "Can't add an in-process client - networking is not active" );
		Assert.True( host.IsHost, "Can't add an in-process client - we are not the host" );

		var socket = host.Sockets.OfType<InProcessSocket>().FirstOrDefault();
		if ( socket is null )
		{
			socket = new InProcessSocket();
			host.AddSocket( socket );
		}

		var session = new InProcessClientSession( socket, name );
		All.Add( session );

		// We are currently under the HOST context - accepting starts the handshake, which
		// sends ServerInfo (host state) into the client's inbox.
		socket.Accept( session._hostEndpoint );

		return session;
	}

	InProcessClientSession( InProcessSocket hostSocket, string name )
	{
		_hostSocket = hostSocket;

		var number = 1;
		while ( All.Any( x => x.Number == number ) )
			number++;

		Number = number;
		PlayerName = string.IsNullOrWhiteSpace( name ) ? $"Client {number}" : name;

		SteamId fakeSteamId = Utility.Steam.BaseFakeSteamId + FakeSteamIdBase + (ulong)number;

		(_hostEndpoint, _clientEndpoint) = InProcessConnection.CreatePair( PlayerName, fakeSteamId );

		System = new NetworkSystem( $"local-client-{number}", Engine.IGameInstanceDll.Current?.TypeLibrary ?? Internal.GlobalGameNamespace.TypeLibrary )
		{
			IsInProcessClient = true
		};

		System.Connect( _clientEndpoint );

		// The handshake replaces this with the host-assigned identity when ServerInfo arrives.
		_localConnection = new LocalConnection( Guid.NewGuid() );

		InputContext = Input.Context.Create( $"InProcessClient{number}" );
	}

	/// <summary>
	/// Swap the process-wide networking globals to this client's world. Dispose swaps back,
	/// saving anything the client's handlers replaced in the meantime.
	/// </summary>
	public IDisposable Push()
	{
		ThreadSafe.AssertIsMainThread();
		Assert.False( _insideScope, "InProcessClientSession scope is not re-entrant" );
		_insideScope = true;

		var savedSystem = Networking.System;
		var savedInstance = SceneNetworkSystem.Instance;
		var savedLocal = Connection.Local;
		var savedScene = Game.ActiveScene;
		var savedTimeNow = Time.NowDouble;
		var savedTimeDelta = (double)Time.Delta;
		var savedSyncContext = SynchronizationContext.Current;

		Networking.System = System;
		SceneNetworkSystem.Instance = _gameSystem;
		Connection.Local = _localConnection;
		Game.ActiveScene = _scene;
		SynchronizationContext.SetSynchronizationContext( _syncContext );

		return new DisposeAction( () =>
		{
			// Save back what this client's handlers wrote during the slice.
			_gameSystem = SceneNetworkSystem.Instance ?? System.GameSystem as SceneNetworkSystem;
			_localConnection = Connection.Local;
			_scene = Game.ActiveScene;

			Networking.System = savedSystem;
			SceneNetworkSystem.Instance = savedInstance;
			Connection.Local = savedLocal;
			Game.ActiveScene = savedScene;
			Time.Update( savedTimeNow, savedTimeDelta );
			SynchronizationContext.SetSynchronizationContext( savedSyncContext );

			_insideScope = false;
		} );
	}

	/// <summary>
	/// Advance this client one frame: pump its networking (handshake and game messages run
	/// under its context) and tick its scene simulation. Mirrors the per-frame flow of
	/// GameInstanceDll.Tick for a real client.
	/// </summary>
	public void Tick()
	{
		if ( _disposed )
			return;

		ThreadSafe.AssertIsMainThread();

		using var _ = Push();

		try
		{
			TickInternal();
		}
		catch ( Exception e )
		{
			Log.Warning( e, $"In-process client {Number} tick error: {e.Message}" );
		}
	}

	void TickInternal()
	{
		// Continuations captured in earlier slices (scene loading etc).
		_syncContext.Pump();

		// Input routing: the focused session ticks under the AMBIENT context - the real
		// per-frame input, so its pawn is driven by the player (the host's tick is muted, see
		// MuteHostInput). Unfocused sessions push their own blank context so their pawns idle.
		using var inputScope = Focused == this ? null : PushBlankInput();

		var scene = Game.ActiveScene;

		if ( scene.IsValid() )
		{
			using var sceneScope = scene.Push();

			scene.UpdateTime( RealTime.Delta );
			scene.SyncServerTime();
			Time.Update( scene.TimeNow, scene.TimeDelta );

			PumpNetwork();

			// The pump can replace the scene (scene-change message) - only tick if it's still current.
			if ( Game.ActiveScene == scene && scene.IsValid() && !System.IsConnecting && !scene.IsLoading )
			{
				scene.GameTick( 0 ); // time already advanced above
			}
		}
		else
		{
			// Still handshaking - no scene yet.
			PumpNetwork();
		}

		_syncContext.Pump();
	}

	IDisposable PushBlankInput()
	{
		var contextScope = InputContext.Push();

		// Device input is accumulated into EVERY Input.Context (see Input.AddMouseMovement
		// and the action press paths) - a private context is NOT naturally blank. Discard
		// whatever accumulated since last tick so the Flip below publishes truly neutral
		// input; otherwise every unfocused client's pawn jumps when the host player jumps.
		InputContext.AccumActionsPressed = 0;
		InputContext.AccumActionsReleased = 0;
		InputContext.AccumKeysPressed.Clear();
		InputContext.AccumKeysReleased.Clear();
		InputContext.AccumMouseDelta = default;
		InputContext.AccumMouseWheel = default;

		InputContext.Flip();

		// Invariant: a cleared context must publish no actions. If this fires, input
		// isolation is broken and unfocused clients will mirror the player's buttons.
		if ( InputContext.ActionsCurrent != 0 && !_warnedDirtyInput )
		{
			_warnedDirtyInput = true;
			Log.Warning( $"In-process client {Number}: blank input context published actions {InputContext.ActionsCurrent:X} - input isolation failure" );
		}

		// The context only covers buttons/actions/mouse - analog statics leak separately.
		var analogScope = ZeroAnalogInput();

		return new DisposeAction( () =>
		{
			analogScope.Dispose();
			contextScope.Dispose();
		} );
	}

	void PumpNetwork()
	{
		System.Tick();
		System.SendTableUpdates();
		_syncContext.Pump();
	}

	/// <summary>
	/// Disconnect and destroy this client. The host is notified through the normal disconnect
	/// path, so INetworkListener.OnDisconnected and orphaned-object cleanup run as usual.
	/// </summary>
	public void Dispose()
	{
		if ( _disposed )
			return;

		_disposed = true;
		All.Remove( this );

		if ( Focused == this )
			Focused = null;

		ThreadSafe.AssertIsMainThread();

		// Tear down the client side under ITS context.
		using ( Push() )
		{
			try
			{
				if ( !System.IsDisconnected )
					System.Disconnect();

				_scene?.Destroy();
				Game.ActiveScene = null;
			}
			catch ( Exception e )
			{
				Log.Warning( e, $"In-process client {Number} shutdown error: {e.Message}" );
			}
		}

		_scene = null;
		_gameSystem = null;

		// Notify the host under the HOST context (we are outside the scope again) so
		// OnLeave cleanup runs against the host's scene.
		try
		{
			_hostSocket.Disconnect( _hostEndpoint );
		}
		catch ( Exception e )
		{
			Log.Warning( e, $"In-process client {Number} host-side disconnect error: {e.Message}" );
		}
	}

	/// <summary>
	/// Captures async continuations started during this session's slices so they can be run
	/// inside later slices, under this session's context, instead of leaking into whatever
	/// context the main thread happens to be in when they'd otherwise resume.
	/// </summary>
	sealed class QueuedSyncContext : SynchronizationContext
	{
		readonly ConcurrentQueue<(SendOrPostCallback Callback, object State)> _queue = new();

		public override void Post( SendOrPostCallback d, object state )
		{
			_queue.Enqueue( (d, state) );
		}

		public override void Send( SendOrPostCallback d, object state )
		{
			// Main-thread-only feature; a synchronous Send can just run inline.
			d( state );
		}

		public void Pump()
		{
			// Bounded drain: a continuation that posts again runs next pump, not forever now.
			var count = _queue.Count;

			while ( count-- > 0 && _queue.TryDequeue( out var item ) )
			{
				try
				{
					item.Callback( item.State );
				}
				catch ( Exception e )
				{
					Log.Warning( e, $"In-process client continuation error: {e.Message}" );
				}
			}
		}
	}
}
