using Sandbox.ContentBuild;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace ContentBuild;

/// <summary>
/// Managed compilers compile in the same workers as native ones, as they do in the editor: a tool project's compiler
/// for a type of its own (reading another asset's compiled output, generating a native texture and variants of
/// itself), the engine's hooks on native types, and generators. What they build is rebuilt when what it was built from
/// changes, as any asset's is.
/// </summary>
[TestClass, DoNotParallelize, TestCategory( "Slow" )]
public class ManagedCompilerTests
{
	static ContentRoot Root( ContentFixture fx, string name = null ) => name is null
		? new( fx.Mod, fx.Root, Path.Combine( fx.Parent, "state" ) )
		: new( fx.Mod + name, Path.Combine( fx.Parent, name ), Path.Combine( fx.Parent, name + "state" ) );

	static BuildReport Build( ContentFixture fx, CompilerFixture compilers ) => ManagedBuilder.Build( new BuildRequest
	{
		Roots = [Root( fx )],
		OutputRoots = new Dictionary<string, string> { ["transient"] = Path.Combine( fx.Parent, "transient" ) },
		MaxJobs = 2,
	}, compilers?.Folder );

	static BuildReport Built( ContentFixture fx, CompilerFixture compilers )
	{
		var report = Build( fx, compilers );
		Assert.IsTrue( report.Success, report.Log.ToString() );
		return report;
	}

	static string Text( ContentFixture fx, string rel ) => Encoding.UTF8.GetString( fx.Read( rel ) );

	static void WriteGraph( ContentFixture fx )
	{
		Sources.WriteTga( fx, "textures/brick.tga", 4, 4 );
		fx.Write( "textures/brick.vtex", Sources.Texture( "textures/brick.tga" ) );
		Sources.WriteTga( fx, "textures/red.tga", 4, 4, 0xFF0000FF );
		fx.Write( "graphs/a.fxg", """{ "Texture": "textures/brick.vtex", "Image": "textures/red.tga", "Variants": [ "left", "right" ] }""" );
	}

	[TestMethod]
	public void ToolCompilerReadsACompiledOutputAndGeneratesNativeAndManagedChildren()
	{
		using var fx = new ContentFixture();
		using var compilers = new CompilerFixture();
		compilers.Compile();
		WriteGraph( fx );

		var report = Built( fx, compilers );

		foreach ( var output in new[] { "graphs/a.fxg_c", "graphs/a_fx.generated.vtex_c", "graphs/a_left.fxg_c", "graphs/a_right.fxg_c" } )
			Assert.IsTrue( fx.Exists( output ), $"{output} missing\n{report.Log}" );
		Assert.IsTrue( report.Jobs.IndexOf( "textures/brick.vtex" ) < report.Jobs.IndexOf( "graphs/a.fxg" ), "the texture it reads builds first" );
		StringAssert.Contains( Text( fx, "graphs/a.fxg_c" ), "\"Size\":\"4x4\"" );
		StringAssert.Contains( Text( fx, "graphs/a.fxg_c" ), "helper 1" );
		StringAssert.Contains( Text( fx, "graphs/a_left.fxg_c" ), "\"Variant\":\"left\"" );

		CollectionAssert.AreEqual( Array.Empty<string>(), Built( fx, compilers ).Jobs, "nothing changed" );

		// Only the texture it reads changes: it follows
		Sources.WriteTga( fx, "textures/brick.tga", 8, 8 );
		CollectionAssert.AreEquivalent( new[] { "textures/brick.vtex", "graphs/a.fxg" }, Built( fx, compilers ).Jobs );
		StringAssert.Contains( Text( fx, "graphs/a.fxg_c" ), "\"Size\":\"8x8\"" );
	}

	[TestMethod]
	public void ReferencesInAGameResourceAreFoundAsTheAssetSystemWouldFindThem()
	{
		using var fx = new ContentFixture();
		using var compilers = new CompilerFixture();
		compilers.Compile();
		Sources.WriteTga( fx, "textures/t.tga", 4, 4 );
		fx.Write( "textures/t.vtex", Sources.Texture( "textures/t.tga" ) );
		Sources.WriteWav( fx, "sounds/s.wav", 440 );
		var generated = Path.Combine( fx.Parent, "transient", "textures", "generated", "g.vtex" );
		Directory.CreateDirectory( Path.GetDirectoryName( generated ) );
		File.WriteAllText( generated, Sources.Texture( "textures/t.tga" ) );

		string[] references =
		[
			"Textures/T.vtex", // an asset, named in another case
			"sounds/s.wav", // a source of another type: the asset's path
			fx.Path( "textures/t.vtex" ), // by absolute path
			generated, // by absolute path, in the output root generated resources go to
			"models/missing.vmdl", // a known type that isn't there (yet): as named
			"models/gone.vmdl_c", // the same, compiled
			"textures/missing.png", // missing, and not its type's main extension (an image's is jpg): nothing
			"sounds/missing.wav", // the same for a sound (vsnd): nothing
			"textures/other.vtex_c", // a compiled name, which the asset system finds by absolute path only: as named
			"textures/t.tga", // an image, which compiles to nothing: a compile reference
			"notes/readme.fixturenotanasset", // no asset type has the extension: nothing
		];
		fx.Write( "graphs/r.fxg", System.Text.Json.JsonSerializer.Serialize( new { References = references } ) );
		Built( fx, compilers );

		var runtime = RuntimeReferences( fx.Read( "graphs/r.fxg_c" ) );
		// Resource names, as the resource system writes them (without _c)
		CollectionAssert.AreEquivalent( new[] { "textures/t.vtex", "sounds/s.vsnd", "textures/generated/g.vtex", "models/missing.vmdl", "models/gone.vmdl", "textures/other.vtex" }, runtime, string.Join( ", ", runtime ) );

		var root = Root( fx );
		var adopter = CompilerWorkerTests.Compiler( [root], compilers: compilers.Folder );
		try
		{
			var facts = CompilerWorkerTests.Run( adopter, root, [root], "graphs/r.fxg", JobMode.Adopt ).Facts;
			StringAssert.Contains( facts, "/textures/t.tga ", facts );
			Assert.IsFalse( facts.Contains( "fixturenotanasset" ), facts );
		}
		finally { adopter.Finished(); }
	}

	/// <summary>
	/// A build cancelled while a compile runs (its generated texture already made): its worker is ended, nothing of the
	/// compile is published, and the same compiler builds the next request in a new worker.
	/// </summary>
	[TestMethod]
	public void BuildCancelledMidCompilePublishesNothingAndTheNextBuildRuns()
	{
		using var fx = new ContentFixture();
		using var compilers = new CompilerFixture();
		compilers.Compile();
		var entered = Path.Combine( fx.Parent, "entered" );
		Sources.WriteTga( fx, "textures/red.tga", 4, 4, 0xFF0000FF );
		fx.Write( "graphs/wait.fxg", $$"""{ "Image": "textures/red.tga", "Entered": {{System.Text.Json.JsonSerializer.Serialize( entered )}} }""" );
		BuildRequest Request() => new()
		{
			Roots = [Root( fx )],
			OutputRoots = new Dictionary<string, string> { ["transient"] = Path.Combine( fx.Parent, "transient" ) },
			MaxJobs = 1,
		};

		var compiler = new ResourceCompiler( ManagedBuilder.Worker, ManagedBuilder.Types, Request().Mounts, compilers.Folder ) { MaxWorkers = 1 };
		using var cancel = new System.Threading.CancellationTokenSource();
		Task<BuildReport> build = null;
		try
		{
			build = Task.Run( () => ContentBuilder.Build( Request(), compiler, cancel.Token ) );
			var timer = System.Diagnostics.Stopwatch.StartNew();
			while ( !File.Exists( entered ) && !build.IsCompleted && timer.Elapsed < TimeSpan.FromMinutes( 2 ) )
				build.Wait( 100 );
			Assert.IsTrue( File.Exists( entered ), "the compile never started" );

			cancel.Cancel();
			Assert.IsTrue( build.Wait( TimeSpan.FromMinutes( 1 ) ), "the cancelled build didn't return" );
			Assert.IsTrue( build.Result.Cancelled, build.Result.Log.ToString() );
			Assert.AreEqual( 0, fx.Outputs().Count, "published part of a cancelled compile" );
			Assert.IsFalse( Directory.Exists( Path.Combine( fx.Parent, "transient" ) ) && Directory.EnumerateFiles( Path.Combine( fx.Parent, "transient" ), "*_c", SearchOption.AllDirectories ).Any(), "published the cancelled compile's generated texture" );
			Assert.AreEqual( 1, compiler.Ended.Count, "the cancelled compile's worker wasn't ended" );

			fx.Write( "graphs/wait.fxg", """{ "Image": "textures/red.tga" }""" );
			var next = ContentBuilder.Build( Request(), compiler );
			Assert.IsTrue( next.Success, next.Log.ToString() );
			Assert.IsTrue( fx.Exists( "graphs/wait.fxg_c" ) );
			Assert.AreEqual( 2, compiler.Launches );
		}
		finally
		{
			// A failed assertion above can leave the waiting compile running: end it before the workers are stopped
			cancel.Cancel();
			try { build?.Wait( TimeSpan.FromMinutes( 1 ) ); } catch ( AggregateException ) { }
			compiler.Finished();
		}
	}

	[TestMethod]
	public void GeneratedTexturesCompileWithoutARenderDevice()
	{
		// A game resource's image generators compile from the images they make on the CPU: an image file's pixels as they
		// are, and a mipped texture's levels each filtered from the one before, as a texture made from it gets them
		using var fx = new ContentFixture();
		using var compilers = new CompilerFixture();
		compilers.Compile();

		using var pattern = new Bitmap( 8, 8 );
		for ( var y = 0; y < 8; y++ )
			for ( var x = 0; x < 8; x++ )
				pattern.SetPixel( x, y, new Color32( (byte)(x * 36), (byte)(y * 36), 128, (byte)((x + y) * 18) ) );
		Directory.CreateDirectory( Path.GetDirectoryName( fx.Path( "textures/pattern.png" ) ) );
		File.WriteAllBytes( fx.Path( "textures/pattern.png" ), pattern.ToPng() );
		var text = Sandbox.Json.Serialize( new TextRendering.Scope( "A", Color.White, 14 ) );
		fx.Write( "graphs/g.fxg", $$"""
			{
				"Photo": { "$compiler": "texture", "$source": "imagefile", "data": { "FilePath": "textures/pattern.png" } },
				"Ramp": { "$compiler": "texture", "$source": "gradientlinear", "data": { "Size": "8,8", "Angle": 30 } },
				"Letter": { "$compiler": "texture", "$source": "text", "data": { "Size": "15,15", "TextScope": {{text}} } },
				"Disc": { "$compiler": "texture", "$source": "gradientradial", "data": { "Size": "8,8" } },
				"Noise": { "$compiler": "texture", "$source": "random", "data": { "Size": "8,8", "Seed": 3 } },
				"Icon": { "$compiler": "texture", "$source": "svgsource", "data": { "Size": "8,8" } },
				"Bumps": { "$compiler": "texture", "$source": "gradientlinear", "data": { "Size": "4,8", "ConvertHeightToNormals": true } }
			}
			""" );

		var report = Built( fx, compilers );
		var generated = Path.Combine( fx.Parent, "transient", "textures", "generated" );
		(int Width, int Height, int Mips, byte[] Data) Output( string generator, int width ) =>
			Directory.GetFiles( Path.Combine( generated, generator ), "*.vtex_c" ).Select( x => Texture( File.ReadAllBytes( x ) ) ).Single( x => x.Width == width );
		var image = Output( "imagefile", 8 );
		var gradient = Output( "gradientlinear", 8 );
		var letter = Output( "text", 15 );

		// Radial, random and svg: every level, the largest the image their generator makes (here too), not a blank one
		foreach ( var (name, generator) in new (string, Sandbox.Resources.TextureGenerator)[] { ("gradientradial", new Sandbox.Resources.RadialGradient { Size = 8 }), ("random", new Sandbox.Resources.RandomTextureGenerator { Size = 8, Seed = 3 }), ("svgsource", new Sandbox.Resources.SvgSourceGenerator { Size = 8 }) } )
		{
			var output = Output( name, 8 );
			Assert.AreEqual( (8, 8, 4, (1 + 4 + 16 + 64) * 4), (output.Width, output.Height, output.Mips, output.Data.Length), name );

			var top = output.Data[^(8 * 8 * 4)..];
			using var made = generator.CreateImage( new Sandbox.Resources.ResourceGenerator.Options { ForDisk = true }, default ).AsTask().Result;
			CollectionAssert.AreEqual( made.GetBuffer().ToArray(), top, name );
			Assert.IsTrue( top.Chunk( 4 ).Select( x => BitConverter.ToInt32( x ) ).Distinct().Count() > 1, $"{name} is one colour" );
		}

		// A linear gradient asked for a normal map compiles to one (master compiled the gradient itself). At angle 0
		// (Vector2.FromDegrees, 0 is up) it runs from white at the bottom to black at the top, so its height rises
		// downward everywhere: every normal leans along y alone, half red, less than half green, more than half blue
		var bumps = Output( "gradientlinear", 4 );
		Assert.AreEqual( (4, 8, 3), (bumps.Width, bumps.Height, bumps.Mips) );
		var normals = bumps.Data[^(4 * 8 * 4)..];
		for ( var i = 0; i < normals.Length; i += 4 )
			Assert.IsTrue( normals[i] == 127 && normals[i + 1] < 127 && normals[i + 2] > 127 && normals[i + 3] == 255, $"normal {i / 4} is {normals[i]},{normals[i + 1]},{normals[i + 2]},{normals[i + 3]}" );

		// The letter, drawn without a render device: 15x15 down to 1x1, ink in the largest level
		Assert.AreEqual( (15, 15, 4), (letter.Width, letter.Height, letter.Mips) );
		Assert.AreEqual( (15 * 15 + 7 * 7 + 3 * 3 + 1) * 4, letter.Data.Length );
		var largest = letter.Data.AsSpan( letter.Data.Length - 15 * 15 * 4 );
		var inked = 0;
		for ( var i = 3; i < largest.Length; i += 4 ) if ( largest[i] > 0 ) inked++;
		Assert.IsTrue( inked is > 4 and < 15 * 15, $"{inked} texels of the letter have ink" );

		// Lossless PNG, one level, the image's own pixels
		Assert.AreEqual( (8, 8, 1), (image.Width, image.Height, image.Mips), report.Log.ToString() );
		using var decoded = Bitmap.CreateFromBytes( image.Data );
		CollectionAssert.AreEqual( pattern.GetPixels32(), decoded.GetPixels32() );

		// Too small to compress: RGBA, smallest level first, every level down to 1x1, each the mean of the 2x2 above it as
		// the GPU blits an sRGB texture: colour in linear light, alpha as it is
		static double Linear( int c ) => c / 255.0 <= 0.04045 ? c / 255.0 / 12.92 : Math.Pow( (c / 255.0 + 0.055) / 1.055, 2.4 );
		static double Encoded( double l ) => 255 * (l <= 0.0031308 ? l * 12.92 : 1.055 * Math.Pow( l, 1 / 2.4 ) - 0.055);
		Assert.AreEqual( (8, 8, 4), (gradient.Width, gradient.Height, gradient.Mips) );
		Assert.AreEqual( (1 + 4 + 16 + 64) * 4, gradient.Data.Length );
		var offsets = new Dictionary<int, int> { [1] = 0, [2] = 4, [4] = 4 + 16, [8] = 4 + 16 + 64 };
		for ( var size = 8; size > 1; size /= 2 )
		{
			var (at, below) = (offsets[size], offsets[size / 2]);
			for ( var y = 0; y < size / 2; y++ )
				for ( var x = 0; x < size / 2; x++ )
					for ( var c = 0; c < 4; c++ )
					{
						int Texel( int px, int py ) => gradient.Data[at + (py * size + px) * 4 + c];
						double Mean( Func<int, double> f ) => (f( Texel( x * 2, y * 2 ) ) + f( Texel( x * 2 + 1, y * 2 ) ) + f( Texel( x * 2, y * 2 + 1 ) ) + f( Texel( x * 2 + 1, y * 2 + 1 ) )) / 4;
						var expected = c == 3 ? Mean( v => v ) : Encoded( Mean( Linear ) );
						Assert.AreEqual( (byte)Math.Round( expected ), gradient.Data[below + (y * (size / 2) + x) * 4 + c], $"{size / 2}x{size / 2} level, {x},{y} channel {c}" );
					}
		}
	}

	/// <summary>
	/// A compiled texture's size and level count (its DATA block's header) and its pixel data, which follows the resource.
	/// </summary>
	static (int Width, int Height, int Mips, byte[] Data) Texture( byte[] file )
	{
		int Offset( int field ) => field + BitConverter.ToInt32( file, field );
		var blocks = Offset( 8 );
		for ( var i = 0; i < BitConverter.ToInt32( file, 12 ); i++ )
		{
			var block = blocks + i * 12;
			if ( BitConverter.ToUInt32( file, block ) != 0x41544144 ) continue;

			var header = Offset( block + 4 );
			var size = BitConverter.ToInt32( file, 0 );
			return (BitConverter.ToUInt16( file, header + 20 ), BitConverter.ToUInt16( file, header + 22 ), file[header + 27], file[size..]);
		}
		throw new InvalidDataException( "no DATA block" );
	}

	/// <summary>
	/// The resource names of a compiled file's external reference list (RERL), the resources it loads with it.
	/// </summary>
	static List<string> RuntimeReferences( byte[] file )
	{
		int Offset( int field ) => field + BitConverter.ToInt32( file, field );
		var names = new List<string>();
		var blocks = Offset( 8 );
		for ( var i = 0; i < BitConverter.ToInt32( file, 12 ); i++ )
		{
			var block = blocks + i * 12;
			if ( BitConverter.ToUInt32( file, block ) != 0x4C524552 ) continue;

			var list = Offset( block + 4 );
			var entries = Offset( list );
			for ( var j = 0; j < BitConverter.ToInt32( file, list + 4 ); j++ )
			{
				var name = Offset( entries + j * 16 + 8 );
				names.Add( Encoding.UTF8.GetString( file, name, Array.IndexOf( file, (byte)0, name ) - name ) );
			}
		}
		return names;
	}

	[TestMethod]
	public void ReusedWorkerKeepsManagedErrorsWithTheFailedJob()
	{
		using var fx = new ContentFixture();
		using var compilers = new CompilerFixture();
		compilers.Compile();
		fx.Write( "graphs/bad.fxg", """{ "Fail": "worker diagnostic belongs to bad" }""" );
		fx.Write( "graphs/good.fxg", "{}" );
		var root = Root( fx );
		var compiler = CompilerWorkerTests.Compiler( [root], compilers: compilers.Folder );
		try
		{
			var bad = CompilerWorkerTests.Run( compiler, root, [root], "graphs/bad.fxg" );
			Assert.IsFalse( bad.Success );
			StringAssert.Contains( bad.Log, "worker diagnostic belongs to bad" );
			var good = CompilerWorkerTests.Run( compiler, root, [root], "graphs/good.fxg" );
			Assert.IsTrue( good.Success, good.Log );
			Assert.IsFalse( good.Log.Contains( "worker diagnostic belongs to bad" ), good.Log );
			Assert.AreEqual( 1, compiler.Launches );
		}
		finally { compiler.Finished(); }
	}

	[TestMethod]
	public void FailedManagedCompilesPublishNothing()
	{
		using var fx = new ContentFixture();
		using var compilers = new CompilerFixture();
		compilers.Compile();
		fx.Write( "graphs/throws.fxg", """{ "Fail": "the compiler threw" }""" );
		fx.Write( "graphs/variant.fxg", """{ "Variants": [ "x", "y" ], "FailVariant": "y" }""" );
		fx.Write( "graphs/embedded.fxg", """{ "Embedded": { "$compiler": "texture", "$source": "nosuchgenerator", "data": {} } }""" );
		fx.Write( "graphs/fine.fxg", """{ "Variants": [ "x" ] }""" );

		var report = Build( fx, compilers );

		CollectionAssert.AreEquivalent( new[] { "graphs/throws.fxg", "graphs/variant.fxg", "graphs/embedded.fxg" }, report.Failed, report.Log.ToString() );
		foreach ( var output in new[] { "graphs/throws.fxg_c", "graphs/variant.fxg_c", "graphs/variant_x.fxg_c", "graphs/variant_y.fxg_c", "graphs/embedded.fxg_c" } )
			Assert.IsFalse( fx.Exists( output ), output );
		Assert.IsTrue( fx.Exists( "graphs/fine.fxg_c" ) && fx.Exists( "graphs/fine_x.fxg_c" ) );

		// Failed ones are tried again, and fail again; the rest stays
		var again = Build( fx, compilers );
		CollectionAssert.AreEquivalent( new[] { "graphs/throws.fxg", "graphs/variant.fxg", "graphs/embedded.fxg" }, again.Jobs );
		CollectionAssert.AreEquivalent( new[] { "graphs/throws.fxg", "graphs/variant.fxg", "graphs/embedded.fxg" }, again.Failed );
	}

	/// <summary>
	/// A compile whose managed part fails writes nothing at all, not only nothing published: a compile that writes in
	/// place (the editor compiling what it loads) would otherwise replace the last good output with the failed one.
	/// </summary>
	[TestMethod]
	public void FailedManagedCompileWritesNothing()
	{
		using var fx = new ContentFixture();
		using var compilers = new CompilerFixture();
		compilers.Compile();
		fx.Write( "graphs/embedded.fxg", """{ "Embedded": { "$compiler": "texture", "$source": "nosuchgenerator", "data": {} } }""" );

		var outcome = CompilerWorkerTests.Fresh( Root( fx ), [Root( fx )], "graphs/embedded.fxg", compilers: compilers.Folder );

		Assert.IsFalse( outcome.Success, outcome.Log );
		CollectionAssert.AreEqual( Array.Empty<string>(), outcome.Files.Keys.ToArray(), outcome.Log );
	}

	[TestMethod]
	public void FileChangesBetweenJobsAreSeenAsByAFreshWorker()
	{
		// A graph in the later root names an image the way game resources name assets: whether and where it is decides
		// what the compile records reading
		using var fx = new ContentFixture();
		using var compilers = new CompilerFixture();
		compilers.Compile();
		var a = Root( fx );
		var b = Root( fx, "b" );
		var mounts = new[] { a, b };
		Directory.CreateDirectory( Path.Combine( b.Path, "graphs" ) );
		File.WriteAllText( Path.Combine( b.Path, "graphs/g.fxg" ), """{ "Picture": "textures/s.tga" }""" );
		Sources.WriteWav( fx, "sounds/voice.wav", 440 );

		// With its guid: one the editor hasn't written yet would be made up anew by every process
		fx.Write( "sounds/voice.wav.meta", """{ "visemes": [ { "viseme": 3, "start": 0.0, "end": 0.01 } ], "guid": "dad3db5a-9438-4c9a-abc6-ebddd3a40ff8" }""" );

		var reused = CompilerWorkerTests.Compiler( mounts, compilers: compilers.Folder );
		try
		{
			void Same( ContentRoot root, string rel, string what ) =>
				CompilerWorkerTests.AssertSame( CompilerWorkerTests.Fresh( root, mounts, rel, compilers: compilers.Folder ), CompilerWorkerTests.Run( reused, root, mounts, rel ), what );

			Same( b, "graphs/g.fxg", "no image anywhere" );

			Sources.WriteTga( fx, "textures/s.tga", 4, 4 );
			Same( b, "graphs/g.fxg", "the image added in the earlier root" );

			Directory.CreateDirectory( Path.Combine( b.Path, "textures" ) );
			File.Copy( fx.Path( "textures/s.tga" ), Path.Combine( b.Path, "textures/s.tga" ) );
			Same( b, "graphs/g.fxg", "the later root's image shadowing it" );

			File.Delete( Path.Combine( b.Path, "textures/s.tga" ) );
			Same( b, "graphs/g.fxg", "the later root's image gone" );

			Sources.WriteTga( fx, "textures/s.tga", 8, 8 );
			Same( b, "graphs/g.fxg", "the image edited" );

			fx.Delete( "textures/s.tga" );
			Same( b, "graphs/g.fxg", "the image deleted" );

			Same( a, "sounds/voice.wav", "a sound" );
			fx.Write( "sounds/voice.wav.meta", """{ "visemes": [ { "viseme": 5, "start": 0.0, "end": 0.02 } ], "guid": "dad3db5a-9438-4c9a-abc6-ebddd3a40ff8" }""" );
			Same( a, "sounds/voice.wav", "its .meta edited" );

			File.WriteAllText( Path.Combine( b.Path, "graphs/g.fxg" ), """{ "Picture": "textures/s.tga", "Edited": true }""" );
			Same( b, "graphs/g.fxg", "the graph edited" );

			Assert.AreEqual( 1, reused.Launches, string.Join( "; ", reused.Ended ) );
		}
		finally { reused.Finished(); }
	}

	[TestMethod]
	public void CompiledOutputReplacedBetweenJobsIsReadAgain()
	{
		using var fx = new ContentFixture();
		using var compilers = new CompilerFixture();
		compilers.Compile();
		var root = Root( fx );
		Sources.WriteTga( fx, "textures/brick.tga", 4, 4 );
		fx.Write( "textures/brick.vtex", Sources.Texture( "textures/brick.tga" ) );
		fx.Write( "graphs/a.fxg", """{ "Texture": "textures/brick.vtex" }""" );
		Built( fx, compilers );

		var reused = CompilerWorkerTests.Compiler( [root], compilers: compilers.Folder );
		try
		{
			CompilerWorkerTests.Run( reused, root, [root], "graphs/a.fxg" );

			Sources.WriteTga( fx, "textures/brick.tga", 8, 8 );
			Built( fx, compilers );

			var fresh = CompilerWorkerTests.Fresh( root, [root], "graphs/a.fxg", compilers: compilers.Folder );
			CompilerWorkerTests.AssertSame( fresh, CompilerWorkerTests.Run( reused, root, [root], "graphs/a.fxg" ), "after the texture it reads was rebuilt" );
			StringAssert.Contains( Encoding.UTF8.GetString( fresh.Files.Single( x => x.Key.EndsWith( "a.fxg_c" ) ).Value ), "\"Size\":\"8x8\"" );
		}
		finally { reused.Finished(); }
	}
}
