using NativeEngine;
using Sandbox.Audio;
using Sandbox.Engine;
using Sandbox.Engine.Settings;
using Sandbox.Network;
using Sandbox.TextureLoader;
using Sandbox.UI;
using Sandbox.Utility;
using Sandbox.VR;
using System.Threading.Channels;
using Sandbox.Platform;

namespace Sandbox;

[SkipHotload]
internal static class EngineLoop
{
	static double previousTime;
	static readonly FramePacer framePacer = new();

	/// <summary>
	/// The time shaders see this frame (<c>g_flTime</c>), in seconds: the playing game's scene time, else real time.
	/// Null before the first frame.
	/// </summary>
	internal static float? RenderTime { get; private set; }

	// Loop iterations vs frames that actually rendered. Native skips client output when it can't present.
	internal static long LoopFrames;
	internal static long RenderedFrames;

	static Superluminal _runFrame = new Superluminal( "RunFrame", "#4d5e73" );
	static Superluminal _frameStart = new Superluminal( "FrameStart", "#2c3541" );
	static Superluminal _frameEnd = new Superluminal( "FrameEnd", "#2c3541" );

	internal static void RunFrame( CMaterialSystem2AppSystemDict appDict, out bool wantsQuit )
	{
		if ( Application.WantsExit )
		{
			SoundHandle.Shutdown();
			MixingThread.DrainDisposals();
			g_pEngineServiceMgr.ExitMainLoop();
		}

		GameWindow.Current?.CompleteStartup();

		LoopFrames++;

		// Enumerating gamepads is slow, so it waits until the first frames have been shown.
		if ( LoopFrames == 30 ) SdlGamepads.Initialize();

		double time = RealTime.NowDouble;
		FastTimer frameTimer = FastTimer.StartNew();
		var cpuAtStart = ThreadCpuTime.NowMs();

		using ( _runFrame.Start() )
		{
			Application.FrameCount++;
			Sandbox.Diagnostics.FrameAllocations.MainFrameStart( Application.FrameCount );
			RealTime.Update( time );
			Time.Update( RealTime.Now, RealTime.Delta );

			DebugOverlay.Reset();

			try
			{
				using ( _frameStart.Start() )
				{
					FrameStart();
				}
			}
			catch ( System.Exception e )
			{
				Log.Error( e );
			}

			using ( PerformanceStats.Timings.Render.Scope() )
			{
				wantsQuit = !EngineGlobal.SourceEngineFrame( appDict, time, previousTime );
			}

			if ( wantsQuit )
			{
				SoundHandle.Shutdown();
				MixingThread.DrainDisposals();
			}

			try
			{
				GameWindow.Current?.ApplyPendingMode();

				// The next frame's device frame starts on the frame thread from here, while this one ends
				GameWindow.Current?.HandOverFrame();
			}
			catch ( System.Exception e )
			{
				Log.Error( e );
			}

			try
			{
				using ( _frameEnd.Start() )
				{
					FrameEnd();
				}
				IToolsDll.Current?.RunFrame();
				Bootstrap.TryFinishStartup();
			}
			catch ( System.Exception e )
			{
				Log.Error( e );
			}
		}

		Sandbox.Utility.ProfilerZones.FrameMark();
		// The main thread's CPU time this frame, besides the wall time the timings add up to
		MainThreadCpuTiming.AddMilliseconds( ThreadCpuTime.NowMs() - cpuAtStart );

		var idleMs = framePacer.Wait( FrameRateLimit.FramesPerSecond );

		// The deadline latency mode: when the render thread draws, the next frame's input waits until just in time for the GPU.
		// Not idle time: it takes the place of the wait for the render thread later in the frame, which isn't either
		if ( GameWindow.Current?.HandedOverFrame == Application.FrameCount ) BorealisGpuFrameLatency.SleepBeforeInput( Application.FrameCount );
		PerformanceStats.Timings.Idle.AddMilliseconds( idleMs );
		DevTools.SampleFrame( frameTimer.ElapsedMilliSeconds, idleMs );

		previousTime = time;
	}

	static readonly PerformanceStats.Timings MainThreadCpuTiming = PerformanceStats.Timings.Get( "Borealis main thread CPU" );

	internal static FrameRateLimit FrameRateLimit
	{
		get
		{
			if ( Application.IsBenchmark ) return new( -1, "benchmark" );
			if ( Application.IsHeadless ) return new( 60, "headless" );

			if ( GameSurface.Current is { } surface )
			{
				var vsync = surface.VSync;
				return FrameRateLimit.FromSettings( WindowInput.IsAppActive(), vsync, vsync ? surface.RefreshRate : 0 );
			}
			return new( -1, "uncapped" );
		}
	}

	/// <summary>
	/// Pumps the input system
	/// </summary>
	static void UpdateInput()
	{
		using var __ = PerformanceStats.Timings.Input.Scope();

		SdlEvents.Poll();
	}

	internal static void FrameStart()
	{
		ThreadSafe.AssertIsMainThread();

		RunSteamCallbacks();

		//
		// Update performance stats (should be called every frame)
		//
		UpdatePerformance();
		DebugOverlay.Draw();
		BorealisGpuFrameLatency.InputSampled( Application.FrameCount );
		UpdateInput();

		//
		// Dispatch callbacks for any changed files
		//
		FileWatch.Tick();

		//
		// Update any animated textures
		//
		using ( PerformanceStats.Timings.Video.Scope() )
		{
			Texture.Tick();
		}

		//
		// Update VR
		//
		VRSystem.FrameStart();

		//
		// Expire any unused resources
		//
		NativeResourceCache.Tick();
		Game.Resources.PruneWeakIndex();
		Mounting.MountUtility.TickPreviewRenders();

		//
		// Run Tasks
		//
		RunAsyncTasks();

		//
		// Let the context's tick
		//

		IMenuDll.Current?.Tick();
		IGameInstanceDll.Current?.Tick();
		IMenuDll.Current?.LateTick();
		IToolsDll.Current?.Tick();


		//
		// Run Tasks
		//
		RunAsyncTasks();

		//
		// Misc client systems
		//
		if ( !Application.IsHeadless )
		{
			using ( IGameInstanceDll.Current?.PushScope() ?? default )
			{
				VoiceChat.Tick();
				Sandbox.TextRendering.Tick();
			}
		}

		//
		// If we have any queued console messages, we can print them now
		//
		Logging.PushQueuedMessages();

		//
		// Allow the events to push if they want
		//
		Api.Events.TickEvents();
		Api.Stats.TickStats();
		Sandbox.Services.Messaging.ProcessMessages();

		// Simulate UI last. This works out all the styles and shit, so we want
		// that to be reflected right BEFORE the frame is rendered.
		using ( PerformanceStats.Timings.Ui.Scope() )
		{
			SimulateUI();
		}

		// After the game and menu UI so it draws over them. Outside the UI scope so it doesn't count as the game's UI.
		DevTools.Simulate();

		// Give each sound handle an opportunity to for a frame think
		using ( PerformanceStats.Timings.Audio.Scope() )
		{
			MixingThread.UpdateGlobals();
		}

		//
		// Update the mouse visibility status
		//
		if ( !Application.IsHeadless )
		{
			Engine.InputRouter.Frame();
		}

		// Keep room up to date
		PartyRoom.Current?.Tick();

		Audio.AudioEngine.Tick();
	}

	/// <summary>
	/// Let the Steam API and Steam Game Server API think
	/// </summary>
	internal static void RunSteamCallbacks()
	{
		NativeEngine.Steam.SteamGameServer_RunCallbacks();
		NativeEngine.Steam.SteamAPI_RunCallbacks();
	}

	public static void RunAsyncTasks()
	{
		using ( PerformanceStats.Timings.Async.Scope() )
		{
			using var sceneScope = IGameInstanceDll.Current?.PushScope() ?? default;

			ThreadSafe.AssertIsMainThread();
			MainThread.RunQueues();
			SyncContext.MainThread?.ProcessQueue();
		}
	}

	internal static void FrameEnd()
	{
		ThreadSafe.AssertIsMainThread();

		//
		// Run Tasks
		//
		Engine.Streamer.CurrentService?.Tick();
		RunAsyncTasks();

		//
		// Update VR
		//
		VRSystem.FrameEnd();

		// Free strings allocated by interop.
		Interop.Free();

		//
		// Run threaded stuff that needed to
		// happen on the main thread
		//
		MainThread.RunQueues();

		//
		// Trigger recompile of Project 
		//
		Project.Tick();

		//
		// Free anything that needs to be disposed of at end of frame, and render targets: once the render thread has drawn
		// the frame handed to it, when it draws one
		//
		if ( BorealisGpuRenderBackend.DrawingOnRenderThread ) DeferFrameEndDisposals();
		else
		{
			DrainFrameEndDisposables();
			RenderTarget.EndOfFrame();
		}
	}

	// What was queued to be disposed of before a frame was handed to the render thread, disposed once it's drawn
	static readonly List<IDisposable> handedOverDisposals = new();
	static bool handedOverTargets;

	static void DeferFrameEndDisposals()
	{
		while ( FrameEndDisposables.Reader.TryRead( out var disposable ) ) handedOverDisposals.Add( disposable );
		handedOverTargets = true;
	}

	/// <summary>
	/// The render thread has drawn the frame handed to it (the main thread joined it): what that frame's end left to dispose of
	/// goes now, and its render targets return to the pool.
	/// </summary>
	internal static void RenderThreadFrameDone()
	{
		// Cameras and worlds that went while it drew
		Rendering.SceneRenderer.ForgetAfterDrawing();

		if ( handedOverDisposals.Count > 0 )
		{
			foreach ( var disposable in handedOverDisposals ) disposable.Dispose();
			handedOverDisposals.Clear();
		}

		if ( handedOverTargets )
		{
			handedOverTargets = false;
			RenderTarget.EndOfFrame();
		}
	}


	static void UpdatePerformance()
	{
		PerformanceStats.Frame();
		Api.Performance.Frame();
	}


	static Superluminal _simulateUiGame = new Superluminal( "Simulate GameUI", "#2c3541" );
	static Superluminal _simulateUiMenu = new Superluminal( "Simulate GameUI", "#2c3541" );

	private static void SimulateUI()
	{
		ThreadSafe.AssertIsMainThread();
		VideoTextureLoader.TickVideoPlayers();
		PanelRealTime.Update();

		using ( _simulateUiGame.Start() )
		{
			IGameInstanceDll.Current?.SimulateUI();
		}

		using ( _simulateUiMenu.Start() )
		{
			// The editor only shows the menu's UI as an overlay on a game view, so it's left alone while nothing draws it
			MenuUISimulated = !Application.IsEditor || LoopFrames - MenuUIOverlayWanted <= 2;
			if ( MenuUISimulated ) IMenuDll.Current?.SimulateUI();
		}

		StandaloneLoadingScreen.Tick();
	}

	/// <summary>
	/// The last frame something wanted to draw the menu's UI overlay.
	/// </summary>
	internal static long MenuUIOverlayWanted;

	/// <summary>
	/// Whether the menu's UI was simulated this frame, so its command lists are current.
	/// </summary>
	internal static bool MenuUISimulated = true;

	static Superluminal _clientOutput = new Superluminal( "OnClientOutput", "#3a6ea5" );
	static Superluminal _toolsRender = new Superluminal( "Tools Render", "#6e6e3a" );

	internal static void OnClientOutput()
	{
		RenderedFrames++;

		using var _outputScope = _clientOutput.Start();

		// The render thread has finished the last frame (the game window's, or the editor's game view's) before anything here
		// touches the render side or the device
		BorealisGpuRenderBackend.JoinRenderThread();

		// The frame's scene renderer, picked once, so a frame never mixes renderers
		Rendering.SceneRenderer.BeginFrame();

		// Choose g_flTime before rendering, independently of temporary menu and preview scene scopes.
		RenderTime = Game.IsPlaying && GlobalContext.Game.ActiveScene is { IsValid: true } gameScene
			? (float)gameScene.TimeNow
			: RealTime.Now;

		// The sync point: the game's changes since the last frame are written into the worlds every view of this one draws
		Rendering.RenderSync.Run();

		// Flush envmaps in their own view scope before we do any view rendering
		Scene.RenderAllEnvmaps();

		// UI windows own their own swap chains, they're not part of anyone's view
		Sandbox.UI.PanelWindows.FrameAll();

		// The editor renders it's own game scene
		if ( Application.IsEditor )
		{
			Sandbox.UI.ScenePanel.RenderPending();

			using ( _toolsRender.Start() )
				IToolsDll.Current?.OnRender();

			// The frame's end for deleted scene objects (the game window's Render runs them in the game)
			SceneWorld.RunEndOfFrameDeletes();
			return;
		}

		GameWindow.Current?.Render();
	}

	static Channel<IDisposable> FrameEndDisposables = Channel.CreateUnbounded<IDisposable>();

	/// <summary>
	/// Queue something to be disposed of after the frame has ended and everything has finished rendering.
	/// </summary>
	internal static void DisposeAtFrameEnd( IDisposable disposable ) => FrameEndDisposables.Writer.TryWrite( disposable );

	/// <summary>
	/// Drain all queued frame-end disposables immediately. Called during shutdown
	/// since no more frames will run to process them naturally.
	/// </summary>
	internal static void DrainFrameEndDisposables()
	{
		while ( FrameEndDisposables.Reader.TryRead( out var disposable ) )
		{
			disposable.Dispose();
		}
	}
}
