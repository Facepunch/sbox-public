using NativeEngine;

namespace Sandbox.Engine;

/// <summary>
/// Borrowed snapshot of the game window or editor play widget. Do not retain it across window or swapchain disposal.
/// </summary>
internal readonly record struct GameSurface( IntPtr Window, SwapChainHandle_t SwapChain, bool? VSyncOverride = null )
{
	internal static GameSurface? Current => GameWindow.Current?.Surface ?? IToolsDll.Current?.GameSurface;
	internal bool VSync => VSyncOverride ?? (SwapChain != default && g_pRenderDevice.GetSwapChainInfo( SwapChain ).m_bWaitForVSync != 0);
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
	/// Actual swapchain size in pixels, or zero when there is no swapchain.
	/// </summary>
	internal Vector2 Size
	{
		get
		{
			if ( SwapChain == default ) return default;
			var mode = g_pRenderDevice.GetSwapChainInfo( SwapChain ).m_DisplayMode;
			return new Vector2( mode.m_nWidth, mode.m_nHeight );
		}
	}
}
