using NativeEngine;
using Sandbox.Utility;

namespace Sandbox.Engine;

internal sealed partial class GameWindow
{
	static readonly ProfileScope gameRender = new( "Game.Render" );
	static readonly ProfileScope menuRender = new( "Menu.Render" );

	// Scene views derive their dimensions and MSAA from this swapchain; scratch textures are pooled on demand.
	internal void InitializeRendering()
	{
		renderingRequested = true;
		if ( window.SwapChain == default ) return;

		CSceneSystem.SetMainSwapChain( window.SwapChain );
		renderingInitialized = true;
	}

	internal static bool Present() => Current is not { } game || game.window.Present();

	internal void Render()
	{
		// All views join one batch so scene jobs overlap and we wait once at the end.
		CSceneSystem.BeginRenderingViews( true );
		try
		{
			Sandbox.UI.ScenePanel.RenderPending();

			using ( gameRender.Start() )
				IGameInstanceDll.Current?.OnRender( window.SwapChain );
			using ( menuRender.Start() )
				IMenuDll.Current?.OnRender( window.SwapChain );

			// The menu adds the overlay: its UI, the loading screen, debug overlays and screen capture. An exported game
			// has no menu, but still needs the rest.
			if ( IMenuDll.Current is null )
				CCameraRenderer.RenderOverlay( window.SwapChain );
		}
		finally
		{
			CSceneSystem.FinishRenderingViews();
			Graphics.WaitForRenderingToComplete();
		}
	}
}
