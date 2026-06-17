namespace Editor;

public static partial class EditorUtility
{
	public static class Streaming
	{
		/// <summary>
		/// Returns true if the stream is active
		/// </summary>
		public static bool IsActive => Sandbox.Engine.Streamer.IsActive;

		/// <summary>
		/// Init a stream service
		/// </summary>
		public static async Task<bool> Connect( string serviceName )
		{
			return await Sandbox.Engine.Streamer.Init( serviceName );
		}

		/// <summary>
		/// Disconnect from streaming service
		/// </summary>
		public static void Disconnect( string serviceName )
		{
			Sandbox.Engine.Streamer.Shutdown( serviceName );
		}

		/// <summary>
		/// Begin linking a third-party service to the player's account (eg "Twitch").
		/// </summary>
		public static async Task<string> BeginServiceLink( string service )
		{
			var result = await Backend.Account.BeginServiceLink( service );
			return result.Url;
		}

		/// <summary>
		/// List the player's linked services with their public info (name, avatar). Contains no tokens.
		/// </summary>
		public static async Task<List<LinkedService>> ListServices()
		{
			var services = await Backend.Account.ListServices();
			return services.Select( x => new LinkedService( x.Type, x.Id, x.Name, x.Avatar ) ).ToList();
		}

		/// <summary>
		/// Public, token-free info about a third-party service account (eg Twitch) linked to the player.
		/// This is the menu-facing proxy for the backend's service link data.
		/// </summary>
		public record struct LinkedService( string Service, string Id, string Name, string Avatar );
	}
}
