namespace Sandbox.Mounting;

internal interface ISteamIntegration
{
	public bool IsAppInstalled( long appid );
	public bool IsDlcInstalled( long appid );
	public string GetAppDirectory( long appid );
}
