namespace Sandbox.UI;

public partial class Notices
{
	/// <summary>
	/// Sends a phrase and its parameters to a connection, resolving them in the recipient's language.
	/// Must be called from the host.
	/// </summary>
	public static void SendLocalizedNotice( Connection target, string icon, Color iconColor, string phrase, Dictionary<string, string> tokens = null, float seconds = 5 )
	{
		Assert.True( Networking.IsHost, "Must be the host" );

		using ( Rpc.FilterInclude( target ) )
		{
			RpcAddLocalizedNotice( icon, iconColor, phrase, Sandbox.Json.Serialize( tokens ), seconds );
		}
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	private static void RpcAddLocalizedNotice( string icon, Color iconColor, string phrase, string tokensJson, float seconds )
	{
		var tokens = Sandbox.Json.Deserialize<Dictionary<string, string>>( tokensJson );
		AddNotice( icon, iconColor, LocalizedText.Resolve( phrase, tokens ), seconds );
	}
}
