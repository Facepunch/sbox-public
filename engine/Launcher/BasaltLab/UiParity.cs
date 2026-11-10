using System.Reflection;

namespace Sandbox.BasaltLab;

/// <summary>
/// <c>-ui-parity folder [-case name] [-tolerance N] [-validation] [-basalt-driver-names] [-reflection]</c>: paints cards of UI with
/// the engine's <see cref="Painter"/> (<see cref="UiParityCases"/>) into a texture twice, through
/// <c>Painter.Begin( Texture )</c> on Source 2's device and on a Basalt.Gpu device through the engine's own
/// <see cref="BasaltGpuRenderBackend"/> under <c>Graphics</c>, and compares the pixels. Each case writes
/// <c>native.png</c>, <c>basalt.png</c> and <c>diff.png</c> (differences x16) to a folder of its own, and the folder gets
/// <c>results.md</c>. Exits 1 when a case differs by more than <c>-tolerance</c> (default 2) in any channel, a case
/// threw, or validation reported errors.
/// <para>
/// Stage 1 of moving the engine's own rendering (UI, <c>Graphics</c>, <c>CommandList</c>) onto Basalt.Gpu; see
/// docs/managed/basalt-gpu-path.md. <c>-basalt-driver-names</c> introduces the device to the driver as Basalt rather than
/// as Source 2 does, which changes AMD's trilinear filtering. <c>-reflection</c> binds every Basalt.Gpu draw by reflection
/// instead of through material objects.
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

		using var gpu = BasaltGpuRenderBackend.Create( Args.Has( "-validation" ), m => Log.Info( $"Basalt.Gpu: {m.Severity} {m.Text}" ), source2Names: !Args.Has( "-basalt-driver-names" ) );
		gpu.MaterialObjects = !Args.Has( "-reflection" );
		backend.Phase( $"Basalt.Gpu device ({gpu.Device.Adapter.Name})" );

		var rows = new List<string>();
		var failed = 0;
		foreach ( var test in cases )
		{
			var result = RunCase( backend, gpu, images, test, System.IO.Path.Combine( folder, test.Name ), tolerance );
			var ok = result.Over == 0 && result.Error is null;
			if ( !ok ) failed++;

			var line = $"{test.Name}: max channel difference {result.Max}, {result.Over} pixels over {tolerance}, {result.Over2} over 2; {result.Draws} draws"
				+ (result.Error is null ? "" : $"; {result.Error}");
			Log.Info( $"UI parity {line}" );
			Console.WriteLine( line );
			rows.Add( $"| {test.Name} | {test.Covers} | {result.Max} | {result.Over2} | {result.Draws} | {(ok ? "pass" : "FAIL")} |" );
		}

		gpu.WaitIdle();
		var errors = gpu.Device.ValidationErrors;
		var stats = gpu.MaterialObjectStats;
		var binding = gpu.MaterialObjects ? $"material objects ({stats.Objects} objects, {stats.SetsWritten} sets written, {stats.SetsHeld} held, {stats.Fallbacks} draws by reflection)" : "binding by reflection";
		var summary = $"UI parity: {cases.Length - failed} of {cases.Length} cases within {tolerance} on {gpu.Device.Adapter.Name}, {Width}x{Height} ({folder}); {binding}; Basalt.Gpu validation errors {errors}";
		Log.Info( summary );
		Console.WriteLine( summary );

		var report = new List<string>
		{
			$"# UI parity on {gpu.Device.Adapter.Name}",
			"",
			summary,
			"",
			"Each card is painted with `Painter.Begin( Texture )` on Source 2's device, then on a Basalt.Gpu device through `BasaltGpuRenderBackend`.",
			"",
			"| Case | Covers | Max channel difference | Pixels over 2 | Basalt.Gpu draws | |",
			"|---|---|---|---|---|---|",
		};
		report.AddRange( rows );
		System.IO.File.WriteAllLines( System.IO.Path.Combine( folder, "results.md" ), report );

		return failed == 0 && errors == 0 ? 0 : 1;
	}

	sealed class Result
	{
		public int Max, Over, Over2, Draws;
		public string Error;
	}

	static Result RunCase( Source2Backend backend, BasaltGpuRenderBackend gpu, UiParityCases.Images images, UiParityCases.Case test, string folder, int tolerance )
	{
		var result = new Result();
		backend.BeginFrame();

		// Source 2
		var target = Texture.CreateRenderTarget().WithSize( Width, Height ).Create();
		using ( var painter = Painter.Begin( target ) ) test.Draw( painter, images );
		g_pRenderDevice.Flush();
		var native = target.GetPixels();
		target.Dispose();
		backend.Phase( $"{test.Name}: Source 2" );

		// Basalt.Gpu: the same card, painted again on the other device
		var basalt = new Color32[Width * Height];
		var other = Texture.CreateRenderTarget().WithSize( Width, Height ).Create();
		var draws = gpu.Context.Draws;
		try
		{
			using ( var painter = Painter.Begin( other, gpu ) ) test.Draw( painter, images );
			basalt = gpu.ReadPixels( other );
		}
		catch ( Exception e )
		{
			result.Error = $"{e.GetType().Name}: {e.Message}";
			Log.Warning( e, $"UI parity {test.Name}" );
		}
		finally
		{
			gpu.WaitIdle();
			other.Dispose();
			gpu.NextFrame();
		}

		result.Draws = gpu.Context.Draws - draws;
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
