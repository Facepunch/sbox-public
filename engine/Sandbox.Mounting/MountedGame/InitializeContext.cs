namespace Sandbox.Mounting;

public ref struct InitializeContext
{
	private MountHost system;
	private BaseGameMount mount;

	internal InitializeContext( MountHost system, BaseGameMount mount )
	{
		this.system = system;
		this.mount = mount;
	}

	// progress output, error recording etc
	// tools to find games on Steam, and their install status
	public void AddError( string v )
	{
		// TODO
	}

	/// <summary>
	/// Dedicated servers have no SteamApps, so the host points each mount at its game install from the config.
	/// Null if there isn't one or it doesn't exist.
	/// </summary>
	readonly string ServerInstallPath
	{
		get
		{
			var path = mount?.Config?.Path;
			if ( string.IsNullOrWhiteSpace( path ) ) return null;

			return System.IO.Directory.Exists( path ) ? path : null;
		}
	}

	/// <summary>
	/// Return true if this app is installed on Steam
	/// </summary>
	public bool IsAppInstalled( long appid )
	{
		if ( system.IsDedicatedServer ) return ServerInstallPath is not null;

		return system.Steam?.IsAppInstalled( appid ) ?? false;
	}

	/// <summary>
	/// Return true if this DLC is installed on Steam
	/// </summary>
	public bool IsDlcInstalled( long appid )
	{
		// sure? maybe this should be a settings list
		if ( system.IsDedicatedServer ) return ServerInstallPath is not null;

		return system.Steam?.IsDlcInstalled( appid ) ?? false;
	}

	/// <summary>
	/// If this app is installed, return a read-only virtual filesystem rooted at its install
	/// directory. Returns null if the app isn't installed. The mount only ever knows the AppID -
	/// it never sees or handles a real disk path, so this works from sandboxed mount code.
	/// </summary>
	public BaseFileSystem GetFileSystem( long appid )
	{
		if ( !IsAppInstalled( appid ) ) return null;

		if ( system.IsDedicatedServer )
			return new LocalFileSystem( ServerInstallPath, makereadonly: true );

		return system.Steam?.GetFileSystem( appid );
	}
}
