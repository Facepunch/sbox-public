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

	/// <summary>
	/// A material records its shader's version (<c>m_shaderVersion</c>), as the <c>.shader</c>'s HEADER gave it: blendable
	/// declares 1, which its surface reads to pick the layout roughness is packed in; a module that declares none records 0.
	/// </summary>
	[TestMethod]
	public void MaterialsRecordTheirShadersVersion()
	{
		var blend = VmatSource.Parse( "Layer0 { shader \"shaders/blendable.shader\" F_MULTIBLEND 1 }" );
		var blendable = MaterialCompile.Compile( blend, "materials/x.vmat", "shaders/blendable.shader", MaterialInputs.Read( Module( "blendable" ), blend.Features ), _ => false );
		StringAssert.Contains( blendable.Data, "m_shaderVersion = 1\n" );

		var sprite = VmatSource.Parse( "Layer0 { shader \"shaders/tools_sprite.shader\" }" );
		var tools = MaterialCompile.Compile( sprite, "materials/y.vmat", "shaders/tools_sprite.shader", MaterialInputs.Read( Module( "tools_sprite" ), sprite.Features ), _ => false );
		StringAssert.Contains( tools.Data, "m_shaderVersion = 0\n" );
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

	/// <summary>
	/// A surface's inputs are its MATERIAL_INPUTS block's (water_simple's, the common inputs): the made textures its fields
	/// take, the attributes evaluated with the material's features, and the representative texture (a texture attribute, the
	/// material's first) and its size (a colour's is one texel; an image's from its file).
	/// </summary>
	[TestMethod]
	public void SurfaceInputsComeFromItsMaterialInputsBlock()
	{
		var material = VmatSource.Parse( "Layer0 { shader \"shaders/water_simple.shader\" F_RENDER_BACKFACES 1 TextureColor \"[1.000000 1.000000 1.000000 0.000000]\" TextureNormal \"textures/water/water_normal.jpg\" }" );
		var inputs = MaterialInputs.Read( Module( "water_simple" ), material.Features );
		CollectionAssert.AreEquivalent( new[] { "g_tColor", "g_tNormal", "g_tRma" }, inputs.Textures.Select( t => t.Name ).ToArray() );
		Assert.AreEqual( "g_tColor", inputs.RepresentativeTexture );
		Assert.AreEqual( 1f, inputs.Attributes.Single( a => a.Name == "renderbackfaces" ).Value[0], "F_RENDER_BACKFACES ? true : false" );
		Assert.AreEqual( 0f, inputs.Attributes.Single( a => a.Name == "DoNotCastShadows" ).Value[0] );

		var compiled = MaterialCompile.Compile( material, "materials/water/x.vmat", "shaders/water_simple.shader", inputs, _ => false, _ => (512, 256) );
		StringAssert.Contains( compiled.Data, "{ m_name = \"RepresentativeTextureWidth\" m_nValue = 1 }" );
		var color = compiled.Generated.Single( t => t.Path.Contains( "color", StringComparison.OrdinalIgnoreCase ) ).Path;
		StringAssert.Contains( compiled.Data, $"m_textureAttributes = [ {{ m_name = \"RepresentativeTexture\" m_pValue = resource:\"{color}\" }} ]" );
		StringAssert.Contains( compiled.Data, "{ m_name = \"renderbackfaces\" m_nValue = 1 }" );
		Assert.AreEqual( 3, compiled.Generated.Count );
	}

	/// <summary>Image sizes from the start of PNG, TGA, PSD and JPEG files.</summary>
	[TestMethod]
	public void ImageSizesRead()
	{
		byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, (byte)'I', (byte)'H', (byte)'D', (byte)'R', 0, 0, 1, 44, 0, 0, 0, 200];
		Assert.AreEqual( (300, 200), MaterialCompile.ImageSize( png ) );

		var tga = new byte[18];
		tga[2] = 2;
		(tga[12], tga[13], tga[14], tga[15]) = (0, 2, 128, 0);
		Assert.AreEqual( (512, 128), MaterialCompile.ImageSize( tga ) );

		var psd = new byte[26];
		"8BPS"u8.CopyTo( psd );
		(psd[16], psd[17], psd[20], psd[21]) = (1, 0, 2, 0);
		Assert.AreEqual( (512, 256), MaterialCompile.ImageSize( psd ), "height first" );

		byte[] jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0, 4, 0, 0, 0xFF, 0xC0, 0, 17, 8, 0, 64, 0, 96, 3, 0, 0, 0];
		Assert.AreEqual( (96, 64), MaterialCompile.ImageSize( jpeg ) );
		Assert.IsNull( MaterialCompile.ImageSize( [1, 2, 3] ) );
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

	// complex.shader's vertex inputs as the native material compiler wrote them into a material's edit data
	// (VSInputSignature of materials/dev/primary_white.vmat_c, compiled by an engine that still had complex.shader)
	const string ComplexSignature = "<!-- kv3 encoding:text:version{e21c7f3c-8a33-41c5-9977-a76d3a32aa0d} format:generic:version{7412167c-06e9-4698-aff2-e63eb59037e7} -->\n{\n\tm_elems = \n\t[\n"
		+ "\t\t{\n\t\t\tm_pName = \"nBoneIndex\"\n\t\t\tm_pSemantic = \"BlendIndices\"\n\t\t\tm_pD3DSemanticName = \"BLENDINDICES\"\n\t\t\tm_nD3DSemanticIndex = 0\n\t\t},\n"
		+ "\t\t{\n\t\t\tm_pName = \"vBlendWeight\"\n\t\t\tm_pSemantic = \"BlendWeight\"\n\t\t\tm_pD3DSemanticName = \"BLENDWEIGHT\"\n\t\t\tm_nD3DSemanticIndex = 0\n\t\t},\n"
		+ "\t\t{\n\t\t\tm_pName = \"vNormalOs\"\n\t\t\tm_pSemantic = \"OptionallyCompressedTangentFrame\"\n\t\t\tm_pD3DSemanticName = \"NORMAL\"\n\t\t\tm_nD3DSemanticIndex = 0\n\t\t},\n"
		+ "\t\t{\n\t\t\tm_pName = \"vPositionOs\"\n\t\t\tm_pSemantic = \"PosXyz\"\n\t\t\tm_pD3DSemanticName = \"POSITION\"\n\t\t\tm_nD3DSemanticIndex = 0\n\t\t},\n"
		+ "\t\t{\n\t\t\tm_pName = \"vTangentUOs_flTangentVSign\"\n\t\t\tm_pSemantic = \"TangentU_SignV\"\n\t\t\tm_pD3DSemanticName = \"TANGENT\"\n\t\t\tm_nD3DSemanticIndex = 0\n\t\t},\n"
		+ "\t\t{\n\t\t\tm_pName = \"vTexCoord\"\n\t\t\tm_pSemantic = \"LowPrecisionUv\"\n\t\t\tm_pD3DSemanticName = \"TEXCOORD\"\n\t\t\tm_nD3DSemanticIndex = 0\n\t\t},\n"
		+ "\t\t{\n\t\t\tm_pName = \"vLightmapUV\"\n\t\t\tm_pSemantic = \"LightmapUV\"\n\t\t\tm_pD3DSemanticName = \"TEXCOORD\"\n\t\t\tm_nD3DSemanticIndex = 3\n\t\t},\n"
		+ "\t\t{\n\t\t\tm_pName = \"nInstanceTransformID\"\n\t\t\tm_pSemantic = \"InstanceTransformUv\"\n\t\t\tm_pD3DSemanticName = \"TEXCOORD\"\n\t\t\tm_nD3DSemanticIndex = 13\n\t\t},\n"
		+ "\t\t{\n\t\t\tm_pName = \"nVertexIndex\"\n\t\t\tm_pSemantic = \"MorphIndex\"\n\t\t\tm_pD3DSemanticName = \"TEXCOORD\"\n\t\t\tm_nD3DSemanticIndex = 14\n\t\t},\n"
		+ "\t\t{\n\t\t\tm_pName = \"nVertexCacheIndex\"\n\t\t\tm_pSemantic = \"MorphIndex\"\n\t\t\tm_pD3DSemanticName = \"TEXCOORD\"\n\t\t\tm_nD3DSemanticIndex = 15\n\t\t},\n"
		+ "\t]\n}";

	static string Inputs( MaterialInputs inputs ) => string.Join( " ", inputs.VertexInputs.Select( i => $"{i.Name}:{i.Semantic}:{i.D3DSemanticName}{i.D3DSemanticIndex}" ) );

	/// <summary>
	/// A standard material (complex, simple and generic are its aliases) gives the vertex inputs complex.shader did, written as
	/// the native compiler wrote them, byte for byte: the packed tangent frame, the tangent, the skinning streams (bone index
	/// and weights, the animated vertex cache's indices), the lightmap uv and the instance. The model compiler lays meshes
	/// out by these; without them models of these materials lost everything but position. The secondary uv set and generic's
	/// vertex paint come with the features that added them to the .shader's.
	/// </summary>
	[TestMethod]
	public void StandardMaterialsGiveComplexsVertexInputs()
	{
		var module = Module( "standard" );
		var plain = VmatSource.Parse( "Layer0 { shader \"shaders/complex.shader\" TextureColor \"materials/default/default_color.tga\" }" );
		var compiled = MaterialCompile.Compile( plain, "materials/x.vmat", "shaders/standard.shader", MaterialInputs.Read( module, plain.Features ), _ => false );
		Assert.AreEqual( ComplexSignature, compiled.VertexSignature );

		var uv2 = MaterialInputs.Read( module, new Dictionary<string, int> { ["F_SECONDARY_UV"] = 1 } );
		StringAssert.Contains( Inputs( uv2 ), "vTexCoord:LowPrecisionUv:TEXCOORD0 vTexCoord2:LowPrecisionUv1:TEXCOORD1 vLightmapUV" );
		var paint = MaterialInputs.Read( module, new Dictionary<string, int> { ["F_PAINT_VERTEX_COLORS"] = 1 } );
		StringAssert.Contains( Inputs( paint ), "vLightmapUV:LightmapUV:TEXCOORD3 vColorPaintValues:VertexPaintTintColor:TEXCOORD5 nInstanceTransformID" );
	}

	/// <summary>
	/// Every surface standing in for a deleted .shader gives that .shader's vertex inputs (as an engine with it compiled them
	/// into the materials named), from the VS_INPUT in its MATERIAL_INPUTS block; a standalone module, its vertex stage's.
	/// </summary>
	[TestMethod]
	[DataRow( "skin", "", "nBoneIndex:BlendIndices:BLENDINDICES0 vBlendWeight:BlendWeight:BLENDWEIGHT0 vNormalOs:OptionallyCompressedTangentFrame:NORMAL0 vPositionOs:PosXyz:POSITION0 vTangentUOs_flTangentVSign:TangentU_SignV:TANGENT0 vTexCoord:LowPrecisionUv:TEXCOORD0 nInstanceTransformID:InstanceTransformUv:TEXCOORD13 nVertexIndex:MorphIndex:TEXCOORD14 nVertexCacheIndex:MorphIndex:TEXCOORD15" )]
	[DataRow( "skin", "F_USE_PER_VERTEX_CURVATURE", "nBoneIndex:BlendIndices:BLENDINDICES0 vBlendWeight:BlendWeight:BLENDWEIGHT0 vNormalOs:OptionallyCompressedTangentFrame:NORMAL0 vPositionOs:PosXyz:POSITION0 vTangentUOs_flTangentVSign:TangentU_SignV:TANGENT0 vTexCoord:LowPrecisionUv:TEXCOORD0 flSSSCurvature:Curvature:TEXCOORD2 nInstanceTransformID:InstanceTransformUv:TEXCOORD13 nVertexIndex:MorphIndex:TEXCOORD14 nVertexCacheIndex:MorphIndex:TEXCOORD15" )]
	[DataRow( "blendable", "", "nBoneIndex:BlendIndices:BLENDINDICES0 vBlendWeight:BlendWeight:BLENDWEIGHT0 vNormalOs:OptionallyCompressedTangentFrame:NORMAL0 vPositionOs:PosXyz:POSITION0 vTangentUOs_flTangentVSign:TangentU_SignV:TANGENT0 vTexCoord:LowPrecisionUv:TEXCOORD0 vTexCoord2:LowPrecisionUv1:TEXCOORD1 vLightmapUV:LightmapUV:TEXCOORD3 vColorBlendValues:VertexPaintBlendParams:TEXCOORD4 vColorPaintValues:VertexPaintTintColor:TEXCOORD5 nInstanceTransformID:InstanceTransformUv:TEXCOORD13 nVertexIndex:MorphIndex:TEXCOORD14 nVertexCacheIndex:MorphIndex:TEXCOORD15" )]
	[DataRow( "fur", "", "nBoneIndex:BlendIndices:BLENDINDICES0 vBlendWeight:BlendWeight:BLENDWEIGHT0 vColor:Color:COLOR0 vNormalOs:OptionallyCompressedTangentFrame:NORMAL0 vPositionOs:PosXyz:POSITION0 vTangentUOs_flTangentVSign:TangentU_SignV:TANGENT0 vTexCoord:LowPrecisionUv:TEXCOORD0 vTexCoord2:LowPrecisionUv1:TEXCOORD1 vLightmapUV:LightmapUV:TEXCOORD3 nInstanceTransformID:InstanceTransformUv:TEXCOORD13 nVertexIndex:MorphIndex:TEXCOORD14 nVertexCacheIndex:MorphIndex:TEXCOORD15" )]
	[DataRow( "vertex_color", "", "nBoneIndex:BlendIndices:BLENDINDICES0 vBlendWeight:BlendWeight:BLENDWEIGHT0 vColor:Color:COLOR0 vNormalOs:OptionallyCompressedTangentFrame:NORMAL0 vPositionOs:PosXyz:POSITION0 vTangentUOs_flTangentVSign:TangentU_SignV:TANGENT0 vTexCoord:LowPrecisionUv:TEXCOORD0 vLightmapUV:LightmapUV:TEXCOORD3 nInstanceTransformID:InstanceTransformUv:TEXCOORD13 nVertexIndex:MorphIndex:TEXCOORD14 nVertexCacheIndex:MorphIndex:TEXCOORD15" )]
	[DataRow( "glass", "", "nBoneIndex:BlendIndices:BLENDINDICES0 vBlendWeight:BlendWeight:BLENDWEIGHT0 vNormalOs:OptionallyCompressedTangentFrame:NORMAL0 vPositionOs:PosXyz:POSITION0 vTangentUOs_flTangentVSign:TangentU_SignV:TANGENT0 vTexCoord:LowPrecisionUv:TEXCOORD0 vTexCoord2:LowPrecisionUv1:TEXCOORD1 vLightmapUV:LightmapUV:TEXCOORD3 nInstanceTransformID:InstanceTransformUv:TEXCOORD13 nVertexIndex:MorphIndex:TEXCOORD14 nVertexCacheIndex:MorphIndex:TEXCOORD15" )]
	[DataRow( "water_simple", "", "nBoneIndex:BlendIndices:BLENDINDICES0 vBlendWeight:BlendWeight:BLENDWEIGHT0 vNormalOs:OptionallyCompressedTangentFrame:NORMAL0 vPositionOs:PosXyz:POSITION0 vTangentUOs_flTangentVSign:TangentU_SignV:TANGENT0 vTexCoord:LowPrecisionUv:TEXCOORD0 vTexCoord2:LowPrecisionUv1:TEXCOORD1 vLightmapUV:LightmapUV:TEXCOORD3 nInstanceTransformID:InstanceTransformUv:TEXCOORD13 nVertexIndex:MorphIndex:TEXCOORD14 nVertexCacheIndex:MorphIndex:TEXCOORD15" )]
	[DataRow( "tools_solid", "", "vColor:Color:COLOR0 vNormalOs:OptionallyCompressedTangentFrame:NORMAL0 vPositionOs:PosXyz:POSITION0 nInstanceTransformID:InstanceTransformUv:TEXCOORD13" )]
	[DataRow( "terrain", "", "PositionAndLod:PosXyz:POSITION0 InstanceID:InstanceTransformUv:SV_InstanceID0" )]
	public void SurfacesGiveTheirShadersVertexInputs( string shader, string feature, string expected )
	{
		var features = new Dictionary<string, int>();
		if ( feature.Length > 0 ) features[feature] = 1;
		Assert.AreEqual( expected, Inputs( MaterialInputs.Read( Module( shader ), features ) ) );
	}
}
