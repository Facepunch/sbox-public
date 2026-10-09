using NativeEngine;
using Sandbox.Engine.Settings;

namespace Sandbox.Engine;

/// <summary>
/// SDL display queries shared by the game window, video settings and system information.
/// </summary>
internal static class SdlDisplay
{
	internal static uint Current => GameSurface.Current?.Display ?? Primary;

	internal static uint Primary => Application.IsHeadless ? 0 : Sdl.GetPrimaryDisplay();

	internal static int Count
	{
		get
		{
			if ( Application.IsHeadless ) return 0;

			var displays = Sdl.GetDisplays( out var count );
			if ( displays == IntPtr.Zero ) return 0;

			Sdl.Free( displays );
			return count;
		}
	}

	/// <summary>
	/// Content scale before a window exists. Null requests a centered window on the primary display.
	/// </summary>
	internal static float GetScaleAt( Vector2? position )
	{
		var point = new Sdl.Point { X = (int)(position?.x ?? 0), Y = (int)(position?.y ?? 0) };
		var display = position.HasValue ? Sdl.GetDisplayForPoint( ref point ) : 0;
		var scale = Sdl.GetDisplayContentScale( display == 0 ? Primary : display );
		return scale > 0 ? scale : 1;
	}

	internal static uint ForWindow( IntPtr window ) => window == IntPtr.Zero ? Primary : Sdl.GetDisplayForWindow( window );

	/// <summary>
	/// Display scale for sizing UI content in pixels, including the user's display scaling preference.
	/// </summary>
	internal static float GetWindowScale( IntPtr window )
	{
		var scale = window == IntPtr.Zero ? 0 : Sdl.GetWindowDisplayScale( window );
		return scale > 0 ? scale : 1;
	}

	/// <summary>
	/// Pixel-to-window-coordinate ratio; distinct from the preferred UI content scale.
	/// </summary>
	internal static float GetPixelDensity( IntPtr window )
	{
		var density = window == IntPtr.Zero ? 0 : Sdl.GetWindowPixelDensity( window );
		return density > 0 ? density : 1;
	}

	/// <summary>
	/// Display bounds in desktop/window coordinates, optionally excluding taskbars and docks. Display modes use pixels.
	/// </summary>
	internal static Rect GetBounds( uint display, bool usable = false )
	{
		Sdl.Rect bounds;
		if ( usable ) Sdl.GetDisplayUsableBounds( display, out bounds );
		else Sdl.GetDisplayBounds( display, out bounds );
		return new Rect( bounds.X, bounds.Y, bounds.Width, bounds.Height );
	}

	internal static Vector2 GetDesktopSize( uint display )
	{
		var mode = GetDesktopMode( display );
		return new Vector2( mode.Width, mode.Height );
	}

	internal static Sdl.DisplayMode GetDesktopMode( uint display ) =>
		display == 0 ? default : Read( Sdl.GetDesktopDisplayMode( display ) );

	internal static Sdl.DisplayMode GetCurrentMode( uint display ) =>
		display == 0 ? default : Read( Sdl.GetCurrentDisplayMode( display ) );

	static unsafe Sdl.DisplayMode Read( IntPtr mode ) => mode == IntPtr.Zero ? default : *(Sdl.DisplayMode*)mode;

	internal static unsafe RenderSettings.VideoDisplayMode[] GetModes( uint display )
	{
		if ( display == 0 ) return [];

		var modes = (Sdl.DisplayMode**)Sdl.GetFullscreenDisplayModes( display, out var count );
		if ( modes == null ) return [];

		try
		{
			var result = new RenderSettings.VideoDisplayMode[count];
			for ( var i = 0; i < count; ++i )
			{
				result[i] = new()
				{
					Width = modes[i]->Width,
					Height = modes[i]->Height,
					RefreshRate = modes[i]->RefreshRate,
					Format = ImageFormat.RGBA8888
				};
			}
			return result;
		}
		finally
		{
			Sdl.Free( (IntPtr)modes );
		}
	}
}
