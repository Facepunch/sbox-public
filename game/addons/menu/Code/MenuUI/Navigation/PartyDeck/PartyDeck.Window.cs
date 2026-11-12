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

	/// <summary>
	/// As wide as the friends list off the dock (FriendsListModal.MinWidth), so the two read as one set
	/// </summary>
	const float WindowWidth = 384;

	/// <summary>
	/// The floating window owned and configured by this party view.
	/// </summary>
	public FloatingWindow Window { get; private set; }

	float? cornerBottom;
	bool windowVisible;

	/// <summary>
	/// Register the party view so the menu overlay can keep a single instance alive.
	/// </summary>
	public PartyDeck()
	{
		Instance = this;
	}

	bool InParty => !LoadingView.IsVisible && (Draft is not null || PartyView.Exists && ShownOnPlatform);

	/// <summary>
	/// Brought up from the platform menu's dock - its party button toggles it
	/// </summary>
	internal static bool ShownOnPlatform { get; set; }

	/// <summary>
	/// The dock's party button it's pinned over on the platform menu
	/// </summary>
	internal static Panel PlatformAnchor { get; set; }

	void UpdateWindow()
	{
		if ( windowVisible && !InParty )
			sizedHeight = 0;
		windowVisible = InParty;

		Current = InParty ? this : null;
		// Kept up while the window's on its way out, so it can fade rather than vanish
		var leaving = Children.Any( x => x is FloatingWindow { IsDeleting: true } );
		Style.Display = InParty || leaving ? DisplayMode.Flex : DisplayMode.None;

		// Unseen till it's pinned over the dock, then fades up into place - not a frame of it wherever it was before
		if ( Window.IsValid() && !InParty )
			Window.SetClass( "pinned", false );

		EaseHeight();

		if ( InParty && Window.IsValid() && !Window.IsMinimized && !Window.IsAnimating )
		{
			PinOverDock();
		}

		KeepToastsClear();
	}

	/// <summary>
	/// What's in the window, and the box round it that clips it to the height it's easing to
	/// </summary>
	Panel Sized, Sizer;
	float sizedHeight;

	/// <summary>
	/// Ease the window's height to what's in it - settings opening or closing, members coming and going - rather than
	/// jumping to it. Shown again, it starts where it is.
	/// </summary>
	void EaseHeight()
	{
		if ( !Sizer.IsValid() || !Sized.IsValid() ) return;

		var target = Sized.Box.Rect.Height * Sizer.ScaleFromScreen;
		if ( target <= 0 ) return;

		var next = sizedHeight <= 0 ? target : MathX.Approach( sizedHeight.LerpTo( target, 1 - MathF.Exp( -RealTime.Delta * 18 ) ), target, 0.5f );
		if ( next == sizedHeight ) return;

		sizedHeight = next;
		Sizer.Style.Height = Length.Pixels( sizedHeight );
	}

	/// <summary>
	/// Over the dock's party button, its left edge with the button's and clear of the dock - put there, not dragged
	/// </summary>
	void PinOverDock()
	{
		if ( !PlatformAnchor.IsValid() ) return;

		var bounds = FindRootPanel().Box.Rect;
		var width = Math.Min( WindowWidth * ScaleToScreen, bounds.Width );
		Window.Style.Width = width * Window.ScaleFromScreen;

		var height = Window.Box.Rect.Height;
		if ( bounds.Width <= 0 || height <= 0 ) return;

		var button = PlatformAnchor.Box.Rect;
		var left = Math.Clamp( button.Left, bounds.Left, Math.Max( bounds.Left, bounds.Right - width ) );
		var top = button.Top - Platform.Dock.PopupOffset * PlatformAnchor.ScaleToScreen - height;
		Window.MoveTo( new Vector2( left, top ) );

		if ( !Window.HasClass( "pinned" ) )
		{
			Window.SetClass( "pinned", true );
			sinceShown = 0;
			growing = true;
		}

		if ( growing ) Grow( button, new Rect( left, top, width, height ) );
	}

	const float GrowTime = 0.18f;
	RealTimeSince sinceShown;
	bool growing;

	/// <summary>
	/// Up out of what it came up from on the dock - from exactly where that is to where the window goes, easing out
	/// </summary>
	void Grow( Rect from, Rect to )
	{
		var t = Math.Clamp( sinceShown / GrowTime, 0, 1 );
		if ( t >= 1 )
		{
			Window.Style.Transform = null;
			growing = false;
			return;
		}

		var ease = 1 - MathF.Pow( 1 - t, 3 );
		var rect = new Rect(
			MathX.Lerp( from.Left, to.Left, ease ),
			MathX.Lerp( from.Top, to.Top, ease ),
			MathX.Lerp( from.Width, to.Width, ease ),
			MathX.Lerp( from.Height, to.Height, ease ) );

		var transform = new PanelTransform();
		transform.AddTranslate( (rect.Left - to.Left) * Window.ScaleFromScreen, (rect.Top - to.Top) * Window.ScaleFromScreen );
		transform.AddScale( new Vector3( rect.Width / to.Width, rect.Height / to.Height, 1 ) );

		Window.Style.TransformOriginX = 0;
		Window.Style.TransformOriginY = 0;
		Window.Style.Transform = transform;
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
		Draft?.Cancel();

		if ( Current == this ) Current = null;
		if ( Instance == this ) Instance = null;

		if ( MenuOverlay.Instance?.NotificationCorner is { } corner && corner.IsValid() )
			corner.Style.Bottom = null;

		base.OnDeleted();
	}
}
