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
		renderingRequested = true;
		if ( window.SwapChain == default ) return;

		CSceneSystem.SetMainSwapChain( window.SwapChain );
		renderingInitialized = true;
	}

	/// <summary>
	/// The frame gate: native's loop calls it after the frame's rendering has finished, and renders the next frame only if
	/// it returns true. On Basalt.Gpu it presents the frame <see cref="Render"/> drew, then lets native end a frame with
	/// nothing to present, so its frame count, per-frame cleanup and resource loading go on as they do after a present.
	/// </summary>
	internal static bool Present()
	{
		if ( Current is not { } game ) return true;
		if ( game.basalt is null ) return game.window.Present();

		game.basalt.PresentFrame();
		g_pRenderDevice.Present( default );
		return true;
	}

	internal void Render()
	{
		// All views join one batch so scene jobs overlap and we wait once at the end.
		CSceneSystem.BeginRenderingViews( true );
		try
		{
			if ( basalt is not null )
			{
				RenderOnBasaltGpu();
				return;
			}

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

	/// <summary>
	/// A frame on Basalt.Gpu: the game's and the menu's cameras draw into the window's target (<see cref="BasaltGpuWindow.Drawing"/>),
	/// then the overlay, in the block that copies it into the acquired image. Scene panels draw on Source 2's device and
	/// don't run, as in a Basalt.Gpu UI window. A minimised window draws nothing.
	/// </summary>
	void RenderOnBasaltGpu()
	{
		if ( !basalt.Update( window.PixelSize ) ) return;
		if ( !basalt.BeginFrame( Color.Black ) ) return;

		try
		{
			using ( gameRender.Start() )
				IGameInstanceDll.Current?.OnRender( default );
			using ( menuRender.Start() )
				IMenuDll.Current?.OnRender( default );
		}
		finally
		{
			basalt.EndFrame( Graphics.RenderOverlay );
		}
	}
}
