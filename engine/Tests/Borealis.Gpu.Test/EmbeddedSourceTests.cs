using Borealis.Backend;
using Borealis.Source2.Shaders;
using System.Buffers.Binary;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BorealisGpuTests;

/// <summary>
/// Shaders an older engine compiled (a package's, docs/managed/pass-modules.md): their stages come from a pass module made
/// from the source embedded in the file, compiled here, not from a recompile through the ShaderCompiler; and the same
/// variants come out as the recompiled file's.
/// </summary>
[TestClass]
[DoNotParallelize]
public class EmbeddedSourceTests : GpuTest
{
	const string Shader = "shaders/postprocess/pp_vignette.shader";

	/// <summary>
	/// pp_vignette as a package would ship it from an older engine: the ABI epoch it was compiled for isn't this one, and
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
			var broken = Embedded( File.ReadAllText( Source() ).Replace( "return lerp(", "return lerpp(" ) );
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

	static string Refusal( ShaderFile shader ) => Sandbox.ShaderCompiler.PassConverter.RefuseEmbedded( shader.EmbeddedSource ) ?? "no module";

	static string Folder()
	{
		var folder = Path.Combine( Path.GetTempPath(), $"borealis-embedded-{Guid.NewGuid():N}" );
		Directory.CreateDirectory( Path.Combine( folder, "shaders" ) );
		return folder;
	}

	static string Source() => Path.Combine( ShaderFileTests.Files.Roots[0], Shader.Replace( '/', Path.DirectorySeparatorChar ) );

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

		if ( source is null ) return file;

		// The blocks as they are, then the HLSL block, then a table of them all
		var json = JsonSerializer.SerializeToUtf8Bytes( new { Programs = source } );
		var table = 8 + BinaryPrimitives.ReadInt32LittleEndian( file.AsSpan( 8 ) );
		var count = BinaryPrimitives.ReadInt32LittleEndian( file.AsSpan( 12 ) );
		var entries = new List<(byte[] Name, int Offset, int Size)>();
		for ( int i = 0; i < count; i++ )
		{
			var entry = table + i * 12;
			entries.Add( (file.AsSpan( entry, 4 ).ToArray(), entry + 4 + BinaryPrimitives.ReadInt32LittleEndian( file.AsSpan( entry + 4 ) ), BinaryPrimitives.ReadInt32LittleEndian( file.AsSpan( entry + 8 ) )) );
		}

		entries.Add( (Encoding.ASCII.GetBytes( "HLSL" ), file.Length, json.Length) );
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
