using Sandbox.Engine;
using Sandbox.Internal;
using System.Diagnostics;
using System.Reflection;

namespace Sandbox;

/// <summary>
/// The isolated world of one in-process client session: its own <see cref="GlobalContext"/>
/// (the same mechanism that separates the Menu and Game worlds) plus private copies of the
/// game assemblies in a collectible <see cref="Sandbox.Internal.LoadContext"/>, so game
/// statics are per-client like they would be across real processes. Engine assemblies stay
/// shared by identity. If isolation can't be built we fall back to fully shared mode rather
/// than a half-isolated session.
/// </summary>
internal sealed class InProcessTenant : IDisposable
{
	/// <summary>
	/// The tenant's GlobalContext. Null when running in shared (fallback) mode.
	/// </summary>
	public GlobalContext Context { get; private set; }

	/// <summary>
	/// The TypeLibrary the session's networking and scenes resolve types through.
	/// The tenant's own library when isolated, the host's when shared.
	/// </summary>
	public TypeLibrary TypeLibrary { get; private set; }

	/// <summary>
	/// True when this tenant has private game assembly copies (isolated statics).
	/// </summary>
	public bool IsIsolated { get; private set; }

	LoadContext _loadContext;
	List<Assembly> _assemblies = new();
	readonly string _debugName;

	/// <summary>
	/// The tenant's private game assembly copies. Empty in shared mode.
	/// </summary>
	public IReadOnlyList<Assembly> Assemblies => _assemblies;

	/// <summary>
	/// Give each docked client private copies of the game assemblies (per-client statics).
	/// Turn off to run docked clients against the host's assemblies, to A/B a problem you
	/// suspect is isolation-related.
	/// </summary>
	[ConVar( "docked_client_isolation", ConVarFlags.Protected )]
	internal static bool IsolationEnabled { get; set; } = true;

	static InProcessTenant()
	{
		// Tenants never watch files: watcher callbacks live on the host's (shared) filesystems,
		// so they'd outlive the tenant and root its collectible assemblies.
		BaseFileSystem.SuppressWatcherCreation = static () => GlobalContext.Current.IsInProcessTenant;
	}

	InProcessTenant( string debugName )
	{
		_debugName = debugName;
	}

	/// <summary>
	/// Swap the world to this tenant. No-op scope in shared mode.
	/// </summary>
	public IDisposable Push()
	{
		if ( Context is null )
			return default;

		return new GlobalContext.GlobalContextScope( Context );
	}

	/// <summary>
	/// Build a tenant for an in-process client. Never throws: any failure logs and returns
	/// a shared-mode tenant, so the session still works exactly like before isolation.
	/// </summary>
	public static InProcessTenant Create( string debugName, IReadOnlyList<(string Name, byte[] Bytes)> gameAssemblies = null )
	{
		var tenant = new InProcessTenant( debugName );

		try
		{
			if ( !IsolationEnabled )
			{
				Log.Info( $"[{debugName}] docked_client_isolation is off - running in shared mode" );
				gameAssemblies = null;
			}
			else
			{
				gameAssemblies ??= IGameInstanceDll.Current?.GetGameAssemblies();
			}

			tenant.Initialize( gameAssemblies );
		}
		catch ( Exception e )
		{
			Log.Warning( e, $"[{debugName}] Assembly isolation failed, falling back to shared mode: {e.Message}" );
			tenant.TearDownPartialIsolation();
		}

		if ( !tenant.IsIsolated )
		{
			tenant.Context = null;
			tenant.TypeLibrary = IGameInstanceDll.Current?.TypeLibrary ?? GlobalGameNamespace.TypeLibrary;
		}

		return tenant;
	}

	void Initialize( IReadOnlyList<(string Name, byte[] Bytes)> gameAssemblies )
	{
		if ( gameAssemblies is null || gameAssemblies.Count == 0 )
			return;

		if ( gameAssemblies.Any( x => x.Bytes is null || x.Bytes.Length == 0 ) )
		{
			Log.Warning( $"[{_debugName}] A game assembly has no compiled bytes - falling back to shared mode" );
			return;
		}

		var sw = Stopwatch.StartNew();
		var host = GlobalContext.Game;

		var context = new GlobalContext
		{
			IsInProcessTenant = true,

			// Shared with the host: same process, same files, same language.
			LocalAssembly = host.LocalAssembly,
			FileMount = host.FileMount,
			FileData = host.FileData,
			FileOrg = host.FileOrg,
			Language = host.Language,
			Cookies = host.Cookies,
		};

		// The build steps below reach the context through this property; on failure the
		// caller tears it back down.
		Context = context;

		using ( new GlobalContext.GlobalContextScope( context ) )
		{
			// Created under the tenant scope so it binds the tenant's cancellation source.
			context.TaskSource = new TaskSource( 1 );

			BuildTypeLibrary( gameAssemblies );

			// Own UI world, so in-game panels don't route through the host's UI system.
			// While this session has focus the input router puts this context first.
			var uiSystem = new UISystem();
			var inputContext = new InputContext
			{
				Name = _debugName,
				TargetUISystem = uiSystem
			};

			inputContext.OnGameMouseWheel += Input.AddMouseWheel;
			inputContext.OnMouseMotion += Input.AddMouseMovement;
			inputContext.OnGameButton += Input.OnButton;

			context.UISystem = uiSystem;
			context.InputContext = inputContext;

			Json.Initialize( updateProcessDefaults: false );

			LoadResources();
		}

		IsIsolated = true;

		Log.Info( $"[{_debugName}] Isolated tenant ready: {_assemblies.Count} private game assemblies in {sw.Elapsed.TotalMilliseconds:0}ms" );
	}

	/// <summary>
	/// Mirror of <see cref="Game.InitTypeLibrary"/>: shared engine assemblies by identity,
	/// then private tenant copies of every game assembly from the host's compiled bytes.
	/// </summary>
	void BuildTypeLibrary( IReadOnlyList<(string Name, byte[] Bytes)> gameAssemblies )
	{
		var typeLibrary = new TypeLibrary();
		Context.TypeLibrary = typeLibrary;
		TypeLibrary = typeLibrary;

		typeLibrary.ShouldExposePrivateMember = m => m.HasAttribute( typeof( RpcAttribute ) );
		typeLibrary.AddIntrinsicTypes();
		typeLibrary.AddAssembly( typeof( Vector3 ).Assembly, false );
		typeLibrary.AddAssembly( typeof( EngineLoop ).Assembly, false );
		typeLibrary.AddAssembly( typeof( Facepunch.ActionGraphs.ActionGraph ).Assembly, false );

		// Null when no game/menu dll is loaded (headless test hosts).
		if ( Context.LocalAssembly is not null )
		{
			typeLibrary.AddAssembly( Context.LocalAssembly, false );
		}

		var nodeLibrary = new Facepunch.ActionGraphs.NodeLibrary( new ActionGraphs.TypeLoader( () => Context.TypeLibrary ), new ActionGraphs.GraphLoader() );
		nodeLibrary.VoidTaskFaulted += ( _, e ) => Log.Error( e );
		Context.NodeLibrary = nodeLibrary;

		nodeLibrary.AddAssembly( typeof( Vector3 ).Assembly );
		nodeLibrary.AddAssembly( typeof( ActionGraphs.LogNodes ).Assembly );

		if ( Context.LocalAssembly is not null )
		{
			nodeLibrary.AddAssembly( Context.LocalAssembly );
		}

		// Root the tenant load context at Sandbox.Engine so engine references resolve to the
		// shared assemblies; only the game assemblies themselves get private copies.
		_loadContext = new LoadContext( typeof( InProcessTenant ).Assembly );

		// Load every assembly first, so sibling references (game -> base) resolve to
		// tenant copies rather than the host's.
		foreach ( var (name, bytes) in gameAssemblies )
		{
			var assembly = _loadContext.LoadWithEmbeds( bytes, false );
			if ( assembly is null )
				throw new InvalidOperationException( $"Failed to load tenant copy of {name}" );

			_assemblies.Add( assembly );
		}

		// Then run static constructors and register types, in the host's original load order.
		foreach ( var assembly in _assemblies )
		{
			using ( Context.DisableTypelibraryScope( "Disabled during static constructors." ) )
			{
				try
				{
					ReflectionUtility.RunAllStaticConstructors( assembly );
				}
				catch ( Exception ex )
				{
					Log.Warning( ex, $"[{_debugName}] {ex.GetType().Name} in static constructors for {assembly.GetName().Name}" );
				}
			}

			typeLibrary.AddAssembly( assembly, true );
			nodeLibrary.AddAssembly( assembly );
		}
	}

	/// <summary>
	/// Load tenant-typed GameResources (a host-typed resource is a foreign type to tenant
	/// code). JSON only - the heavy native data (textures, models) stays shared.
	/// </summary>
	void LoadResources()
	{
		if ( FileSystem.Mounted is null )
			return;

		var sw = Stopwatch.StartNew();

		ResourceLoader.LoadAllGameResource( FileSystem.Mounted );

		Log.Info( $"[{_debugName}] Tenant resources loaded in {sw.Elapsed.TotalMilliseconds:0}ms" );
	}

	/// <summary>
	/// Look up a resource in this tenant's resource system, for engine callbacks that fire
	/// outside any context scope.
	/// </summary>
	public Resource FindResource( string resourceName )
	{
		return Context?.ResourceSystem?.Get( typeof( Resource ), resourceName );
	}

	void TearDownPartialIsolation()
	{
		try
		{
			Context?.Shutdown();
		}
		catch ( Exception e )
		{
			Log.Warning( e, $"[{_debugName}] Error tearing down partial tenant: {e.Message}" );
		}

		Context = null;
		TypeLibrary = null;
		IsIsolated = false;

		UnloadAssemblies();
	}

	void UnloadAssemblies()
	{
		try
		{
			// Engine reflection caches hold Type keys from any scene that ever ticked.
			foreach ( var asm in _assemblies )
			{
				ReflectionCacheBase.PruneAssembly( asm );
			}

			// Each assembly lives in its own child context inside the LoadContext - they
			// must be unloaded explicitly, the parent's Unload doesn't cascade.
			if ( _loadContext is not null )
			{
				foreach ( var asm in _assemblies )
				{
					_loadContext.UnloadChild( asm );
				}

				_loadContext.Unload();
			}
		}
		catch ( Exception e )
		{
			Log.Warning( e, $"[{_debugName}] Error unloading tenant assemblies: {e.Message}" );
		}

		_assemblies.Clear();
		_loadContext = null;
	}

	public void Dispose()
	{
		if ( Context is not null )
		{
			try
			{
				// Teardown must run under the tenant's scope: resource destruction resolves
				// Game.Resources ambiently, and under the host's context it would evict the
				// host's entries for the same files.
				using var scope = new GlobalContext.GlobalContextScope( Context );

				ClearJsonCaches( Context.JsonSerializerOptions );

				// Remove every private assembly's types so no TypeDescription roots the
				// collectible load context.
				if ( Context.TypeLibrary is not null )
				{
					foreach ( var asm in _assemblies )
					{
						Context.TypeLibrary.RemoveAssembly( asm );
					}

					Context.TypeLibrary.ClearRemovedTypes();
					Context.TypeLibrary.Dispose();
				}

				Context.Shutdown();

				// Shutdown() leaves references a live context needs - scrub everything that
				// can reach tenant types, in case the context object itself is retained
				// somewhere (captured execution contexts, logging).
				if ( Context.NodeLibrary is not null )
				{
					// Reset alone doesn't release definitions referencing tenant types.
					foreach ( var asm in _assemblies )
					{
						Context.NodeLibrary.RemoveAssembly( asm );
					}

					Context.NodeLibrary.Reset();
				}

				Context.NodeLibrary = null;
				Context.TypeLibrary = null;
				Context.JsonSerializerOptions = null;
				Context.FileMount = null;
				Context.FileData = null;
				Context.FileOrg = null;
				Context.Language = null;
				Context.Cookies = null;
				Context.LocalAssembly = null;
			}
			catch ( Exception e )
			{
				Log.Warning( e, $"[{_debugName}] Error disposing tenant context: {e.Message}" );
			}

			Context = null;
		}

		TypeLibrary = null;

		// Engine-wide reflection caches are keyed by Type - drop this tenant's entries.
		foreach ( var asm in _assemblies )
		{
			ReflectionQueryCache.RemoveAssembly( asm );
		}

		UnloadAssemblies();
	}

	static System.Reflection.MethodInfo _jsonClearInstanceCaches;

	/// <summary>
	/// Clear the cached JsonTypeInfo held by ONE options instance - it roots the tenant's
	/// private types. Never the global STJ cache, that would corrupt the host's serializer
	/// state mid-session.
	/// </summary>
	static void ClearJsonCaches( System.Text.Json.JsonSerializerOptions options )
	{
		if ( options is null )
			return;

		try
		{
			_jsonClearInstanceCaches ??= typeof( System.Text.Json.JsonSerializerOptions )
				.GetMethod( "ClearCaches", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance );

			_jsonClearInstanceCaches?.Invoke( options, null );
		}
		catch ( Exception e )
		{
			Log.Warning( e, $"Failed to clear tenant Json caches: {e.Message}" );
		}
	}
}
