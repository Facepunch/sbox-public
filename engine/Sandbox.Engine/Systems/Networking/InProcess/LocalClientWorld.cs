using Facepunch.ActionGraphs;
using Sandbox.ActionGraphs;
using Sandbox.Audio;
using Sandbox.Engine;
using Sandbox.Internal;
using Sandbox.Network;
using Sandbox.Utility;

namespace Sandbox;

internal sealed class LocalClientWorld : IDisposable
{
	const ulong FakeSteamIdOffset = 1000;

	[SkipHotload]
	static readonly List<LocalClientWorld> _all = new();

	[ConVar( "net_local_client_isolation", ConVarFlags.Protected )]
	internal static bool IsolationEnabled { get; set; } = true;

	public static IReadOnlyList<LocalClientWorld> All => _all;

	public int Number { get; }
	public string PlayerName { get; }
	public GlobalContext Context { get; }
	public NetworkSystem System => Context.Network.System;
	public Scene Scene => Context.ActiveScene;
	public Mixer Mixer { get; private set; }
	public int CodeVersion { get; }
	public bool IsIsolated => _assemblies is not null;

	public bool IsConnected => !_disposed && Context.Network.LocalConnection?.State == Connection.ChannelState.Connected;
	public bool IsDefunct => _disposed || System is null || System.IsDisconnected;

	readonly InProcessSocket _socket;
	readonly InProcessConnection _hostSide;
	readonly Input.Context _perFrameInput;
	LocalClientAssemblies _assemblies;
	bool _disposed;
	bool _inside;

	public static LocalClientWorld Create( string name = null )
	{
		ThreadSafe.AssertIsMainThread();

		var host = Networking.System;
		if ( host is null || !host.IsHost )
			throw new InvalidOperationException( "Can't create a local client world without an active host" );

		var socket = host.Sockets.OfType<InProcessSocket>().FirstOrDefault();
		if ( socket is null )
		{
			socket = new InProcessSocket();
			host.AddSocket( socket );
		}

		var world = new LocalClientWorld( host, socket, name );
		_all.Add( world );

		socket.Accept( world._hostSide );

		return world;
	}

	LocalClientWorld( NetworkSystem host, InProcessSocket socket, string name )
	{
		_socket = socket;

		var number = 1;
		while ( _all.Any( x => x.Number == number ) )
			number++;

		Number = number;
		PlayerName = string.IsNullOrWhiteSpace( name ) ? $"Client {number}" : name;

		var hostContext = GlobalContext.Current;
		CodeVersion = IGameInstanceDll.Current?.CodeVersion ?? 0;

		Context = new GlobalContext
		{
			IsSecondaryWorld = true,
			LocalAssembly = hostContext.LocalAssembly,
			FileMount = hostContext.FileMount,
			FileData = hostContext.FileData,
			FileOrg = hostContext.FileOrg,
			Language = hostContext.Language,
			Cookies = hostContext.Cookies,
		};

		using ( new GlobalContext.GlobalContextScope( Context ) )
		{
			Context.TaskSource = new TaskSource( 1 );

			if ( !TryInitializeIsolated( hostContext ) )
			{
				Context.TypeLibrary = hostContext.TypeLibrary;
				Context.NodeLibrary = hostContext.NodeLibrary;
				Context.ResourceSystem = hostContext.ResourceSystem;
				Context.JsonSerializerOptions = hostContext.JsonSerializerOptions;
			}

			var uiSystem = new UISystem();
			var input = new InputContext
			{
				Name = PlayerName,
				TargetUISystem = uiSystem
			};

			input.OnGameMouseWheel += Input.AddMouseWheel;
			input.OnMouseMotion += Input.AddMouseMovement;
			input.OnGameButton += Input.OnButton;

			Context.UISystem = uiSystem;
			Context.InputContext = input;

			_perFrameInput = Input.Context.Create( PlayerName );

			if ( Mixer.Master is not null )
			{
				Mixer = Mixer.Master.AddChild();
				Mixer.Name = PlayerName;
				Context.AudioRoot = Mixer;
			}

			SteamId steamId = Utility.Steam.BaseFakeSteamId + FakeSteamIdOffset + (ulong)number;
			(_hostSide, var clientSide) = InProcessConnection.CreatePair( PlayerName, steamId );

			var system = new NetworkSystem( $"local-client-{number}", Context.TypeLibrary )
			{
				Environment = new InProcessClientEnvironment( host )
			};

			Networking.System = system;
			system.Connect( clientSide );
		}
	}

	bool TryInitializeIsolated( GlobalContext hostContext )
	{
		if ( !IsolationEnabled )
			return false;

		var source = IGameInstanceDll.Current?.GetGameAssemblies();
		if ( source is null || source.Count == 0 )
			return false;

		try
		{
			_assemblies = LocalClientAssemblies.Load( source );

			var typeLibrary = new TypeLibrary();
			Context.TypeLibrary = typeLibrary;

			typeLibrary.ShouldExposePrivateMember = m => m.HasAttribute( typeof( RpcAttribute ) );
			typeLibrary.AddIntrinsicTypes();
			typeLibrary.AddAssembly( typeof( Vector3 ).Assembly, false );

			if ( Context.LocalAssembly is not null )
				typeLibrary.AddAssembly( Context.LocalAssembly, false );

			typeLibrary.AddAssembly( typeof( EngineLoop ).Assembly, false );
			typeLibrary.AddAssembly( typeof( ActionGraph ).Assembly, false );

			var nodeLibrary = new NodeLibrary( new TypeLoader( () => Context.TypeLibrary ), new GraphLoader() );
			nodeLibrary.VoidTaskFaulted += ( _, e ) => Log.Error( e );
			Context.NodeLibrary = nodeLibrary;

			nodeLibrary.AddAssembly( typeof( Vector3 ).Assembly );
			nodeLibrary.AddAssembly( typeof( LogNodes ).Assembly );

			if ( Context.LocalAssembly is not null )
				nodeLibrary.AddAssembly( Context.LocalAssembly );

			foreach ( var assembly in _assemblies.Assemblies )
			{
				using ( Context.DisableTypelibraryScope( "Disabled during static constructors." ) )
				{
					try
					{
						ReflectionUtility.RunAllStaticConstructors( assembly );
					}
					catch ( Exception e )
					{
						Log.Warning( e, $"{PlayerName}: {e.GetType().Name} in static constructors for {assembly.GetName().Name}" );
					}
				}

				typeLibrary.AddAssembly( assembly, true );
				nodeLibrary.AddAssembly( assembly );
			}

			Json.Initialize( false );

			Context.ResourceSystem = new ResourceSystem { Fallback = hostContext.ResourceSystem };
			LoadGameResources();

			return true;
		}
		catch ( Exception e )
		{
			Log.Warning( e, $"{PlayerName}: couldn't isolate game code, sharing the host's instead: {e.Message}" );
			TearDownIsolated();
			return false;
		}
	}

	void LoadGameResources()
	{
		var fileSystem = Context.FileMount;
		if ( fileSystem is null )
			return;

		var types = Context.TypeLibrary.GetAttributes<AssetTypeAttribute>()
			.Where( x => x.TargetType is not null && _assemblies.Contains( x.TargetType.Assembly ) )
			.DistinctBy( x => x.Extension )
			.ToDictionary( x => $".{x.Extension}_c", StringComparer.OrdinalIgnoreCase );

		if ( types.Count == 0 )
			return;

		var loaded = new List<GameResource>();

		foreach ( var file in fileSystem.FindFile( "/", "*", true ) )
		{
			if ( !types.TryGetValue( global::System.IO.Path.GetExtension( file ), out var type ) )
				continue;

			try
			{
				var resource = Context.ResourceSystem.LoadGameResource( type, file, fileSystem, true );
				if ( resource is not null )
					loaded.Add( resource );
			}
			catch ( Exception e )
			{
				Log.Warning( e, $"{PlayerName}: couldn't load {file}: {e.Message}" );
			}
		}

		foreach ( var resource in loaded )
		{
			resource.PostLoadInternal();
		}
	}

	void TearDownIsolated()
	{
		if ( _assemblies is null )
			return;

		try
		{
			using ( new GlobalContext.GlobalContextScope( Context ) )
			{
				if ( Context.ResourceSystem?.Fallback is not null )
					Context.ResourceSystem.Clear();

				if ( Context.NodeLibrary is not null )
				{
					foreach ( var assembly in _assemblies.Assemblies )
					{
						Context.NodeLibrary.RemoveAssembly( assembly );
					}
				}

				if ( Context.TypeLibrary is not null )
				{
					foreach ( var assembly in _assemblies.Assemblies )
					{
						Context.TypeLibrary.RemoveAssembly( assembly );
					}

					Context.TypeLibrary.ClearRemovedTypes();
					Context.TypeLibrary.Dispose();
				}
			}
		}
		catch ( Exception e )
		{
			Log.Warning( e, $"{PlayerName}: error tearing down isolated code: {e.Message}" );
		}

		Context.TypeLibrary = null;
		Context.NodeLibrary = null;
		Context.JsonSerializerOptions = null;

		_assemblies.Dispose();
		_assemblies = null;
	}

	public IDisposable Push()
	{
		ThreadSafe.AssertIsMainThread();

		if ( _inside )
			throw new InvalidOperationException( "LocalClientWorld.Push is not re-entrant" );

		_inside = true;

		var screenSize = Screen.Size;
		var timeNow = Time.NowDouble;
		var timeDelta = (double)Time.Delta;
		var scope = new GlobalContext.GlobalContextScope( Context );

		return DisposeAction.Create( () =>
		{
			scope.Dispose();
			Time.Update( timeNow, timeDelta );
			Screen.Size = screenSize;
			_inside = false;
		} );
	}

	public static void TickAll()
	{
		foreach ( var world in _all.ToArray() )
		{
			world.Tick();
		}
	}

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
			Log.Warning( e, $"{PlayerName} tick error: {e.Message}" );
		}
	}

	void TickInternal()
	{
		var system = System;
		var scene = Game.ActiveScene;

		if ( !scene.IsValid() )
		{
			system?.Tick();
			system?.SendTableUpdates();
			return;
		}

		using var sceneScope = scene.Push();

		scene.UpdateTime( RealTime.Delta );
		scene.SyncServerTime();
		Time.Update( scene.TimeNow, scene.TimeDelta );

		_perFrameInput.Flip();
		using var inputScope = _perFrameInput.Push();
		Input.ProcessContext();

		system?.Tick();

		if ( Game.ActiveScene == scene && scene.IsValid() && system is not null && !system.IsConnecting && !scene.IsLoading )
		{
			scene.GameTick( 0 );
		}

		system?.SendTableUpdates();

		Context.UISystem?.Simulate( InputRouter.FocusedWorld == Context );
		scene.ProcessDeletes();
	}

	public void Dispose()
	{
		if ( _disposed )
			return;

		ThreadSafe.AssertIsMainThread();

		_disposed = true;
		_all.Remove( this );

		if ( InputRouter.FocusedWorld == Context )
			InputRouter.FocusedWorld = null;

		using ( Push() )
		{
			try
			{
				if ( System is { IsDisconnected: false } system )
					system.Disconnect();

				Game.ActiveScene?.Destroy();
				Game.ActiveScene = null;

				Context.UISystem?.Clear();
				Context.CancellationTokenSource?.Cancel();
			}
			catch ( Exception e )
			{
				Log.Warning( e, $"{PlayerName} shutdown error: {e.Message}" );
			}
		}

		try
		{
			_socket.Disconnect( _hostSide );
		}
		catch ( Exception e )
		{
			Log.Warning( e, $"{PlayerName} host disconnect error: {e.Message}" );
		}

		if ( Mixer is not null )
		{
			SoundHandle.StopAll( 0f, Mixer );
			Mixer.Destroy();
			Mixer = null;
		}

		TearDownIsolated();
	}

	public static void DisposeAll()
	{
		foreach ( var world in _all.ToArray() )
		{
			world.Dispose();
		}
	}
}
