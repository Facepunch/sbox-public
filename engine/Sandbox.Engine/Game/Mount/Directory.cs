using Sandbox.Engine;
using System.Reflection;

namespace Sandbox.Mounting;

public static class Directory
{
	static MountHost _system;

	/// <summary>
	/// Load the assemblies and collect sources. That's all.
	/// </summary>
	internal static void LoadAssemblies()
	{
		// Create system first.
		var config = new Configuration();
		config.SteamIntegration = new SteamIntegration();
		_system = new MountHost( config );

		//
		// Loop each folder in mount/, look for /mount/x/x.dll and load it.
		//
		foreach ( var file in System.IO.Directory.EnumerateDirectories( "mount/" ) )
		{
			var folderName = System.IO.Path.GetFileName( file );
			var assemblyName = System.IO.Path.Combine( file, folderName + ".dll" );

			if ( !System.IO.File.Exists( assemblyName ) )
			{
				Log.Warning( $"Couldn't find {assemblyName} - skipping." );
				continue;
			}

			assemblyName = System.IO.Path.GetFullPath( assemblyName );

			try
			{
				var assembly = Assembly.LoadFile( assemblyName );
				AddAssembly( assembly );
				TryMountFilesystem( folderName );
				ReflectionUtility.RunAllStaticConstructors( assembly );
			}
			catch ( System.Exception e )
			{
				Log.Warning( e, $"Exception when loading {assemblyName}" );
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

	/// <summary>
	/// If /mount/{x}/assets exists, add it to our filesystem
	/// </summary>
	static void TryMountFilesystem( string name )
	{
		using var scope = GlobalContext.MenuScope();

		var path = EngineFileSystem.Root.GetFullPath( $"/mount/{name}/assets" );
		if ( string.IsNullOrWhiteSpace( path ) ) return;
		if ( !System.IO.Directory.Exists( path ) ) return;

		EngineFileSystem.AddAssetPath( $"mnt_{name}", path );
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
			return result;

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

	internal static void AddAssembly( Assembly assembly )
	{
		_system.RegisterTypes( assembly );
	}

	internal static void RemoveAssembly( Assembly assembly )
	{
		_system.UnregisterTypes( assembly );
	}
}


class SteamIntegration : ISteamIntegration
{
	public string GetAppDirectory( long appid )
	{
		if ( !NativeEngine.Steam.SteamApps().IsValid ) return string.Empty;
		return NativeEngine.Steam.SteamApps().GetAppInstallDir( (int)appid );
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
