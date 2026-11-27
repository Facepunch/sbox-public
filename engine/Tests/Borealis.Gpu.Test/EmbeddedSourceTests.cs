using Borealis.Backend;
using Borealis.Source2.Shaders;
using System.Buffers.Binary;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;

namespace BorealisGpuTests;

/// <summary>
/// Shaders an older engine compiled (a package's, docs/reference/borealis-pass-modules.md): their stages come from a pass module made
/// from the source embedded in the file, compiled here, not from a recompile through the ShaderCompiler; and the same
/// variants come out as the recompiled file's.
/// </summary>
[TestClass]
[DoNotParallelize]
public class EmbeddedSourceTests : GpuTest
{
	// A pass shader kept as a .shader by the tests (unlit, a UI pass: Content/shaders), so there is a current .shader_c to compare with
	const string Shader = "shaders/unlit.shader";

	/// <summary>
	/// unlit as a package would ship it from an older engine: the ABI epoch it was compiled for isn't this one, and
	/// each program's source is embedded as the compiler embeds it. It loads and draws from that source, the variant the
	/// same as the current file's, and nothing recompiles it.
	/// </summary>
	[TestMethod]
	public void OutdatedShaderDrawsFromItsEmbeddedSource()
	{
		var folder = Folder();
		try
		{
			using var backend = Backend( folder, out var current );
			var errors = new List<string>();
			backend.Passes.Error = errors.Add;
			backend.Shaders.UpgradeCache = Path.Combine( folder, "upgraded" );
			backend.Shaders.Upgrader = ( _, _ ) => throw new AssertFailedException( "recompiled through the ShaderCompiler" );

			Write( folder, Outdated( current, Embedded( File.ReadAllText( Source() ) ) ) );
			var shader = backend.Shaders.Load( "shaders/package_vignette.shader" );
			Assert.IsNotNull( shader );
			Assert.IsNotNull( shader.Outdated, "read as it is, outdated" );

			var classic = Classic( backend, current );
			var module = backend.Shaders.Variant( shader, "Default", [] );
			Assert.IsTrue( module is { FromModule: true }, "drawn from its embedded source" );
			Assert.AreEqual( 0, errors.Count, string.Join( "\n", errors ) );
			var differences = PassModuleTests.Compare( classic, module ).ToList();
			Assert.AreEqual( 0, differences.Count, string.Join( "\n", differences ) );
			Assert.IsTrue( Directory.EnumerateFiles( Path.Combine( folder, "embedded" ), "package_vignette.*.slang" ).Any(), "the module is written down" );
		}
		finally
		{
			Directory.Delete( folder, true );
		}
	}

	/// <summary>
	/// A package's outdated shaders compile as it mounts (<see cref="PassModules.CompileMounted"/>): each mode's first stages,
	/// on the compile threads, so the first draw finds them made and compiles nothing; a shader whose source doesn't compile
	/// is told once, by package, and a file that isn't outdated or has no source is left alone.
	/// </summary>
	[TestMethod]
	public void PackageShadersCompileOnMount()
	{
		var folder = Folder();
		try
		{
			using var backend = Backend( folder, out var current );
			var errors = new List<string>();
			backend.Passes.Error = errors.Add;
			var compiles = new List<string>();
			backend.Passes.Info = compiles.Add;
			backend.Passes.UseOffline = false;

			Write( folder, Outdated( current, Embedded( File.ReadAllText( Source() ) ) ) );
			var broken = Embedded( File.ReadAllText( Source() ) );
			broken["VFX_PROGRAM_PS"] = broken["VFX_PROGRAM_PS"].Replace( "MainPs(", "MainPs( NotAType t," );
			Write( folder, Outdated( current, broken ), "package_broken" );
			Write( folder, Outdated( current, null ), "package_nosource" );
			Write( folder, current, "package_current" );

			var files = new[] { "package_vignette", "package_broken", "package_nosource", "package_current" }.Select( n => Path.Combine( folder, "shaders", $"{n}.shader_c" ) ).ToList();
			var mounted = backend.Passes.CompileMounted( "Test Package", files ).GetAwaiter().GetResult();
			Assert.AreEqual( 4, mounted.Files );
			Assert.AreEqual( 2, mounted.Modules, "the two with source" );
			Assert.AreEqual( 1, mounted.NoModule );
			Assert.IsTrue( mounted.Stages > 0 && mounted.Failed > 0 && mounted.Failed < mounted.Stages, $"{mounted}" );
			Assert.IsTrue( errors.Count > 0 && errors.TrueForAll( e => e.StartsWith( "Test Package: package_broken.shader_c doesn't compile", StringComparison.Ordinal ) ), string.Join( "\n", errors ) );

			// The first draw compiles nothing more
			compiles.Clear();
			var shader = backend.Shaders.Load( "shaders/package_vignette.shader" );
			Assert.IsTrue( backend.Shaders.Variant( shader, "Default", [] ) is { FromModule: true }, "drawn from its embedded source" );
			Assert.AreEqual( 0, compiles.Count( c => c.Contains( "compiling", StringComparison.Ordinal ) ), string.Join( "\n", compiles ) );
		}
		finally
		{
			Directory.Delete( folder, true );
		}
	}

	/// <summary>
	/// An outdated shader whose embedded source doesn't compile, or that has none, is recompiled through the ShaderCompiler
	/// as before, and draws from the recompiled file.
	/// </summary>
	[TestMethod]
	public void OutdatedShaderWithoutUsableSourceIsRecompiled()
	{
		var folder = Folder();
		try
		{
			using var backend = Backend( folder, out var current );
			var errors = new List<string>();
			backend.Passes.Error = errors.Add;
			var unloaded = new List<string>();
			backend.Shaders.Unloaded = unloaded.Add;
			var recompiled = 0;
			backend.Shaders.UpgradeCache = Path.Combine( folder, "upgraded" );
			backend.Shaders.Upgrader = ( _, output ) =>
			{
				recompiled++;
				File.WriteAllBytes( output, current );
				return true;
			};

			// Source that doesn't compile: the module is tried, then the recompile
			var broken = Embedded( File.ReadAllText( Source() ).Replace( "return o;", "return oo;" ) );
			Write( folder, Outdated( current, broken ), "package_broken" );
			var shader = backend.Shaders.Load( "shaders/package_broken.shader" );
			Assert.IsNotNull( shader?.Outdated );
			var variant = backend.Shaders.Variant( shader, "Default", [] );
			Assert.IsTrue( variant is { FromModule: false }, $"drawn from the recompiled file (variant {(variant is null ? "none" : $"from module {variant.FromModule}")}, recompiled {recompiled}; {string.Join( "; ", errors.Concat( unloaded ) )})" );
			Assert.AreEqual( 1, errors.Count, "the module's errors, once" );
			Assert.AreEqual( 1, recompiled );
			Assert.AreSame( variant, backend.Shaders.Variant( shader, "Default", [] ), "neither compiled nor recompiled again" );
			Assert.AreEqual( 1, recompiled );

			// No source at all: recompiled as it loads
			Write( folder, Outdated( current, null ), "package_nosource" );
			var bare = backend.Shaders.Load( "shaders/package_nosource.shader" );
			Assert.IsNotNull( bare );
			Assert.IsNull( bare.Outdated, "the recompiled file" );
			Assert.AreEqual( 2, recompiled );
		}
		finally
		{
			Directory.Delete( folder, true );
		}
	}

	/// <summary>
	/// In the background (the game's), that recompile runs on a worker: the thread that draws the shader gets nothing and is
	/// told it's pending, rather than waiting (a package shader's took 9 s on the main thread); once it's done the recompiled
	/// file draws. Before, the first draw waited for the ShaderCompiler.
	/// </summary>
	[TestMethod]
	public void OutdatedShaderRecompilesOffTheDrawingThread()
	{
		var folder = Folder();
		using var release = new ManualResetEventSlim();
		try
		{
			using var backend = Backend( folder, out var current );
			backend.Passes.Error = _ => { };
			var recompiled = 0;
			backend.Shaders.UpgradeCache = Path.Combine( folder, "upgraded" );
			backend.Shaders.UpgradeInBackground = true;
			backend.Shaders.Upgrader = ( _, output ) =>
			{
				Assert.IsTrue( release.Wait( TimeSpan.FromSeconds( 30 ) ), "released" );
				Interlocked.Increment( ref recompiled );
				File.WriteAllBytes( output, current );
				return true;
			};

			var broken = Embedded( File.ReadAllText( Source() ).Replace( "return o;", "return oo;" ) );
			Write( folder, Outdated( current, broken ), "package_slow" );
			var shader = backend.Shaders.Load( "shaders/package_slow.shader" );
			Assert.IsNotNull( shader?.Outdated );

			var watch = System.Diagnostics.Stopwatch.StartNew();
			Assert.IsNull( backend.Shaders.Variant( shader, "Default", [], null, null, false, out var pending ), "nothing while it recompiles" );
			Assert.IsTrue( pending, "pending, so it isn't kept" );
			Assert.IsTrue( watch.Elapsed < TimeSpan.FromSeconds( 10 ), "didn't wait for the recompile" );

			release.Set();
			backend.Shaders.WaitForUpgrades();
			var variant = backend.Shaders.Variant( shader, "Default", [], null, null, false, out pending );
			Assert.IsFalse( pending );
			Assert.IsTrue( variant is { FromModule: false }, "drawn from the recompiled file" );
			Assert.AreEqual( 1, recompiled );
		}
		finally
		{
			release.Set();
			Directory.Delete( folder, true );
		}
	}

	/// <summary>
	/// An outdated shader that neither its embedded source nor the recompile can bring back draws the error shader, as
	/// native's material system draws a material whose shader won't load (Mouse Fishing's toon characters), not nothing.
	/// </summary>
	[TestMethod]
	public void OutdatedShaderThatWontLoadDrawsTheErrorShader()
	{
		var folder = Folder();
		try
		{
			using var backend = Backend( folder, out var current );
			backend.Passes.Error = _ => { };
			var unloaded = new List<string>();
			backend.Shaders.Unloaded = unloaded.Add;
			backend.Shaders.UpgradeCache = Path.Combine( folder, "upgraded" );
			backend.Shaders.Upgrader = ( _, _ ) => false;

			var broken = Embedded( File.ReadAllText( Source() ).Replace( "return o;", "return oo;" ) );
			Write( folder, Outdated( current, broken ), "package_unloadable" );
			var shader = backend.Shaders.Load( "shaders/package_unloadable.shader" );
			Assert.IsNotNull( shader?.Outdated );

			var variant = backend.Shaders.Variant( shader, "Default", [] );
			Assert.IsNotNull( variant, $"draws something ({string.Join( "; ", unloaded )})" );
			StringAssert.Contains( variant.Shader.Name, "error" );
			Assert.AreEqual( 1, unloaded.Count, "said once" );
		}
		finally
		{
			Directory.Delete( folder, true );
		}
	}

	/// <summary>
	/// A shader with a Slang module and no compiled file at all: it loads by its path from what the module declares (its
	/// MODES lines, combos) and draws from the module, the variant the same as unlit's own; with modules off it has
	/// nothing to draw.
	/// </summary>
	[TestMethod]
	public void ShaderWithOnlyAModuleLoads()
	{
		var folder = Folder();
		try
		{
			using var backend = Backend( folder, out var current );
			var errors = new List<string>();
			backend.Passes.Error = errors.Add;
			File.Copy( Path.Combine( Fixtures.Core, "shaders", "unlit.slang" ), Path.Combine( folder, "shaders", "moduleonly_vignette.slang" ) );

			var shader = backend.Shaders.Load( "shaders/moduleonly_vignette.shader" );
			Assert.IsNotNull( shader, "found by its module" );
			Assert.IsTrue( shader.ModuleOnly );
			CollectionAssert.AreEqual( new[] { "Default" }, shader.Modes.Select( m => m.Name ).ToArray() );
			Assert.AreSame( shader, backend.Shaders.Load( "shaders/moduleonly_vignette.shader" ) );

			var module = backend.Shaders.Variant( shader, "Default", [] );
			Assert.IsTrue( module is { FromModule: true } );
			Assert.AreEqual( 0, errors.Count, string.Join( "\n", errors ) );
			var differences = PassModuleTests.Compare( Classic( backend, current ), module ).ToList();
			Assert.AreEqual( 0, differences.Count, string.Join( "\n", differences ) );

			Assert.IsNull( backend.Shaders.Load( "shaders/moduleonly_missing.shader" ), "no module, no shader" );
			backend.SlangPasses = false;
			Assert.IsNull( backend.Shaders.Variant( shader, "Forward", [] ), "nothing to draw without its module" );
		}
		finally
		{
			Directory.Delete( folder, true );
		}
	}

	/// <summary>
	/// Every package shader in <c>PACKAGE_SHADERS</c> (a folder of outdated <c>.shader_c</c>) against its recompiled copy
	/// of the same file name in <c>PACKAGE_UPGRADED</c>: whether its embedded source compiles, and how its variants differ,
	/// each mode at its first combos and no features. The report goes to <c>PACKAGE_REPORT</c>. Inconclusive without them.
	/// </summary>
	[TestMethod]
	public void PackageShadersMatchTheirRecompiles()
	{
		var shaders = Environment.GetEnvironmentVariable( "PACKAGE_SHADERS" );
		var upgraded = Environment.GetEnvironmentVariable( "PACKAGE_UPGRADED" );
		if ( string.IsNullOrEmpty( shaders ) || string.IsNullOrEmpty( upgraded ) ) Assert.Inconclusive( "PACKAGE_SHADERS and PACKAGE_UPGRADED aren't set" );

		using var backend = new BackendDevice( Device );
		var errors = new List<string>();
		backend.Passes.Error = errors.Add;
		var lines = new List<string>();
		var (files, drawn, matched, variants) = (0, 0, 0, 0);
		foreach ( var path in Directory.EnumerateFiles( shaders, "*.shader_c", SearchOption.AllDirectories ).Order( StringComparer.Ordinal ) )
		{
			var name = Path.GetFileName( path );
			ShaderFile shader;
			try
			{
				shader = ShaderFile.LoadOutdated( path );
			}
			catch ( Exception e ) when ( e is InvalidDataException or IOException )
			{
				lines.Add( $"{name}\tunreadable\t{e.Message}" );
				continue;
			}

			files++;
			if ( shader.Outdated is null || !backend.Passes.Has( shader ) )
			{
				lines.Add( $"{name}\t{(shader.Outdated is null ? "current" : Refusal( shader ))}" );
				continue;
			}

			var copy = Path.Combine( upgraded, name );
			var classicFile = File.Exists( copy ) ? ShaderFile.Load( copy ) : null;
			var differences = new List<string>();
			var failed = false;
			var timer = System.Diagnostics.Stopwatch.StartNew();
			foreach ( var mode in shader.Modes.Where( m => string.IsNullOrEmpty( m.Fallback ) ) )
			{
				var combos = PassModuleTests.ComboSets( shader ).First();
				int? Combo( string c ) => combos.TryGetValue( c, out var v ) ? v : null;
				var features = new int[shader.Features?.StaticCombos.Length ?? 0];
				var errorCount = errors.Count;
				ShaderVariant module;
				try
				{
					module = backend.Passes.Compile( shader, mode, features, Combo, null ) is null ? null : backend.Shaders.Variant( shader, mode.Name, features, Combo );
				}
				catch ( Exception e )
				{
					differences.Add( $"{mode.Name}: {e.GetType().Name} {e.Message.Split( '\n' )[0]}" );
					failed = true;
					continue;
				}

				if ( module is not { FromModule: true } )
				{
					differences.Add( $"{mode.Name}: doesn't compile: {(errors.Count > errorCount ? errors[^1].Split( '\n' ).FirstOrDefault( l => l.Contains( "error", StringComparison.OrdinalIgnoreCase ) ) ?? errors[^1].Split( '\n' )[0] : "")}" );
					failed = true;
					continue;
				}

				if ( classicFile is null ) continue;
				ShaderVariant classic;
				try
				{
					classic = backend.Shaders.Variant( classicFile, mode.Name, features, Combo );
				}
				catch ( InvalidDataException )
				{
					continue;
				}

				variants++;
				differences.AddRange( PassModuleTests.Compare( classic, module ).Select( d => $"{mode.Name}: {d}" ) );
			}

			if ( !failed ) drawn++;
			if ( !failed && classicFile is not null && differences.Count == 0 ) matched++;
			var state = failed ? "fails" : classicFile is null ? "compiles (no recompile to compare)" : differences.Count == 0 ? "matches" : "differs";
			lines.Add( $"{name}\t{state}\t{timer.ElapsedMilliseconds} ms\t{string.Join( " | ", differences.Take( 8 ) )}" );
		}

		lines.Insert( 0, $"{files} outdated shaders read, {drawn} draw from their embedded source, {matched} match their recompile over {variants} variants" );
		if ( Environment.GetEnvironmentVariable( "PACKAGE_REPORT" ) is { Length: > 0 } report ) File.WriteAllLines( report, lines );
		TestContext.WriteLine( lines[0] );
	}

	/// <summary>
	/// A sampler whose filter a feature chooses (Material.CommonInputs.hlsl's <c>TextureFiltering</c>, by
	/// <c>F_TEXTURE_FILTERING</c>) follows each material's feature in a module made from embedded source, as in the
	/// compiled programs: each value's module variant matches the recompiled file's, anisotropic, bilinear, trilinear, point.
	/// The shader is the USP's glass_scope, a .shader the game still compiles.
	/// </summary>
	[TestMethod]
	public void EmbeddedSamplersFollowTheMaterialsFeatures()
	{
		var game = Path.GetDirectoryName( Fixtures.Core );
		var scope = Path.Combine( game, "addons", "content", "Assets", "weapons", "usp", "materials", "glass_scope.shader" );
		if ( !File.Exists( scope ) || !File.Exists( scope + "_c" ) ) { Assert.Inconclusive( "glass_scope isn't compiled" ); return; }
		var folder = Folder();
		try
		{
			using var backend = Backend( folder, out _ );
			var errors = new List<string>();
			backend.Passes.Error = errors.Add;
			backend.Shaders.UpgradeCache = Path.Combine( folder, "upgraded" );
			backend.Shaders.Upgrader = ( _, _ ) => throw new AssertFailedException( "recompiled through the ShaderCompiler" );
			var current = File.ReadAllBytes( scope + "_c" );
			Write( folder, Outdated( current, Embedded( File.ReadAllText( scope ) ) ), "package_scope" );
			var shader = backend.Shaders.Load( "shaders/package_scope.shader" );
			Assert.IsNotNull( shader?.Outdated );
			var index = shader.FeatureIndex( "F_TEXTURE_FILTERING" );
			Assert.IsTrue( index >= 0 );

			var variants = new List<ShaderVariant>();
			for ( int value = 0; value < 4; value++ )
			{
				var features = new int[shader.Features.StaticCombos.Length];
				features[index] = value;
				var module = backend.Shaders.Variant( shader, shader.Modes[0].Name, features );
				Assert.IsTrue( module is { FromModule: true }, $"F_TEXTURE_FILTERING {value}: drawn from its embedded source ({string.Join( "; ", errors )})" );
				backend.SlangPasses = false;
				ShaderVariant classic;
				try
				{
					classic = backend.Shaders.Variant( ShaderFile.Read( current, "current" ), shader.Modes[0].Name, features );
				}
				finally
				{
					backend.SlangPasses = true;
				}

				var differences = PassModuleTests.Compare( classic, module ).ToList();
				Assert.AreEqual( 0, differences.Count, $"F_TEXTURE_FILTERING {value}: {string.Join( "\n", differences )}" );
				variants.Add( module );
			}

			Assert.AreEqual( 4, variants.Distinct().Count(), "each value its own variant" );
			Assert.AreEqual( 0, errors.Count, string.Join( "\n", errors ) );
		}
		finally
		{
			Directory.Delete( folder, true );
		}
	}

	const string Rock = "shaders/surface_test_rock.shader";

	/// <summary>
	/// A package's material shader made into a <c>.shader</c> again from its embedded source (each program blanked in place)
	/// converts to the same surface as its own <c>.shader</c> does.
	/// </summary>
	[TestMethod]
	public void EmbeddedSourceConvertsAsItsShaderDoes()
	{
		var text = File.ReadAllText( Source( Rock ) );
		var compiled = ShaderFile.Read( ShaderFileTests.Files.Read( Rock ), Rock );
		var features = (compiled.Features?.StaticCombos ?? []).Select( c => (c.Name, c.Min, c.Max) );

		var direct = Sandbox.ShaderCompiler.ShaderConverter.Convert( text, "surface_test_rock" );
		var shader = Sandbox.ShaderCompiler.ShaderConverter.FromEmbedded( Embedded( text ), features );
		var embedded = Sandbox.ShaderCompiler.ShaderConverter.Convert( shader, "surface_test_rock" );
		Assert.IsNotNull( direct.Source, string.Join( "; ", direct.Notes ) );
		Assert.AreEqual( Normalized( direct.Source ), Normalized( embedded.Source ) );

		static string Normalized( string source ) => Regex.Replace( source, @"\s+", " " ).Trim();
	}

	/// <summary>
	/// With package surfaces on, a material of an outdated package shader that the converter takes loads as the surface made
	/// from its embedded source; off (the default), as its <c>.shader</c>.
	/// </summary>
	[TestMethod]
	public void PackageMaterialShaderLoadsAsASurface()
	{
		var folder = Folder();
		try
		{
			using var backend = Backend( folder, out _ );
			var errors = new List<string>();
			backend.Surfaces.Error = errors.Add;
			backend.Surfaces.CacheFolder = Path.Combine( folder, "surfaces" );
			var rock = ShaderFileTests.Files.Read( Rock );
			Write( folder, Outdated( rock, Embedded( File.ReadAllText( Source( Rock ) ) ) ), "package_rock" );

			static Borealis.Source2.Materials.MaterialFile Material( string name ) => new()
			{
				Name = name,
				Shader = "shaders/package_rock.shader",
				Ints = new Dictionary<string, int>(),
				Floats = new Dictionary<string, float>(),
				Vectors = new Dictionary<string, float[]>(),
				Textures = new Dictionary<string, string>(),
				IntAttributes = new Dictionary<string, int>(),
				ShaderVersion = 1,
			};

			Assert.IsFalse( backend.Surfaces.ConvertPackages, "off by default" );
			using ( var classic = backend.LoadMaterial( "materials/package_rock_a.vmat", Material( "a" ) ) ) Assert.IsInstanceOfType<BackendMaterial>( classic );

			backend.Surfaces.ConvertPackages = true;
			using var surface = backend.LoadMaterial( "materials/package_rock_b.vmat", Material( "b" ) );
			Assert.IsInstanceOfType<SurfaceMaterial>( surface, string.Join( "; ", errors ) );
			Assert.AreEqual( 1, Directory.GetFiles( Path.Combine( folder, "surfaces", "packages" ), "package_rock_*.slang" ).Length );
		}
		finally
		{
			Directory.Delete( folder, true );
		}
	}

	static string Refusal( ShaderFile shader ) => Sandbox.ShaderCompiler.PassConverter.RefuseEmbedded( shader.EmbeddedSource ) ?? "no module";

	static string Folder()
	{
		var folder = Path.Combine( Path.GetTempPath(), $"borealis-embedded-{Guid.NewGuid():N}" );
		Directory.CreateDirectory( Path.Combine( folder, "shaders" ) );
		return folder;
	}

	static string Source() => Source( Shader );

	/// <summary>A .shader's source, from the first content root that has it (the test project's own shaders first).</summary>
	static string Source( string shader ) => ShaderFileTests.Files.Roots.Select( r => Path.Combine( r, shader.Replace( '/', Path.DirectorySeparatorChar ) ) ).FirstOrDefault( File.Exists ) ?? throw new AssertInconclusiveException( $"{shader} isn't there" );

	BackendDevice Backend( string folder, out byte[] current )
	{
		current = ShaderFileTests.Files.Read( Shader );
		var backend = new BackendDevice( Device );
		backend.Content.Add( folder );
		backend.Passes.EmbeddedFolder = Path.Combine( folder, "embedded" );
		backend.Passes.CacheFolder = Path.Combine( folder, "passes" );
		return backend;
	}

	static ShaderVariant Classic( BackendDevice backend, byte[] current )
	{
		backend.SlangPasses = false;
		try
		{
			return backend.Shaders.Variant( ShaderFile.Read( current, "current" ), "Default", [] );
		}
		finally
		{
			backend.SlangPasses = true;
		}
	}

	static void Write( string folder, byte[] file, string name = "package_vignette" ) => File.WriteAllBytes( Path.Combine( folder, "shaders", $"{name}.shader_c" ), file );

	/// <summary>
	/// Each program's source as the compiler embeds it: the whole <c>.shader</c> with HEADER, MODES, FEATURES and the
	/// other programs' blocks blanked, and the block names and braces of COMMON and its own program, so lines stay put.
	/// </summary>
	static Dictionary<string, string> Embedded( string shader )
	{
		shader = shader.Replace( "\r\n", "\n" );
		var blocks = new List<(string Name, int Start, int Open, int Close)>();
		foreach ( Match m in Regex.Matches( shader, @"(?<=^|\n)[ \t]*(?<name>HEADER|MODES|FEATURES|COMMON|VS|PS|CS)\s*\{" ) )
		{
			var open = m.Index + m.Length - 1;
			var depth = 0;
			var close = open;
			for ( ; close < shader.Length; close++ )
			{
				if ( shader[close] == '{' ) depth++;
				if ( shader[close] == '}' && --depth == 0 ) break;
			}

			blocks.Add( (m.Groups["name"].Value, m.Index, open, close) );
		}

		static void Blank( StringBuilder text, int from, int to )
		{
			for ( int i = from; i <= to; i++ ) if ( text[i] != '\n' ) text[i] = ' ';
		}

		var programs = new Dictionary<string, string>();
		foreach ( var program in blocks.Where( b => b.Name is "VS" or "PS" or "CS" ).Select( b => b.Name ) )
		{
			var text = new StringBuilder( shader );
			foreach ( var (name, start, open, close) in blocks )
			{
				if ( name == "COMMON" || name == program )
				{
					Blank( text, start, open );
					Blank( text, close, close );
				}
				else
				{
					Blank( text, start, close );
				}
			}

			programs[$"VFX_PROGRAM_{program}"] = $"#line 1 \"{Shader}\"\n" + text;
		}

		return programs;
	}

	/// <summary>
	/// A compiled file as an older engine would have written it: its programs stamped with another ABI epoch, and the
	/// embedded source (null for none) in an <c>HLSL</c> block, as the compiler writes a package's.
	/// </summary>
	static byte[] Outdated( byte[] current, Dictionary<string, string> source )
	{
		var file = (byte[])current.Clone();
		var resource = Borealis.Source2.Resources.ResourceFile.Read( file );
		var (block, _) = resource.Has( "SPRV" ) && resource.Find( "SPRV" ).Size > 0 ? resource.Find( "SPRV" ) : resource.Find( "DATA" );
		for ( int type = 0; type < 9; type++ )
		{
			var offset = BinaryPrimitives.ReadInt32LittleEndian( file.AsSpan( block + type * 8 ) );
			if ( offset == 0 ) continue;
			BinaryPrimitives.WriteInt32LittleEndian( file.AsSpan( block + offset + 8 ), ShaderFile.AbiVersion - 2 );
		}

		// The blocks as they are, then the HLSL block, then a table of them all. The compiler embeds the file's own source
		// already: that block's entry goes, so the given source (or none) is what's read
		var json = source is null ? [] : JsonSerializer.SerializeToUtf8Bytes( new { Programs = source } );
		var table = 8 + BinaryPrimitives.ReadInt32LittleEndian( file.AsSpan( 8 ) );
		var count = BinaryPrimitives.ReadInt32LittleEndian( file.AsSpan( 12 ) );
		var entries = new List<(byte[] Name, int Offset, int Size)>();
		for ( int i = 0; i < count; i++ )
		{
			var entry = table + i * 12;
			if ( file.AsSpan( entry, 4 ).SequenceEqual( "HLSL"u8 ) ) continue;
			entries.Add( (file.AsSpan( entry, 4 ).ToArray(), entry + 4 + BinaryPrimitives.ReadInt32LittleEndian( file.AsSpan( entry + 4 ) ), BinaryPrimitives.ReadInt32LittleEndian( file.AsSpan( entry + 8 ) )) );
		}

		if ( source is not null ) entries.Add( (Encoding.ASCII.GetBytes( "HLSL" ), file.Length, json.Length) );
		var newTable = file.Length + json.Length;
		var result = new byte[newTable + entries.Count * 12];
		file.CopyTo( result, 0 );
		json.CopyTo( result, file.Length );
		for ( int i = 0; i < entries.Count; i++ )
		{
			var entry = newTable + i * 12;
			entries[i].Name.CopyTo( result, entry );
			BinaryPrimitives.WriteInt32LittleEndian( result.AsSpan( entry + 4 ), entries[i].Offset - (entry + 4) );
			BinaryPrimitives.WriteInt32LittleEndian( result.AsSpan( entry + 8 ), entries[i].Size );
		}

		BinaryPrimitives.WriteUInt32LittleEndian( result, (uint)result.Length );
		BinaryPrimitives.WriteInt32LittleEndian( result.AsSpan( 8 ), newTable - 8 );
		BinaryPrimitives.WriteInt32LittleEndian( result.AsSpan( 12 ), entries.Count );
		return result;
	}
}
