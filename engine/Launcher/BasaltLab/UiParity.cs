using Basalt.Backend;
using Basalt.Gpu;
using Basalt.Source2;
using Basalt.Source2.Shaders;
using Sandbox.Rendering;
using System.Reflection;
using GpuDevice = Basalt.Gpu.Device;
using GpuFormat = Basalt.Gpu.Format;
using EngineCommandList = Sandbox.Rendering.CommandList;

namespace Sandbox.BasaltLab;

/// <summary>
/// <c>-ui-parity folder [-case name] [-tolerance N] [-validation] [-transpose-matrices]</c>: paints cards of UI with the
/// engine's <see cref="Painter"/> (<see cref="UiParityCases"/>), executes each card's command list on Source 2's
/// device as <c>Painter.Begin( Texture )</c> does, then replays the same command list on a Basalt.Gpu device of its
/// own through <see cref="UiReplay"/> and Basalt.Backend's <see cref="GraphicsContext"/>, and compares the pixels.
/// Each case writes <c>native.png</c>, <c>basalt.png</c> and <c>diff.png</c> (differences x16) to a folder of its own,
/// and the folder gets <c>results.md</c>. Exits 1 when a case differs by more than <c>-tolerance</c> (default 2) in any
/// channel, a command had no replay, or validation reported errors.
/// <para>
/// A spike for moving the engine's own rendering (UI, <c>Graphics</c>, <c>CommandList</c>) onto Basalt.Gpu; see
/// docs/managed/basalt-gpu-path.md. <c>-transpose-matrices</c> uploads matrix constants in the wrong order, a negative
/// check the transformed cases must fail.
/// </para>
/// </summary>
internal static class UiParity
{
	const int Width = 320, Height = 200;

	public static int Run( Source2Backend backend, string folder )
	{
		LoadFonts();
		backend.Phase( "Fonts" );
		backend.BeginFrame();

		var only = Args.String( "-case" );
		var cases = UiParityCases.All.Where( c => only is null || only.Split( ',' ).Contains( c.Name ) ).ToArray();
		var tolerance = Args.Int( "-tolerance", 2 );
		folder = System.IO.Path.GetFullPath( folder );
		System.IO.Directory.CreateDirectory( folder );
		var images = new UiParityCases.Images();

		// Introduce the device to the driver with Source 2's names, as native does, so both get the same driver profile
		// (AMD's changes trilinear filtering); -basalt-driver-names keeps Basalt's own to show the difference
		var source2Names = !Args.Has( "-basalt-driver-names" );
		using var gpu = GpuDevice.Create( new DeviceOptions
		{
			ApplicationName = source2Names ? "Sbox" : "Basalt Lab UI parity",
			EngineName = source2Names ? "Source2" : "Basalt",
			Validation = Args.Has( "-validation" ),
			Presentation = false,
			Message = m => Log.Info( $"Basalt.Gpu: {m.Severity} {m.Text}" ),
		} );
		using var library = new ShaderLibrary( gpu, ContentFiles.FindGame() );
		using var defaults = new ShaderDefaults( gpu );
		var drawer = new ShaderDrawer( gpu, library, defaults );
		ShaderFile[] shaders = [.. new[] { "ui_cssbox_batched", "ui_filter", "ui_backdropfilter", "ui_dropshadow", "ui_borderwrap", "downsample_cs" }.Select( s => library.Load( $"shaders/{s}.shader" ) )];
		backend.Phase( $"Basalt.Gpu device ({gpu.Adapter.Name})" );

		var rows = new List<string>();
		var failed = 0;
		var kinds = new SortedDictionary<string, int>();
		foreach ( var test in cases )
		{
			var result = RunCase( backend, gpu, drawer, shaders, images, test, System.IO.Path.Combine( folder, test.Name ), tolerance );
			foreach ( var (kind, count) in result.Replayed ) kinds[kind] = kinds.GetValueOrDefault( kind ) + count;
			var ok = result.Over == 0 && result.Unhandled.Count == 0 && result.Error is null;
			if ( !ok ) failed++;

			var line = $"{test.Name}: max channel difference {result.Max}, {result.Over} pixels over {tolerance}, {result.Over2} over 2; {result.Draws} draws, {result.Commands} commands"
				+ (result.Unhandled.Count > 0 ? $"; no replay for {string.Join( ", ", result.Unhandled )}" : "")
				+ (result.Error is null ? "" : $"; {result.Error}")
				+ (result.Notes.Count > 0 ? $"; {string.Join( "; ", result.Notes )}" : "");
			Log.Info( $"UI parity {line}" );
			Console.WriteLine( line );
			rows.Add( $"| {test.Name} | {test.Covers} | {result.Max} | {result.Over2} | {result.Draws} | {(ok ? "pass" : "FAIL")} |" );
		}

		gpu.WaitIdle();
		var summary = $"UI parity: {cases.Length - failed} of {cases.Length} cases within {tolerance} on {gpu.Adapter.Name}, {Width}x{Height} ({folder}); Basalt.Gpu validation errors {gpu.ValidationErrors}";
		Log.Info( summary );
		Console.WriteLine( summary );

		var report = new List<string>
		{
			$"# UI parity on {gpu.Adapter.Name}",
			"",
			summary,
			"",
			"| Case | Covers | Max channel difference | Pixels over 2 | Draws | |",
			"|---|---|---|---|---|---|",
		};
		report.AddRange( rows );
		report.Add( "" );
		report.Add( "Commands replayed, over every case:" );
		report.Add( "" );
		report.AddRange( kinds.Select( k => $"- `{k.Key}` x{k.Value}" ) );
		System.IO.File.WriteAllLines( System.IO.Path.Combine( folder, "results.md" ), report );
		return failed == 0 && gpu.ValidationErrors == 0 ? 0 : 1;
	}

	sealed class Result
	{
		public int Max, Over, Over2, Draws, Commands;
		public string Error;
		public SortedSet<string> Unhandled = new();
		public SortedDictionary<string, int> Replayed = new();
		public List<string> Notes = new();
	}

	static Result RunCase( Source2Backend backend, GpuDevice gpu, ShaderDrawer drawer, ShaderFile[] shaders, UiParityCases.Images images, UiParityCases.Case test, string folder, int tolerance )
	{
		var result = new Result();
		backend.BeginFrame();

		// Paint the card into a command list
		var commands = new EngineCommandList( $"UI parity {test.Name}" );
		using ( var painter = Painter.Begin( commands, new Rect( 0, 0, Width, Height ) ) ) test.Draw( painter, images );
		var batcher = commands.FindResource<Painter.Context>().Batcher;
		var textures = new List<Texture>( UsedTextures( batcher ) ) { Texture.Transparent, Texture.White, Texture.Black, Texture.Invalid };

		// Source 2: as Painter.Begin( Texture ) submits
		var target = Texture.CreateRenderTarget().WithSize( Width, Height ).Create();
		using ( var scope = Graphics.Scope.Create() )
		{
			Graphics.RenderTarget = RenderTarget.From( target );
			scope.Attributes.Set( "UIGammaOutput", true );
			scope.Attributes.Set( "UIFrameGrabEncoded", true );
			commands.Execute();
		}

		g_pRenderDevice.Flush();
		var native = target.GetPixels();
		backend.Phase( $"{test.Name}: Source 2" );

		// Basalt.Gpu: the same command list, replayed
		var basalt = new Color32[Width * Height];
		var list = gpu.BeginCommandList( QueueType.Graphics, $"UI parity {test.Name}" );
		var context = new GraphicsContext( gpu, drawer, list );
		UiReplay replay = null;
		var readback = gpu.CreateBuffer( new BufferInfo { Size = Width * Height * 4, Memory = MemoryLocation.Readback, Name = "Readback" } );
		try
		{
			var color = context.CreateTarget( Width, Height, GpuFormat.RGBA8Unorm, 1, "UI" );
			context.SetTarget( color );
			replay = new UiReplay( gpu, context, commands, textures, shaders ) { TransposeMatrices = Args.Has( "-transpose-matrices" ) };
			replay.Scope.Set( "UIGammaOutput", 1 );
			replay.Scope.Set( "UIFrameGrabEncoded", 1 );
			replay.Run();
			context.CopyToBuffer( color, readback );
			context.Finish();
			gpu.Wait( gpu.Submit( list ) );

			var bytes = gpu.Map( readback );
			for ( int i = 0; i < basalt.Length; i++ ) basalt[i] = new Color32( bytes[i * 4], bytes[i * 4 + 1], bytes[i * 4 + 2], bytes[i * 4 + 3] );
		}
		catch ( Exception e )
		{
			result.Error = $"{e.GetType().Name}: {e.Message}";
			Log.Warning( e, $"UI parity {test.Name}" );

			// Hand the list back to the device so the next case starts clean
			try
			{
				context.Finish();
				gpu.Wait( gpu.Submit( list ) );
			}
			catch ( Exception )
			{
			}
		}
		finally
		{
			gpu.WaitIdle();
			context.Release();
			replay?.Release();
			gpu.Destroy( readback );
			gpu.BeginFrame();
			commands.Reset();
			target.Dispose();
		}

		if ( replay is not null )
		{
			result.Unhandled = replay.Unhandled;
			result.Replayed = replay.Replayed;
			result.Notes = replay.Notes;
			result.Commands = replay.Replayed.Values.Sum();
		}

		result.Draws = context.Draws;
		backend.Phase( $"{test.Name}: Basalt.Gpu" );

		// Compare
		var diff = new Color32[native.Length];
		for ( int i = 0; i < native.Length; i++ )
		{
			var a = native[i];
			var b = basalt[i];
			var d = Math.Max( Math.Max( Math.Abs( a.r - b.r ), Math.Abs( a.g - b.g ) ), Math.Max( Math.Abs( a.b - b.b ), Math.Abs( a.a - b.a ) ) );
			result.Max = Math.Max( result.Max, d );
			if ( d > tolerance ) result.Over++;
			if ( d > 2 ) result.Over2++;
			var v = (byte)Math.Min( 255, d * 16 );
			diff[i] = new Color32( v, v, v, 255 );
		}

		System.IO.Directory.CreateDirectory( folder );
		Source2Backend.SavePng( native, Width, Height, System.IO.Path.Combine( folder, "native.png" ) );
		Source2Backend.SavePng( basalt, Width, Height, System.IO.Path.Combine( folder, "basalt.png" ) );
		Source2Backend.SavePng( diff, Width, Height, System.IO.Path.Combine( folder, "diff.png" ) );
		return result;
	}

	/// <summary>The textures the painter recorded uses of: what its instances' bindless indices can name.</summary>
	static IEnumerable<Texture> UsedTextures( PainterBatcher batcher )
	{
		var uses = (System.Collections.IEnumerable)typeof( PainterBatcher ).GetField( "_textures", BindingFlags.Instance | BindingFlags.NonPublic ).GetValue( batcher );
		foreach ( var use in uses ) yield return (Texture)use.GetType().GetProperty( "Texture" ).GetValue( use );
	}

	/// <summary>
	/// The core fonts, as the panel app loads them (through reflection: the font and filesystem types' assemblies
	/// aren't referenced here).
	/// </summary>
	static void LoadFonts()
	{
		var manager = typeof( Texture ).Assembly.GetType( "Sandbox.FontManager" );
		var instance = manager.GetField( "Instance", BindingFlags.Static | BindingFlags.Public ).GetValue( null );
		var core = typeof( EngineFileSystem ).GetProperty( "CoreContent", BindingFlags.Static | BindingFlags.Public ).GetValue( null );
		manager.GetMethod( "LoadAll", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic ).Invoke( instance, [core] );
	}
}
