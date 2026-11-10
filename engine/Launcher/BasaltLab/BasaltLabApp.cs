using Basalt;

namespace Sandbox.BasaltLab;

/// <summary>
/// Basalt Lab: Basalt rendering a <see cref="RenderWorld"/> with nothing else of the engine running. Shows it in a window,
/// or with <c>-capture path.png</c> renders it into a texture, reads it back, saves it and exits.
///
/// Arguments:
/// <list type="bullet">
/// <item><c>-capture path.png</c> - render <c>-capture-frame</c> frames (default 10) into a texture, no window, and save the last.</item>
/// <item><c>-width</c> / <c>-height</c> - the window or texture size (default 640x360, the render tests' size).</item>
/// <item><c>-frames N</c> - close the window after N frames.</item>
/// <item><c>-novsync</c> - don't wait for the display.</item>
/// <item><c>-scene-modules</c> - also start the native modules SceneLab starts (world renderer, animation, physics, sound).</item>
/// <item><c>-ui-parity folder</c> - draw a card of UI through Source 2 and through a Basalt.Gpu device, compare and exit (<see cref="UiParity"/>).</item>
/// </list>
/// Startup and frame times go to the log, and a summary to the console.
/// </summary>
internal static class BasaltLabApp
{
	public static int Run()
	{
		var backend = new Source2Backend();
		backend.Start( sceneModules: Args.Has( "-scene-modules" ) );

		if ( Args.String( "-ui-parity" ) is { } parity )
		{
			var code = UiParity.Run( backend, parity );
			backend.Exit( code );
			return code;
		}

		var width = Args.Int( "-width", 640 );
		var height = Args.Int( "-height", 360 );
		var capture = Args.String( "-capture" );
		var captureFrame = Args.Int( "-capture-frame", 10 );
		var frames = Args.Int( "-frames", 0 );

		var scene = LabScene.SunShadows( backend );
		backend.Phase( "Scene" );

		var renderer = new RenderSystem();
		var window = capture is null ? backend.CreateWindow( "Basalt Lab", width, height, vsync: !Args.Has( "-novsync" ) ) : null;
		// The formats a window's frame is drawn in before it's copied to the swap chain, so a capture sees what a window shows
		var target = capture is not null ? ViewTarget.Create( new Vector2Int( width, height ), 1, TextureFormat.RGBA16161616F, TextureFormat.D32FS8, "Basalt Lab" ) : null;
		if ( window is not null ) backend.Phase( "Window" );

		var frameTimes = new List<double>();
		var exitCode = 0;

		for ( var frame = 1; ; frame++ )
		{
			var start = System.Diagnostics.Stopwatch.GetTimestamp();
			backend.BeginFrame();

			if ( window is not null )
			{
				window.Poll();
				if ( window.Closed ) break;

				var size = window.Size;
				if ( size.x >= 1 && size.y >= 1 )
				{
					scene.View.Viewport = new Rect( 0, 0, size.x, size.y );
					renderer.Render( scene.World, scene.View, window.SwapChain, size );
				}
			}
			else
			{
				scene.View.Viewport = new Rect( 0, 0, width, height );
				renderer.Render( scene.World, scene.View, target );
			}

			backend.EndFrame();
			window?.Present();

			if ( frame == 1 ) backend.Phase( "First frame" );
			else frameTimes.Add( System.Diagnostics.Stopwatch.GetElapsedTime( start ).TotalMilliseconds );

			if ( capture is not null && frame == captureFrame )
			{
				var pixels = backend.ReadPixels( target );
				var path = System.IO.Path.GetFullPath( capture );
				System.IO.Directory.CreateDirectory( System.IO.Path.GetDirectoryName( path ) );
				Source2Backend.SavePng( pixels, width, height, path );
				backend.Phase( $"Capture to {path}" );
				break;
			}

			if ( frames > 0 && frame >= frames ) break;
		}

		var summary = $"Basalt Lab: {string.Join( ", ", backend.Phases.Select( x => $"{x.Name} {x.Ms}ms" ) )}; {backend.Phases.Sum( x => x.Ms )}ms from process start";
		if ( frameTimes.Count > 0 ) summary += $"; {frameTimes.Count} more frames, median {frameTimes.Order().ElementAt( frameTimes.Count / 2 ):F2}ms";
		Log.Info( summary );
		Console.WriteLine( summary );
		Log.Info( $"Basalt Lab: {renderer.Stats}" );

		window?.Dispose();
		target?.Dispose();
		renderer.Dispose();

		backend.Exit( exitCode );
		return exitCode;
	}
}

internal static class Args
{
	public static string String( string name )
	{
		var args = Environment.GetCommandLineArgs();
		var i = Array.FindIndex( args, x => x.Equals( name, StringComparison.OrdinalIgnoreCase ) );
		return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
	}

	public static bool Has( string name ) => Environment.GetCommandLineArgs().Any( x => x.Equals( name, StringComparison.OrdinalIgnoreCase ) );

	public static int Int( string name, int fallback ) => int.TryParse( String( name ), out var value ) ? value : fallback;
}
