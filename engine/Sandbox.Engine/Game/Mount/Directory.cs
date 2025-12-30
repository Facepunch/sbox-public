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
	/// Set mounted or not mounted. Called by user via editor.
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
			TryMountFilesystem( name );
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
		var path = EngineFileSystem.Root.GetFullPath( $"/mount/{name}/assets" );
		if ( string.IsNullOrWhiteSpace( path ) ) return;
		if ( !System.IO.Directory.Exists( path ) ) return;

		EngineFileSystem.AddAssetPath( $"mnt_{name}", path );
	}

	internal static bool TryLoad( string filename, ResourceType type, out object resource )
	{
		resource = default;

		if ( !filename.StartsWith( "mount://" ) ) return false;

		var sourceName = filename.Substring( 8 );

		var i = sourceName.IndexOf( '/' );
		sourceName = sourceName.Substring( 0, i );

		var source = Get( sourceName );
		if ( source is null )
		{
			Log.Warning( $"Couldn't find source \"{source}\"" );
			return false;
		}

		var entry = source.Resources.FirstOrDefault( x => string.Equals( x.Path, filename, StringComparison.OrdinalIgnoreCase ) );
		if ( entry is null )
		{
			Log.Warning( $"Couldn't find file \"{filename}\" in {source.Ident}" );
			return false;
		}

		resource = SyncContext.RunBlocking( entry.GetOrCreate() );

		if ( resource is null )
		{
			Log.Warning( $"Loading \"{filename}\" returned null!" );
			return false;
		}

		return resource is not null;
	}

	internal static async Task<object> TryLoadAsync( string filename, ResourceType type )
	{
		if ( !filename.StartsWith( "mount://" ) ) return null;

		var sourceName = filename.Substring( 8 );

		var i = sourceName.IndexOf( '/' );
		sourceName = sourceName.Substring( 0, i );

		var source = Get( sourceName );
		if ( source is null )
		{
			Log.Warning( $"Couldn't find source \"{source}\"" );
			return null;
		}

		var entry = source.Resources.FirstOrDefault( x => string.Equals( x.Path, filename, StringComparison.OrdinalIgnoreCase ) );
		if ( entry is null )
		{
			Log.Warning( $"Couldn't find file \"{filename}\" in {source.Ident}" );
			return null;
		}

		var resource = await entry.GetOrCreate();

		if ( resource is null )
		{
			Log.Warning( $"Loading \"{filename}\" returned null!" );
			return null;
		}

		return resource;
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

	public bool IsDlcInstalled( long appid )
	{
		if ( !NativeEngine.Steam.SteamApps().IsValid ) return false;
		return NativeEngine.Steam.SteamApps().BIsDlcInstalled( (int)appid );
	}
}
