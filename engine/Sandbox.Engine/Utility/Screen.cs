using NativeEngine;

namespace Sandbox;

/// <summary>
/// Access screen dimension etc.
/// </summary>
public static class Screen
{
	/// <summary>
	/// The total size of the game screen
	/// </summary>
	public static Vector2 Size { get; internal set; }

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

		var width = 1024;
		var height = 1024;

		if ( !Application.IsUnitTest )
		{
			g_pEngineServiceMgr.GetEngineSwapChainSize( out width, out height );
		}

		var newSize = new Vector2( width, height );
		if ( newSize == Size )
			return;

		Size = new Vector2( width, height );

		RenderTarget.Flush();
		DesktopScale = EngineGlobal.GetDiagonalDpi() / 96.0f;
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
