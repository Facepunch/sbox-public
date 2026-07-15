using Sandbox.Engine;
using Sandbox.Network;
using Sandbox.Utility;
using System.Collections.Concurrent;
using System.Threading;

namespace Sandbox;

/// <summary>
/// A complete in-process game client - the engine side of the editor's "docked client" tabs.
/// Owns its own <see cref="NetworkSystem"/>, local <see cref="Connection"/> and client
/// <see cref="Scene"/>, connected to the in-editor host through an in-memory transport and
/// driven through the real connection handshake. The engine's networking state is global,
/// so each tick swaps those globals to this client's world and back (see <see cref="Push"/>).
/// Main-thread only.
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
	/// This client's isolated world: its own GlobalContext and private copies of the game
	/// assemblies. Falls back to shared mode if isolation couldn't be built.
	/// </summary>
	internal InProcessTenant Tenant { get; }

	/// <summary>
	/// True when this session runs private copies of the game assemblies (isolated statics).
	/// </summary>
	public bool IsIsolated => Tenant.IsIsolated;

	/// <summary>
	/// This client's game scene. Null until the snapshot has been applied.
	/// </summary>
	public Scene Scene => Tenant.IsIsolated ? Tenant.Context?.ActiveScene : _scene;

	/// <summary>
	/// The host's game code changed (hotload), so this session's private assembly copies are
	/// stale - the owner should dispose it and create a fresh session.
	/// </summary>
	public bool NeedsRebuild { get; private set; }

	/// <summary>
	/// Flag every isolated session for rebuild. Shared-mode sessions hotload with the host.
	/// </summary>
	public static void NotifyHostCodeChanged()
	{
		foreach ( var session in All )
		{
			if ( session.IsIsolated )
				session.NeedsRebuild = true;
		}
	}

	/// <summary>
	/// Find a resource in this session's tenant resource system, for engine callbacks that
	/// fire outside any context scope.
	/// </summary>
	internal Resource FindTenantResource( string resourceName ) => Tenant.FindResource( resourceName );

	/// <summary>
	/// True once the handshake completed and the client is live in the game.
	/// </summary>
	public bool IsConnected => !_disposed && _localConnection?.State == Connection.ChannelState.Connected;

	/// <summary>
	/// True when the session is dead (disposed, disconnected or kicked) and its tab should close.
	/// </summary>
	public bool IsDefunct => _disposed || System.IsDisconnected;

	/// <summary>
	/// This client's input context, pushed blank while the session ticks without input focus
	/// so its pawns get neutral input instead of mirroring the host player's keys.
	/// </summary>
	internal Input.Context InputContext { get; }

	/// <summary>
	/// The pixel size of the view this client renders into, fed by the editor widget every
	/// frame. <see cref="Screen"/> reports this size while the session ticks.
	/// </summary>
	public Vector2 ViewSize { get; set; }

	/// <summary>
	/// The session whose slice is currently executing on the main thread, for engine systems
	/// that need to attribute work to a session (e.g. sounds route to its submix).
	/// </summary>
	public static InProcessClientSession CurrentSlice { get; private set; }

	/// <summary>
	/// This session's private audio submix under Master. Volume follows tab focus.
	/// Null until first used, or when there's no master mixer (headless tests).
	/// </summary>
	public Audio.Mixer ClientMixer
	{
		get
		{
			if ( _clientMixer is null && Audio.Mixer.Master is not null && !_disposed )
			{
				_clientMixer = Audio.Mixer.Master.AddChild();
				_clientMixer.Name = $"Client {Number}";
				_clientMixer.Volume = Focused == this ? 1f : 0f;
			}

			return _clientMixer;
		}
	}

	Audio.Mixer _clientMixer;

	static InProcessClientSession _focused;

	/// <summary>
	/// The session that currently has input focus. It ticks under the real per-frame input
	/// while the host's game tick is muted (see <see cref="MuteHostInput"/>), and its audio
	/// submix is the only unmuted one. Null when the host has input as normal.
	/// </summary>
	public static InProcessClientSession Focused
	{
		get => _focused;
		set
		{
			_focused = value;

			foreach ( var session in All )
			{
				if ( session._clientMixer is not null )
					session._clientMixer.Volume = session == value ? 1f : 0f;
			}
		}
	}

	// A context that is never fed or flipped - all zeros, forever.
	static Input.Context _muteContext;

	/// <summary>
	/// Push a permanently-blank input context and zero the analog statics, so the host pawn
	/// stops responding while the player drives a focused client. Dispose restores everything.
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
	/// AnalogMove/AnalogLook are plain statics recomputed once per frame, not backed by the
	/// input context - a blank context alone doesn't stop pawns from reading them.
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
	/// <paramref name="gameAssemblies"/> overrides the tenant's assembly source (tests).
	/// </summary>
	public static InProcessClientSession Create( string name = null, IReadOnlyList<(string Name, byte[] Bytes)> gameAssemblies = null )
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

		var session = new InProcessClientSession( socket, name, gameAssemblies );
		All.Add( session );

		socket.Accept( session._hostEndpoint );

		return session;
	}

	InProcessClientSession( InProcessSocket hostSocket, string name, IReadOnlyList<(string Name, byte[] Bytes)> gameAssemblies = null )
	{
		_hostSocket = hostSocket;

		var number = 1;
		while ( All.Any( x => x.Number == number ) )
			number++;

		Number = number;
		PlayerName = string.IsNullOrWhiteSpace( name ) ? $"Client {number}" : name;

		Tenant = InProcessTenant.Create( $"InProcessClient{number}", gameAssemblies );

		SteamId fakeSteamId = Utility.Steam.BaseFakeSteamId + FakeSteamIdBase + (ulong)number;

		(_hostEndpoint, _clientEndpoint) = InProcessConnection.CreatePair( PlayerName, fakeSteamId );

		System = new NetworkSystem( $"local-client-{number}", Tenant.TypeLibrary )
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
	/// saving anything the client's handlers replaced in the meantime (the handshake
	/// legitimately replaces Connection.Local and Game.ActiveScene mid-slice).
	/// </summary>
	public IDisposable Push()
	{
		ThreadSafe.AssertIsMainThread();
		Assert.False( _insideScope, "InProcessClientSession scope is not re-entrant" );
		_insideScope = true;

		var savedSystem = Networking.System;
		var savedInstance = SceneNetworkSystem.Instance;
		var savedLocal = Connection.Local;
		var savedTimeNow = Time.NowDouble;
		var savedTimeDelta = (double)Time.Delta;
		var savedSyncContext = SynchronizationContext.Current;
		var savedScreenSize = Screen.Size;

		if ( ViewSize.x >= 1f && ViewSize.y >= 1f )
		{
			Screen.Size = ViewSize;
		}

		// Isolated mode: the tenant context carries ActiveScene. Shared mode: swap it by hand.
		var contextScope = Tenant.Push();
		Scene savedScene = null;

		if ( !Tenant.IsIsolated )
		{
			savedScene = Game.ActiveScene;
			Game.ActiveScene = _scene;
		}

		Networking.System = System;
		SceneNetworkSystem.Instance = _gameSystem;
		Connection.Local = _localConnection;
		SynchronizationContext.SetSynchronizationContext( _syncContext );

		var savedSlice = CurrentSlice;
		CurrentSlice = this;

		return new DisposeAction( () =>
		{
			CurrentSlice = savedSlice;

			_gameSystem = SceneNetworkSystem.Instance ?? System.GameSystem as SceneNetworkSystem;
			_localConnection = Connection.Local;

			if ( !Tenant.IsIsolated )
			{
				_scene = Game.ActiveScene;
				Game.ActiveScene = savedScene;
			}

			Networking.System = savedSystem;
			SceneNetworkSystem.Instance = savedInstance;
			Connection.Local = savedLocal;
			Time.Update( savedTimeNow, savedTimeDelta );
			SynchronizationContext.SetSynchronizationContext( savedSyncContext );
			Screen.Size = savedScreenSize;

			contextScope?.Dispose();

			_insideScope = false;
		} );
	}

	/// <summary>
	/// Advance this client one frame: pump its networking and tick its scene simulation.
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
		_syncContext.Pump();

		// The focused session ticks under the real input; unfocused sessions push a blank context so their pawns idle.
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

			// Only the focused world's UI system processes input this frame - hover/capture state is process-global.
			if ( Tenant.IsIsolated && Tenant.Context?.UISystem is { } uiSystem )
			{
				if ( Focused == this )
				{
					uiSystem.Simulate( allowMouseInput: true );
				}
				else
				{
					uiSystem.SimulateNoInput();
				}
			}
		}
		else
		{
			PumpNetwork();
		}

		_syncContext.Pump();
	}

	IDisposable PushBlankInput()
	{
		var contextScope = InputContext.Push();

		InputContext.ClearAccumulated();
		InputContext.Flip();

		if ( InputContext.ActionsCurrent != 0 && !_warnedDirtyInput )
		{
			_warnedDirtyInput = true;
			Log.Warning( $"In-process client {Number}: blank input context published actions {InputContext.ActionsCurrent:X} - input isolation failure" );
		}

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

		// Tear down the client side under ITS context, so nothing roots the tenant's collectible assemblies.
		using ( Push() )
		{
			try
			{
				if ( !System.IsDisconnected )
					System.Disconnect();

				System.GameSystem?.Dispose();
				System.GameSystem = null;

				Game.ActiveScene?.Destroy();
				Game.ActiveScene = null;
			}
			catch ( Exception e )
			{
				Log.Warning( e, $"In-process client {Number} shutdown error: {e.Message}" );
			}
		}

		// Pending continuations close over scene objects - they must never run after disposal.
		_syncContext.Clear();

		_scene = null;
		_gameSystem = null;

		// Notify the host outside the scope, so OnLeave cleanup runs against the host's scene.
		try
		{
			_hostSocket.Disconnect( _hostEndpoint );
		}
		catch ( Exception e )
		{
			Log.Warning( e, $"In-process client {Number} host-side disconnect error: {e.Message}" );
		}

		try
		{
			if ( _clientMixer is not null )
			{
				SoundHandle.StopAll( 0f, _clientMixer );
				_clientMixer.Destroy();
				_clientMixer = null;
			}
		}
		catch ( Exception e )
		{
			Log.Warning( e, $"In-process client {Number} mixer teardown error: {e.Message}" );
		}

		Tenant.Dispose();
	}

	/// <summary>
	/// Captures async continuations started during this session's slices, so they run inside
	/// later slices under this session's context instead of whatever context the main thread
	/// happens to be in.
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

		public void Clear()
		{
			while ( _queue.TryDequeue( out _ ) )
			{
			}
		}
	}
}
