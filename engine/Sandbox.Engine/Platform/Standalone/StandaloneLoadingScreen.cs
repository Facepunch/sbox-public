using Sandbox.Engine;

namespace Sandbox;

/// <summary>
/// An exported game's loading screen. It's neutral, not the platform's: the game's startup image on black and a plain
/// bar. It carries on from the startup splash, laid out the same, through the game's own load, then comes back for every
/// scene load after, unless the game shows its own <see cref="StandaloneLoadingScene"/>. It's drawn straight into the
/// frame's overlay, since an exported game has no menu UI to draw it.
/// </summary>
internal static class StandaloneLoadingScreen
{
	internal static readonly Color TrackColor = new( 0.12f, 0.12f, 0.12f );
	internal static readonly Color BarColor = new( 0.85f, 0.85f, 0.85f );

	/// <summary>
	/// Where the bar is by the time the game has its window, if the splash didn't say.
	/// </summary>
	const float StartupProgress = 0.4f;

	/// <summary>
	/// Loads can rarely say how far along they are, so the bar eases towards this, and fills once they're done.
	/// </summary>
	const float AlmostReady = 0.93f;

	/// <summary>
	/// Frames under this long count as the game having settled, and need to for this long before it fades.
	/// </summary>
	const float SmoothFrame = 0.05f;
	const float SettleTime = 0.3f;

	/// <summary>
	/// Fade out after this long anyway, rather than staying up over a game that never settles.
	/// </summary>
	const float GiveUpTime = 8f;

	const float FadeTime = 0.4f;

	static Texture image;
	static bool imageLoaded;
	static float opacity = 1;
	static float shownProgress;
	static float loadStart = StartupProgress;
	static bool loading = true;
	static RealTimeSince sinceLoading;
	static RealTimeSince sinceLoaded;
	static RealTimeSince sinceUnsettled;
	static RealTimeSince sinceTick;

	static bool IsActive => Application.IsStandalone && !Application.IsHeadless;

	static bool IsLoading => Standalone.IsLoadingGame || LoadingScreen.IsVisible;

	/// <summary>
	/// Take over from the startup splash once the game has its window, carrying its bar on.
	/// </summary>
	internal static void TakeOver( float splashProgress )
	{
		shownProgress = splashProgress;
		loadStart = Math.Max( loadStart, splashProgress );
	}

	/// <summary>
	/// Follow what's loading. Every frame, on the main thread.
	/// </summary>
	internal static void Tick()
	{
		if ( !IsActive ) return;

		if ( !imageLoaded )
		{
			imageLoaded = true;
			image = LoadImage();
		}

		var frameTime = (float)sinceTick;
		sinceTick = 0;

		// The game's own loading scene has taken over - get straight out of its way, rather than waiting to settle
		if ( StandaloneLoadingScene.IsShowing )
		{
			opacity = 0;
			loading = false;
			return;
		}

		float target;

		if ( IsLoading )
		{
			if ( !loading )
			{
				// Another load, over whatever was showing, with the bar from the start
				loading = true;
				loadStart = 0;
				shownProgress = 0;
				sinceLoading = 0;
			}

			// A game with its own loading scene shows that instead, from the next frame. Only cover the gap when this
			// was already up, like at startup before the scene is known.
			if ( StandaloneLoadingScene.Path is null || opacity > 0 )
				opacity = 1;
			sinceUnsettled = 0;
			target = loadStart.LerpTo( AlmostReady, LoadFraction() );
		}
		else
		{
			if ( loading )
			{
				loading = false;
				sinceLoaded = 0;
			}

			if ( opacity <= 0 ) return;

			if ( frameTime > SmoothFrame )
				sinceUnsettled = 0;

			// Fill the bar, then fade once the game's frames have settled
			target = 1;

			var settled = sinceUnsettled >= SettleTime && shownProgress >= 0.99f;
			if ( settled || sinceLoaded >= GiveUpTime )
				opacity = Math.Max( 0, opacity - frameTime / FadeTime );
		}

		// Ease along rather than jump. Steps are capped, so after a stall it carries on from where it was.
		shownProgress = shownProgress.LerpTo( target, 1 - MathF.Exp( -Math.Min( frameTime, 0.1f ) * 8 ) );
	}

	/// <summary>
	/// How far the load has got. When it can't say, ease on towards nearly done, slowing as it goes, rather than sit still.
	/// </summary>
	static float LoadFraction() => LoadingScreen.LoadFraction is { } known ? (float)known : 1 - MathF.Exp( -sinceLoading / 10f );

	/// <summary>
	/// The game's startup image, the one the splash showed. None when it didn't set one.
	/// </summary>
	static Texture LoadImage()
	{
		try
		{
			if ( !EngineFileSystem.Root.FileExists( GameStartupWindow.LogoPath ) ) return null;

			using var bitmap = Bitmap.CreateFromFile( EngineFileSystem.Root, GameStartupWindow.LogoPath );
			return bitmap is { IsValid: true } ? bitmap.ToTexture( false ) : null;
		}
		catch ( Exception exception )
		{
			Log.Warning( exception, $"Couldn't load the startup image: {GameStartupWindow.LogoPath}" );
			return null;
		}
	}

	/// <summary>
	/// Draw it over the game, in the frame's overlay.
	/// </summary>
	internal static void Render()
	{
		if ( !IsActive || opacity <= 0 ) return;

		var width = (int)Graphics.Viewport.Width;
		var height = (int)Graphics.Viewport.Height;

		Graphics.DrawRoundedRectangle( new Rect( 0, 0, width, height ), Color.Black.WithAlpha( opacity ) );

		if ( image is { IsValid: true } texture )
		{
			var (x, y, w, h) = GameStartupWindow.LogoRect( width, height, texture.Width, texture.Height );
			Graphics.Attributes.Set( "Texture", texture );
			Graphics.DrawQuad( new Rect( x, y, w, h ), Material.UI.Basic, Color.White.WithAlpha( opacity ) );
		}

		var (barX, barY, barWidth, barHeight) = GameStartupWindow.BarRect( width, height );
		Graphics.DrawRoundedRectangle( new Rect( barX, barY, barWidth, barHeight ), TrackColor.WithAlpha( opacity ) );

		var filled = (int)(barWidth * shownProgress);
		if ( filled > 0 )
			Graphics.DrawRoundedRectangle( new Rect( barX, barY, filled, barHeight ), BarColor.WithAlpha( opacity ) );
	}
}
