using System;
using Sandbox.Generator;

namespace CompilingTests;

/// <summary>
/// Game code's typed class per Slang surface: <see cref="SurfaceClassWriter"/> from a surface's description, and the
/// description carried in the code archive (<see cref="Compiler.AdditionalFilesProvider"/>) so an archive compiles it too.
/// </summary>
[TestClass]
[DoNotParallelize]
public class SurfaceClassTest
{
	// As MaterialClassWriter.Describe writes one: a float3 with a default and group, a float with a range, a bool, a texture
	// and a field with a [Source] (set by that name)
	const string Rock = "sbox-surface\t1\ntype\tRock\nshader\tshaders/rock.shader\nfile\trock.slang\n"
		+ "field\tTint\tTint\tfloat\t3\t1,0.5,0.25\tColor\t\t\t\n"
		+ "field\tRoughnessScale\tRoughnessScale\tfloat\t1\t1\tRoughness\t0\t2\t\n"
		+ "field\tUseMask\tUseMask\tbool\t1\t1\t\t\t\t\n"
		+ "field\tColor\tColor\ttexture\t1\t\tColor\t\t\tsrgb\n"
		+ "field\tBlendMode\tF_BLEND_MODE\tint\t1\t0\t\t\t\t\n";

	[TestMethod]
	public void WritesTheClass()
	{
		var code = SurfaceClassWriter.Write( Rock, "MyGame" );
		Assert.IsNotNull( code );
		StringAssert.Contains( code, "namespace MyGame.Surfaces" );
		StringAssert.Contains( code, "public sealed partial class Rock" );
		StringAssert.Contains( code, "public const string ShaderPath = \"shaders/rock.shader\";" );
		StringAssert.Contains( code, "public global::Vector3 Tint" );
		StringAssert.Contains( code, "new global::Vector3( 1.0f, 0.5f, 0.25f )" );
		StringAssert.Contains( code, "public float RoughnessScale" );
		StringAssert.Contains( code, "public bool UseMask" );
		StringAssert.Contains( code, "public global::Sandbox.Texture Color" );
		StringAssert.Contains( code, "Material?.Set( \"F_BLEND_MODE\", value );" );
		StringAssert.Contains( code, "public int BlendMode" );
	}

	[TestMethod]
	public void RefusesWhatIsntADescription()
	{
		Assert.IsNull( SurfaceClassWriter.Write( null, "MyGame" ) );
		Assert.IsNull( SurfaceClassWriter.Write( Rock.Replace( "sbox-surface\t1", "sbox-surface\t2" ), "MyGame" ) );
		Assert.IsNull( SurfaceClassWriter.Write( Rock.Replace( "type\tRock", "type\tRock { } class Evil" ), "MyGame" ) );
		Assert.IsNull( SurfaceClassWriter.Write( Rock.Replace( "type\tRock", "type\tclass" ), "MyGame" ) );
		Assert.IsNull( SurfaceClassWriter.Write( Rock.Replace( "shaders/rock.shader", "a\"); Evil(); (\"" ), "MyGame" ) );
	}

	[TestMethod]
	public void LeavesOutFieldsThatArentSafe()
	{
		var evil = Rock
			+ "field\tBad Name\tBad\tfloat\t1\t0\t\t\t\t\n"
			+ "field\tQuote\tQ\"; Evil(); \"\tfloat\t1\t0\t\t\t\t\n"
			+ "field\tMatrix\tMatrix\tfloat\t16\t\t\t\t\t\n"
			+ "field\tTint\tTint\tfloat\t3\t\t\t\t\t\n";
		var code = SurfaceClassWriter.Write( evil, "MyGame" );
		Assert.IsNotNull( code );
		Assert.IsFalse( code.Contains( "Bad Name" ) );
		Assert.IsFalse( code.Contains( "Evil" ) );
		Assert.IsFalse( code.Contains( "Matrix" ) );
		Assert.AreEqual( 1, code.Split( "public global::Vector3 Tint" ).Length - 1 );
	}

	/// <summary>A project's descriptions go into its archive, the class compiles into its assembly, and again from the archive.</summary>
	[TestMethod]
	public async Task CompilesFromTheArchive()
	{
		var codePath = System.IO.Path.GetFullPath( "Data/code/base" );
		using var group = new CompileGroup( "Test" );
		var compiler = group.CreateCompiler( "test", codePath, new Compiler.Configuration() );
		compiler.AdditionalFilesProvider = () => [new CodeArchive.AdditionalFile( Rock, "surfaces/shaders/rock" + SurfaceClassWriter.Extension )];
		await group.BuildAsync();

		var output = compiler.Output;
		Assert.IsNull( output.Exception );
		Assert.IsTrue( compiler.BuildSuccess, string.Join( "\n", output.Diagnostics ) );
		Assert.IsTrue( output.Archive.AdditionalFiles.Any( f => f.LocalPath.EndsWith( SurfaceClassWriter.Extension ) ) );
		var root = output.Archive.Configuration.RootNamespace;
		Assert.IsNotNull( System.Reflection.Assembly.Load( output.AssemblyData ).GetType( $"{root}.Surfaces.Rock" ) );

		var archive = new CodeArchive();
		archive.Deserialize( output.Archive.Serialize() );
		using var again = new CompileGroup( "Test" );
		var fromArchive = again.GetOrCreateCompiler( "test" );
		fromArchive.UpdateFromArchive( archive );
		await again.BuildAsync();
		Assert.IsTrue( fromArchive.BuildSuccess, string.Join( "\n", fromArchive.Output.Diagnostics ) );
		Assert.IsNotNull( System.Reflection.Assembly.Load( fromArchive.Output.AssemblyData ).GetType( $"{root}.Surfaces.Rock" ) );
	}
}
