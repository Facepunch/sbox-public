using NativeEngine;
using Sandbox.Engine.Settings;

namespace Sandbox.Engine;

/// <summary>
/// Borrowed snapshot of the game window or editor play widget. Do not retain it across window disposal.
/// </summary>
internal readonly record struct GameSurface( IntPtr Window, bool? VSyncOverride = null )
{
	internal static GameSurface? Current => GameWindow.Current?.Surface ?? IToolsDll.Current?.GameSurface;

	/// <summary>Whether the window's swap chain waits for vertical sync: as it was made, else the setting.</summary>
	internal bool VSync => VSyncOverride ?? RenderSettings.Instance.VSync;
	internal uint Display => SdlDisplay.ForWindow( Window );
	/// <summary>
	/// Display/UI scale relative to authored UI units.
	/// </summary>
	internal float Scale => SdlDisplay.GetWindowScale( Window );
	/// <summary>
	/// Current display refresh rate in Hz.
	/// </summary>
	internal float RefreshRate => SdlDisplay.GetCurrentMode( Display ).RefreshRate;
	/// <summary>
	/// The window's size in pixels, or zero without a window.
	/// </summary>
	internal Vector2 Size => Window != default && Sdl.GetWindowSizeInPixels( Window, out var width, out var height ) ? new Vector2( width, height ) : default;
}
