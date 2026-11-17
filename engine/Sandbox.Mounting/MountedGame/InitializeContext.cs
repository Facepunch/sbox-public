namespace Sandbox.Mounting;

public ref struct InitializeContext
{
	private MountHost system;

	internal InitializeContext( MountHost system )
	{
		this.system = system;
	}

	// progress output, error recording etc
	// tools to find games on Steam, and their install status
	public void AddError( string v )
	{
		// TODO
	}

	/// <summary>
	/// Return true if this app is installed on Steam
	/// </summary>
	public bool IsAppInstalled( long appid )
	{
		return system.Steam?.IsAppInstalled( appid ) ?? false;
	}

	/// <summary>
	/// Return true if this DLC is installed on Steam
	/// </summary>
	public bool IsDlcInstalled( long appid )
	{
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

		// Note: a read-only, rooted filesystem stops writes and directory traversal above the app
		// root, but it can't hide files within that game's folder (configs, etc.) from a mount.
		return system.Steam?.GetFileSystem( appid );
	}
}
