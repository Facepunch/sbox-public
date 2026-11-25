using Borealis.Source2.Shaders;

namespace BorealisGpuTests;

/// <summary>
/// A shader's feature rules (<c>FeatureRule( Allow1( ... ) )</c>) applied to a material's feature values as native applies
/// them: of the features an Allow1 names, the first set stays and the rest clear. The rules are made here: no shipped
/// compiled shader has one now (complex.shader was the last), though package shaders compiled from it do.
/// </summary>
[TestClass]
public class FeatureRuleTests
{
	[TestMethod]
	public void AllowOneKeepsTheFirstFeatureSet()
	{
		// FeatureRule( Allow1( F_CLOTH_SHADING, F_METALNESS_TEXTURE ) ), in that order, over features 2 and 5
		const int cloth = 2, metalness = 5;
		var rule = new ShaderRule( ShaderRuleKind.AllowNum, ShaderRuleType.Feature, [ShaderRuleType.Feature, ShaderRuleType.Feature], [cloth, metalness], [-1, -1], [1, 2] );

		// Needlestack's gloves set both: cloth shading, no metalness texture, as native draws them
		var values = new int[8];
		values[cloth] = 1;
		values[metalness] = 1;
		ShaderRule.Apply( [rule], ShaderRuleType.Feature, values );
		Assert.AreEqual( 1, values[cloth] );
		Assert.AreEqual( 0, values[metalness] );

		// One set alone stays; static combo rules don't touch features
		var alone = new int[8];
		alone[metalness] = 1;
		ShaderRule.Apply( [rule], ShaderRuleType.Feature, alone );
		Assert.AreEqual( 1, alone[metalness] );
		var other = new int[8];
		other[cloth] = other[metalness] = 1;
		ShaderRule.Apply( [rule], ShaderRuleType.Static, other );
		Assert.AreEqual( 1, other[metalness], "a feature rule applied as combo rules changes nothing" );
	}
}
