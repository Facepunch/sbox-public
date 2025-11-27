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
	/// If this app is installed we'll return the folder in which it is installed
	/// </summary>
	public string GetAppDirectory( long appid )
	{
		if ( !IsAppInstalled( appid ) ) return null;

		// Maybe we want to return a sandboxed, readonly filesystem to this directory instead?
		// but even then, what if there are files with passwords or rcon details in, it's
		// not 100% safe, it'll never be truly sandboxed, so should we even try?

		return system.Steam?.GetAppDirectory( appid ) ?? null;
	}
}
