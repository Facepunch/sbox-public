using Borealis.Backend;
using Sandbox.ShaderCompiler;
using System.IO;

namespace BorealisGpuTests;

/// <summary>
/// Every place that looks for Slang shaders in a folder finds a Slang source named <c>.shader</c> (told by content,
/// <see cref="ShaderFormat"/>) as it finds a <c>.slang</c>, and a Slang compile named <c>.shader_c</c> (<c>SLGC</c>) as it
/// finds a <c>.slang_c</c>, while a VFX <c>.shader</c> and a vfx <c>.shader_c</c> stay legacy: so renaming <c>.slang</c>
/// to <c>.shader</c> is only a rename.
/// </summary>
[TestClass]
public class ShaderScanTests
{
	const string Surface = "import sbox.surface;\n\n[Material]\nstruct Renamed : ISurface\n{\n\t[Color] float3 Tint = 1;\n}\n";
	const string Pass = "#include \"sbox/pass.slangh\"\nMode( Default );\n";
	const string Vfx = "HEADER\n{\n\tDescription = \"legacy\";\n}\nMODES\n{\n\tForward();\n}\nPS\n{\n\tfloat4 MainPs() : SV_Target0 { return 1; }\n}\n";

	string game, core, shaders, package;

	[TestInitialize]
	public void Lay()
	{
		game = Path.Combine( Path.GetTempPath(), $"borealis-scans-{Guid.NewGuid():N}", "game" );
		core = Path.Combine( game, "core" );
		shaders = Path.Combine( core, "shaders" );
		package = Path.Combine( Path.GetDirectoryName( game ), "package" );
		Directory.CreateDirectory( Path.Combine( shaders, "sbox" ) );
		Directory.CreateDirectory( package );

		// Core: a .slang, a Slang .shader, a VFX .shader and a library file
		File.WriteAllText( Path.Combine( shaders, "plain.slang" ), Surface );
		File.WriteAllText( Path.Combine( shaders, "renamed.shader" ), Surface );
		File.WriteAllText( Path.Combine( shaders, "legacy.shader" ), Vfx );
		File.WriteAllText( Path.Combine( shaders, "sbox", "library.slang" ), "module library;\n" );

		// A package: a Slang .shader with its compile, compiles alone under both names, a pass module alone, and legacy files
		File.WriteAllText( Path.Combine( package, "renamed.shader" ), Surface );
		Compile( Path.Combine( package, "renamed.shader_c" ), "renamed.shader", Surface );
		Compile( Path.Combine( package, "alone.shader_c" ), "alone.shader", Surface );
		Compile( Path.Combine( package, "older.slang_c" ), "older.slang", Surface );
		Compile( Path.Combine( package, "passes.shader_c" ), "passes.shader", Pass );
		File.WriteAllText( Path.Combine( package, "legacy.shader" ), Vfx );
		File.WriteAllBytes( Path.Combine( package, "legacy.shader_c" ), [0x56, 0x43, 0x53, 0x32, 0, 0, 0, 0, 1, 2, 3, 4] );
	}

	[TestCleanup]
	public void Clear() => Directory.Delete( Path.GetDirectoryName( game ), true );

	/// <summary>A Slang compile (<c>SLGC</c>) embedding <paramref name="text"/> as its module's source, with no stages.</summary>
	static void Compile( string path, string name, string text ) =>
		SlangOutput.Write( path, "test", text, true, [], [], null, [(name, text)] );

	static string[] Names( IEnumerable<string> files, string folder ) =>
		files.Select( f => Path.GetRelativePath( folder, f ).Replace( '\\', '/' ) ).Order( StringComparer.Ordinal ).ToArray();

	[TestMethod]
	public void FormatFindsSourcesAndCompilesByContent()
	{
		CollectionAssert.AreEqual( new[] { "plain.slang", "renamed.shader" }, Names( ShaderFormat.SlangSources( shaders ), shaders ) );
		CollectionAssert.AreEqual( new[] { "alone.shader_c", "older.slang_c", "passes.shader_c" }, Names( ShaderFormat.CompiledAlone( package ), package ) );
		Assert.IsTrue( ShaderFormat.IsSlangSourcePath( Path.Combine( package, "renamed.shader" ) ) );
		Assert.IsFalse( ShaderFormat.IsSlangSourcePath( Path.Combine( package, "legacy.shader" ) ) );
		Assert.IsTrue( ShaderFormat.IsSlangCompiledPath( Path.Combine( package, "alone.shader_c" ) ) );
		Assert.IsFalse( ShaderFormat.IsSlangCompiledPath( Path.Combine( package, "legacy.shader_c" ) ) );
		Assert.AreEqual( "a/x.shader", ShaderFormat.ShaderNameOf( "a/x.slang_c" ) );
		Assert.AreEqual( "a/x.shader", ShaderFormat.ShaderNameOf( "a/x.shader_c" ) );
		Assert.AreEqual( "a/x.shader", ShaderFormat.ShaderNameOf( "a/x.slang" ) );
		Assert.AreEqual( 0, ShaderFormat.SlangSources( Path.Combine( game, "missing" ) ).Count() );
	}

	[TestMethod]
	public void PassModulesWarmTheRenamedSource()
	{
		CollectionAssert.AreEqual( new[] { "plain.slang", "renamed.shader", "sbox/library.slang" }, Names( PassModules.WarmSources( core ), shaders ) );
	}

	[TestMethod]
	public void OfflineShadersCompileTheRenamedSource()
	{
		CollectionAssert.AreEqual( new[] { "plain.slang", "renamed.shader", "sbox/library.slang" }, Names( OfflineShaders.SlangSources( shaders ), shaders ) );
	}

	[TestMethod]
	public void CorpusTakesTheRenamedSourceAndCompileAlone()
	{
		var (slang, packages) = ShaderCorpus.Collect( game, [package], null );
		CollectionAssert.AreEqual(
			new[] { "core/plain.slang", "core/renamed.shader", "slang/package/alone.shader", "slang/package/older.slang", "slang/package/passes.shader", "slang/package/renamed.shader" },
			slang.Select( s => s.Id ).Order( StringComparer.Ordinal ).ToArray() );
		Assert.AreEqual( Path.Combine( package, "alone.shader_c" ), slang.Single( s => s.Id == "slang/package/alone.shader" ).Path, "compiled alone: from the source it embeds" );
		Assert.AreEqual( Path.Combine( package, "renamed.shader" ), slang.Single( s => s.Id == "slang/package/renamed.shader" ).Path, "with its source: the source" );
		CollectionAssert.AreEqual( new[] { "package/package/legacy.shader_c" }, packages.Select( p => p.Id ).ToArray(), "only the vfx .shader_c is a package shader" );
	}

	[TestMethod]
	public void SurfaceMountCompilesTheRenamedSourceAndCompileAlone()
	{
		// The pass module compiled alone isn't a surface; the legacy files are vfx's
		CollectionAssert.AreEqual( new[] { "alone.shader", "older.slang", "renamed.shader" }, Names( SurfaceShaders.MountFiles( package ), package ) );
		Assert.AreEqual( 0, SurfaceShaders.MountFiles( Path.Combine( game, "missing" ) ).Length );
	}

	[TestMethod]
	public void CompiledAloneNamesTheShader()
	{
		Assert.AreEqual( "shaders/alone.shader", CompiledAlone.ShaderName( Path.Combine( package, "shaders", "alone.shader_c" ) ) );
		Assert.AreEqual( "shaders/older.shader", CompiledAlone.ShaderName( Path.Combine( package, "shaders", "older.slang_c" ) ) );
	}
}
