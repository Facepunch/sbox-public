namespace Sandbox.Mounting;

internal struct Configuration
{
	public ISteamIntegration SteamIntegration { get; set; }

	internal bool IsDedicatedServer { get; set; }
}
