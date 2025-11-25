using MenuProject.Overlay.Overlays;
using Sandbox;
using Sandbox.UI.Construct;

public partial class MenuOverlay : RootPanel
{
	public static MenuOverlay Instance;

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

	public Panel PopupCanvasTop;
	public Panel PopupCanvas;
	public Panel PopupCanvasTopLeft;
	public Panel PopupCanvasBottomRight;

	static Queue<Panel> Popups = new Queue<Panel>();
	static Panel CurrentPopup;

	public MenuOverlay()
	{
		PopupCanvas = Add.Panel( "popup_canvas" );
		PopupCanvasTop = Add.Panel( "popup_canvas_top" );
		PopupCanvasTopLeft = Add.Panel( "popup_canvas_topleft" );
		PopupCanvasBottomRight = Add.Panel( "popup_canvas_bottomright" );

		if ( !Application.IsEditor )
		{
			AddChild<VersionOverlay>();
		}

		AddChild<LoadingOverlay>();
		AddChild<MicOverlay>();
	}

	protected override void UpdateScale( Rect screenSize )
	{
		Scale = Screen.DesktopScale;

		var minimumHeight = 1080.0f * Screen.DesktopScale;

		// If the screen height is less than 1080, it's less than supported
		// so scale the screen size down.
		if ( screenSize.Height < minimumHeight )
		{
			Scale *= screenSize.Height / minimumHeight;
		}
	}

	/// <summary>
	/// Dismiss the current popup
	/// </summary>
	public static async Task SkipPopup()
	{
		if ( CurrentPopup == null ) return;

		CurrentPopup.Delete();

		await GameTask.DelayRealtimeSeconds( 0.3f );

		CurrentPopup = null;

		_ = SwitchPopup();
	}

	/// <summary>
	/// Dismiss the current popup
	/// </summary>
	static async Task SwitchPopup()
	{
		if ( Popups.Count == 0 )
			return;

		CurrentPopup = Popups.Dequeue();
		CurrentPopup.Parent = Instance.PopupCanvas;

		var popup = CurrentPopup;

		if ( CurrentPopup.HasClass( "has-options" ) )
			await GameTask.DelayRealtimeSeconds( 6.0f );

		await GameTask.DelayRealtimeSeconds( 4.0f );


		if ( CurrentPopup != popup )
			return;

		await SkipPopup();

	}

	/// <summary>
	/// Dismiss the current popup
	/// </summary>
	public static void AddPopup( Panel popup, string withClass = "popup" )
	{
		if ( withClass is not null )
		{
			popup.AddClass( withClass );
		}

		popup.AddClass( "hidden" );

		Popups.Enqueue( popup );

		if ( CurrentPopup is null )
		{
			_ = SwitchPopup();
		}
	}

	/// <summary>
	/// Add a message
	/// </summary>
	public static void Message( string message, string icon = "info" )
	{
		var popup = new Panel( null, "has-message" );
		if ( popup == null ) return;

		popup.Add.Icon( icon );

		popup.Add.Label( message, "message" );
		popup.AddEventListener( "onmousedown", () => _ = SkipPopup() );

		AddPopup( popup );
	}

	/// <summary>
	/// Add a message
	/// </summary>
	public static void Message( string type, string message, string subtitle )
	{
		var popup = new Panel( null, "has-message" );
		if ( popup == null ) return;

		popup.AddClass( type );

		popup.Add.Label( message, "message" );
		popup.Add.Label( subtitle, "message" );
		popup.AddEventListener( "onmousedown", () => _ = SkipPopup() );

		AddPopup( popup );
	}

	/// <summary>
	/// Add a message
	/// </summary>
	public static void Message( string message, Texture image )
	{
		var popup = new Panel( null, "has-message" );
		if ( popup == null ) return;

		var icon = popup.Add.Panel();
		icon.AddClass( "iconpanel" );
		icon.Style.SetBackgroundImage( image );

		popup.Add.Label( message, "message" );
		popup.AddEventListener( "onmousedown", () => _ = SkipPopup() );

		AddPopup( popup );
	}

	/// <summary>
	/// Add a message
	/// </summary>
	public static void Message( string message, string subtitle, Texture image )
	{
		var popup = new Panel( null, "has-message" );
		if ( popup == null ) return;

		var icon = popup.Add.Panel();
		icon.AddClass( "iconpanel" );
		icon.Style.SetBackgroundImage( image );

		popup.Add.Label( message, "message" );

		if ( subtitle != null )
		{
			popup.Add.Label( subtitle, "subtitle" );
			popup.AddClass( "has-subtitle" );
		}

		popup.AddEventListener( "onmousedown", () => _ = SkipPopup() );

		AddPopup( popup );
	}

	/// <summary>
	/// Add a question
	/// </summary>
	public static void Question( string message, string icon, Action yes, Action no )
	{
		var popup = new Panel( null, "has-message has-options" );
		if ( popup == null ) return;

		popup.Add.Icon( icon );
		popup.Add.Label( message, "message" );

		var options = popup.Add.Panel( "options" );

		options.AddChild( new Button( null, "close", null, () => { no?.Invoke(); _ = SkipPopup(); } ) );
		options.AddChild( new Button( null, "done", null, () => { yes?.Invoke(); _ = SkipPopup(); } ) );

		popup.Add.Panel( "progress-bar" );

		AddPopup( popup );
	}
}
