namespace Sandbox.BasaltLab;

/// <summary>
/// <c>-exposure-parity folder [-frames N] [-step seconds] [-tolerance t] [-validation]</c>: runs auto exposure for a
/// fixed number of frames on both devices and compares the exposure each frame. A camera with the engine's tonemapping
/// component sets its scene camera's auto exposure, as in a game. On Source 2 that camera's native tonemap system runs as
/// a camera's stages run it (its frame update, then <c>RenderTools.RenderAutoExposure</c> over the frame); on a
/// Basalt.Gpu device its managed port runs (<see cref="TonemapSystem.GpuExposure"/>, counting with occlusion queries),
/// over the same HDR frames: Sun Shadows from Source 2, bright for the first half and a tenth as bright for the second.
/// Both advance <c>-step</c> seconds a frame: native's frame update is called with that time rather than real time, as
/// <c>RenderTools.TonemapFrameUpdate</c> passes. With no step the component's rate is 0, which adapts at once. Writes
/// <c>results.md</c>; exits 1 when a frame's exposure differs by more than <c>-tolerance</c> (default 0 with a step,
/// 3e-7 without: a float step in the weighted average), the exposure never moved, or validation reported errors.
/// </summary>
internal static class ExposureParity
{
	const int Width = 640, Height = 360;

	/// <summary>
	/// <c>ITonemapSystem::FrameUpdate( float64 )</c>, the interface's first virtual, at a time of our choosing: the interop
	/// only reaches it through <c>RenderTools.TonemapFrameUpdate</c>, which passes real time. Test-only.
	/// </summary>
	static unsafe void NativeFrameUpdate( ITonemapSystem system, double time )
	{
		IntPtr self = system;
		var vtable = *(IntPtr**)self;
		((delegate* unmanaged< IntPtr, double, void >)vtable[0])( self, time );
	}

	public static int Run( Source2Backend backend, string folder )
	{
		var frames = Args.Int( "-frames", 60 );
		var step = Args.Float( "-step", 0 );

		// Adapting at once, the exposure is the ten-target average itself, where native's compiled float arithmetic differs
		// from C#'s by a float step
		var tolerance = Args.Float( "-tolerance", step > 0 ? 0 : 3e-7f );
		folder = System.IO.Path.GetFullPath( folder );
		System.IO.Directory.CreateDirectory( folder );

		using var gpu = BasaltGpuRenderBackend.Create( Args.Has( "-validation" ), m => Log.Info( $"Basalt.Gpu: {m.Severity} {m.Text}" ) );
		var (bright, dark) = PostParity.ExposureFrames( backend, folder );
		backend.Phase( "Sun Shadows on Source 2" );

		// The camera's settings come from the engine's tonemapping component, at its defaults but for the rate
		PostParity.EnsureTypeLibrary();
		var scene = new Scene();
		SceneCamera camera;
		using ( scene.Push() )
		{
			var go = new GameObject( true, "Exposure parity" );
			var cameraComponent = go.Components.Create<CameraComponent>();
			var tonemapping = go.Components.Create<Tonemapping>();
			if ( step <= 0 ) tonemapping.Rate = 0;
			tonemapping.Build( new PostProcessContext { Camera = cameraComponent, Components = [new WeightedEffect { Effect = tonemapping, Weight = 1 }] } );
			camera = cameraComponent.SceneCamera;
			cameraComponent.AutoExposure.Apply( camera );
		}

		var native = camera.ToneMapping.GetNative();
		var managed = camera.Tonemap.GpuExposure( gpu );

		// The managed histogram each frame
		var debug = Args.Has( "-exposure-debug" );

		var target = Texture.CreateRenderTarget().WithSize( Width, Height ).WithFormat( ImageFormat.RGBA16161616F ).Create();
		var rows = new List<string>();
		var differing = 0;
		var firstMoved = -1;
		double maxDifference = 0;
		double time = 0;

		for ( var i = 0; i < frames; i++ )
		{
			var frame = i < frames / 2 ? bright : dark;
			backend.BeginFrame();

			// Source 2: the frame's update at the fixed time (handed over as a float, as RenderTools does with real time), then
			// the luminance query over its colour
			time += step;
			NativeFrameUpdate( native, (float)time );
			var nativeScale = NativeEngine.RenderTools.TonemapFrameUpdate( native, false );
			using ( Graphics.Scope.CreateOn( RenderBackend.Default ) )
			{
				Graphics.RenderTarget = RenderTarget.From( target );
				NativeEngine.RenderTools.RenderAutoExposure( Graphics.Context, native, frame.native, 0, 0, Width, Height );
			}

			// Source 2 resolves queries when its render thread presents or reaches a fence after their work has retired; with
			// no window, wait for the GPU and pass a fence, so its counts come back when Basalt.Gpu's do, as a presented frame's would
			g_pRenderDevice.ForceFlushGPU( default );
			g_pRenderDevice.Flush();

			// Basalt.Gpu: the same
			managed.FrameUpdate( (float)time );
			var managedScale = managed.Scale;
			using ( Graphics.Scope.CreateOn( gpu ) )
			{
				Graphics.RenderTarget = RenderTarget.From( target );
				gpu.CountExposure( managed, frame, new Rect( 0, 0, Width, Height ) );
			}

			gpu.WaitIdle();
			gpu.NextFrame();
			backend.EndFrame();

			if ( debug ) Log.Info( $"Exposure frame {i}: managed {managedScale} target {managed.Target}, buckets {string.Join( " ", Enumerable.Range( 0, managed.Buckets ).Select( b => managed.BucketAt( b ).Pixels ) )}" );

			var difference = Math.Abs( (double)nativeScale - managedScale );
			maxDifference = Math.Max( maxDifference, difference );
			if ( difference > tolerance ) differing++;
			if ( firstMoved < 0 && (nativeScale != 1 || managedScale != 1) ) firstMoved = i;
			rows.Add( $"| {i} | {(i < frames / 2 ? "bright" : "dark")} | {nativeScale:R} | {managedScale:R} | {difference:G3} |" );
		}

		gpu.WaitIdle();
		var errors = gpu.Device.ValidationErrors;
		var how = step > 0 ? $"{step}s a frame" : "adapting at once";
		var summary = $"Exposure parity: {frames - differing} of {frames} frames within {tolerance} ({how}), largest difference {maxDifference:G3}, first change at frame {firstMoved}, on {gpu.Device.Adapter.Name}; Basalt.Gpu validation errors {errors}";
		Log.Info( summary );
		Console.WriteLine( summary );

		var report = new List<string>
		{
			$"# Exposure parity on {gpu.Device.Adapter.Name}",
			"",
			summary,
			"",
			"| Frame | Input | Source 2 | Basalt.Gpu | Difference |",
			"|---|---|---|---|---|",
		};
		report.AddRange( rows );
		System.IO.File.WriteAllLines( System.IO.Path.Combine( folder, "results.md" ), report );

		scene.Destroy();

		// Made once for every scene, and kept for the process; this one ends here
		ParticleLightingSystem.Unlit.Dispose();
		target.Dispose();
		bright.Dispose();
		dark.Dispose();
		return differing == 0 && errors == 0 && firstMoved >= 0 ? 0 : 1;
	}
}
