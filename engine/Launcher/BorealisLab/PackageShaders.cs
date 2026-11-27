using Borealis;
using Borealis.Backend;
using Borealis.Gpu;
using Borealis.Source2;
using Borealis.Source2.Shaders;
using Sandbox.ShaderCompiler;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using GpuDevice = Borealis.Gpu.Device;

namespace Sandbox.BorealisLab;

/// <summary>
/// <c>-package-shaders folder -package-roots "a;b" [-package-draw] [-package-filter name]</c>: every package <c>.shader_c</c>
/// under the roots (workshop items, the download cache) read as Borealis.Gpu reads an outdated one, then sorted: what its
/// embedded source is (geometry or tessellation stage, compute, a pixel stage), whether the pass module made from it compiles
/// (today's route), and whether <see cref="ShaderConverter"/> makes a surface of it (<see cref="ShaderConverter.FromEmbedded"/>)
/// that compiles. <c>-package-draw</c> also draws each surface that compiles beside its <c>.shader_c</c> on a Borealis.Gpu
/// device, with the rope's textures in every texture. <c>-package-list file</c> takes only the files it names (one path a
/// line), <c>-package-skip-today</c> leaves the module compile out, <c>-package-skip-surfaces</c> the surface conversion;
/// <c>-package-threads n</c> surveys n files at once (the cores by default). Writes <c>results.tsv</c>, the converted surfaces and
/// the pictures into the folder. Nothing of the engine starts.
/// </summary>
internal static class PackageShaders
{
	public static bool Requested => Args.String( "-package-shaders" ) is not null;

	sealed record Row( string Path, string Root, string Name )
	{
		public string Abi = "", Programs = "", Kind = "", Today = "", Convert = "", Surface = "", Picture = "", Notes = "";
		public string SurfacePath;
	}

	public static int Run()
	{
		var folder = Path.GetFullPath( Args.String( "-package-shaders" ) );
		Directory.CreateDirectory( folder );
		var game = ContentFiles.FindGame();
		var core = game.Roots.First( r => File.Exists( Path.Combine( r, "shaders", "sbox", "pass.slangh" ) ) );
		var shaders = Path.Combine( core, "shaders" );
		var native = EngineFolders.NativeIn( Path.GetDirectoryName( core ) );
		var filter = Args.String( "-package-filter" );

		// -package-list file: only the .shader_c files it names, one path a line (an earlier run's surfaces that compiled)
		var list = Args.String( "-package-list" ) is { } listFile ? new HashSet<string>( File.ReadAllLines( listFile ).Select( l => Path.GetFullPath( l.Trim() ) ), StringComparer.OrdinalIgnoreCase ) : null;

		var rows = new List<Row>();
		foreach ( var root in (Args.String( "-package-roots" ) ?? "").Split( ';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries ) )
		{
			foreach ( var file in Directory.EnumerateFiles( root, "*.shader_c", SearchOption.AllDirectories ).Order( StringComparer.OrdinalIgnoreCase ) )
			{
				var name = Path.GetFileName( file ).Split( '.' )[0];
				if ( filter is not null && !name.Contains( filter, StringComparison.OrdinalIgnoreCase ) ) continue;
				if ( list is not null && !list.Contains( Path.GetFullPath( file ) ) ) continue;
				rows.Add( new Row( file, Path.GetFullPath( root ), name ) );
			}
		}

		var compilers = new Dictionary<string, SlangCompiler>( StringComparer.OrdinalIgnoreCase );
		SlangCompiler CompilerFor( string root )
		{
			if ( !compilers.TryGetValue( root, out var compiler ) )
				compilers[root] = compiler = new SlangCompiler( native, [shaders, root] ) { CacheFolder = Path.Combine( folder, "cache" ) };
			return compiler;
		}

		var converted = Path.Combine( folder, "converted" );
		var modules = Path.Combine( folder, "modules" );
		Directory.CreateDirectory( converted );
		Directory.CreateDirectory( modules );

		Parallel.ForEach( rows, new ParallelOptions { MaxDegreeOfParallelism = Math.Max( 1, Args.Int( "-package-threads", Environment.ProcessorCount ) ) }, row =>
		{
			try
			{
				SlangCompiler compiler;
				lock ( compilers ) compiler = CompilerFor( row.Root );
				Survey( row, compiler, shaders, converted, modules );
			}
			catch ( Exception e )
			{
				row.Notes += $"failed: {e.GetType().Name}: {Flat( e.Message )}";
			}

			Console.WriteLine( $"{row.Name}\t{row.Kind}\t{row.Today}\t{row.Convert}\t{row.Surface}" );
		} );

		if ( Args.Has( "-package-draw" ) ) Draw( rows, folder );

		var lines = new List<string> { "path\tname\tabi\tprograms\tkind\ttoday\tconvert\tsurface\tpicture\tnotes" };
		lines.AddRange( rows.Select( r => string.Join( '\t', r.Path, r.Name, r.Abi, r.Programs, r.Kind, r.Today, r.Convert, r.Surface, r.Picture, r.Notes ) ) );
		File.WriteAllLines( Path.Combine( folder, "results.tsv" ), lines );
		Console.WriteLine( $"Package shaders: {rows.Count} files, results in {folder}" );
		return 0;
	}

	static void Survey( Row row, SlangCompiler compiler, string shaders, string converted, string modules )
	{
		var shader = ShaderFile.LoadOutdated( row.Path );
		var source = shader.EmbeddedSource;
		row.Abi = shader.Outdated is null ? "current" : "outdated";
		row.Programs = source is null ? "" : string.Join( ' ', source.Keys.Select( k => k.Replace( "VFX_PROGRAM_", "" ) ).Order() );
		var features = (shader.Features?.StaticCombos ?? []).Select( c => (c.Name, c.Min, c.Max) ).ToList();

		if ( source is null ) row.Kind = "no source";
		else if ( source.Keys.Any( k => k is "VFX_PROGRAM_GS" ) ) row.Kind = "geometry";
		else if ( source.Keys.Any( k => k is "VFX_PROGRAM_HS" or "VFX_PROGRAM_DS" ) ) row.Kind = "tessellation";
		else if ( source.ContainsKey( "VFX_PROGRAM_CS" ) && !source.ContainsKey( "VFX_PROGRAM_PS" ) ) row.Kind = "compute";
		else if ( source.ContainsKey( "VFX_PROGRAM_PS" ) ) row.Kind = "pixel";
		else row.Kind = "other";
		if ( source is null ) return;

		// Today's route: the pass module made from the embedded source
		var stem = Regex.Replace( row.Name, "[^a-zA-Z0-9_]", "_" );
		if ( PassConverter.RefuseEmbedded( source ) is { } refused ) row.Today = "refused: " + refused;
		else if ( Args.Has( "-package-skip-today" ) ) row.Today = "not checked";
		else
		{
			var path = Path.Combine( modules, $"{stem}_{Hash( row.Path )}.slang" );
			var text = PassConverter.FromEmbedded( source, features, stem );
			File.WriteAllText( path, text );
			var errors = PassConversion.Compile( compiler, PassModule.FromText( path, text, [shaders, row.Root] ), out var stages );
			row.Today = errors is null ? $"compiles ({stages})" : "fails: " + FirstError( errors );
			if ( errors is not null ) File.WriteAllText( Path.ChangeExtension( path, ".errors.txt" ), errors );
		}

		if ( row.Kind != "pixel" || Args.Has( "-package-skip-surfaces" ) ) return;

		// The converter over the .shader made again from the embedded source
		var reconstructed = ShaderConverter.FromEmbedded( source, features );
		var surfaceName = $"{stem}_{Hash( row.Path )}";
		File.WriteAllText( Path.Combine( converted, surfaceName + ".shader.txt" ), reconstructed );
		var result = ShaderConverter.Convert( reconstructed, stem, include => Include( include, shaders, row.Root ) );
		row.Notes = string.Join( "; ", result.Notes );
		if ( result.Source is null )
		{
			row.Convert = "no";
			return;
		}

		row.Convert = result.Notes.Count == 0 ? "clean" : "with notes";
		var surfacePath = Path.Combine( converted, surfaceName + ".slang" );
		File.WriteAllText( surfacePath, result.Source );
		try
		{
			var compiled = compiler.CompileSurface( surfacePath );
			row.Surface = $"compiles ({compiled.Passes.Count} passes)";
			row.SurfacePath = surfacePath;
		}
		catch ( Sandbox.ShaderCompiler.ShaderCompileException e )
		{
			row.Surface = "fails: " + FirstError( e.Errors );
			File.WriteAllText( Path.ChangeExtension( surfacePath, ".errors.txt" ), e.Errors );
		}
	}

	static string Include( string include, string shaders, string root )
	{
		foreach ( var folder in new[] { shaders, root } )
		{
			var path = Path.Combine( folder, include );
			if ( File.Exists( path ) ) return File.ReadAllText( path );
		}

		return null;
	}

	/// <summary>Each surface that compiled beside its <c>.shader_c</c>, on a device with the shader's package as content.</summary>
	static void Draw( List<Row> rows, string folder )
	{
		var pictures = Path.Combine( folder, "pictures" );
		Directory.CreateDirectory( pictures );

		using var gpu = GpuDevice.Create( new DeviceOptions
		{
			ApplicationName = "Borealis Lab package shaders",
			Validation = Args.Has( "-validation" ),
			Presentation = false,
			Message = m => { if ( m.Severity == MessageSeverity.Error ) Console.WriteLine( $"Borealis.Gpu: {m.Severity} {m.Text}" ); },
		} );

		var previous = RenderHost.Current;
		try
		{
			foreach ( var row in rows.Where( r => r.SurfacePath is not null ) )
			{
				try
				{
					// The .shader_c by a plain name in a content root of its own (the download cache names files name.hash.shader_c)
					var stage = Path.Combine( folder, "content", Path.GetFileNameWithoutExtension( row.SurfacePath ) );
					var relative = Path.GetRelativePath( row.Root, row.Path ).Replace( '\\', '/' );
					relative = Regex.Replace( relative, @"\.[0-9a-f]{12,16}\.shader_c$", ".shader_c" );
					Directory.CreateDirectory( Path.GetDirectoryName( Path.Combine( stage, relative ) ) );
					File.Copy( row.Path, Path.Combine( stage, relative ), true );

#pragma warning disable CA2000 // Disposed at the end of the iteration (the analyzer loses it through the render host)
					using var device = new BackendDevice( gpu );
#pragma warning restore CA2000
					device.Content.Add( stage );
					device.Surfaces.ConvertShaders = false;
					device.Surfaces.AddFolder( Path.Combine( device.Content.Roots[0], "shaders" ) );
					string error = null;
					device.Surfaces.Error = m => error ??= m;
					RenderHost.Current = new BackendRenderHost( device );

					var shaderPath = Path.ChangeExtension( relative, ".shader" );
					var classic = (BackendMaterial)((Borealis.GpuDevice)device).CreateMaterial( $"{row.Name} (.shader)", shaderPath );
					if ( classic.Shader is null )
					{
						row.Picture = "the .shader_c doesn't load here";
						continue;
					}

					var color = SurfaceParity.Rope( device ).Color;
					var textures = new Dictionary<string, string>();
					foreach ( var variable in classic.Shader.Programs.Where( p => p is not null ).SelectMany( p => p.Variables ).Where( v => v.Name.StartsWith( "g_t", StringComparison.Ordinal ) ) ) textures[variable.Name] = color;
					foreach ( var (variable, path) in textures ) classic.SetTexture( variable, (BackendTexture)device.LoadTexture( path ) );

					using var surface = device.Surfaces.CreateMaterial( row.SurfacePath, textures, null, $"{row.Name} (surface)" ) as SurfaceMaterial;
					if ( surface is null )
					{
						row.Picture = "surface material not made: " + Flat( error ?? "" );
						continue;
					}

					var a = SurfaceParity.Render( device, classic );
					var b = SurfaceParity.Render( device, surface );
					var (mean, max, over, diff) = SurfaceParity.Compare( a, b );
					var name = Path.GetFileNameWithoutExtension( row.SurfacePath );
					SurfaceParity.Save( a, Path.Combine( pictures, name + "-shader.png" ) );
					SurfaceParity.Save( b, Path.Combine( pictures, name + "-surface.png" ) );
					SurfaceParity.Save( diff, Path.Combine( pictures, name + "-diff.png" ) );
					var blank = a.All( p => p.r == a[0].r && p.g == a[0].g && p.b == a[0].b );
					row.Picture = $"mean {mean:F2} max {max} over8 {over}{(blank ? " (shader side blank)" : "")}";
				}
				catch ( Exception e )
				{
					row.Picture = $"failed: {e.GetType().Name}: {Flat( e.Message )}";
				}

				Console.WriteLine( $"{row.Name}\tpicture\t{row.Picture}" );
			}
		}
		finally
		{
			gpu.WaitIdle();
			RenderHost.Current = previous;
		}
	}

	static string Hash( string text ) => System.Convert.ToHexString( System.Security.Cryptography.SHA256.HashData( System.Text.Encoding.UTF8.GetBytes( text ) ), 0, 3 ).ToLowerInvariant();

	static string FirstError( string errors ) => Flat( errors.Split( '\n' ).FirstOrDefault( l => l.Contains( "error", StringComparison.OrdinalIgnoreCase ) ) ?? errors.Split( '\n' ).FirstOrDefault( l => l.Trim().Length > 0 ) ?? "" );

	static string Flat( string text ) => text.Replace( '\t', ' ' ).Replace( "\r", "" ).Replace( '\n', ' ' ).Trim();
}
