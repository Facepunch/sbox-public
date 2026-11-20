using Borealis.Backend;
using Borealis.Source2;
using Sandbox.ShaderCompiler;
using System.IO;

namespace BorealisGpuTests;

/// <summary>
/// Materials of shaders only a module describes (<see cref="MaterialInputs"/>, <see cref="MaterialCompile"/>): the input
/// textures and made textures read from the module for the material's features, the <c>.vmat</c>'s values typed as
/// KeyValues types them, and the data and texture definitions written as the native material compiler writes them.
/// Compiles nothing; no device.
/// </summary>
[TestClass]
public class MaterialCompileTests
{
	static string Shaders()
	{
		var core = ContentFiles.FindGame().Roots.FirstOrDefault( r => File.Exists( Path.Combine( r, "shaders", "sbox", "pass.slangh" ) ) );
		if ( core is null ) Assert.Inconclusive( "No core shaders" );
		return Path.Combine( core, "shaders" );
	}

	static PassModule Module( string name )
	{
		var shaders = Shaders();
		return PassModule.Read( Path.Combine( shaders, name + ".slang" ), [shaders] );
	}

	/// <summary>tools_sprite's colour texture is the one its alpha test feature picks, with that one's channels and format.</summary>
	[TestMethod]
	public void FeaturesPickTheMadeTexture()
	{
		var module = Module( "tools_sprite" );

		var none = MaterialInputs.Read( module, new Dictionary<string, int> { ["F_TEXTURED"] = 1 } );
		var a = none.Textures.Single( t => t.Name.StartsWith( "g_tColor", StringComparison.Ordinal ) );
		Assert.AreEqual( "g_tColorA", a.Name );
		Assert.AreEqual( "DXT1", a.Format );
		Assert.IsTrue( a.SrgbRead );
		Assert.AreEqual( "rgb", a.Channels.Single().Channels );
		Assert.AreEqual( "Box", a.Channels.Single().Algorithm );
		CollectionAssert.AreEqual( new[] { "TextureColor" }, a.Channels.Single().Inputs.ToArray() );

		var separate = MaterialInputs.Read( module, new Dictionary<string, int> { ["F_TEXTURED"] = 1, ["F_ALPHA_TEST"] = 1 } );
		var b = separate.Textures.Single( t => t.Name.StartsWith( "g_tColor", StringComparison.Ordinal ) );
		Assert.AreEqual( "g_tColorB", b.Name );
		Assert.AreEqual( 2, b.Channels.Count );
		Assert.AreEqual( "AlphaWeighted", b.Channels[0].Algorithm );
		CollectionAssert.AreEqual( new[] { "TextureColor", "TextureTranslucency" }, b.Channels[0].Inputs.ToArray() );
		Assert.AreEqual( "a", b.Channels[1].Channels );
		Assert.IsFalse( b.Channels[1].Srgb );

		var input = separate.Inputs["TextureColor"];
		Assert.IsTrue( input.Srgb );
		Assert.AreEqual( "color", input.FileEnding );
		Assert.AreEqual( "2D", input.Type );
		Assert.AreEqual( 1f, input.Default[0] );
	}

	/// <summary>A material's values typed as KeyValues types them, in both <c>.vmat</c> spellings, and its attribute blocks.</summary>
	[TestMethod]
	public void MaterialTextIsTypedAsKeyValues()
	{
		var quoted = VmatSource.Parse( "\"Layer0\"\n{\n\t\"shader\"\t\t\"shaders/tools_sprite.shader_c\"\n\t\"F_ALPHA_TEST\"\t\t\"2\"\n\t\"g_flAlphaTestReference\"\t\t\"0.300000\"\n\t\"g_vOverrideColor\"\t\t\"[1.000000 0.5 0.000000 1.000000]\"\n\t\"TextureColor\"\t\t\"materials\\tools\\x.tga\"\n\t\"Attributes\"\n\t{\n\t\t\"tools.toolsmaterial\"\t\t\"1\"\n\t}\n}\n" );
		Assert.AreEqual( "shaders/tools_sprite.shader", MaterialCompile.ShaderName( quoted.Shader ) );
		Assert.AreEqual( 2, quoted.Features["F_ALPHA_TEST"] );
		Assert.AreEqual( VmatSource.Kind.Float, VmatSource.TypeOf( quoted.Value( "g_flAlphaTestReference" ) ) );
		Assert.AreEqual( VmatSource.Kind.Vector, VmatSource.TypeOf( quoted.Value( "g_vOverrideColor" ) ) );
		Assert.AreEqual( "materials\\tools\\x.tga", quoted.Value( "TextureColor" ), "backslashes are kept" );
		Assert.AreEqual( ("tools.toolsmaterial", "1"), quoted.Attributes.Single() );

		var bare = VmatSource.Parse( "// THIS FILE IS AUTO-GENERATED\n\nLayer0\n{\n\tshader \"shaders/gizmo/gizmo_line.shader\"\n\n\tF_TRANSLUCENT 1\n\t//---- Line Thickness ----\n\tg_LineThickness \"1.000\"\n\tAttributes\n\t{\n\t\ttools.toolsmaterial \"1\"\n\t}\n}" );
		Assert.AreEqual( "shaders/gizmo/gizmo_line.shader", MaterialCompile.ShaderName( bare.Shader ) );
		Assert.AreEqual( 1, bare.Features["F_TRANSLUCENT"] );
		Assert.AreEqual( VmatSource.Kind.Float, VmatSource.TypeOf( bare.Value( "g_LineThickness" ) ) );
		Assert.AreEqual( "shaders/complex.shader", MaterialCompile.ShaderName( "complex.vfx" ) );
	}

	/// <summary>
	/// A tools_sprite material's data has its values and its made texture, which is defined from the material's input file
	/// (and the shader's default colour for an input it doesn't set) and named after that file.
	/// </summary>
	[TestMethod]
	public void MaterialDataAndTexturesAreWritten()
	{
		var material = VmatSource.Parse( "\"Layer0\"\n{\n\t\"shader\"\t\t\"shaders/tools_sprite.shader_c\"\n\t\"F_ALPHA_TEST\"\t\t\"1\"\n\t\"F_TEXTURED\"\t\t\"1\"\n\t\"g_flAlphaTestReference\"\t\t\"0.3\"\n\t\"g_vTint\"\t\t\"[1 0.5 0 1]\"\n\t\"TextureColor\"\t\t\"materials/tools/handle_circle_color.tga\"\n\t\"Attributes\"\n\t{\n\t\t\"tools.toolsmaterial\"\t\t\"1\"\n\t}\n}\n" );
		var inputs = MaterialInputs.Read( Module( "tools_sprite" ), material.Features );
		var compiled = MaterialCompile.Compile( material, "materials/tools/handle_circle.vmat", "shaders/tools_sprite.shader", inputs, _ => false );

		StringAssert.Contains( compiled.Data, "m_shaderName = \"shaders/tools_sprite.shader\"" );
		StringAssert.Contains( compiled.Data, "{ m_name = \"F_ALPHA_TEST\" m_nValue = 1 }" );
		StringAssert.Contains( compiled.Data, "{ m_name = \"g_flAlphaTestReference\" m_flValue = 0.3 }" );
		StringAssert.Contains( compiled.Data, "{ m_name = \"g_vTint\" m_value = [ 1.0, 0.5, 0.0, 1.0 ] }" );
		StringAssert.Contains( compiled.Data, "m_intAttributes = [ { m_name = \"tools.toolsmaterial\" m_nValue = 1 }" );

		var texture = compiled.Generated.Single();
		StringAssert.StartsWith( texture.Path, "materials/tools/handle_circle_color_tga_" );
		StringAssert.EndsWith( texture.Path, ".generated.vtex" );
		StringAssert.Contains( compiled.Data, $"{{ m_name = \"g_tColorB\" m_pValue = resource:\"{texture.Path}\" }}" );
		StringAssert.Contains( texture.Definition, "\"m_fileName\" \"string\" \"materials/tools/handle_circle_color.tga\"" );
		StringAssert.Contains( texture.Definition, "\"m_fileName\" \"string\" \"materials/default/default.tga\"", "the translucency input it doesn't set, with no default image for its ending, is the grey default image" );
		StringAssert.Contains( texture.Definition, "\"m_outputFormat\" \"string\" \"DXT5\"" );
		StringAssert.Contains( texture.Definition, "\"m_algorithm\" \"string\" \"AlphaWeighted\"" );
		CollectionAssert.AreEqual( new[] { texture.Path }, compiled.Textures.ToArray() );
	}

	/// <summary>A compiled texture the material names for the variable is used as it is, read before the material compiles.</summary>
	[TestMethod]
	public void NamedCompiledTexturesAreKept()
	{
		var material = VmatSource.Parse( "Layer0 { shader \"shaders/tools_sprite.shader\" F_TEXTURED 1 g_tColorA \"materials/x.vtex_c\" }" );
		var compiled = MaterialCompile.Compile( material, "materials/x.vmat", "shaders/tools_sprite.shader", MaterialInputs.Read( Module( "tools_sprite" ), material.Features ), _ => false );
		Assert.AreEqual( 0, compiled.Generated.Count );
		CollectionAssert.AreEqual( new[] { "materials/x.vtex" }, compiled.Prerequisites.ToArray() );
		StringAssert.Contains( compiled.Data, "m_pValue = resource:\"materials/x.vtex\"" );
	}

	/// <summary>The new-shader templates are standalone modules whose stages compile.</summary>
	[TestMethod]
	public void ShaderTemplatesCompile()
	{
		var shaders = Shaders();
		var templates = Path.Combine( Path.GetDirectoryName( Path.GetDirectoryName( shaders ) ), "templates" );
		var native = EngineFolders.NativeIn( Path.GetDirectoryName( Path.GetDirectoryName( shaders ) ) );
		if ( !File.Exists( Path.Combine( native, EngineFolders.Library( "slang" ) ) ) ) Assert.Inconclusive( "The engine's Slang isn't built" );
		var compiler = new SlangCompiler( native, [shaders] );

		foreach ( var name in new[] { "material", "unlit", "compute" } )
		{
			var module = PassModule.Read( Path.Combine( templates, name + ".slang" ), [shaders] );
			Assert.IsTrue( module.Standalone, name );
			var compiled = 0;
			foreach ( var stage in new[] { StageKind.Vertex, StageKind.Pixel, StageKind.Compute } )
			{
				if ( !module.Has( stage ) ) continue;
				compiler.CompilePass( module, stage, module.Combos( stage ).ToDictionary( c => c.Name, c => c.Min ) );
				compiled++;
			}

			Assert.IsTrue( compiled > 0, name );
		}
	}

	/// <summary>
	/// A Slang compile reads our headers in place of the old .fxc ones they replace, also where a shared header that .shader
	/// files include names the old one (ui_basic's vertex stage, through ui/vertex.hlsl), so it never reads a replaced header.
	/// </summary>
	[TestMethod]
	public void CompilesReadOurHeadersInPlaceOfTheOldOnes()
	{
		Assert.AreEqual( "\t#include \"sbox/math.slangh\" // x\n#include \"common/shared.hlsl\"\n", SlangHeaders.Replace( "\t#include \"math_general.fxc\" // x\n#include \"common/shared.hlsl\"\n" ) );

		var shaders = Shaders();
		var native = EngineFolders.NativeIn( Path.GetDirectoryName( Path.GetDirectoryName( shaders ) ) );
		if ( !File.Exists( Path.Combine( native, EngineFolders.Library( "slang" ) ) ) ) Assert.Inconclusive( "The engine's Slang isn't built" );
		var compiler = new SlangCompiler( native, [shaders] );
		var module = Module( "ui_basic" );
		var read = new List<string>();
		foreach ( var stage in new[] { StageKind.Vertex, StageKind.Pixel } )
			read.AddRange( compiler.CompilePass( module, stage, module.Combos( stage ).ToDictionary( c => c.Name, c => c.Min ) ).Dependencies );

		var names = read.Select( d => Path.GetRelativePath( shaders, d ).Replace( '\\', '/' ) ).ToHashSet( StringComparer.OrdinalIgnoreCase );
		Assert.IsTrue( names.Contains( "sbox/math.slangh" ), string.Join( ", ", names ) );
		Assert.IsTrue( names.Contains( "sbox/lighting.slangh" ), string.Join( ", ", names ) );
		foreach ( var old in SlangHeaders.Replaced.Keys ) Assert.IsFalse( names.Contains( old ), old );
	}
}
