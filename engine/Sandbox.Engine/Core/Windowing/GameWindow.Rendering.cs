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
	/// it returns true. On Borealis.Gpu it presents the frame <see cref="Render"/> drew. Source 2's device there is the empty
	/// one, which has no work queued for a frame's end, so it isn't told the frame ended.
	/// </summary>
	internal static bool Present()
	{
		if ( Current is not { } game ) return true;
		if ( game.borealis is null ) return game.window.Present();

		using ( WindowPresentTiming.Scope() )
		{
			game.borealis.PresentFrame();
			game.UpdateBorealisGpuTitle();
		}

		return true;
	}

	internal void Render()
	{
		try
		{
			// Without a Borealis.Gpu device there's nothing to draw on
			if ( borealis is not null ) RenderOnBorealisGpu();
		}
		finally
		{
			// What the scene system does at a frame's end besides rendering views: queued deletes, dirty objects, its frame
			// counter and render time
			CSceneSystem.FinishFrameWithoutViews();
		}
	}

	/// <summary>
	/// A frame on Borealis.Gpu: the game's and the menu's cameras draw into the window's target (<see cref="BorealisGpuWindow.Drawing"/>),
	/// then the overlay, in the block that copies it into the acquired image. Scene panels render into their textures first,
	/// in blocks of their own. A minimised window draws nothing.
	/// </summary>
	void RenderOnBorealisGpu()
	{
		using var _ = WindowFrameTiming.Scope();
		if ( !borealis.Update( window.PixelSize ) ) return;

		Sandbox.UI.ScenePanel.RenderPending();

		using ( WindowBeginTiming.Scope() )
		{
			if ( !borealis.BeginFrame( Color.Black ) ) return;
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
				borealis.EndFrame( Graphics.RenderOverlay );
		}
	}

	BorealisGpuTitle borealisTitle;

	// Live stats in the title on Borealis.Gpu: the game's name, else the map's, else the window's own
	void UpdateBorealisGpuTitle()
	{
		if ( RenderBackend.Default is not BorealisGpuRenderBackend backend ) return;
		borealisTitle ??= new BorealisGpuTitle();
		borealisTitle.Frame( borealis, backend, Application.GamePackage?.Title ?? backend.LastMap ?? title, setTitle ??= value => window.Title = value );
	}

	Action<string> setTitle;

	// Where a Borealis.Gpu window's frame goes, in the dev tools' timings
	static readonly Sandbox.Diagnostics.PerformanceStats.Timings WindowFrameTiming = Sandbox.Diagnostics.PerformanceStats.Timings.Get( "Borealis.Gpu window" );
	static readonly Sandbox.Diagnostics.PerformanceStats.Timings WindowBeginTiming = Sandbox.Diagnostics.PerformanceStats.Timings.Get( "Borealis.Gpu window begin" );
	static readonly Sandbox.Diagnostics.PerformanceStats.Timings WindowOverlayTiming = Sandbox.Diagnostics.PerformanceStats.Timings.Get( "Borealis.Gpu window overlay" );
	static readonly Sandbox.Diagnostics.PerformanceStats.Timings WindowPresentTiming = Sandbox.Diagnostics.PerformanceStats.Timings.Get( "Borealis.Gpu window present" );
}
