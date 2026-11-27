using Borealis.Source2;
using Sandbox.ShaderCompiler;
using System.IO;

namespace BorealisGpuTests;

/// <summary>
/// A shader's format told by content, never its name (<see cref="ShaderFormat"/>): a VFX source by its top-level blocks, a
/// Slang compile by <c>SLGC</c>. Every engine Slang file reads as Slang and every VFX <c>.shader</c> as VFX, whatever its
/// extension.
/// </summary>
[TestClass]
public class ShaderFormatTests
{
	[TestMethod]
	public void VfxSourceIsToldByItsTopLevelBlocks()
	{
		Assert.IsTrue( ShaderFormat.IsVfxSource( "HEADER\n{\n\tDescription = \"x\";\n}\nMODES\n{\n\tForward();\n}\nPS\n{\n\tfloat4 MainPs() : SV_Target0 { return 1; }\n}\n" ) );
		Assert.IsTrue( ShaderFormat.IsVfxSource( "COMMON { }\r\nCS\r\n{\r\n\tvoid MainCs() {}\r\n}" ), "a compute shader, CRLF" );
		Assert.IsFalse( ShaderFormat.IsVfxSource( "import sbox.surface;\nstruct PS\n{\n\tfloat4 c;\n};\n" ), "a struct named PS isn't a block" );
		Assert.IsFalse( ShaderFormat.IsVfxSource( "// MODES { Forward(); }\n/* VS { } */\n#include \"sbox/pass.slangh\"\nMode( Default );\n" ), "blocks in comments" );
		Assert.IsFalse( ShaderFormat.IsVfxSource( "void f()\n{\n\tif ( x )\n\tPS\n\t{\n\t}\n}\n" ), "not at the top level" );
		Assert.IsFalse( ShaderFormat.IsVfxSource( "" ) );
	}

	[TestMethod]
	public void EngineShadersReadAsTheirFormat()
	{
		var roots = ContentFiles.FindGame().Roots;
		var core = roots.FirstOrDefault( r => File.Exists( Path.Combine( r, "shaders", "sbox", "pass.slangh" ) ) );
		if ( core is null ) Assert.Inconclusive( "No core shaders" );
		var game = Path.GetDirectoryName( core );

		var slang = Directory.GetFiles( Path.Combine( core, "shaders" ), "*.slang", SearchOption.AllDirectories );
		Assert.IsTrue( slang.Length > 50 );
		var vfx = slang.Where( ShaderFormat.IsVfxSourceFile ).ToList();
		Assert.AreEqual( 0, vfx.Count, "Slang read as VFX: " + string.Join( ", ", vfx ) );

		var addons = Path.Combine( game, "addons" );
		var shaders = Directory.Exists( addons ) ? Directory.GetFiles( addons, "*.shader", SearchOption.AllDirectories ) : [];
		var notVfx = shaders.Where( s => !ShaderFormat.IsVfxSourceFile( s ) ).ToList();
		Assert.AreEqual( 0, notVfx.Count, "VFX read as Slang: " + string.Join( ", ", notVfx ) );

		var compiled = slang.Select( SlangOutput.PathOf ).Where( File.Exists ).ToList();
		if ( compiled.Count > 0 ) Assert.IsTrue( compiled.TrueForAll( ShaderFormat.IsSlangCompiledFile ), "an offline compile is SLGC" );
		var vcs = Directory.GetFiles( game, "*.shader_c", SearchOption.AllDirectories ).Take( 20 ).ToList();
		Assert.IsFalse( vcs.Exists( ShaderFormat.IsSlangCompiledFile ), "a vfx .shader_c isn't" );
	}

	[TestMethod]
	public void SlangSourceBesideIsTheSlangFileOrASlangShader()
	{
		var folder = Path.Combine( Path.GetTempPath(), $"borealis-format-{Guid.NewGuid():N}" );
		Directory.CreateDirectory( folder );
		try
		{
			File.WriteAllText( Path.Combine( folder, "legacy.shader" ), "MODES\n{\n\tDefault();\n}\nPS\n{\n}\n" );
			File.WriteAllText( Path.Combine( folder, "renamed.shader" ), "#include \"sbox/pass.slangh\"\nMode( Default );\n" );
			File.WriteAllText( Path.Combine( folder, "both.shader" ), "MODES\n{\n}\nVS\n{\n}\n" );
			File.WriteAllText( Path.Combine( folder, "both.slang" ), "#include \"sbox/pass.slangh\"\n" );

			Assert.IsNull( ShaderFormat.SlangSourceBeside( Path.Combine( folder, "legacy.shader_c" ) ) );
			Assert.AreEqual( Path.Combine( folder, "renamed.shader" ), ShaderFormat.SlangSourceBeside( Path.Combine( folder, "renamed.shader_c" ) ) );
			Assert.AreEqual( Path.Combine( folder, "both.slang" ), ShaderFormat.SlangSourceBeside( Path.Combine( folder, "both.shader" ) ) );
			Assert.IsNull( ShaderFormat.SlangSourceBeside( Path.Combine( folder, "missing.shader_c" ) ) );
		}
		finally
		{
			Directory.Delete( folder, true );
		}
	}
}
