using NativeEngine;
using Sandbox.Utility;

namespace Sandbox.Engine;

internal sealed partial class GameWindow
{
	static readonly Superluminal gameRender = new( "Game Render", "#3a6e4d" );
	static readonly Superluminal menuRender = new( "Menu Render", "#6e3a6e" );

	// Scene views derive their dimensions and MSAA from this swapchain; scratch textures are pooled on demand.
	internal void InitializeRendering()
	{
		CSceneSystem.SetMainSwapChain( window.SwapChain );
		renderingInitialized = true;
	}

	internal static bool Present() => Current is not { } game || game.window.Present();

	internal void Render()
	{
		startupFinished = true;

		// All views join one batch so scene jobs overlap and we wait once at the end.
		CSceneSystem.BeginRenderingViews( true );
		try
		{
			Sandbox.UI.ScenePanel.RenderPending();

			using ( gameRender.Start() )
				IGameInstanceDll.Current?.OnRender( window.SwapChain );
			using ( menuRender.Start() )
				IMenuDll.Current?.OnRender( window.SwapChain );
		}
		finally
		{
			CSceneSystem.FinishRenderingViews();
			CSceneSystem.WaitForRenderingToComplete();
		}
	}
}
