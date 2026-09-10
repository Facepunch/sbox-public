using Sandbox.Engine;
using Sandbox.Modals;

namespace Sandbox;

public static partial class MenuUtility
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
		/// Open the service connector modal, which allows the player to link/unlink third-party services like Twitch.
		/// </summary>
		public static void OpenModal()
		{
			using ( IMenuDll.Current.PushScope() )
			{
				IModalSystem.Current.ServiceConnector();
			}
		}
	}
}
