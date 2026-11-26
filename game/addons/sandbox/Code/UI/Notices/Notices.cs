namespace Sandbox.UI;

/// <summary>
/// Displays transient HUD notices and delivers messages to individual players.
/// </summary>
public partial class Notices : PanelComponent
{
	/// <summary>
	/// The notice display in the active scene.
	/// </summary>
	public static Notices Current => Game.ActiveScene?.Get<Notices>();

	/// <summary>
	/// Space between the list of who is talking and the notices above it
	/// </summary>
	const float VoicesGap = 8;

	/// <summary>
	/// Displays already formatted text without interpreting leading hashes as phrase references.
	/// </summary>
	public static NoticePanel AddNotice( string text, float seconds = 5 ) => Create( null, Color.White, text, seconds );

	/// <summary>
	/// Displays an icon and already formatted text without interpreting leading hashes as phrase references.
	/// </summary>
	public static NoticePanel AddNotice( string icon, Color iconColor, string text, float seconds = 5 ) => Create( icon, iconColor, text, seconds );

	/// <summary>
	/// The colour of a notice's icon, and its timer bar, unless it is given another
	/// </summary>
	public static readonly Color DefaultColor = new Color( 0.302f, 0.565f, 0.988f ); // 4D90FC

	/// <summary>
	/// Displays an icon in the default colour and already formatted text.
	/// </summary>
	public static NoticePanel AddNotice( string icon, string text, float seconds = 5 ) => Create( icon, DefaultColor, text, seconds );

	/// <summary>
	/// Every notice is made here, so anything a notice can be configured with (icon, colour, duration) goes through this one place.
	/// </summary>
	static NoticePanel Create( string icon, Color iconColor, string text, float seconds )
	{
		var current = Current;
		if ( current == null || current.Panel == null ) return null;

		var notice = new NoticePanel( icon, iconColor, text, seconds );
		current.Panel.AddChild( notice );

		return notice;
	}

	/// <summary>
	/// Send a notice to a specific connection. Must be called from the host.
	/// </summary>
	public static void SendNotice( Connection target, string icon, Color iconColor, string text, float seconds = 5 )
	{
		Assert.True( Networking.IsHost, "Must not be the host" );

		using ( Rpc.FilterInclude( target ) )
		{
			RpcAddNotice( icon, iconColor, text, seconds );
		}
	}

	[Rpc.Broadcast]
	private static void RpcAddNotice( string icon, Color iconColor, string text, float seconds )
	{
		AddNotice( icon, iconColor, text, seconds );
	}

	protected override void OnUpdate()
	{
		base.OnUpdate();

		// notices stack themselves, the layout lives in Notices.cs.scss
		Panel.Style.Display = Hud.IsVisible( HudElement.Notices ) ? DisplayMode.Flex : DisplayMode.None;
		Panel.Style.Right = Hud.MarginLength;

		var voices = Facepunch.UI.Voices.ListHeight;
		var bottom = Hud.StackBottom + (voices > 0 ? voices : 0);
		Panel.Style.Bottom = Length.Pixels( bottom * Panel.ScaleFromScreen + (voices > 0 ? VoicesGap : 0) );
	}
}
