using Borealis;
using NativeEngine;
using Sandbox.Engine;
using System.Diagnostics;
using System.Globalization;
using System.Threading;

namespace Sandbox.BorealisLab;

/// <summary>
/// The engine for Borealis Lab's runs that need it, in one place: starting and stopping it, and its frames. Model parity
/// checks the engine's loading against Borealis.Source2's, and posing takes its animation system. Source 2's render device
/// is the empty one.
/// <para>
/// Boots the panel app's slice of the engine (<c>SourceEnginePreInit</c> then <c>SourceEnginePanelAppInit</c>): the
/// filesystem, resource, render, material, input, mesh and scene systems. <c>-scene-modules</c> loads what SceneLab loads
/// on top - world renderer, animation, physics, sound. No Steam, no game, no UI, no editor, and on the managed side no type
/// library, fonts, convars, mounting or backend.
/// </para>
/// </summary>
internal sealed class Source2Backend
{
	CMaterialSystem2AppSystemDict appSystem;

	readonly Stopwatch timer = Stopwatch.StartNew();
	long lastMs;

	/// <summary>
	/// How long each part of startup took, in order.
	/// </summary>
	public List<(string Name, long Ms)> Phases { get; } = new();

	public void Phase( string name )
	{
		var now = timer.ElapsedMilliseconds;
		Phases.Add( (name, now - lastMs) );
		Log.Info( $"Borealis Lab: {name} took {now - lastMs}ms (at {now}ms)" );
		lastMs = now;
	}

	/// <param name="sceneModules">Also start the native modules SceneLab starts: world renderer, animation, physics, sound.</param>
	/// <param name="windows">Set up SDL's window input, for a window. A capture makes none.</param>
	public void Start( bool sceneModules, bool windows )
	{
		// The runtime starting and the launcher finding its dlls, before any of this ran
		Phases.Add( ("Process", (long)(DateTime.Now - Process.GetCurrentProcess().StartTime).TotalMilliseconds) );

		NetCore.InitializeInterop( Environment.CurrentDirectory );
		Phase( "Interop" );

		Application.Initialize( dedicated: false, headless: false, toolsMode: true, testMode: false, isRetail: EngineGlobal.IsRetail() );
		DLLImportResolver.SetupResolvers();
		Sandbox.Tasks.SyncContext.Init();
		ThreadSafe.MarkMainThread();

		// The engine formats and parses in one culture everywhere
		var culture = CultureInfo.CreateSpecificCulture( "en-US" );
		CultureInfo.DefaultThreadCurrentCulture = culture;
		CultureInfo.DefaultThreadCurrentUICulture = culture;
		Thread.CurrentThread.CurrentCulture = culture;
		Thread.CurrentThread.CurrentUICulture = culture;

		Diagnostics.Logging.Enabled = true;
		EngineFileSystem.Initialize( Environment.CurrentDirectory );
		Phase( "Managed" );

		appSystem = CMaterialSystem2AppSystemDict.Create( new MaterialSystem2AppSystemDictCreateInfo() );
		appSystem.SetModGameSubdir( "core" );
		appSystem.SetInToolsMode();
		appSystem.SetSteamAppId( (uint)Application.AppId );

		// Tools mode makes the render device with no window: windows are made here. Source 2's is the empty one: the lab draws
		// on a Borealis.Gpu device of its own, and Source 2 only describes and loads resources
		var commandLine = Environment.CommandLine.Replace( ".dll", ".exe" ) + " -nosteam -novr -empty";
		if ( !EngineGlobal.SourceEnginePreInit( commandLine, appSystem ) )
			throw new Exception( "SourceEnginePreInit failed" );
		Phase( "SourceEnginePreInit" );

		if ( windows ) WindowInput.Initialize();
		Graphics.Initialize();
		Phase( "Graphics" );

		if ( !EngineGlobal.SourceEnginePanelAppInit( appSystem, sceneModules ) )
			throw new Exception( "SourceEnginePanelAppInit failed" );
		Phase( sceneModules ? "Scene modules (SceneLab's list)" : "Scene modules (panel list)" );

		EngineGlobal.Plat_SetCurrentFrame( 0 );
	}

	/// <summary>
	/// The engine's side of a frame: clocks, native frame updates, main thread queues.
	/// </summary>
	public void BeginFrame()
	{
		Application.FrameCount++;
		RealTime.Update( RealTime.NowDouble );
		Time.Update( RealTime.Now, RealTime.Delta );

		EngineGlobal.SourceEnginePanelAppFrame();
		Sandbox.Tasks.SyncContext.MainThread?.ProcessQueue();
		NativeResourceCache.Tick();
		MainThread.RunQueues();
	}

	/// <summary>
	/// Return the frame's borrowed render targets and run its deferred disposals.
	/// </summary>
	public void EndFrame()
	{
		RenderTarget.EndOfFrame();
		EngineLoop.DrainFrameEndDisposables();
	}

	public static void SavePng( Color32[] pixels, int width, int height, string path )
	{
		using var bitmap = new Bitmap( width, height );
		bitmap.SetPixels( pixels.Select( x => x.ToColor() ).ToArray() );
		System.IO.File.WriteAllBytes( path, bitmap.ToPng() );
	}

	/// <summary>
	/// Tear down what <see cref="Start"/> started, in the order the engine's own shutdown does, so anything still holding a
	/// native resource shows up as a leak: the run's content and frame-end work, the managed caches, then the engine. The
	/// launcher returns <paramref name="code"/>, or 1 when it succeeded but leaked.
	/// </summary>
	public int Exit( int code )
	{
		var leaks = Shutdown();

		// A leak fails the run, so the render tests can't pass with one
		return code != 0 ? code : leaks > 0 ? 1 : 0;
	}

	/// <summary>
	/// Drop what the engine's and Borealis's statics keep for the life of the process - default textures, fallback models,
	/// shared materials - as the engine's own shutdown does, so they're freed rather than reported as leaks.
	/// </summary>
	static void ReleaseStatics()
	{
		Type[] held = [typeof( Texture ), typeof( Model ), typeof( Material ), typeof( ComputeShader ), typeof( Sandbox.GpuBuffer ),
			typeof( GpuTexture ), typeof( GpuMaterial ), typeof( GpuMesh ), typeof( RenderMesh ), typeof( Borealis.GpuBuffer )];
		var flags = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;

		foreach ( var assembly in new[] { typeof( Texture ).Assembly, typeof( RenderSystem ).Assembly } )
		{
			foreach ( var type in assembly.GetTypes().Where( x => !x.IsGenericTypeDefinition ) )
			{
				foreach ( var field in type.GetFields( flags ) )
				{
					if ( field.IsLiteral || field.IsInitOnly || !held.Any( x => x.IsAssignableFrom( field.FieldType ) ) ) continue;
					field.SetValue( null, null );
				}
			}
		}
	}

	/// <returns>How many engine resources were still alive at the end, each logged as a leak.</returns>
	int Shutdown()
	{
		Graphics.FlushGPU();
		Borealis.Shadows.ShadowMapper.ResetCache();
		ReleaseStatics();
		RenderHost.Current = null;

		Material.Shutdown();
		NativeResourceCache.ClearCache();
		RenderTarget.Shutdown();
		RenderAttributes.Pool.Clear();

		// No more frames will run, so run what's queued for the end of one
		EngineLoop.DrainFrameEndDisposables();
		MainThread.RunQueues();

		// Finalizers release what the managed side still wraps, and some queue more work
		for ( int i = 0; i < 2; i++ )
		{
			GC.Collect();
			GC.WaitForPendingFinalizers();
			EngineLoop.DrainFrameEndDisposables();
			MainThread.RunQueues();
		}

		var leaks = 0;
		void CountLeak( LogEvent e )
		{
			if ( e.Message?.StartsWith( "NativeResourceCache: Resource still alive", StringComparison.Ordinal ) == true ) leaks++;
		}

		Diagnostics.Logging.OnMessage += CountLeak;
		NativeResourceCache.HandleShutdownLeaks();
		Diagnostics.Logging.OnMessage -= CountLeak;
		if ( leaks > 0 ) Log.Warning( $"Borealis Lab: {leaks} engine resources leaked" );
		MainThread.RunQueues();
		SceneWorld.FlushQueuedDeletes();

		Graphics.Shutdown();
		WindowInput.Shutdown();
		Sandbox.Tasks.SyncContext.Shutdown();
		EngineGlobal.SourceEngineShutdown( appSystem, false );
		EngineLoop.DrainFrameEndDisposables();

		if ( appSystem.IsValid )
		{
			appSystem.Destroy();
			appSystem = default;
		}

		NativeErrorReporter.Shutdown();
		Managed.SandboxEngine.NativeInterop.Free();

		EngineFileSystem.Shutdown();
		Application.Shutdown();
		return leaks;
	}
}
