using Sandbox.Engine;
using System.IO;
using System.Reflection;

namespace Sandbox.Mounting;

public static class Directory
{
	static MountHost _system;

	internal static void LoadSources()
	{
		// Create system first.
		var config = new Configuration();
		config.SteamIntegration = new SteamIntegration();
		_system = new MountHost( config );

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

		var assembly = Assembly.Load( result.AssemblyData );
		AddAssembly( assembly, project.FileSystem );
		ReflectionUtility.RunAllStaticConstructors( assembly );
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

					Assembly assembly = Assembly.Load( bytes );
					AddAssembly( assembly, fileSystem );
					ReflectionUtility.RunAllStaticConstructors( assembly );
				}
				catch ( System.Exception e )
				{
					Log.Warning( e, $"Exception when loading mount assembly: {dll}" );
				}
			}

			var assetsPath = System.IO.Path.Combine( fullPath, "Assets" );
			if ( System.IO.Directory.Exists( assetsPath ) )
			{
				using var scope = GlobalContext.MenuScope();
				EngineFileSystem.AddAssetPath( $"mnt_{folderName}", assetsPath );
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
	/// Get a specific mount by name
	/// </summary>
	public static BaseGameMount Get( string name )
	{
		return _system.GetSource( name );
	}

	/// <summary>
	/// Mount this game if we can. Returns null if it can't be mounted, or the mount object if it can.
	/// If we're already mounted, will just return the mount straight away.
	/// </summary>
	public static async Task<BaseGameMount> Mount( string name )
	{
		var source = _system.GetSource( name );
		if ( source is null ) return null;
		if ( source.IsMounted ) return source;
		if ( source.IsInstalled == false ) return source;

		await SetMountState( name, true );

		if ( !source.IsMounted ) return null;

		return source;
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

	internal static void AddAssembly( Assembly assembly, BaseFileSystem projectFileSystem = null )
	{
		_system.RegisterTypes( assembly, projectFileSystem );
	}

	internal static void RemoveAssembly( Assembly assembly )
	{
		_system.UnregisterTypes( assembly );
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
