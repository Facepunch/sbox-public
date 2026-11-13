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

		using ( WindowPresentTiming.Scope() )
		{
			game.basalt.PresentFrame();
			game.UpdateBasaltGpuTitle();
			g_pRenderDevice.Present( default );
		}

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
	/// then the overlay, in the block that copies it into the acquired image. Scene panels render into their textures first,
	/// in blocks of their own. A minimised window draws nothing.
	/// </summary>
	void RenderOnBasaltGpu()
	{
		using var _ = WindowFrameTiming.Scope();
		if ( !basalt.Update( window.PixelSize ) ) return;

		Sandbox.UI.ScenePanel.RenderPending();

		using ( WindowBeginTiming.Scope() )
		{
			if ( !basalt.BeginFrame( Color.Black ) ) return;
		}

		try
		{
			using ( gameRender.Start() )
				IGameInstanceDll.Current?.OnRender( default );
			using ( menuRender.Start() )
				IMenuDll.Current?.OnRender( default );
		}
		finally
		{
			using ( WindowOverlayTiming.Scope() )
				basalt.EndFrame( Graphics.RenderOverlay );
		}
	}

	BasaltGpuTitle basaltTitle;

	// Live stats in the title on Basalt.Gpu: the game's name, else the map's, else the window's own
	void UpdateBasaltGpuTitle()
	{
		if ( RenderBackend.Default is not BasaltGpuRenderBackend backend ) return;
		basaltTitle ??= new BasaltGpuTitle();
		basaltTitle.Frame( basalt, backend, Application.GamePackage?.Title ?? backend.LastMap ?? title, setTitle ??= value => window.Title = value );
	}

	Action<string> setTitle;

	// Where a Basalt.Gpu window's frame goes, in the dev tools' timings
	static readonly Sandbox.Diagnostics.PerformanceStats.Timings WindowFrameTiming = Sandbox.Diagnostics.PerformanceStats.Timings.Get( "Basalt.Gpu window" );
	static readonly Sandbox.Diagnostics.PerformanceStats.Timings WindowBeginTiming = Sandbox.Diagnostics.PerformanceStats.Timings.Get( "Basalt.Gpu window begin" );
	static readonly Sandbox.Diagnostics.PerformanceStats.Timings WindowOverlayTiming = Sandbox.Diagnostics.PerformanceStats.Timings.Get( "Basalt.Gpu window overlay" );
	static readonly Sandbox.Diagnostics.PerformanceStats.Timings WindowPresentTiming = Sandbox.Diagnostics.PerformanceStats.Timings.Get( "Basalt.Gpu window present" );
}
