using Sandbox.UI.Construct;
using Sandbox.UI.Navigation;

public partial class MainMenuPanel : RootPanel
{
	public static MainMenuPanel Instance { get; private set; }

	public Panel Popup { get; protected set; }
	public Label PopupTitle { get; protected set; }
	public Label PopupMessage { get; protected set; }

	public NavigationHost Navigator { get; protected set; }

	public MainMenuPanel()
	{
		Popup = Add.Panel( "popup" );
		{
			var inner = Popup.Add.Panel( "inner" );
			PopupTitle = inner.Add.Label( "...", "title" );
			PopupMessage = inner.Add.Label( "...", "message" );
			inner.AddChild( new Button( "Close", () => SetClass( "popup-active", false ) ) );
		}

		Instance = this;
	}

	protected override int BuildHash() => HashCode.Combine( Sandbox.LoadingScreen.IsVisible, Sandbox.LoadingScreen.Title );

	internal void ShowPopup( string type, string title, string subtitle )
	{
		AddClass( "popup-active" );

		PopupTitle.Text = title;
		PopupMessage.Text = subtitle;
	}

	public override void Tick()
	{
		base.Tick();

		if ( !IsVisible ) return;

		SetClass( "is-vr", IsVR );
		SetClass( "has-streamer-account", Sandbox.MenuEngine.Account.HasLinkedStreamerServices );
	}

	public override void OnButtonEvent( ButtonEvent e )
	{
		base.OnButtonEvent( e );
	}
}
