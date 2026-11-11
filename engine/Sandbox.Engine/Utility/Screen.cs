using Sandbox.Engine;
using Sandbox.Utility;

namespace Sandbox;

/// <summary>
/// Access screen dimension etc.
/// </summary>
[Expose]
public static class Screen
{
	static readonly float? forcedDpi = ReadForcedDpi();

	static float? ReadForcedDpi() => float.TryParse( CommandLine.GetSwitch( "-force_dpi", "" ),
		System.Globalization.CultureInfo.InvariantCulture, out var dpi ) ? dpi : null;

	/// <summary>
	/// The last known pixel size of the game screen. Defaults to 1024x1024 before a game view is available.
	/// </summary>
	public static Vector2 Size { get; internal set; } = new( 1024, 1024 );

	/// <summary>
	/// The width of the game screen. Equal to Screen.x
	/// </summary>
	public static float Width => Size.x;

	/// <summary>
	/// The height of the game screen. Equal to Screen.y
	/// </summary>
	public static float Height => Size.y;

	/// <summary>
	/// The part of the screen that's the game's to put its UI in - all of it, unless the platform menu has something
	/// over part of it (its dock, over a game's own menu). Screen panels lay out inside it.
	/// </summary>
	public static Rect SafeArea
	{
		get
		{
			var screen = new Rect( 0, 0, Width, Height );
			if ( SafeAreaOverride is not { } area ) return screen;

			// Kept to the screen, as it may have changed size since
			var left = Math.Clamp( area.Left, 0, Width );
			var top = Math.Clamp( area.Top, 0, Height );
			var right = Math.Clamp( area.Right, left, Width );
			var bottom = Math.Clamp( area.Bottom, top, Height );
			return new Rect( left, top, MathF.Max( 1, right - left ), MathF.Max( 1, bottom - top ) );
		}
	}

	/// <summary>
	/// Set by the platform menu (MenuUtility.SetSafeArea) - null is the whole screen
	/// </summary>
	internal static Rect? SafeAreaOverride { get; set; }

	/// <summary>
	/// The aspect ratio of the screen. Equal to Width/Height, or 1 if the screen size is not yet known.
	/// </summary>
	public static float Aspect => Height > 0 ? Width / Height : 1f;

	/// <summary>
	/// The desktop's dpi scale on the current monitor.
	/// </summary>
	public static float DesktopScale { get; private set; } = 1.0f;

	internal static void UpdateFromEngine()
	{
		ThreadSafe.AssertIsMainThread();

		var surface = GameSurface.Current;
		var size = surface?.Size ?? Size;

		// A monitor change can affect DPI without changing the window's pixel dimensions.
		var scale = forcedDpi / 96.0f ?? surface?.Scale ?? 1.0f;
		if ( size.x <= 0 || size.y <= 0 ) size = Size;
		if ( size == Size && scale == DesktopScale ) return;

		Size = size;
		DesktopScale = scale;
	}

	/// <summary>
	/// Converts a vertical field of view to a horizontal field of view based on the screen aspect ratio.
	/// </summary>
	public static float CreateVerticalFieldOfView( float fieldOfView )
	{
		return CreateVerticalFieldOfView( fieldOfView, Aspect );
	}

	/// <summary>
	/// Converts a vertical field of view to a horizontal field of view based on the given aspect ratio.
	/// </summary>
	public static float CreateVerticalFieldOfView( float fieldOfView, float aspectRatio )
	{
		float t = MathF.Tan( fieldOfView.DegreeToRadian() * 0.5f );
		return MathF.Atan( t * aspectRatio ).RadianToDegree() * 2.0f;
	}

}
