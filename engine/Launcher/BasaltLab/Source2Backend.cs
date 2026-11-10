using Basalt;
using Basalt.Gpu.Source2;
using NativeEngine;
using Sandbox.Engine;
using System.Diagnostics;
using System.Globalization;
using System.Threading;

namespace Sandbox.BasaltLab;

/// <summary>
/// Everything Basalt Lab gets from Source 2, in one place: starting the engine, the device, windows, content and reading
/// frames back. A device of Basalt's own would replace this class and nothing else.
/// <para>
/// Boots the panel app's slice of the engine (<c>SourceEnginePreInit</c> then <c>SourceEnginePanelAppInit</c>): the
/// filesystem, resource, render, material, input, mesh and scene systems. The scene system is there because the device
/// draws through it (well known buffers and materials, the vertex cache, morphs). <c>-scene-modules</c> loads what
/// SceneLab loads on top - world renderer, animation, physics, sound - to measure them. No Steam, no game, no UI, no
/// editor, and on the managed side no type library, fonts, convars, mounting or backend.
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
		Log.Info( $"Basalt Lab: {name} took {now - lastMs}ms (at {now}ms)" );
		lastMs = now;
	}

	public void Start( bool sceneModules )
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

		// Tools mode makes the render device with no window: windows are made here
		var commandLine = Environment.CommandLine.Replace( ".dll", ".exe" ) + " -nosteam -novr";
		if ( !EngineGlobal.SourceEnginePreInit( commandLine, appSystem ) )
			throw new Exception( "SourceEnginePreInit failed" );
		Phase( "SourceEnginePreInit" );

		WindowInput.Initialize();
		Graphics.Initialize();
		Phase( "Graphics" );

		if ( !EngineGlobal.SourceEnginePanelAppInit( appSystem, sceneModules ) )
			throw new Exception( "SourceEnginePanelAppInit failed" );
		Phase( sceneModules ? "Scene modules (SceneLab's list)" : "Scene modules (panel list)" );

		EngineGlobal.Plat_SetCurrentFrame( 0 );
		RenderHost.Current = new LabRenderHost();
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

		((LabRenderHost)RenderHost.Current).NextFrame();
	}

	/// <summary>
	/// Return the frame's borrowed render targets and run its deferred disposals.
	/// </summary>
	public void EndFrame()
	{
		RenderTarget.EndOfFrame();
		EngineLoop.DrainFrameEndDisposables();
	}

	/// <summary>
	/// Lab worlds' meshes, materials and textures, loaded through the engine and kept for the run.
	/// </summary>
	public Source2LabContent Content { get; } = new( "Basalt Lab" );

	public RenderMesh LoadMesh( string path ) => Content.Mesh( path );

	/// <summary>
	/// Load textures whole rather than streaming their mips in over real time, so frames are the same every run.
	/// </summary>
	public void PreloadTextures() => g_pRenderDevice.SetForcePreloadStreamingData( true );

	/// <summary>
	/// Wait for the GPU to finish everything submitted, before freeing what it might still read.
	/// </summary>
	public void Flush() => g_pRenderDevice.Flush();

	/// <summary>
	/// A target's resolved colour as 8-bit sRGB: a float target encoded as native's final blit would.
	/// </summary>
	public Color32[] ReadPixels( ViewTarget target )
	{
		var texture = target.Resolved.Engine();
		if ( !target.Resolved.Format.IsFloat() ) return texture.GetPixels();

		var halves = new ushort[texture.Width * texture.Height * 4];
		texture.GetPixels( (0, 0, texture.Width, texture.Height), 0, 0, halves.AsSpan(), ImageFormat.RGBA16161616F );

		var pixels = new Color32[texture.Width * texture.Height];
		for ( int i = 0; i < pixels.Length; i++ )
			pixels[i] = new Color32( Encode( halves[i * 4] ), Encode( halves[i * 4 + 1] ), Encode( halves[i * 4 + 2] ), 255 );

		return pixels;
	}

	static byte Encode( ushort half )
	{
		var linear = Math.Clamp( (float)BitConverter.UInt16BitsToHalf( half ), 0, 1 );
		var gamma = linear <= 0.0031308f ? linear * 12.92f : 1.055f * MathF.Pow( linear, 1.0f / 2.4f ) - 0.055f;
		return (byte)MathF.Round( gamma * 255 );
	}

	public static void SavePng( Color32[] pixels, int width, int height, string path )
	{
		using var bitmap = new Bitmap( width, height );
		bitmap.SetPixels( pixels.Select( x => x.ToColor() ).ToArray() );
		System.IO.File.WriteAllBytes( path, bitmap.ToPng() );
	}

	public LabWindow CreateWindow( string title, int width, int height, bool vsync ) => new( title, width, height, vsync );

	/// <summary>
	/// Wait for the GPU, then end the process without native teardown, as the shader compiler does: nothing is left
	/// to save, and a clean teardown of this slice of the engine isn't something anything has needed yet.
	/// </summary>
	public void Exit( int code )
	{
		g_pRenderDevice.Flush();
		EngineGlobal.Plat_ExitProcess( code );
	}
}

/// <summary>
/// An SDL window with a swap chain, which Basalt draws straight into.
/// </summary>
internal sealed class LabWindow : IDisposable
{
	readonly SdlWindow window;

	public LabWindow( string title, int width, int height, bool vsync )
	{
		window = new SdlWindow( Sdl.CreateWindow( title, width, height, Sdl.WindowFlags.Hidden | Sdl.WindowFlags.Resizable | Sdl.WindowFlags.Vulkan ) );
		window.CreateSwapChain( title, RenderMultisampleType.RENDER_MULTISAMPLE_NONE, vsync );
		g_pRenderDevice.SetSwapChainIsMainWindow( window.SwapChain, true );
	}

	public GpuSwapChain SwapChain => new( window.SwapChain );

	public Vector2 Size => window.SwapChainSize;

	public bool Closed { get; private set; }

	/// <summary>
	/// Handle the window's events. Only closing it means anything here.
	/// </summary>
	public void Poll()
	{
		while ( Sdl.PollEvent( out var e ) )
		{
			if ( e.Type is Sdl.EventType.Quit or Sdl.EventType.WindowCloseRequested ) Closed = true;
		}

		window.UpdateSwapChain();
	}

	public void Present()
	{
		window.Present();
		if ( (window.Flags & Sdl.WindowFlags.Hidden) != 0 ) window.Show();
	}

	public void Dispose()
	{
		window.Flush();
		window.Dispose();
	}
}

/// <summary>
/// Basalt's defaults, fixed: no convars, so a run renders the same whatever the machine's saved settings are.
/// </summary>
internal sealed class LabRenderHost : RenderHost
{
	public override GpuDevice Device => Source2Device.Instance;
}
