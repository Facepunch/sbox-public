using MenuProject.Overlay;
using MenuProject.Overlay.Overlays;
using Sandbox;
using Sandbox.UI.Construct;

public partial class MenuOverlay : RootPanel
{
	public static MenuOverlay Instance;

	public ToastArea Top;
	public ToastArea BottomRight;
	public ToastArea TopLeft;
	public ToastArea TopCenter;

	/// <summary>
	/// Along the bottom, in the middle - the post-game "thanks for playing" card.
	/// </summary>
	public ToastArea BottomCenter;

	/// <summary>
	/// Holds the bottom right toasts (the post-game popup and friends). The party deck moves it up to
	/// sit above itself when it's showing.
	/// </summary>
	public Panel NotificationCorner;

	public static void Init()
	{
		Shutdown();
		Instance = new MenuOverlay();

		// After the overlay, so it draws over it - see LoadingRoot
		LoadingRoot.Instance = new LoadingRoot();
	}

	public static void Shutdown()
	{
		Instance?.Delete();
		Instance = null;

		LoadingRoot.Instance?.Delete();
		LoadingRoot.Instance = null;
	}

	public MenuOverlay()
	{
		Top = AddChild<ToastArea>( "popup_canvas" );
		TopCenter = AddChild<ToastArea>( "popup_canvas_top" );
		TopLeft = AddChild<ToastArea>( "popup_canvas_topleft" );
		BottomCenter = AddChild<ToastArea>( "popup_canvas_bottomcenter" );
		NotificationCorner = AddChild<Panel>( "notification-corner" );
		BottomRight = NotificationCorner.AddChild<ToastArea>( "popup_canvas_bottomright" );

		AddChild<MicOverlay>();
		AddChild<SubtitleOverlay>();
		AddChild<ChatOverlay>();

		// The party view owns its window and moves between here and the menu page.
		AddChild<MenuProject.PartyDeck>();
	}

	public override void Tick()
	{
		base.Tick();

		// The deck spends most of its time living in the menu page, and goes with it if the page is
		// torn down (the avatar editor swaps the scene) - bring a new one back
		if ( !MenuProject.PartyDeck.Instance.IsValid() )
			AddChild<MenuProject.PartyDeck>();
	}

	// No UpdateScale override - the root panel's own sizes things by screen height (1080 tall is 1:1),
	// the same as the main menu. It used to go by desktop scale, which left everything in here (the
	// post-game toast, the party deck when it's up here) smaller than the menu it sits over on any
	// screen taller than 1080.

	public static void Show( Panel content, float duration = 4f )
		=> Instance.Top.Show( content, duration );

	public static void Show( string message, string icon = "info", float duration = 4f )
		=> Show( BuildMessage( message, icon ), duration );

	public static void Queue( Panel content, float duration = 4f )
		=> Instance.Top.Queue( content, duration );

	public static void Queue( string message, string icon = "info", float duration = 4f )
		=> Queue( BuildMessage( message, icon ), duration );

	public static void Question( string message, string icon, Action yes, Action no )
	{
		var content = new Panel( null, "popup has-message has-options" );
		content.Add.Icon( icon );
		content.Add.Label( message, "message" );

		var options = content.Add.Panel( "options" );
		content.Add.Panel( "progress-bar" );

		var area = Instance.Top;
		area.Queue( content, duration: 10f, clickToDismiss: false );

		bool answered = false;
		void Answer( Action action )
		{
			if ( answered ) return;
			answered = true;
			area.Dismiss( content );
			action?.Invoke();
		}
		options.AddChild( new Button( null, "close", null, () => Answer( no ) ) );
		options.AddChild( new Button( null, "done", null, () => Answer( yes ) ) );
	}

	static Panel BuildMessage( string message, string icon )
	{
		var p = new Panel( null, "popup has-message" );
		p.Add.Icon( icon );
		p.Add.Label( message, "message" );
		return p;
	}
}
