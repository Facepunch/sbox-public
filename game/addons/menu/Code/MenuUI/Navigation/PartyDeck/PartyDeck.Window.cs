using Sandbox;
using Sandbox.UI;

namespace MenuProject;

/// <summary>
/// The party view and its floating window, available throughout the menu.
/// </summary>
[StyleSheet( "PartyDeck.razor.scss" )]
public partial class PartyDeck : Panel
{
	/// <summary>
	/// The visible party view, used to keep other UI clear of its window.
	/// </summary>
	public static PartyDeck Current { get; private set; }

	/// <summary>
	/// The party view, retained across menu pages.
	/// </summary>
	public static PartyDeck Instance { get; private set; }

	const float WindowWidth = 384;
	const string PositionCookie = "party.window.position";

	/// <summary>
	/// The floating window owned and configured by this party view.
	/// </summary>
	public FloatingWindow Window { get; private set; }

	float? cornerBottom;
	FloatingWindow positionedWindow;
	Vector2? savedWindowPosition;
	Vector2? lastWindowPosition;
	bool windowVisible;

	/// <summary>
	/// Register the party view so the menu overlay can keep a single instance alive.
	/// </summary>
	public PartyDeck()
	{
		Instance = this;
	}

	bool InParty => !LoadingView.IsVisible && (Draft is not null || PartyView.Exists && (InMainMenu || PausePage is not null));

	static bool InMainMenu => !Game.InGame && MainMenu.Instance.IsValid() && MainMenu.Instance.Active;

	/// <summary>
	/// In a game, the pause menu while it's up - the deck lives in it, over its shade. Not while it's
	/// hidden back in the game, or on its way out.
	/// </summary>
	static Panel PausePage => Modals.PauseMenuModal.PauseModal.Open is { IsDeleting: false } pause ? pause : null;

	void UpdateWindow()
	{
		if ( windowVisible && !InParty )
		{
			SaveWindowPosition();
			positionedWindow = null;
		}
		windowVisible = InParty;

		Current = InParty ? this : null;
		Style.Display = InParty ? DisplayMode.Flex : DisplayMode.None;

		Rehome();

		if ( InParty && Window.IsValid() && !Window.IsMinimized && !Window.IsAnimating )
		{
			PositionWindow();
		}

		KeepToastsClear();
	}

	void Rehome()
	{
		// Keep the window on the same input surface while it is moving.
		if ( Window is { IsDragging: true } or { IsAnimating: true } ) return;

		var page = !Game.InGame && MainMenu.Instance.IsValid() ? MainMenu.Instance.Panel?.FindPopupPanel() : PausePage;
		var home = InParty && page.IsValid() ? page : MenuOverlay.Instance;
		if ( !home.IsValid() || Parent == home ) return;

		Parent = home;
		StateHasChanged();
	}

	void PositionWindow()
	{
		if ( positionedWindow != Window )
		{
			positionedWindow = Window;
			savedWindowPosition = Game.Cookies.Get<Vector2?>( PositionCookie, null );
			lastWindowPosition = null;
		}

		var page = MainMenu.Instance?.Panel;
		var pageScale = page.IsValid() ? page.ScaleToScreen : ScaleToScreen;
		var bounds = FindRootPanel().Box.Rect;
		var height = Window.Box.Rect.Height;
		var width = Math.Min( WindowWidth * pageScale, bounds.Width );
		Window.Style.Width = width * Window.ScaleFromScreen;
		if ( bounds.Width <= 0 || bounds.Height <= 0 || height <= 0 ) return;

		var position = Window.Box.Rect.Position;
		if ( !Window.HasBeenDragged )
		{
			position = savedWindowPosition is { } saved && float.IsFinite( saved.x ) && float.IsFinite( saved.y )
				? bounds.Position + saved * bounds.Size
				: bounds.Center - new Vector2( width, height ) * 0.5f;
			position = new Vector2(
				Math.Clamp( position.x, bounds.Left, Math.Max( bounds.Left, bounds.Right - width ) ),
				Math.Clamp( position.y, bounds.Top, Math.Max( bounds.Top, bounds.Bottom - height ) ) );
			Window.MoveTo( position );
		}

		// Store a fraction of the viewport so the position survives resolution changes.
		lastWindowPosition = (position - bounds.Position) / bounds.Size;
	}

	void SaveWindowPosition()
	{
		if ( lastWindowPosition is null ) return;

		// Keep the last expanded position when closing a minimized or hidden window.
		if ( Window.IsValid() && Window == positionedWindow && Window.IsVisible && !Window.IsMinimized && !Window.IsAnimating && Window.HasBeenDragged )
		{
			var bounds = FindRootPanel().Box.Rect;
			if ( bounds.Width > 0 && bounds.Height > 0 )
				lastWindowPosition = (Window.Box.Rect.Position - bounds.Position) / bounds.Size;
		}

		Game.Cookies.Set( PositionCookie, lastWindowPosition.Value );
	}

	void KeepToastsClear()
	{
		var corner = MenuOverlay.Instance?.NotificationCorner;
		if ( !corner.IsValid() ) return;

		float? bottom = null;
		var rect = Window.IsValid() ? Window.Box.Rect : default;
		var screen = FindRootPanel().Box.Rect;
		var toastLeft = screen.Right - corner.Box.Rect.Width;

		// A window moved away from the right edge no longer needs space above the toasts.
		if ( InParty && Window is { IsMinimized: false } && rect.Width > 0 && rect.Right > toastLeft )
		{
			bottom = MathF.Round( (screen.Bottom - rect.Top) * corner.ScaleFromScreen + 12 );
		}

		if ( bottom == cornerBottom ) return;
		cornerBottom = bottom;
		corner.Style.Bottom = bottom;
	}

	/// <summary>
	/// Clear the party window references and restore normal toast placement.
	/// </summary>
	public override void OnDeleted()
	{
		SaveWindowPosition();
		Draft?.Cancel();
		if ( Current == this ) Current = null;
		if ( Instance == this ) Instance = null;

		if ( MenuOverlay.Instance?.NotificationCorner is { } corner && corner.IsValid() )
			corner.Style.Bottom = null;

		base.OnDeleted();
	}
}
