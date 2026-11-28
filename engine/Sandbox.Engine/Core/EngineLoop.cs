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

	// Loop iterations vs frames that actually rendered. Native skips client output when it can't present.
	internal static long LoopFrames;
	internal static long RenderedFrames;

	static readonly ProfileScope CpuFrame = new( Diagnostics.Profiler.FrameScopeName );
	static readonly ProfileScope CpuFrameStart = new( "Engine.FrameStart" );
	static readonly ProfileScope CpuFrameEnd = new( "Engine.FrameEnd" );
	static readonly ProfileScope CpuPerformance = new( "Engine.PerformanceStats" );
	static readonly ProfileScope CpuOverlay = new( "Engine.DebugOverlay" );
	static readonly ProfileScope CpuFileWatch = new( "Engine.FileWatch" );
	static readonly ProfileScope CpuVr = new( "Engine.VR" );
	static readonly ProfileScope CpuResources = new( "Engine.Resources" );
	static readonly ProfileScope CpuPreviews = new( "Engine.PreviewRenders" );
	static readonly ProfileScope CpuMenuTick = new( "Engine.MenuTick" );
	static readonly ProfileScope CpuGameTick = new( "Engine.GameTick" );
	static readonly ProfileScope CpuMenuLateTick = new( "Engine.MenuLateTick" );
	static readonly ProfileScope CpuToolsTick = new( "Engine.ToolsTick" );
	static readonly ProfileScope CpuVoice = new( "Engine.VoiceChat" );
	static readonly ProfileScope CpuText = new( "Engine.TextRendering" );
	static readonly ProfileScope CpuLogging = new( "Engine.Logging" );
	static readonly ProfileScope CpuServices = new( "Engine.Services" );
	static readonly ProfileScope CpuUi = new( "Engine.SimulateUI" );
	static readonly ProfileScope CpuDevTools = new( "Engine.DevTools" );
	static readonly ProfileScope CpuAudioEngine = new( "Engine.AudioEngine" );
	static readonly ProfileScope CpuInputRouter = new( "Engine.InputRouter" );
	static readonly ProfileScope CpuParty = new( "Engine.PartyRoom" );
	static readonly ProfileScope CpuToolsFrame = new( "Engine.ToolsFrame" );
	static readonly ProfileScope CpuOutput = new( "Engine.ClientOutput" );
	static readonly ProfileScope CpuToolsRender = new( "Engine.ToolsRender" );

	internal static void RunFrame( CMaterialSystem2AppSystemDict appDict, out bool wantsQuit )
	{
		Diagnostics.Profiler.Update( frameBoundary: true );
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

		using ( CpuFrame.Start() )
		{
			Application.FrameCount++;
			RealTime.Update( time );
			Time.Update( RealTime.Now, RealTime.Delta );

			DebugOverlay.Reset();

			try
			{
				using ( CpuFrameStart.Start() )
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
			}
			catch ( System.Exception e )
			{
				Log.Error( e );
			}

			try
			{
				using ( CpuFrameEnd.Start() )
				{
					FrameEnd();
				}
				using ( CpuToolsFrame.Start() ) IToolsDll.Current?.RunFrame();
				Bootstrap.TryFinishStartup();
			}
			catch ( System.Exception e )
			{
				Log.Error( e );
			}
		}

		var idleMs = framePacer.Wait( FrameRateLimit.FramesPerSecond );
		PerformanceStats.Timings.Idle.AddMilliseconds( idleMs );
		DevTools.SampleFrame( frameTimer.ElapsedMilliSeconds, idleMs );

		previousTime = time;
	}

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

		SteamNative.Runtime.RunCallbacks();

		//
		// Update performance stats (should be called every frame)
		//
		using ( CpuPerformance.Start() ) UpdatePerformance();
		using ( CpuOverlay.Start() ) DebugOverlay.Draw();
		UpdateInput();

		//
		// Dispatch callbacks for any changed files
		//
		using ( CpuFileWatch.Start() ) FileWatch.Tick();

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
		using ( CpuVr.Start() ) VRSystem.FrameStart();

		//
		// Expire any unused resources
		//
		using ( CpuResources.Start() )
		{
			NativeResourceCache.Tick();
			Game.Resources.PruneWeakIndex();
		}
		using ( CpuPreviews.Start() ) Mounting.MountUtility.TickPreviewRenders();

		//
		// Run Tasks
		//
		RunAsyncTasks();

		//
		// Let the context's tick
		//

		using ( CpuMenuTick.Start() ) IMenuDll.Current?.Tick();
		using ( CpuGameTick.Start() ) IGameInstanceDll.Current?.Tick();
		using ( CpuMenuLateTick.Start() ) IMenuDll.Current?.LateTick();
		using ( CpuToolsTick.Start() ) IToolsDll.Current?.Tick();


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
				using ( CpuVoice.Start() ) VoiceChat.Tick();
				using ( CpuText.Start() ) Sandbox.TextRendering.Tick();
			}
		}

		//
		// If we have any queued console messages, we can print them now
		//
		using ( CpuLogging.Start() ) Logging.PushQueuedMessages();

		//
		// Allow the events to push if they want
		//
		using ( CpuServices.Start() )
		{
			Api.Events.TickEvents();
			Api.Stats.TickStats();
			Sandbox.Services.Messaging.ProcessMessages();
		}

		// Simulate UI last. This works out all the styles and shit, so we want
		// that to be reflected right BEFORE the frame is rendered.
		using ( PerformanceStats.Timings.Ui.Scope() )
		{
			SimulateUI();
		}

		// After the game and menu UI so it draws over them. Outside the UI scope so it doesn't count as the game's UI.
		using ( CpuDevTools.Start() ) DevTools.Simulate();

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
			using ( CpuInputRouter.Start() ) Engine.InputRouter.Frame();
		}

		// Keep room up to date
		using ( CpuParty.Start() ) PartyRoom.Current?.Tick();

		using ( CpuAudioEngine.Start() ) Audio.AudioEngine.Tick();
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
		// Free anything that needs to be disposed of at end of frame
		// 
		DrainFrameEndDisposables();

		// Free render targets
		RenderTarget.EndOfFrame();
	}


	static void UpdatePerformance()
	{
		PerformanceStats.Frame();
		Api.Performance.Frame();
	}


	static readonly ProfileScope CpuGameUi = new( "Engine.SimulateGameUI" );
	static readonly ProfileScope CpuMenuUi = new( "Engine.SimulateMenuUI" );

	private static void SimulateUI()
	{
		using var cpu = CpuUi.Start();
		ThreadSafe.AssertIsMainThread();
		VideoTextureLoader.TickVideoPlayers();
		PanelRealTime.Update();

		using ( CpuGameUi.Start() )
		{
			IGameInstanceDll.Current?.SimulateUI();
		}

		using ( CpuMenuUi.Start() )
		{
			IMenuDll.Current?.SimulateUI();
		}

		StandaloneLoadingScreen.Tick();
	}

	internal static void OnClientOutput()
	{
		using var cpu = CpuOutput.Start();
		RenderedFrames++;

		// Choose g_flTime before rendering, independently of temporary menu and preview scene scopes.
		var renderTime = Game.IsPlaying && GlobalContext.Game.ActiveScene is { IsValid: true } gameScene
			? (float)gameScene.TimeNow
			: RealTime.Now;

		CSceneSystem.SetNextRenderTime( renderTime );

		// Flush envmaps in their own view scope before we do any view rendering
		Scene.RenderAllEnvmaps();

		// r_managed_scene_compare renders to a bitmap, which has to happen before any views are rendering
		Rendering.ManagedSceneRendering.BeforeRenderingViews();

		// UI windows own their own swap chains, they're not part of anyone's view
		Sandbox.UI.PanelWindows.FrameAll();

		// The editor renders it's own game scene
		if ( Application.IsEditor )
		{
			Sandbox.UI.ScenePanel.RenderPending();

			using ( CpuToolsRender.Start() )
				IToolsDll.Current?.OnRender();
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
