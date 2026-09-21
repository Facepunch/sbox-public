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

	public static void Init()
	{
		Shutdown();
		Instance = new MenuOverlay();
	}

	public static void Shutdown()
	{
		Instance?.Delete();
		Instance = null;
	}

	public MenuOverlay()
	{
		Top = AddChild<ToastArea>( "popup_canvas" );
		TopCenter = AddChild<ToastArea>( "popup_canvas_top" );
		TopLeft = AddChild<ToastArea>( "popup_canvas_topleft" );
		var corner = AddChild<Panel>( "notification-corner" );
		BottomRight = corner.AddChild<ToastArea>( "popup_canvas_bottomright" );
		corner.AddChild<MenuProject.PartyJoinStatus>();

		AddChild<LoadingOverlay>();
		AddChild<MicOverlay>();
		AddChild<SubtitleOverlay>();
		AddChild<ChatOverlay>();
	}

	protected override void UpdateScale( Rect screenSize )
	{
		Scale = Screen.DesktopScale;

		var minimumHeight = 1080.0f * Screen.DesktopScale;

		if ( screenSize.Height < minimumHeight )
		{
			Scale *= screenSize.Height / minimumHeight;
		}
	}

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
