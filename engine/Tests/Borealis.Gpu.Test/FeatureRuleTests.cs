using Borealis.Backend;
using Borealis.Source2.Materials;
using Borealis.Source2.Shaders;

namespace BorealisGpuTests;

/// <summary>
/// A shader's feature rules (<c>FeatureRule( Allow1( ... ) )</c>), read from its compiled file and applied to a material's
/// feature values as native applies them: of the features an Allow1 names, the first set stays and the rest clear.
/// </summary>
[TestClass]
public class FeatureRuleTests
{
	[TestMethod]
	public void AllowOneKeepsTheFirstFeatureSet()
	{
		var shader = ShaderFile.Read( ShaderFileTests.Files.Read( "shaders/complex.shader" ), "complex" );
		var cloth = shader.FeatureIndex( "F_CLOTH_SHADING" );
		var metalness = shader.FeatureIndex( "F_METALNESS_TEXTURE" );
		Assert.IsTrue( cloth >= 0 && metalness >= 0 );

		// FeatureRule( Allow1( F_CLOTH_SHADING, F_METALNESS_TEXTURE ) ), in that order
		var rule = shader.Features.StaticRules.Single( r => r.Kind == ShaderRuleKind.AllowNum && r.ArgumentIndices.Take( r.Extra[1] ).Order().SequenceEqual( new[] { cloth, metalness }.Order() ) );
		Assert.AreEqual( 1, rule.Extra[0] );
		Assert.AreEqual( ShaderRuleType.Feature, rule.Type );
		Assert.AreEqual( cloth, rule.ArgumentIndices[0] );

		// Needlestack's gloves set both: cloth shading, no metalness texture, as native draws them
		var file = new MaterialFile
		{
			Name = "gloves",
			Shader = "shaders/complex.shader",
			Ints = new Dictionary<string, int> { ["F_CLOTH_SHADING"] = 1, ["F_METALNESS_TEXTURE"] = 1 },
			Floats = new Dictionary<string, float>(),
			Vectors = new Dictionary<string, float[]>(),
			Textures = new Dictionary<string, string>(),
			IntAttributes = new Dictionary<string, int>(),
		};

		var features = BackendMaterial.FeatureValues( shader, file );
		Assert.AreEqual( 1, features[cloth] );
		Assert.AreEqual( 0, features[metalness] );
	}
}
