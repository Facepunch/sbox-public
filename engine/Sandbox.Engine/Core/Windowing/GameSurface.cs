using NativeEngine;
using Sandbox.Engine.Settings;

namespace Sandbox.Engine;

/// <summary>
/// Borrowed snapshot of the game window or editor play widget. Do not retain it across window disposal.
/// </summary>
/// <param name="Window">The SDL window, or zero where the host owns the window and none wraps it.</param>
/// <param name="VSyncOverride">Whether the swap chain waits for vertical sync, if not the setting.</param>
/// <param name="HostSize">The surface's size in pixels where the host owns the window and no SDL window wraps it.</param>
/// <param name="HostScale">The surface's display scale where the host owns the window and no SDL window wraps it, else zero.</param>
internal readonly record struct GameSurface( IntPtr Window, bool? VSyncOverride = null, Vector2 HostSize = default, float HostScale = 0 )
{
	internal static GameSurface? Current => GameWindow.Current?.Surface ?? IToolsDll.Current?.GameSurface;

	/// <summary>Whether the window's swap chain waits for vertical sync: as it was made, else the setting.</summary>
	internal bool VSync => VSyncOverride ?? RenderSettings.Instance.VSync;
	internal uint Display => SdlDisplay.ForWindow( Window );
	/// <summary>
	/// Display/UI scale relative to authored UI units.
	/// </summary>
	internal float Scale => HostScale > 0 ? HostScale : SdlDisplay.GetWindowScale( Window );
	/// <summary>
	/// Current display refresh rate in Hz.
	/// </summary>
	internal float RefreshRate => SdlDisplay.GetCurrentMode( Display ).RefreshRate;
	/// <summary>
	/// The window's size in pixels, or zero without a window.
	/// </summary>
	internal Vector2 Size => Window != default && Sdl.GetWindowSizeInPixels( Window, out var width, out var height ) ? new Vector2( width, height ) : HostSize;
}
