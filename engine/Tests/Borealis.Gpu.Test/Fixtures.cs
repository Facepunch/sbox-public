using Borealis.Source2;
using System.IO;

namespace BorealisGpuTests;

/// <summary>
/// Where tests keep what they compile. Slang surfaces take seconds each, so tests share the backend's disk cache
/// (<c>game/.source2/borealissurfaces</c>), and source files a test writes go to a fixed folder of its own under
/// <c>game/.source2</c> rather than a new temporary one, so their compiles are found again on the next run (the test may
/// still delete them: the next run writes the same text). An entry is used only while the compiler, the source and
/// everything it read are unchanged, so a test reads what a compile now makes.
/// </summary>
static class Fixtures
{
	static string Game => ContentFiles.FindGame().Roots.FirstOrDefault() is { } core ? Path.GetDirectoryName( core ) : null;

	/// <summary>The backend's surface cache (<c>SurfaceShaders.CacheFolder</c>'s default).</summary>
	public static string SurfaceCache => Path.Combine( Game, ".source2", "borealissurfaces" );

	/// <summary>
	/// The test project's own <c>.shader</c> files (<c>Content/shaders</c>: engine shaders whose materials now compile from
	/// their Slang modules, kept here as compiled vfx shaders to read), compiled beside themselves by build-shaders. Null
	/// outside a checkout.
	/// </summary>
	public static string CompiledShaderRoot => Game is { } game ? Path.Combine( Path.GetDirectoryName( game ), "engine", "Tests", "Borealis.Gpu.Test", "Content" ) : null;

	/// <summary>
	/// The game's content with <see cref="CompiledShaderRoot"/> searched first, so <c>shaders/line.shader</c> and the other
	/// fixtures resolve to their compiled files. Inconclusive when they aren't compiled.
	/// </summary>
	public static ContentFiles WithCompiledShaders()
	{
		var root = CompiledShaderRoot;
		if ( root is null || !File.Exists( Path.Combine( root, "shaders", "line.shader_c" ) ) ) Assert.Inconclusive( "The test shaders aren't compiled (SboxBuild build-shaders)" );
		var game = ContentFiles.FindGame();
		var files = new ContentFiles( [root, .. game.Roots] );
		foreach ( var fallback in game.Fallbacks ) files.AddFallback( fallback );
		return files;
	}

	/// <summary>
	/// <c>materials/default/rope01.vmat</c>'s colour, normal and roughness-metalness-occlusion textures, as its compiled
	/// material names them: a generated texture's name is a hash of how it was made, which the material compiler decides.
	/// Inconclusive when it isn't compiled.
	/// </summary>
	public static (string Color, string Normal, string Rma) Rope
	{
		get
		{
			var path = ContentFiles.FindGame().Find( "materials/default/rope01.vmat" ) ?? throw new AssertInconclusiveException( "materials/default/rope01.vmat isn't compiled" );
			var material = Borealis.Source2.Materials.MaterialFile.Load( path );
			string Texture( string name ) => material.Textures.TryGetValue( name, out var texture ) ? texture : throw new AssertInconclusiveException( $"rope01.vmat has no {name}" );
			return (Texture( "g_tColor" ), Texture( "g_tNormal" ), Texture( "g_tRma" ));
		}
	}

	/// <summary>The game's core folder (<c>game/core</c>).</summary>
	public static string Core => ContentFiles.FindGame().Roots.FirstOrDefault();

	/// <summary>A test's own folder for the files it writes, at the same path every run.</summary>
	public static string Folder( string name )
	{
		var folder = Path.Combine( Game, ".source2", "borealis-gpu-tests", name );
		Directory.CreateDirectory( folder );
		return folder;
	}
}
