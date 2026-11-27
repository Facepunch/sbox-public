using Sandbox.Engine.Shaders;

namespace EngineTests;

/// <summary>
/// Shader.CompileAsync tells Slang from vfx's .shader text by content, as the rest of the engine does (ShaderFormat)
/// </summary>
[TestClass]
public class GameShadersFormatTest
{
	[TestMethod]
	public void VfxSourceIsNotSlang()
	{
		Assert.IsFalse( GameShaders.IsSlang( "HEADER\n{\n\tDescription = \"x\";\n}\nMODES\n{\n\tDefault();\n}\nVS\n{\n}\nPS\n{\n}\n" ) );
	}

	[TestMethod]
	public void SlangSurfaceIsSlang()
	{
		Assert.IsTrue( GameShaders.IsSlang( "import sbox.surface;\n\n[Material]\nstruct Rock\n{\n\tfloat3 Tint;\n}\n" ) );
	}

	/// <summary>
	/// A Slang module that imports nothing of the engine's and declares no material or entry point is still Slang
	/// </summary>
	[TestMethod]
	public void PlainSlangModuleIsSlang()
	{
		Assert.IsTrue( GameShaders.IsSlang( "module noise;\n\npublic float Noise( float2 uv ) { return frac( sin( dot( uv, float2( 12.9898, 78.233 ) ) ) * 43758.5453 ); }\n" ) );
	}

	[TestMethod]
	public void BlankIsNotSlang()
	{
		Assert.IsFalse( GameShaders.IsSlang( null ) );
		Assert.IsFalse( GameShaders.IsSlang( "  \n" ) );
	}
}
