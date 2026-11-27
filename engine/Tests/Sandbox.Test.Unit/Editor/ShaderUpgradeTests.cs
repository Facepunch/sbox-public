using Editor;
using System.IO;

namespace EditorTests;

/// <summary>
/// "Upgrade to Slang" (<see cref="ShaderUpgrade"/>): telling a VFX shader from Slang by its content, and what's never
/// upgraded. A shader's surface and module are compiled by the shader compiler, which these tests don't start.
/// </summary>
[TestClass]
public class ShaderUpgradeTests
{
	const string Vfx = """
		HEADER
		{
			Description = "A VFX shader";
		}

		MODES
		{
			Forward();
		}

		COMMON
		{
			#include "common/shared.hlsl"
		}

		VS
		{
			PixelInput MainVs( VertexInput i ) { return FinalizeVertex( ProcessVertex( i ) ); }
		}

		PS
		{
			float4 MainPs( PixelInput i ) : SV_Target0 { return 1; }
		}
		""";

	const string Surface = """
		import sbox.surface;

		[Material]
		public struct Rock : ISurface
		{
			public Material Evaluate( SurfaceInput i ) { Material m = Material.Init( i ); return m; }
		}
		""";

	const string Module = """
		#include "sbox/pass.slangh"

		Mode( Forward );

		#if ( PROGRAM == VFX_PROGRAM_PS )
		[shader( "pixel" )] float4 MainPs( float4 p : SV_Position ) : SV_Target0 { return 1; }
		#endif
		""";

	[TestMethod]
	public void VfxIsLegacyByItsBlocks()
	{
		Assert.IsTrue( ShaderUpgrade.IsLegacySource( Vfx ) );
		Assert.IsTrue( ShaderUpgrade.IsLegacySource( "MODES {\n\tForward();\n}\n" ), "a block's brace on its own line or not" );
		Assert.IsFalse( ShaderUpgrade.IsLegacySource( Surface ) );
		Assert.IsFalse( ShaderUpgrade.IsLegacySource( Module ) );
		Assert.IsFalse( ShaderUpgrade.IsLegacySource( "// MODES { in a comment isn't a block\nimport sbox.surface;" ) );
		Assert.IsFalse( ShaderUpgrade.IsLegacySource( null ) );
	}

	[TestMethod]
	public async Task GeometryStageIsRefusedAndNothingIsWritten()
	{
		var folder = Directory.CreateTempSubdirectory( "shaderupgrade" ).FullName;
		try
		{
			var shader = Path.Combine( folder, "outline.shader" );
			File.WriteAllText( shader, Vfx.Replace( "PS\n{", "GS\n{\n\t[maxvertexcount( 3 )] void MainGs( triangle in PixelInput v[3], inout TriangleStream<PixelInput> s ) { }\n}\n\nPS\n{" ) );

			var result = await ShaderUpgrade.UpgradeAsync( shader );

			Assert.AreEqual( ShaderUpgrade.UpgradeKind.Refused, result.Kind );
			StringAssert.Contains( result.Message, "ExpandPrimitives" );
			Assert.IsFalse( File.Exists( Path.ChangeExtension( shader, ".slang" ) ) );
			Assert.IsTrue( File.Exists( shader ), "the .shader is never deleted" );
		}
		finally
		{
			Directory.Delete( folder, true );
		}
	}

	[TestMethod]
	public async Task SlangAndExistingFilesAreLeftAlone()
	{
		var folder = Directory.CreateTempSubdirectory( "shaderupgrade" ).FullName;
		try
		{
			var slangShader = Path.Combine( folder, "rock.shader" );
			File.WriteAllText( slangShader, Surface );
			Assert.AreEqual( ShaderUpgrade.UpgradeKind.Failed, (await ShaderUpgrade.UpgradeAsync( slangShader )).Kind );

			var vfxShader = Path.Combine( folder, "plain.shader" );
			File.WriteAllText( vfxShader, Vfx );
			File.WriteAllText( Path.Combine( folder, "plain.slang" ), Module );
			var result = await ShaderUpgrade.UpgradeAsync( vfxShader );
			Assert.AreEqual( ShaderUpgrade.UpgradeKind.Failed, result.Kind, "an existing .slang isn't replaced unless asked" );
			Assert.AreEqual( Module, File.ReadAllText( Path.Combine( folder, "plain.slang" ) ) );
			Assert.AreEqual( Vfx, File.ReadAllText( vfxShader ) );

			Assert.IsTrue( ShaderUpgrade.IsLegacy( vfxShader ) );
			Assert.IsFalse( ShaderUpgrade.IsLegacy( slangShader ) );
			Assert.IsFalse( ShaderUpgrade.IsLegacy( Path.Combine( folder, "plain.slang" ) ) );
		}
		finally
		{
			Directory.Delete( folder, true );
		}
	}
}
