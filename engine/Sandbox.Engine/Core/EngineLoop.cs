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

namespace Sandbox;

[SkipHotload]
internal static class EngineLoop
{
	static double previousTime;
	static readonly FramePacer framePacer = new();

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

		LoopFrames++;

		double time = RealTime.NowDouble;
		FastTimer frameTimer = FastTimer.StartNew();

		using ( _runFrame.Start() )
		{
			Application.FrameCount++;
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
			}
			catch ( System.Exception e )
			{
				Log.Error( e );
			}
		}

		PerformanceStats.Timings.Idle.AddMilliseconds( framePacer.Wait( FrameRateLimit.FramesPerSecond ) );
		DebugOverlay.FrameTimeGraph.Sample( frameTimer.ElapsedMilliSeconds );

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

		//
		// Let the Steam API and Steam Game Server API think
		//
		NativeEngine.Steam.SteamGameServer_RunCallbacks();
		NativeEngine.Steam.SteamAPI_RunCallbacks();

		//
		// Update performance stats (should be called every frame)
		//
		UpdatePerformance();
		DebugOverlay.Draw();
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
			using ( IGameInstanceDll.Current?.PushScope() )
			{
				VoiceManager.Tick();
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

	public static void RunAsyncTasks()
	{
		using ( PerformanceStats.Timings.Async.Scope() )
		{
			using var sceneScope = IGameInstanceDll.Current?.PushScope();

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
			IMenuDll.Current?.SimulateUI();
		}
	}

	static Superluminal _clientOutput = new Superluminal( "OnClientOutput", "#3a6ea5" );
	static Superluminal _toolsRender = new Superluminal( "Tools Render", "#6e6e3a" );

	internal static void OnClientOutput()
	{
		RenderedFrames++;

		using var _outputScope = _clientOutput.Start();

		// UI windows own their own swap chains, they're not part of anyone's view
		Sandbox.UI.PanelWindows.FrameAll();

		// The editor renders it's own game scene
		if ( Application.IsEditor )
		{
			Sandbox.UI.ScenePanel.RenderPending();

			using ( _toolsRender.Start() )
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
