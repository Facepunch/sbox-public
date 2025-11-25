using Sandbox;

namespace Menu;

public static class StreamerMode
{
	public static void ActiveStreamPopup( Panel parent )
	{
		var popup = new Popup( parent, Popup.PositionMode.BelowLeft, 8.0f );
		popup.AddClass( "medium" );

		if ( Streamer.IsActive )
		{
			popup.Title = $"Connected To {Streamer.Service}";
			popup.Icon = "wifi";

			popup.AddChild( new Button( $"Disconnect", () =>
			{
				MenuUtility.DisconnectStream();
				popup.Delete();
			} ) );
		}
		else
		{
			popup.Title = "Streamer Mode";
			popup.Icon = "wifi";

			popup.AddChild( new Button( $"Connect to Twitch", "live_tv", null, () =>
			{
				MenuUtility.ConnectStream( StreamService.Twitch );
				popup.Delete();
			} ) );
		}
	}

	//
	// Tests
	//

	//	[Event.Streamer.ChatMessage]
	public static void OnStreamMessage( StreamChatMessage message )
	{
		Log.Trace( $"[{message.Username}] {message.Message}" );
	}

	//	[Event.Streamer.JoinChat]
	public static void OnStreamJoinEvent( string user )
	{
		Log.Info( $"{user} joined" );
	}

	//	[Event.Streamer.LeaveChat]
	public static void OnStreamLeaveEvent( string user )
	{
		Log.Info( $"{user} left" );
	}
}
