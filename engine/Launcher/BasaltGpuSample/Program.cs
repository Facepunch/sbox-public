using Basalt.Gpu;
using System.Diagnostics;
using System.Threading;

namespace BasaltGpuSample;

/// <summary>
/// Opens a window and renders <see cref="SampleScene"/> through Basalt.Gpu until it's closed or a limit is reached.
/// Exits with 1 if the validation layer reported an error.
/// </summary>
/// <remarks>
/// <c>-frames N</c> and <c>-seconds S</c> stop it; <c>-resize</c> resizes the window every 20 frames;
/// <c>-novalidation</c> turns the validation layer off, <c>-syncvalidation</c> adds its synchronisation checks;
/// <c>-immediate</c> presents without vsync. <c>-basalt</c> draws Basalt's own renderer instead
/// (<see cref="BasaltScene"/>); <c>-capture path.png</c> saves its last frame.
/// </remarks>
internal static class Program
{
	static int Main( string[] args )
	{
		var frameLimit = IntArg( args, "-frames", int.MaxValue );
		var secondsLimit = IntArg( args, "-seconds", int.MaxValue );
		var resize = args.Contains( "-resize" );
		var validation = !args.Contains( "-novalidation" );
		var immediate = args.Contains( "-immediate" );
		var syncValidation = args.Contains( "-syncvalidation" );
		var basalt = args.Contains( "-basalt" );
		var capture = StringArg( args, "-capture" );

		if ( !OperatingSystem.IsWindows() )
		{
			Console.Error.WriteLine( "The sample's window is Win32; Basalt.Gpu itself also takes Xlib and Wayland surfaces." );
			return 2;
		}

		var errors = 0;
		using var window = new Win32Window( "Basalt.Gpu", 1280, 800 );
		using var device = Device.Create( new DeviceOptions
		{
			ApplicationName = "Basalt.Gpu sample",
			Validation = validation,
			SynchronizationValidation = syncValidation,
			Message = m =>
			{
				if ( m.Severity < MessageSeverity.Warning ) return;
				if ( m.Severity == MessageSeverity.Error ) Interlocked.Increment( ref errors );
				Console.WriteLine( $"[{m.Severity}] {m.Id}: {m.Text}" );
			},
		} );

		Console.WriteLine( $"{device.Adapter}, validation {(device.ValidationEnabled ? "on" : "off (not installed?)")}, async compute {device.Capabilities.AsyncCompute}" );

		var (width, height) = window.ClientSize;
		using var swapChain = device.CreateSwapChain( new SwapChainInfo
		{
			Surface = SurfaceSource.Win32( window.Instance, window.Handle ),
			Width = width,
			Height = height,
			PresentMode = immediate ? PresentMode.Immediate : PresentMode.Fifo,
		} );

		Console.WriteLine( $"Swap chain {swapChain.Width}x{swapChain.Height} {swapChain.Format}, {swapChain.ImageCount} images, {swapChain.PresentMode}" );

		using var scene = basalt ? null : new SampleScene( device, swapChain.Format );
		using var world = basalt ? new BasaltScene( device ) : null;
		var clock = Stopwatch.StartNew();
		var frames = 0;
		var resizes = 0;
		var lastTitle = 0.0;
		var frameTimes = new List<double>();
		var renderTimes = new List<double>();
		var last = clock.Elapsed.TotalMilliseconds;

		while ( !window.Closed && frames < frameLimit && clock.Elapsed.TotalSeconds < secondsLimit )
		{
			window.Pump();

			if ( resize && frames > 0 && frames % 20 == 0 && resizes < frames / 20 )
			{
				resizes++;
				int[][] sizes = [[960, 540], [1280, 800], [640, 720], [1600, 600]];
				var size = sizes[resizes % sizes.Length];
				window.SetClientSize( size[0], size[1] );
				window.Pump();
			}

			if ( window.Resized )
			{
				window.Resized = false;
				var (w, h) = window.ClientSize;
				swapChain.Resize( w, h );
			}

			device.BeginFrame();
			if ( !swapChain.Acquire( out var image ) )
			{
				Thread.Sleep( 16 );
				continue;
			}

			var time = (float)clock.Elapsed.TotalSeconds;
			if ( world is not null )
			{
				// Basalt renders and submits into its own HDR target, which is then copied into the window
				var renderStart = clock.Elapsed.TotalMilliseconds;
				var (texture, layout, color) = world.Render( swapChain.Width, swapChain.Height, time );
				renderTimes.Add( clock.Elapsed.TotalMilliseconds - renderStart );

				var copy = device.BeginCommandList( QueueType.Graphics, "Copy to window" );
				copy.Barrier( TextureBarrier.Transition( texture, layout, ImageLayout.CopySource, PipelineStage.AllCommands, MemoryAccess.MemoryWrite, PipelineStage.Transfer, MemoryAccess.TransferRead ) );
				copy.Barrier( TextureBarrier.Transition( image, ImageLayout.Undefined, ImageLayout.CopyDestination, PipelineStage.Transfer, MemoryAccess.None, PipelineStage.Transfer, MemoryAccess.TransferWrite ) );
				var rect = (0, 0, swapChain.Width, swapChain.Height);
				copy.Blit( texture, rect, image, rect, Filter.Nearest );
				copy.Barrier( TextureBarrier.Transition( image, ImageLayout.CopyDestination, ImageLayout.Present, PipelineStage.Transfer, MemoryAccess.TransferWrite, PipelineStage.BottomOfPipe, MemoryAccess.None ) );
				Basalt.Backend.BackendDevice.SetLayout( color, ImageLayout.CopySource );
				device.Submit( copy, swapChain: swapChain );
			}
			else
			{
				var list = device.BeginCommandList( QueueType.Graphics, "Frame" );
				scene.Render( list, image, ImageLayout.Undefined, ImageLayout.Present, time );
				device.Submit( list, swapChain: swapChain );
			}

			swapChain.Present();
			frames++;

			var now = clock.Elapsed.TotalMilliseconds;
			frameTimes.Add( now - last );
			last = now;

			if ( now - lastTitle > 500 )
			{
				lastTitle = now;
				window.Title = $"Basalt.Gpu - {device.Adapter.Name} - {swapChain.Width}x{swapChain.Height} - {frameTimes[^1]:0.00} ms";
			}
		}

		device.WaitIdle();

		if ( capture is not null && world is not null && frames > 0 )
		{
			var (texture, layout, color) = world.Render( swapChain.Width, swapChain.Height, (float)clock.Elapsed.TotalSeconds );
			device.WaitIdle();
			Capture.Save( device, texture, layout, capture );
			Basalt.Backend.BackendDevice.SetLayout( color, ImageLayout.CopySource );
			Console.WriteLine( $"Saved {capture}" );
		}

		frameTimes.Sort();
		var median = frameTimes.Count > 0 ? frameTimes[frameTimes.Count / 2] : 0;
		Console.WriteLine( $"{frames} frames in {clock.Elapsed.TotalSeconds:0.0}s, median {median:0.00} ms, {resizes} resizes, {device.ValidationErrors} validation errors" );
		if ( renderTimes.Count > 0 )
		{
			renderTimes.Sort();
			Console.WriteLine( $"Basalt: {world.ObjectCount} objects, {renderTimes[renderTimes.Count / 2]:0.00} ms median on the CPU to render and submit a frame" );
		}

		Console.WriteLine( device.DescribeResources() );
		return device.ValidationErrors > 0 || errors > 0 ? 1 : 0;
	}

	static string StringArg( string[] args, string name )
	{
		var i = Array.IndexOf( args, name );
		return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
	}

	static int IntArg( string[] args, string name, int fallback )
	{
		var i = Array.IndexOf( args, name );
		return i >= 0 && i + 1 < args.Length && int.TryParse( args[i + 1], out var value ) ? value : fallback;
	}
}
