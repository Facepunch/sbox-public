using Sandbox.Engine;
using System.IO;
using System.Reflection;

namespace Sandbox.Mounting;

public static partial class Directory
{
	static MountHost _system;

	/// <summary>
	/// Mount assemblies whose exposed types are available without a game package dependency.
	/// <see cref="Game.InitTypeLibrary"/> adds them back on rebuild, including addon mounts loaded by the editor.
	/// </summary>
	static readonly List<Assembly> _typeAssemblies = new();
	static readonly HashSet<Assembly> _standaloneAssemblies = new();

	/// <summary>
	/// The mount assemblies whose console variables and commands are registered: those with a mount that is mounted.
	/// </summary>
	static readonly HashSet<Assembly> _consoleAssemblies = new();

	internal static void LoadSources()
	{
		// Create system first.
		var config = new Configuration();
		config.SteamIntegration = new SteamIntegration();
		config.IsDedicatedServer = Application.IsDedicatedServer;
		_system = new MountHost( config );
		_system.MountStateChanged = mount => UpdateConsoleMembers( mount.GetType().Assembly );

		LoadConfig();

		if ( !System.IO.Directory.Exists( "mounts/" ) )
			return;

		LoadAddons();
		LoadAssemblies();
	}

	//
	// Load mounts from .sbprojs, setup compilers and mount their assets.
	//
	static void LoadAddons()
	{
		var projects = new List<Project>();

		foreach ( var projFile in System.IO.Directory.EnumerateFiles( "mounts/", "*.sbproj", SearchOption.AllDirectories ) )
		{
			string projPath = System.IO.Path.GetFullPath( projFile );

			try
			{
				// don't load the open project as a mount here (again)
				if ( Project.All.Any( x => x.ConfigFilePath.Equals( projPath, StringComparison.OrdinalIgnoreCase ) ) )
					continue;

				var project = Project.AddFromFile( projPath );
				if ( project.Config.Type != "addon" )
				{
					Log.Info( $"Mount {project.Config.Title} is not an addon, skipping." );
					Project.Remove( project );
					continue;
				}

				if ( project.HasAssetsPath() )
				{
					using var scope = GlobalContext.MenuScope();
					EngineFileSystem.AddAssetPath( $"mnt_{project.Config.FullIdent}", project.GetAssetsPath() );
				}

				projects.Add( project );
			}
			catch ( System.Exception e )
			{
				Log.Warning( e, $"Exception when loading mount addon: {projPath}" );
			}
		}

		// in editor, the package manager will compile and register mounts for us
		if ( Application.IsEditor )
			return;

		if ( projects.Count == 0 )
			return;

		// :(
		SyncContext.RunBlocking( Project.CompileAsync() );

		foreach ( var project in projects )
		{
			try
			{
				LoadFromAddon( project );
			}
			catch ( System.Exception e )
			{
				Log.Warning( e, $"Exception when loading mount addon: {project.ConfigFilePath}" );
			}
		}
	}

	static void LoadFromAddon( Project project )
	{
		if ( project.Compiler is null )
		{
			Log.Error( $"Mount {project.Config.Title} has no code to compile" );
			return;
		}

		var result = SyncContext.RunBlocking( project.Compiler.GetCompileOutputAsync() );
		if ( !result.Successful )
		{
			Log.Error( $"Failed to compile mount: {project.Config.Title}" );
			return;
		}

		LoadAssembly( Assembly.Load( result.AssemblyData ), project.FileSystem );
	}

	/// <summary>
	/// Load mounts from DLLs and mount their assets.
	/// </summary>
	static void LoadAssemblies()
	{
		foreach ( var folder in System.IO.Directory.EnumerateDirectories( "mounts/" ) )
		{
			var folderName = System.IO.Path.GetFileName( folder );
			var assemblies = System.IO.Directory.GetFiles( folder, "*.dll" );
			if ( assemblies.Length == 0 )
				continue;

			var fullPath = System.IO.Path.GetFullPath( folder );
			var fileSystem = new LocalFileSystem( fullPath );

			// Before the mounts initialize, so their static constructors can find their own assets
			var assetsPath = System.IO.Path.Combine( fullPath, "Assets" );
			if ( System.IO.Directory.Exists( assetsPath ) )
			{
				using var scope = GlobalContext.MenuScope();
				EngineFileSystem.AddAssetPath( $"mnt_{folderName}", assetsPath );
			}

			// Compiled thumbnails, loaded as {ident}/thumbs/{path}.vtex. Only native resource loads see them, not the managed filesystems.
			var mountsPath = System.IO.Path.Combine( fullPath, "Mounts" );
			if ( System.IO.Directory.Exists( mountsPath ) )
				NativeEngine.EngineGlue.AddSearchPath( mountsPath, "GAME", false );

			foreach ( var dll in assemblies )
			{
				try
				{
					string dllPath = System.IO.Path.GetFullPath( dll );

					byte[] bytes = File.ReadAllBytes( dllPath );
					if ( bytes is null ) continue;

					var attrs = AssemblyMetadata.GetCustomAttributes( bytes );
					var metadata = attrs
						.Where( a => a.AttributeFullName == "System.Reflection.AssemblyMetadataAttribute" )
						.Where( a => a.Arguments.Length == 2 )
						.ToDictionary(
							a => (string)a.Arguments[0],
							a => (string)a.Arguments[1]
						);

					if ( metadata.TryGetValue( "Ident", out var ident ) == false || Project.FindByIdent( ident ) is not null )
					{
						// this is an already open project, don't load it twice
						continue;
					}

					LoadAssembly( Assembly.Load( bytes ), fileSystem );
				}
				catch ( System.Exception e )
				{
					Log.Warning( e, $"Exception when loading mount assembly: {dll}" );
				}
			}

		}
	}

	/// <summary>
	/// Get information about all the current mounts
	/// </summary>
	public static MountInfo[] GetAll()
	{
		return _system.All.Select( e => new MountInfo( e ) ).ToArray();
	}

	/// <summary>
	/// Get metadata for a resource
	/// </summary>
	public static MountResourceInfo? GetMetadata( string filename )
	{
		if ( !MountUtility.TryParse( filename, out string sourceName ) )
			return null;

		var source = Get( sourceName );
		if ( source is null )
		{
			Log.Warning( $"Couldn't find source \"{sourceName}\"" );
			return null;
		}

		var entry = source.GetByPath( filename );
		if ( entry is null )
		{
			Log.Warning( $"Couldn't find file \"{filename}\" in {source.Ident}" );
			return null;
		}

		return new MountResourceInfo( entry );
	}

	/// <summary>
	/// Get a specific mount by ident
	/// </summary>
	public static BaseGameMount Get( string ident )
	{
		return _system.GetSource( ident );
	}

	/// <summary>
	/// Mount this game if we can. Returns null if it can't be mounted, or the mount object if it can.
	/// If we're already mounted, will just return the mount straight away.
	/// </summary>
	public static async Task<BaseGameMount> Mount( string ident )
	{
		var source = _system.GetSource( ident );
		if ( source is null ) return null;
		if ( source.IsMounted ) return source;
		if ( source.IsInstalled == false ) return source;

		await SetMountState( ident, true );

		if ( !source.IsMounted ) return null;

		return source;
	}

	[ConCmd( "mount", ConVarFlags.Protected, Help = "Mount a game by ident" )]
	private static async Task Cmd_Mount( string ident ) => await CmdSetMountState( ident, true );

	[ConCmd( "unmount", ConVarFlags.Protected, Help = "Unmount a game by ident" )]
	private static async Task Cmd_Unmount( string ident ) => await CmdSetMountState( ident, false );

	private static async Task CmdSetMountState( string ident, bool mount )
	{
		var source = _system.GetSource( ident );
		if ( source is null )
		{
			Log.Info( $"Unknown mount '{ident}'" );
			return;
		}

		if ( source.IsMounted == mount )
		{
			Log.Info( $"{source.Title} is already {(mount ? "mounted" : "unmounted")}!" );
			return;
		}

		if ( mount && !source.IsInstalled )
		{
			Log.Info( $"{source.Title} is not installed. It must be installed before it can be mounted." );
			return;
		}

		await SetMountState( ident, mount );

		if ( !Application.IsEditor )
		{
			SaveConfig();
		}

		if ( source.IsMounted != mount )
		{
			Log.Info( $"Failed to {(mount ? "mount" : "unmount")} '{ident}'. See log for details." );
			return;
		}

		Log.Info( $"Successfully {(mount ? "mounted" : "unmounted")} {source.Title}" );
	}

	/// <summary>
	/// Set mounted or not mounted.
	/// </summary>
	internal static async Task SetMountState( string name, bool state )
	{
		var source = _system.GetSource( name );
		if ( source is null ) return;
		if ( state == source.IsMounted ) return;
		if ( source.IsInstalled == false ) return;

		if ( !state )
		{
			_system.Unmount( name );
		}
		else
		{
			await _system.Mount( name );
		}

		if ( source.IsMounted )
		{
			IToolsDll.Current?.RunEvent<IMountEvents>( x => x.OnMountEnabled( Get( name ) ) );
		}
		else
		{
			IToolsDll.Current?.RunEvent<IMountEvents>( x => x.OnMountDisabled( Get( name ) ) );
		}
	}

	internal static bool TryLoad( string filename, out object resource )
	{
		resource = default;

		if ( !MountUtility.TryParse( filename, out string sourceName ) )
			return false;

		var source = Get( sourceName );
		if ( source is null )
		{
			Log.Warning( $"Couldn't find source \"{sourceName}\"" );
			return false;
		}

		var entry = source.GetByPath( filename );
		if ( entry is null )
		{
			Log.Warning( $"Couldn't find file \"{filename}\" in {source.Ident}" );
			return false;
		}

		resource = SyncContext.RunBlocking( LoadResource( entry ) );

		if ( resource is null )
		{
			Log.Warning( $"Loading \"{filename}\" returned null!" );
			return false;
		}

		return resource is not null;
	}

	internal static async Task<object> TryLoadAsync( string filename )
	{
		if ( !MountUtility.TryParse( filename, out string sourceName ) )
			return null;

		var source = Get( sourceName );
		if ( source is null )
		{
			Log.Warning( $"Couldn't find source \"{sourceName}\"" );
			return null;
		}

		var entry = source.GetByPath( filename );
		if ( entry is null )
		{
			Log.Warning( $"Couldn't find file \"{filename}\" in {source.Ident}" );
			return null;
		}

		var resource = await LoadResource( entry );

		if ( resource is null )
		{
			Log.Warning( $"Loading \"{filename}\" returned null!" );
			return null;
		}

		return resource;
	}

	static async Task<object> LoadResource( ResourceLoader entry )
	{
		if ( entry.Type == ResourceType.GameResource )
			ThreadSafe.AssertIsMainThread();

		if ( entry.Type == ResourceType.GameResource
			&& ResourceLibrary.TryGet<GameResource>( entry.Path, out var cached ) && !cached.IsPromise )
			return cached;

		var result = await entry.GetOrCreate();
		if ( entry.Type != ResourceType.GameResource || result is null )
		{
			if ( result is Resource mounted )
			{
				if ( entry.IsShutdown )
				{
					entry.InvalidateCachedResult( mounted );
					Log.Warning( $"Mount shut down while loading resource '{entry.Path}'." );
					return null;
				}

				try
				{
					mounted.OnMounted( entry.Path );
				}
				catch ( InvalidOperationException e )
				{
					entry.InvalidateCachedResult( mounted );
					Log.Warning( e, $"Exception when registering mounted resource '{entry.Path}'." );
					return null;
				}
			}

			return result;
		}

		ThreadSafe.AssertIsMainThread();

		if ( result is not GameResource resource )
		{
			Log.Warning( $"Mounted GameResource '{entry.Path}' returned '{result.GetType().FullName}'." );
			return null;
		}

		if ( entry.IsShutdown )
		{
			resource.DestroyInternal();
			Log.Warning( $"Mount shut down while loading GameResource '{entry.Path}'." );
			return null;
		}

		if ( ResourceLibrary.TryGet<GameResource>( entry.Path, out cached ) && !cached.IsPromise )
		{
			if ( !ReferenceEquals( resource, cached ) )
				resource.DestroyInternal();
			return cached;
		}

		resource.RegisterMounted( entry );
		if ( resource.PostLoadInternal() )
			return resource;

		resource.DestroyInternal();
		return null;
	}

	static void LoadAssembly( Assembly assembly, BaseFileSystem fileSystem )
	{
		try
		{
			AddTypeAssembly( assembly );
		}
		catch ( System.Exception e )
		{
			Log.Warning( e, $"Couldn't add the types of mount assembly {assembly.GetName().Name} to the type library, not loading it" );
			return;
		}

		try
		{
			AddAssembly( assembly, fileSystem );
		}
		catch
		{
			// Don't keep the types of a mount that failed to register
			RemoveAssembly( assembly );
			throw;
		}

		ReflectionUtility.RunAllStaticConstructors( assembly );
	}

	internal static void AddAssembly( Assembly assembly, BaseFileSystem projectFileSystem = null, bool exposeMountTypes = false )
	{
		// The editor loads global mount addons through its package enroller, but a game with no dependency
		// on that addon never enrolls it. Its mounted prefabs still need the mount's exposed components.
		if ( exposeMountTypes && assembly.GetTypes().Any( x => !x.IsAbstract && typeof( BaseGameMount ).IsAssignableFrom( x ) ) )
			AddTypeAssembly( assembly, registerConsoleMembers: false );

		_system.RegisterTypes( assembly, projectFileSystem );
	}

	internal static void RemoveAssembly( Assembly assembly )
	{
		_system.UnregisterTypes( assembly );
		RemoveTypeAssembly( assembly );
	}

	/// <summary>
	/// Enroll a mount assembly's exposed types in the menu and game type libraries, and keep them there across rebuilds.
	/// Game code only gets the types the mount marks, such as components and [Library] types, not the rest of the assembly.
	/// Assemblies loaded by the package system keep their console members managed by their package.
	/// </summary>
	internal static void AddTypeAssembly( Assembly assembly, bool registerConsoleMembers = true )
	{
		lock ( _typeAssemblies )
		{
			if ( _typeAssemblies.Contains( assembly ) ) return;
			_typeAssemblies.Add( assembly );
			if ( registerConsoleMembers ) _standaloneAssemblies.Add( assembly );
		}

		var libraries = ContextLibraries().ToArray();

		try
		{
			foreach ( var library in libraries )
			{
				library.AddAssembly( assembly, false );
			}
		}
		catch
		{
			// Left in the list, it would throw again on every type library rebuild
			lock ( _typeAssemblies )
			{
				_typeAssemblies.Remove( assembly );
				_standaloneAssemblies.Remove( assembly );
			}

			foreach ( var library in libraries )
			{
				library.RemoveAssembly( assembly );
			}

			throw;
		}

		UpdateUpgraders();
		UpdateConsoleMembers( assembly );
	}

	internal static void RemoveTypeAssembly( Assembly assembly )
	{
		lock ( _typeAssemblies )
		{
			if ( !_typeAssemblies.Remove( assembly ) ) return;
			_standaloneAssemblies.Remove( assembly );
		}

		foreach ( var library in ContextLibraries() )
		{
			library.RemoveAssembly( assembly );
		}

		UpdateUpgraders();
		UpdateConsoleMembers( assembly );
	}

	/// <summary>
	/// Called by <see cref="Game.InitTypeLibrary"/> so a rebuilt library keeps the mount types.
	/// </summary>
	internal static void AddTypesTo( Internal.TypeLibrary library )
	{
		Assembly[] assemblies;
		lock ( _typeAssemblies ) assemblies = _typeAssemblies.ToArray();

		foreach ( var assembly in assemblies )
		{
			library.AddAssembly( assembly, false );
		}
	}

	/// <summary>
	/// Console variables and commands declared in a mount assembly exist while at least one mount it defines is
	/// mounted, and are removed with the last of them or with the assembly. Only for the assemblies loaded here:
	/// one that came through the package system has its own registered with it.
	/// </summary>
	static void UpdateConsoleMembers( Assembly assembly )
	{
		if ( !ThreadSafe.IsMainThread )
		{
			MainThread.Queue( () => UpdateConsoleMembers( assembly ) );
			return;
		}

		bool loadedHere;
		lock ( _typeAssemblies ) loadedHere = _standaloneAssemblies.Contains( assembly );

		var mounted = loadedHere && _system is not null && _system.All.Any( x => x.IsMounted && x.GetType().Assembly == assembly );
		if ( mounted == _consoleAssemblies.Contains( assembly ) )
			return;

		if ( mounted )
		{
			_consoleAssemblies.Add( assembly );
			ConVarSystem.AddAssembly( assembly, "mount" );
		}
		else
		{
			_consoleAssemblies.Remove( assembly );
			ConVarSystem.RemoveAssembly( assembly );
		}
	}

	// The game's type library holds the upgraders, and nothing else refreshes them until a game assembly loads
	static void UpdateUpgraders()
	{
		if ( GlobalContext.Game?.TypeLibrary is { } library )
			JsonUpgrader.UpdateUpgraders( library );
	}

	static IEnumerable<Internal.TypeLibrary> ContextLibraries()
	{
		return new[] { GlobalContext.Menu?.TypeLibrary, GlobalContext.Game?.TypeLibrary }
			.Where( x => x is not null )
			.Distinct();
	}
}


class SteamIntegration : ISteamIntegration
{
	public BaseFileSystem GetFileSystem( long appid )
	{
		if ( !NativeEngine.Steam.SteamApps().IsValid ) return null;

		var dir = NativeEngine.Steam.SteamApps().GetAppInstallDir( (int)appid );
		if ( string.IsNullOrWhiteSpace( dir ) ) return null;

		// Read-only and rooted at the install dir - the mount can't write or escape above it.
		return new LocalFileSystem( dir, makereadonly: true );
	}

	public bool IsAppInstalled( long appid )
	{
		if ( !NativeEngine.Steam.SteamApps().IsValid ) return false;
		return NativeEngine.Steam.SteamApps().BIsAppInstalled( (int)appid );
	}

	public bool IsAppOwned( long appid )
	{
		if ( !NativeEngine.Steam.SteamApps().IsValid ) return false;
		return NativeEngine.Steam.SteamApps().BIsSubscribedApp( (int)appid );
	}

	public bool IsDlcInstalled( long appid )
	{
		if ( !NativeEngine.Steam.SteamApps().IsValid ) return false;
		return NativeEngine.Steam.SteamApps().BIsDlcInstalled( (int)appid );
	}
}
