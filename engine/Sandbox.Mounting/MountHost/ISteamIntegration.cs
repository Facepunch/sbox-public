namespace Sandbox.Mounting;

internal interface ISteamIntegration
{
	public bool IsAppInstalled( long appid );
	public bool IsAppOwned( long appid );
	public bool IsDlcInstalled( long appid );

	/// <summary>
	/// A read-only virtual filesystem rooted at the app's install directory, or null if not installed.
	/// Mounts get this instead of a raw path so they never touch the disk directly.
	/// </summary>
	public BaseFileSystem GetFileSystem( long appid );

	/// <summary>
	/// A read-only virtual filesystem rooted at the app's downloaded Workshop content, one folder per item,
	/// or null if the app isn't installed or has no Workshop content.
	/// </summary>
	public BaseFileSystem GetWorkshopFileSystem( long appid );
}
