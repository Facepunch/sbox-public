using Sandbox.Engine;
using Sandbox.Utility;

namespace Sandbox;

/// <summary>
/// Access screen dimension etc.
/// </summary>
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
