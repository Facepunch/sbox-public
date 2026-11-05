using Sandbox.UI;

namespace Sandbox.Engine;

/// <summary>
/// Carries the splash on over the menu once the game has taken over its window, still showing what's
/// loading, then fades it away when the menu has finished coming in, so the splash fades out onto a
/// settled menu rather than giving way to one that's still filling in.
/// </summary>
internal sealed class StartupScreen : RootPanel
{
	/// <summary>
	/// Frames under this long count as the menu having settled.
	/// </summary>
	const float SmoothFrame = 0.05f;

	/// <summary>
	/// How long frames have to stay smooth, once everything's in, before fading out.
	/// </summary>
	const float SettleTime = 0.3f;

	/// <summary>
	/// Fade out after this long however the menu is doing, rather than staying up.
	/// </summary>
	const float GiveUpTime = 8f;

	const float FadeTime = 0.4f;

	/// <summary>
	/// Progress for each stage after the takeover. Startup sets the ones before it. Roughly how much of
	/// startup has passed by then, so the bar moves at an even pace.
	/// </summary>
	const float BuildingMenu = 0.4f;
	const float Held = 0.72f;
	const float AlmostReady = 0.93f;

	static StartupScreen current;
	static readonly List<Hold> holds = new();

	/// <summary>
	/// True until it starts fading, while nobody can see the menu behind it.
	/// </summary>
	internal static bool IsCovering => current is { fading: false };

	/// <summary>
	/// Something the startup screen is waiting on. It stays up, showing this status, until the hold is disposed.
	/// </summary>
	sealed class Hold( string status ) : IDisposable
	{
		public string Status { get; } = status;

		public void Dispose() => holds.Remove( this );
	}

	/// <summary>
	/// Keep the startup screen up while something is about to appear, so it doesn't pop in after the fade.
	/// Does nothing when the startup screen isn't up.
	/// </summary>
	internal static IDisposable HoldFor( string status )
	{
		var hold = new Hold( status );
		if ( IsCovering ) holds.Add( hold );
		return hold;
	}

	readonly GameStartupWindow splash;
	Texture image;
	Hold shownHold;
	RealTimeSince sinceShown = 0;
	RealTimeSince sinceUnsettled = 0;
	RealTimeSince sinceTick = 0;
	RealTimeSince sinceFading;
	RealTimeSince sinceHeld;
	bool fading;

	/// <summary>
	/// Take over from the splash once the game has its window, showing the menu being built.
	/// </summary>
	internal static void Show( GameStartupWindow splash )
	{
		using var scope = GlobalContext.MenuScope();
		current = new StartupScreen( splash );
	}

	StartupScreen( GameStartupWindow splash )
	{
		this.splash = splash;

		Style.BackgroundSizeX = Length.Percent( 100 );
		Style.BackgroundSizeY = Length.Percent( 100 );
		Style.PointerEvents = PointerEvents.All;

		// Roots made first draw last, so put this one first to draw over the menu
		var roots = GlobalContext.Current.UISystem.RootPanels;
		roots.Remove( this );
		roots.Insert( 0, this );

		ShowProgress( BuildingMenu, "Building the menu" );
	}

	/// <summary>
	/// Draw the splash heading for this progress. It's drawn every frame, so the bar eases along and its sheen keeps moving.
	/// </summary>
	void ShowProgress( float value, string text )
	{
		var size = Screen.Size;
		if ( splash.DrawToTexture( (int)size.x, (int)size.y, value, text ) is not { } drawn || drawn == image ) return;

		// A new texture when the size changes, otherwise the same one redrawn
		image = drawn;
		Style.SetBackgroundImage( drawn );
	}

	public override void Tick()
	{
		base.Tick();

		var frameTime = (float)sinceTick;
		sinceTick = 0;

		if ( fading )
		{
			Style.Opacity = 1 - Math.Clamp( sinceFading / FadeTime, 0, 1 );
			if ( sinceFading >= FadeTime ) Delete();
			return;
		}

		if ( frameTime > SmoothFrame )
			sinceUnsettled = 0;

		// Whatever's still holding it up, don't keep covering the menu for good
		if ( sinceShown >= GiveUpTime )
			holds.Clear();

		if ( holds.FirstOrDefault() is { } hold )
		{
			// No way to tell how far along it is, so ease towards the next stage over a couple of seconds
			if ( hold != shownHold ) sinceHeld = 0;
			shownHold = hold;
			sinceUnsettled = 0;
			ShowProgress( Held.LerpTo( AlmostReady, sinceHeld / 2f ), hold.Status );
			return;
		}

		var settled = Math.Clamp( sinceUnsettled / SettleTime, 0, 1 );
		ShowProgress( AlmostReady.LerpTo( 1, settled ), "Almost ready" );

		// Let the bar finish filling before fading
		if ( (settled < 1 || splash.ShownProgress < 0.99f) && sinceShown < GiveUpTime )
			return;

		fading = true;
		sinceFading = 0;
		Style.PointerEvents = PointerEvents.None;
	}

	public override void OnDeleted()
	{
		base.OnDeleted();

		splash.Dispose();
		holds.Clear();
		if ( current == this ) current = null;
	}
}
