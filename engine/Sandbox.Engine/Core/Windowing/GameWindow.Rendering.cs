using NativeEngine;
using Sandbox.Utility;

namespace Sandbox.Engine;

internal sealed partial class GameWindow
{
	static readonly Superluminal gameRender = new( "Game Render", "#3a6e4d" );
	static readonly Superluminal menuRender = new( "Menu Render", "#6e3a6e" );

	/// <summary>
	/// The frame gate: native's loop calls it after the frame's rendering has finished, and renders the next frame only if
	/// it returns true. On Borealis.Gpu it presents the frame <see cref="Render"/> drew; without it there's nothing to present.
	/// </summary>
	internal static bool Present()
	{
		if ( Current is not { } game || game.borealis is null ) return true;

		using ( WindowPresentTiming.Scope() )
		{
			// A frame the render thread draws is presented there (DrawFrame)
			if ( game.handedOver ) game.presented = true;
			else
			{
				game.presented = game.borealis.PresentFrame();
				if ( game.presented ) game.PresentedForLatency();
			}
			game.UpdateBorealisGpuTitle();
		}

		return true;
	}

	// Whether this engine frame's drawing was handed to the render thread (Render), which presents it and starts the next
	// device frame itself
	bool handedOver;

	// Whether this engine frame presented on Borealis.Gpu, so the next one's device frame starts ahead (HandOverFrame)
	bool presented;

	/// <summary>
	/// At the end of the native frame, once mode changes are applied: when this frame presented on Borealis.Gpu, hand the
	/// next frame's device frame start and the window's next acquire to the frame thread, so the wait for the GPU and the
	/// swap chain is off the main thread while it ends this frame and runs the next one's game code.
	/// </summary>
	internal void HandOverFrame()
	{
		if ( borealis is null ) return;

		// The render thread starts the next device frame itself, after the frame it was handed
		if ( handedOver )
		{
			handedOver = false;
			presented = false;
			return;
		}

		if ( !presented ) return;
		presented = false;
		if ( RenderBackend.Default is BorealisGpuRenderBackend backend ) backend.HandOverFrame( borealis );
	}

	/// <summary>
	/// Wait for the render thread to finish the frame handed to it, and finish the next frame's start here: before the sync
	/// point writes the game's changes into the render side, and before anything else in the frame's output uses the device.
	/// </summary>
	internal void JoinRenderThread()
	{
		if ( borealis is not null ) BorealisGpuRenderBackend.JoinRenderThread();
	}

	/// <summary>
	/// At shutdown, before anything is released: the render thread (or the frame thread) finishes the frame handed to it and
	/// stops, and what the game wrote meanwhile is written. Frames from then on start on the main thread.
	/// </summary>
	internal void StopRenderThread()
	{
		if ( borealis is null || RenderBackend.Default is not BorealisGpuRenderBackend backend ) return;
		backend.StopFrameThread();
		backend.WriteStagedNow();
		borealis.PresentAhead();
	}

	/// <summary>Whether the render thread is drawing a frame handed to it: what the frame's end disposes of waits for it.</summary>
	internal bool DrawingOnRenderThread => borealis is not null && BorealisGpuRenderBackend.DrawingOnRenderThread;

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
			// counter and render time. Managed objects' deletes first
			SceneWorld.RunEndOfFrameDeletes();
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

		// The game's half on this thread: its cameras prepared, with every game hook that runs for them (the extract). One that
		// throws leaves the frame undrawn by the game: said, and the window shows the overlay over the cleared image
		extractThrew = false;
		Rendering.GameDrawExtract.BeginFrame();
		try
		{
			using ( global::Borealis.Zones.Extract.Start() )
			using ( gameRender.Start() )
			{
				drawnScene = IGameInstanceDll.Current is null ? null : Game.ExtractRender( drawnCameras );
				using ( menuRender.Start() )
					IMenuDll.Current?.OnRender( default );

				// The game's own custom objects' drawing, recorded per camera for the render thread to replay, when the frame may
				// draw there; last frame's recordings let go either way
				Rendering.GameDrawExtract.Extract( drawnCameras, record: drawnScene is not null && DrawsHereAnyway() is null );
			}
		}
		catch ( Exception e )
		{
			drawnScene = null;
			drawnCameras.Clear();
			extractThrew = true;
			if ( BorealisGpuErrors.ShouldLog( e ) ) Log.Warning( e, $"Preparing the frame's cameras threw, so the game isn't drawn this frame: {e.Message}" );
		}

		// The drawing on the render thread, which presents it, unless this frame draws here (InlineReason)
		var inline = InlineReason();
		LastInlineReason = inline;
		if ( inline is null && RenderBackend.Default is BorealisGpuRenderBackend backend && backend.HandOverRenderFrame( borealis, drawFrame ??= DrawFrame ) )
		{
			handedOver = true;
			HandedOverFrame = Application.FrameCount;
			BorealisGpuFrameLatency.HandedOver( Application.FrameCount );
			return;
		}

		// Drawn here, a game's custom objects run their drawing, which teaches the extract what they draw in
		Rendering.GameDrawExtract.Learning( true );
		try
		{
			DrawFrame();
		}
		finally
		{
			Rendering.GameDrawExtract.Learning( false );
		}
	}

	/// <summary>The last frame whose drawing was handed to the render thread, for the deadline latency mode.</summary>
	internal ulong HandedOverFrame { get; private set; }

	// The window's frame was presented: the GPU's last work for it is watched for its input-to-GPU latency
	void PresentedForLatency()
	{
		if ( RenderBackend.Default is BorealisGpuRenderBackend backend ) BorealisGpuFrameLatency.Presented( backend.Device, Application.FrameCount, backend.Device.LastFlushed( global::Borealis.Gpu.QueueType.Graphics ) );
	}

	// What the frame draws, prepared by the extract: the game's scene and its cameras in order (null with nothing extracted:
	// the game draws as Game.Render does, on the main thread). The render thread reads them until the next frame's extract,
	// which waits for it
	Scene drawnScene;
	readonly List<CameraComponent> drawnCameras = new();
	bool extractThrew;
	Action drawFrame;

	/// <summary>Why the last frame was drawn on the main thread rather than the render thread, or null if it wasn't.</summary>
	internal static string LastInlineReason { get; private set; }

	/// <summary>
	/// Why this frame draws on the main thread (serial, as before the render thread), or null for the render thread: game code
	/// would run in its drawing (a load drawn by the game itself, a game's custom object, a renderer drawn from a command list),
	/// or something reads the frame on this thread (a recording, the dev tools' dock).
	/// </summary>
	string InlineReason()
	{
		if ( DrawsHereAnyway() is { } here ) return here;
		if ( Game.IsPlaying && Game.ActiveScene is not null && drawnScene is null && !extractThrew ) return "loading";
		if ( Rendering.RenderSync.GameDrawsInFrame is { } game ) return game;
		return null;
	}

	// Why this frame draws on the main thread whatever the game draws in it, or null (known before the extract)
	internal static string DrawsHereAnyway()
	{
		if ( !BorealisGpuFrameThread.Enabled ) return "serial";
		if ( !BorealisGpuFrameThread.Draws ) return "off";
		if ( Sandbox.Diagnostics.DevTools.IsOpen ) return "dev tools";
		if ( ScreenRecorder.IsRecording() ) return "recording";
		return null;
	}

	/// <summary>
	/// The frame's drawing (the extract prepared it): the window's image, the game's cameras into it, the overlay, and on the
	/// render thread the present. A throw on the render thread is said and the frame dropped, its image presented as drawn so far.
	/// </summary>
	void DrawFrame()
	{
		if ( !ThreadSafe.IsRenderThread )
		{
			DrawWindow();
			return;
		}

		var drawStart = System.Diagnostics.Stopwatch.GetTimestamp();
		try
		{
			DrawWindow();
		}
		catch ( Exception e ) when ( !(e is Borealis.Gpu.GpuException { IsDeviceLost: true }) )
		{
			if ( BorealisGpuErrors.ShouldLog( e ) ) Log.Warning( e, $"The render thread's frame threw: {e.Message}" );
		}

		BorealisGpuFrameLatency.Drawn( Application.FrameCount, System.Diagnostics.Stopwatch.GetElapsedTime( drawStart ).TotalMilliseconds );
		if ( borealis.PresentFrame() ) PresentedForLatency();
	}

	void DrawWindow()
	{
		// Envmap cube faces the frame's output left for its drawing, before anything draws into the window
		BorealisGpuFrameThread.Doing = "envmap captures";
		Rendering.SceneRenderer.Current.DrawCaptures();

		BorealisGpuFrameThread.Doing = "the window's begin";
		using ( WindowBeginTiming.Scope() )
		{
			if ( !borealis.BeginFrame( Color.Black ) ) return;
		}

		try
		{
			BorealisGpuFrameThread.Doing = "the game's cameras";
			using ( gameRender.Start() )
			{
				if ( drawnScene is not null ) drawnScene.DrawRender( drawnCameras, default, null );
				else if ( !extractThrew ) IGameInstanceDll.Current?.OnRender( default );
			}
		}
		finally
		{
			BorealisGpuFrameThread.Doing = "the overlay";
			using ( WindowOverlayTiming.Scope() )
				borealis.EndFrame( Graphics.RenderOverlay );
			BorealisGpuFrameThread.Doing = "the present";
		}
	}

	BorealisGpuTitle borealisTitle;

	// Live stats in the title on Borealis.Gpu: the game's name, else the map's, else the window's own
	void UpdateBorealisGpuTitle()
	{
		if ( RenderBackend.Default is not BorealisGpuRenderBackend backend ) return;
		borealisTitle ??= new BorealisGpuTitle();
		borealisTitle.Frame( borealis, backend, Application.GamePackage?.Title ?? backend.LastMap ?? title, setTitle ??= window.SetTitle );
	}

	Action<System.Text.StringBuilder> setTitle;

	// Where a Borealis.Gpu window's frame goes, in the dev tools' timings
	static readonly Sandbox.Diagnostics.PerformanceStats.Timings WindowFrameTiming = Sandbox.Diagnostics.PerformanceStats.Timings.Get( "Borealis.Gpu window" );
	static readonly Sandbox.Diagnostics.PerformanceStats.Timings WindowBeginTiming = Sandbox.Diagnostics.PerformanceStats.Timings.Get( "Borealis.Gpu window begin" );
	static readonly Sandbox.Diagnostics.PerformanceStats.Timings WindowOverlayTiming = Sandbox.Diagnostics.PerformanceStats.Timings.Get( "Borealis.Gpu window overlay" );
	static readonly Sandbox.Diagnostics.PerformanceStats.Timings WindowPresentTiming = Sandbox.Diagnostics.PerformanceStats.Timings.Get( "Borealis.Gpu window present" );
}
