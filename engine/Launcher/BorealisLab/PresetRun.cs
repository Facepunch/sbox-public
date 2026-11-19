using Borealis;
using Borealis.Backend;
using Borealis.Gpu;
using Borealis.Sdl;
using GpuDevice = Borealis.Gpu.Device;

namespace Sandbox.BorealisLab;

/// <summary>
/// Lab presets (<see cref="LabPresets"/>) through Borealis on a Borealis.Gpu device of its own, with content Borealis.Source2
/// reads (<see cref="BackendLabContent"/>): one in a window, or captures of one or more into a texture, each read back and
/// saved, and with <c>-golden-truth</c> checked against Borealis's approved images. The engine is up beside it for posing
/// and for models made in code.
/// </summary>
internal static class PresetRun
{
	/// <summary>
	/// The presets the command line names: <c>-presets "A;B"</c>, <c>-suite core</c>, or <c>-preset "Name"</c>, which
	/// defaults to Sun Shadows. Names that aren't presets come back as nulls, so they fail the run.
	/// </summary>
	public static (string Name, LabPreset Preset)[] Selected()
	{
		string[] names = Args.String( "-suite" ) is { } suite
			? suite.Equals( "core", StringComparison.OrdinalIgnoreCase ) ? LabPresets.Core : [suite]
			: Args.String( "-presets" ) is { } list
				? list.Split( ';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries )
				: [Args.String( "-preset" ) ?? "Sun Shadows"];

		// -shard i/n: every nth from the ith, so n BorealisLabs can split them
		if ( Args.String( "-shard" )?.Split( '/' ) is [var a, var b] && int.TryParse( a, out var shard ) && int.TryParse( b, out var shards ) && shard >= 1 && shard <= shards )
			names = names.Where( ( _, i ) => i % shards == shard - 1 ).ToArray();

		return names.Select( x => (x, LabPresets.Find( x )) ).ToArray();
	}

	/// <summary>
	/// Whether a capture run, rather than a window: <c>-capture</c>, <c>-out</c> or <c>-golden-truth</c>.
	/// </summary>
	public static bool Capturing => Args.Has( "-capture" ) || Args.Has( "-out" ) || GoldenTruthRun.Enabled;

	public static int Run( Source2Backend backend, (string Name, LabPreset Preset)[] presets )
	{
		var width = Args.Int( "-width", 640 );
		var height = Args.Int( "-height", 360 );

		if ( !Capturing )
		{
			if ( presets[0].Preset is not { } preset )
			{
				Log.Warning( $"Borealis Lab: no preset called {presets[0].Name}" );
				return 1;
			}

			return Window( backend, preset, width, height );
		}

		// -msaa N: the capture's samples, 1, 2, 4 or 8
		var samples = Args.Int( "-msaa", 1 ) is var msaa && msaa is 2 or 4 or 8 ? msaa : 1;

		// Lavapipe loses the blue of lit surfaces in single-sample targets; multisampled ones are right
		if ( GoldenTruthRun.Device == "Lavapipe" && samples == 1 )
			Log.Warning( "Borealis Lab: on Lavapipe, single-sample targets lose the blue of lit surfaces; capture with -msaa 4 (docs/reference/borealis-host.md)" );

		var captureFrame = Math.Max( 1, Args.Int( "-capture-frame", 10 ) );
		var single = Args.String( "-capture" );
		var folder = System.IO.Path.GetFullPath( Args.String( "-out" ) ?? System.IO.Path.Combine( Environment.CurrentDirectory, "screenshots", "borealislab" ) );

		// Validation with -vulkan_enable_validation, as the render tests ask for it, or -validation; synchronisation validation
		// too with -sync-validation, as map runs take it
		var validation = Args.Has( "-vulkan_enable_validation" ) || Args.Has( "-validation" ) || Args.Has( "-sync-validation" );
		using var gpu = GpuDevice.Create( new DeviceOptions { ApplicationName = "Borealis Lab", Validation = validation, SynchronizationValidation = Args.Has( "-sync-validation" ), Presentation = false, Message = Message } );
		using var device = new BackendDevice( gpu );
		GoldenTruthRun.Adapter = gpu.Adapter.Name;

		// A capture is the same frame on any machine, however fast: no draw is skipped for a pipeline workers are still making,
		// and casters never move into the sun's cached far cascade, which they do after seconds of wall-clock time (a frame
		// takes half a second on Lavapipe), as in the GPU tests
		gpu.SkipDrawsUntilReady = false;
		Borealis.Shadows.SettledCasters.Reset();
		Borealis.Shadows.SettledCasters.Seconds = float.PositiveInfinity;
		Borealis.Shadows.SettledCasters.RestlessSeconds = float.PositiveInfinity;

		// Lavapipe (Mesa 26.2.4) leaves casters out of a shadow map's multi-draw of the faded list (one vkCmdDrawIndexedIndirect
		// of several draws, no colour target, a pixel stage that dithers) that the same commands draw issued one per call; no
		// shader reads the draw index. The faded list draws the classic way there
		if ( GoldenTruthRun.Device == "Lavapipe" ) device.GpuDrivenFaded = false;
		if ( validation ) GoldenTruthRun.ValidationLayer();
		backend.Phase( $"Borealis.Gpu device ({gpu.Adapter.Name})" );
		var previousHost = RenderHost.Current;
		RenderHost.Current = new BackendRenderHost( device );
		using var content = new BackendLabContent( device );

		var results = new List<(string Name, bool Passed, string Text)>();
		foreach ( var (name, preset) in presets )
		{
			if ( preset is null )
			{
				results.Add( (name, false, "no such preset") );
				continue;
			}

			var path = single is not null && presets.Length == 1 ? System.IO.Path.GetFullPath( single ) : System.IO.Path.Combine( folder, GoldenTruthRun.ImageName( name ) );
			System.IO.Directory.CreateDirectory( System.IO.Path.GetDirectoryName( path ) );

			// A capture that couldn't render writes nothing, so an older file at the path mustn't stand in for it
			if ( System.IO.File.Exists( path ) ) System.IO.File.Delete( path );

			try
			{
				Capture( backend, device, content, preset, width, height, samples, captureFrame, path );
			}
			catch ( Exception e )
			{
				Log.Warning( e, $"Borealis Lab: {name} failed: {e.Message}" );
				results.Add( (name, false, $"threw {e.GetType().Name}: {e.Message}") );
				continue;
			}

			if ( GoldenTruthRun.Enabled )
			{
				var golden = GoldenTruthRun.Check( preset.Name, path );
				results.Add( (name, golden.Passed, golden.Text) );
			}
			else
			{
				results.Add( (name, true, $"captured to {path}") );
			}
		}

		gpu.WaitIdle();
		RenderHost.Current = previousHost;

		// Validation messages from the last preset's teardown
		if ( GoldenTruthRun.Enabled && !GoldenTruthRun.Finish() ) results.Add( ("(teardown)", false, "validation messages after the last check") );

		var table = string.Join( "\n", results.Select( x => $"{x.Name,-28} {x.Text}" ) );
		var failed = results.Count( x => !x.Passed );
		var summary = $"{table}\n\n{(failed == 0 ? "All passed." : $"{failed} of {results.Count} failed.")}";
		Log.Info( $"Borealis Lab:\n{summary}" );
		Console.WriteLine( summary );

		if ( presets.Length > 1 || single is null )
			System.IO.File.WriteAllText( System.IO.Path.Combine( folder, "results.txt" ), summary + "\n" );

		return failed == 0 ? 0 : 1;
	}

	/// <summary>
	/// A Borealis.Gpu device's messages: warnings and errors to the log, and validation's to the golden truth check.
	/// </summary>
	static void Message( DeviceMessage m )
	{
		if ( m.Severity < MessageSeverity.Warning ) return;
		var text = $"Borealis.Gpu: {m.Severity} {m.Id}: {m.Text}";
		Log.Warning( text );
		if ( m.Severity == MessageSeverity.Error || m.Text.Contains( "Validation", StringComparison.OrdinalIgnoreCase ) ) GoldenTruthRun.Validation( text );
	}

	/// <summary>
	/// Render <paramref name="captureFrame"/> frames of the preset into a texture, with a fresh renderer and world, and
	/// save the last, resolved when it's multisampled. The lights stay where they start, so the frame is repeatable.
	/// </summary>
	static void Capture( Source2Backend backend, BackendDevice device, BackendLabContent content, LabPreset preset, int width, int height, int samples, int captureFrame, string path )
	{
		var lab = LabWorld.Build( preset, content );
		var view = NewView( lab, width, height );
		var renderer = new RenderSystem { ParallelRecording = false };

		// The formats a window's frame is drawn in before it's copied to the swap chain, so a capture sees what a window shows
		var target = ViewTarget.Create( new Vector2Int( width, height ), samples, TextureFormat.RGBA16161616F, renderer.DepthFormat, "Borealis Lab" );
		backend.Phase( $"{preset.Name}: scene" );

		try
		{
			for ( var frame = 1; frame <= captureFrame; frame++ )
			{
				backend.BeginFrame();
				device.BeginFrame();
				RenderHost.Current.NextFrame();
				renderer.Render( lab.World, view, target );
				backend.EndFrame();
			}

			Source2Backend.SavePng( ModelParity.ReadBack( device, target, width, height ), width, height, path );
			backend.Phase( $"{preset.Name}: {captureFrame} frames, captured to {path}" );
			Log.Info( $"Borealis Lab: {preset.Name}: {renderer.Stats}" );
		}
		finally
		{
			// The GPU may still be reading what's about to be freed
			device.Gpu.WaitIdle();
			target.Dispose();
			renderer.Dispose();
		}
	}

	/// <summary>
	/// The preset in a resizable window, its lights circling, until it's closed or <c>-frames N</c> have been drawn.
	/// </summary>
	static int Window( Source2Backend backend, LabPreset preset, int width, int height )
	{
		using var window = new SdlWindow( new SdlWindowOptions { Title = $"Borealis Lab - {preset.Name}", Width = width, Height = height } );
		using var gpu = GpuDevice.Create( new DeviceOptions { ApplicationName = "Borealis Lab", Validation = Args.Has( "-validation" ), InstanceExtensions = Sdl.VulkanInstanceExtensions, Message = Message } );
		var (clientWidth, clientHeight) = window.PixelSize;
		using var swapChain = gpu.CreateSwapChain( new SwapChainInfo
		{
			Surface = SurfaceSource.Custom( window.CreateVulkanSurface ),
			Width = clientWidth,
			Height = clientHeight,
			PresentMode = Args.Has( "-novsync" ) ? PresentMode.Immediate : PresentMode.Fifo,
		} );

		using var device = new BackendDevice( gpu );
		var previousHost = RenderHost.Current;
		RenderHost.Current = new BackendRenderHost( device );
		using var content = new BackendLabContent( device );
		var lab = LabWorld.Build( preset, content );
		var view = NewView( lab, width, height );
		var renderer = new RenderSystem();
		var target = ViewTarget.Create( new Vector2Int( clientWidth, clientHeight ), 1, TextureFormat.RGBA16161616F, renderer.DepthFormat, "Borealis Lab" );
		backend.Phase( "Scene and window" );

		var frames = Args.Int( "-frames", 0 );
		var frameTimes = new List<double>();
		var title = new BorealisGpuTitle();
		var titleName = $"Borealis Lab: {preset.Name}";
		Action<string> setTitle = text => window.Title = text;

		try
		{
			for ( var frame = 1; !window.CloseRequested; frame++ )
			{
				var start = System.Diagnostics.Stopwatch.GetTimestamp();
				Maps.MapRun.Pump( window );
				backend.BeginFrame();
				device.BeginFrame();
				if ( !swapChain.Acquire( out var image ) ) image = default;

				if ( preset.Orbit ) lab.MoveLights( RealTime.Now );
				RenderHost.Current.NextFrame();
				renderer.Render( lab.World, view, target );

				var copy = gpu.BeginCommandList( QueueType.Graphics, "Borealis Lab to window" );
				if ( image.IsValid ) CrowdRun.CopyTo( gpu, copy, target, image );
				gpu.Submit( copy, swapChain: image.IsValid ? swapChain : null );
				if ( image.IsValid ) swapChain.Present();
				title.Frame( titleName, Sandbox.Rendering.ManagedFrameCounters.From( renderer.Stats ), null, device, view.Position, setTitle );
				backend.EndFrame();

				if ( frame == 1 ) backend.Phase( "First frame" );
				else frameTimes.Add( System.Diagnostics.Stopwatch.GetElapsedTime( start ).TotalMilliseconds );

				if ( frames > 0 && frame >= frames ) break;
			}

			var summary = $"Borealis Lab: {string.Join( ", ", backend.Phases.Select( x => $"{x.Name} {x.Ms}ms" ) )}";
			if ( frameTimes.Count > 0 ) summary += $"; {frameTimes.Count} more frames, median {frameTimes.Order().ElementAt( frameTimes.Count / 2 ):F2}ms";
			Log.Info( summary );
			Console.WriteLine( summary );
			Log.Info( $"Borealis Lab: {renderer.Stats}" );
			return gpu.ValidationErrors > 0 ? 1 : 0;
		}
		finally
		{
			gpu.WaitIdle();
			target.Dispose();
			renderer.Dispose();
			RenderHost.Current = previousHost;
		}
	}

	static RenderView NewView( LabWorld lab, int width, int height )
	{
		var view = new RenderView { Name = "Borealis Lab", ClearColor = new Color( 0.08f, 0.09f, 0.11f ), Viewport = new Rect( 0, 0, width, height ) };
		lab.Aim( view, LabWorld.DefaultPitch, LabWorld.DefaultYaw, lab.DefaultDistance * Args.Float( "-zoom", 1 ) );
		return view;
	}
}
