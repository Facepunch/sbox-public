using Borealis.Backend;
using Borealis.Source2;
using Borealis.Source2.Materials;
using Sandbox.ShaderCompiler;
using System.IO;

namespace BorealisGpuTests;

/// <summary>
/// The feature rules of complex.shader, simple.shader and generic.shader (deleted; their names are standard's now) as
/// standard.slang's own block declares them (<see cref="FeatureRules"/>): each rule of the original shaders, listed here as
/// they were, is in the block in its order, the material compiler honours it (a material setting a forbidden pair compiles
/// as the one the rule leaves) and a standard surface material drawn for the shader takes its features through it.
/// </summary>
[TestClass]
public class StandardFeatureRuleTests
{
	/// <summary>complex.shader's rules before it was deleted (75faf9a31a0^), vr_shared_standard_features.fxc's first, in order.</summary>
	internal static readonly string[] ComplexRules =
	[
		"Allow1( F_ALPHA_TEST, F_TRANSLUCENT )",
		"Requires1( F_ADDITIVE_BLEND, F_TRANSLUCENT )",
		"Allow1( F_TRANSLUCENT, F_DO_NOT_CAST_SHADOWS )",
		"Allow1( F_ANISOTROPIC_GLOSS, F_ADDITIVE_BLEND )",
		"Allow1( F_RETRO_REFLECTIVE, F_ANISOTROPIC_GLOSS )",
		"Allow1( F_RETRO_REFLECTIVE, F_ALPHA_TEST )",
		"Allow1( F_RETRO_REFLECTIVE, F_TRANSLUCENT )",
		"Allow1( F_RETRO_REFLECTIVE, F_TINT_MASK )",
		"Allow1( F_RETRO_REFLECTIVE, F_ADDITIVE_BLEND )",
		"Allow1( F_SCALE_NORMAL_MAP, F_ALPHA_TEST )",
		"Allow1( F_SCALE_NORMAL_MAP, F_TRANSLUCENT )",
		"Allow1( F_SCALE_NORMAL_MAP, F_ANISOTROPIC_GLOSS )",
		"Allow1( F_RETRO_REFLECTIVE, F_SCALE_NORMAL_MAP )",
		"Allow1( F_SCALE_NORMAL_MAP, F_ADDITIVE_BLEND )",
		"Allow1( F_ENABLE_NORMAL_SELF_SHADOW, F_ADDITIVE_BLEND )",
		"Allow1( F_USE_BENT_NORMALS, F_TRANSLUCENT )",
		"Allow1( F_USE_BENT_NORMALS, F_TINT_MASK )",
		"Allow1( F_USE_BENT_NORMALS, F_RETRO_REFLECTIVE )",
		"Allow1( F_USE_BENT_NORMALS, F_ADDITIVE_BLEND )",
		"Allow1( F_USE_BENT_NORMALS, F_SCALE_NORMAL_MAP )",
		"Allow1( F_SELF_ILLUM, F_RETRO_REFLECTIVE )",
		"Allow1( F_DETAIL_TEXTURE, F_SELF_ILLUM )",
		"Allow1( F_DETAIL_TEXTURE, F_ANISOTROPIC_GLOSS )",
		"Allow1( F_DETAIL_TEXTURE, F_RETRO_REFLECTIVE )",
		"Allow1( F_DETAIL_TEXTURE, F_ADDITIVE_BLEND )",
		"Allow1( F_METALNESS_TEXTURE, F_ADDITIVE_BLEND )",
		"Allow1( F_SECONDARY_UV, F_RETRO_REFLECTIVE )",
		"Allow1( F_SECONDARY_UV, F_ENABLE_NORMAL_SELF_SHADOW )",
		"Allow1( F_SECONDARY_UV, F_USE_BENT_NORMALS )",
		"Allow1( F_SECONDARY_UV, F_SCALE_NORMAL_MAP )",
		"Allow1( F_DIFFUSE_WRAP, F_RETRO_REFLECTIVE )",
		"Allow1( F_DIFFUSE_WRAP, F_SCALE_NORMAL_MAP )",
		"Allow1( F_DIFFUSE_WRAP, F_DETAIL_TEXTURE )",
		"Allow1( F_DIFFUSE_WRAP, F_ADDITIVE_BLEND )",
		"Allow1( F_TRANSMISSIVE_BACKFACE_NDOTL, F_DIFFUSE_WRAP )",
		"Allow1( F_TRANSMISSIVE_BACKFACE_NDOTL, F_RETRO_REFLECTIVE )",
		"Allow1( F_TRANSMISSIVE_BACKFACE_NDOTL, F_SCALE_NORMAL_MAP )",
		"Allow1( F_TRANSMISSIVE_BACKFACE_NDOTL, F_DETAIL_TEXTURE )",
		"Allow1( F_TRANSMISSIVE_BACKFACE_NDOTL, F_ADDITIVE_BLEND )",
		"Allow1( F_CLOTH_SHADING, F_ANISOTROPIC_GLOSS )",
		"Allow1( F_CLOTH_SHADING, F_RETRO_REFLECTIVE )",
		"Allow1( F_CLOTH_SHADING, F_METALNESS_TEXTURE )",
		"Allow1( F_CLOTH_SHADING, F_SCALE_NORMAL_MAP )",
		"Allow1( F_CLOTH_SHADING, F_DIFFUSE_WRAP )",
		"Allow1( F_PARALLAX_OCCLUSION, F_TRANSLUCENT )",
		"Allow1( F_PARALLAX_OCCLUSION, F_ALPHA_TEST )",
		"Allow1( F_PARALLAX_OCCLUSION, F_CLOTH_SHADING )",
		"Allow1( F_PARALLAX_OCCLUSION, F_USE_BENT_NORMALS )",
		"Allow1( F_PARALLAX_OCCLUSION, F_DIFFUSE_WRAP )",
		"Allow1( F_PARALLAX_OCCLUSION, F_ADDITIVE_BLEND )",
		"Allow1( F_PARALLAX_OCCLUSION, F_DETAIL_TEXTURE )",
		"Allow1( F_PARALLAX_OCCLUSION, F_METALNESS_TEXTURE )",
		"Allow1( F_PARALLAX_OCCLUSION, F_RETRO_REFLECTIVE )",
	];

	/// <summary>generic.shader's rules before it was deleted, in order.</summary>
	internal static readonly string[] GenericRules =
	[
		"Allow1( F_UNLIT, F_SPECULAR )",
		"Allow1( F_TRANSLUCENT, F_ALPHA_TEST )",
		"Requires1( F_ADDITIVE_BLEND, F_TRANSLUCENT )",
		"Allow1( F_SELF_ILLUM, F_UNLIT )",
		"Allow1( F_UNLIT, F_TINT_MASK )",
	];

	/// <summary>The features complex declared that standard doesn't do (no Feature line in its block).</summary>
	static readonly HashSet<string> NotDone = ["F_ANISOTROPIC_GLOSS", "F_RETRO_REFLECTIVE", "F_ENABLE_NORMAL_SELF_SHADOW", "F_USE_BENT_NORMALS", "F_SECONDARY_UV", "F_DIFFUSE_WRAP", "F_CLOTH_SHADING", "F_PARALLAX_OCCLUSION"];

	internal static PassModule Standard()
	{
		var core = ContentFiles.FindGame().Roots.FirstOrDefault( r => File.Exists( Path.Combine( r, "shaders", "standard.slang" ) ) );
		if ( core is null ) Assert.Inconclusive( "No core shaders" );
		var shaders = Path.Combine( core, "shaders" );
		return PassModule.Read( Path.Combine( shaders, "standard.slang" ), [shaders] );
	}

	static string Text( FeatureRules.Rule rule ) => $"{rule.Kind}{rule.Count}( {string.Join( ", ", rule.Arguments.Select( a => a.Value == -1 ? a.Feature : $"{a.Feature} == {a.Value}" ) )} )";

	/// <summary>
	/// What a material setting a rule's features sets, and what it draws with once the rule applies: an AllowN's arguments all
	/// set (the first N stay), a RequiresN's first argument alone (it clears). The original shaders' rules are all of the two.
	/// </summary>
	internal static (Dictionary<string, int> Set, Dictionary<string, int> Expected) Case( string rule )
	{
		var parsed = FeatureRules.Parse( $"FeatureRule( {rule}, \"\" );" ).Rules.Single();
		var set = new Dictionary<string, int>( StringComparer.OrdinalIgnoreCase );
		var expected = new Dictionary<string, int>( StringComparer.OrdinalIgnoreCase );
		if ( parsed.Kind == "Allow" )
		{
			for ( int i = 0; i < parsed.Arguments.Count; i++ )
			{
				set[parsed.Arguments[i].Feature] = 1;
				expected[parsed.Arguments[i].Feature] = i < parsed.Count ? 1 : 0;
			}
		}
		else
		{
			Assert.AreEqual( "Requires", parsed.Kind, rule );
			set[parsed.Arguments[0].Feature] = 1;
			expected[parsed.Arguments[0].Feature] = 0;
		}

		return (set, expected);
	}

	/// <summary>Every rule of complex and generic is in standard's block, in the shader's order; simple had none.</summary>
	[TestMethod]
	public void StandardDeclaresEveryRuleOfTheThreeShaders()
	{
		var module = Standard();
		CollectionAssert.AreEqual( ComplexRules, FeatureRules.Read( module, 0 ).Rules.Select( Text ).ToArray(), "complex's" );
		CollectionAssert.AreEqual( Array.Empty<string>(), FeatureRules.Read( module, 1 ).Rules.Select( Text ).ToArray(), "simple's" );
		CollectionAssert.AreEqual( GenericRules, FeatureRules.Read( module, 2 ).Rules.Select( Text ).ToArray(), "generic's" );
	}

	/// <summary>
	/// The material editor gets complex's rules over the features standard does, in order, and none naming one it doesn't
	/// offer (its feature text would name a feature it doesn't declare).
	/// </summary>
	[TestMethod]
	public void EditorLinesHoldTheRulesOverOfferedFeatures()
	{
		var lines = MaterialInputs.Read( Standard(), new Dictionary<string, int>() ).FeatureLines;
		var shown = FeatureRules.Parse( lines ).Rules.Select( Text ).ToArray();
		CollectionAssert.AreEqual( ComplexRules.Where( r => !FeatureRules.Parse( $"FeatureRule( {r}, \"\" );" ).Features.Any( NotDone.Contains ) ).ToArray(), shown );
		foreach ( var feature in NotDone ) Assert.IsFalse( lines.Contains( $"Feature( {feature}", StringComparison.Ordinal ), $"{feature} isn't offered" );
	}

	/// <summary>The rules apply as vfx's do: in order, again until nothing changes, a feature they name but nothing sets off.</summary>
	[TestMethod]
	public void RulesApplyInOrderUntilNothingChanges()
	{
		var rules = FeatureRules.Parse( string.Join( "\n", ComplexRules.Select( r => $"FeatureRule( {r}, \"\" );" ) ) );

		// Retro-reflection clears alpha test, then self-illumination clears retro-reflection: alpha test stays off
		var values = rules.Apply( new Dictionary<string, int> { ["F_RETRO_REFLECTIVE"] = 1, ["F_ALPHA_TEST"] = 1, ["F_SELF_ILLUM"] = 1 } );
		Assert.AreEqual( 0, values["F_ALPHA_TEST"] );
		Assert.AreEqual( 0, values["F_RETRO_REFLECTIVE"] );
		Assert.AreEqual( 1, values["F_SELF_ILLUM"] );
		Assert.AreEqual( 0, values["F_CLOTH_SHADING"], "named, not set" );

		// Additive blending needs translucency; a value-matching argument matches only that value
		Assert.AreEqual( 0, rules.Apply( new Dictionary<string, int> { ["F_ADDITIVE_BLEND"] = 1 } )["F_ADDITIVE_BLEND"] );
		Assert.AreEqual( 1, rules.Apply( new Dictionary<string, int> { ["F_ADDITIVE_BLEND"] = 1, ["F_TRANSLUCENT"] = 1 } )["F_ADDITIVE_BLEND"] );
		var valued = FeatureRules.Parse( "FeatureRule( Allow1( F_A == 2, F_B ), \"\" );" );
		Assert.AreEqual( 1, valued.Apply( new Dictionary<string, int> { ["F_A"] = 1, ["F_B"] = 1 } )["F_B"] );
		Assert.AreEqual( 0, valued.Apply( new Dictionary<string, int> { ["F_A"] = 2, ["F_B"] = 1 } )["F_B"] );
	}

	/// <summary>What a compile of the material makes and writes: its textures (name, format, channels), parameters and attributes.</summary>
	static string Compiled( MaterialInputs inputs ) => string.Join( "\n",
		inputs.Textures.Select( t => $"texture {t.Name} {t.Format} {t.SrgbRead} {string.Join( ";", t.Channels.Select( c => $"{c.Channels}={c.Algorithm}({string.Join( ",", c.Inputs )})" ) )}" )
		.Concat( inputs.Parameters.Select( p => $"parameter {p.Name}" ) )
		.Concat( inputs.Attributes.Select( a => $"attribute {a.Name} {string.Join( ",", a.Value )}" ) ) );

	/// <summary>
	/// The material compiler honours each of complex's rules: a material setting a rule's features compiles as the material
	/// the rule leaves (the made textures, parameters and attributes are that one's), as complex's compiled.
	/// </summary>
	[TestMethod]
	public void EachRuleOfComplexHoldsInTheCompile()
	{
		var module = Standard();
		foreach ( var rule in ComplexRules )
		{
			var (set, expected) = Case( rule );
			Assert.AreEqual( Compiled( MaterialInputs.Read( module, expected ) ), Compiled( MaterialInputs.Read( module, set ) ), rule );
		}

		// The ones that move what's made: cloth keeps the colour's alpha from metalness, parallax occlusion keeps it from
		// opacity, alpha test wins over translucency, and a translucent material casts shadows
		string Color( params string[] features ) => MaterialInputs.Read( module, features.ToDictionary( f => f, _ => 1 ) ).Textures.Single( t => t.Name == "g_tColor" ).Format;
		Assert.AreEqual( "BC7", Color( "F_METALNESS_TEXTURE" ) );
		Assert.AreEqual( "DXT1", Color( "F_METALNESS_TEXTURE", "F_CLOTH_SHADING" ) );
		Assert.AreEqual( "BC7", Color( "F_ALPHA_TEST" ) );
		Assert.AreEqual( "DXT1", Color( "F_ALPHA_TEST", "F_PARALLAX_OCCLUSION" ) );
		var both = MaterialInputs.Read( module, new Dictionary<string, int> { ["F_ALPHA_TEST"] = 1, ["F_TRANSLUCENT"] = 1 } );
		Assert.AreEqual( "PreserveCoverage", both.Textures.Single( t => t.Name == "g_tColor" ).Channels[1].Algorithm );
		var shadows = MaterialInputs.Read( module, new Dictionary<string, int> { ["F_TRANSLUCENT"] = 1, ["F_DO_NOT_CAST_SHADOWS"] = 1 } );
		Assert.AreEqual( 0f, shadows.Attributes.Single( a => a.Name == "DoNotCastShadows" ).Value[0] );
	}
}

/// <summary>
/// A standard surface material takes a complex, standard or generic material's features through that shader's rules, as
/// native drew the material with its shader: checked on the surface's record, field by field.
/// </summary>
[TestClass]
public class StandardFeatureRuleDrawTests : GpuTest
{
	static MaterialFile File( string shader, IReadOnlyDictionary<string, int> features ) => new()
	{
		Name = "rules",
		Shader = shader,
		Ints = new Dictionary<string, int>( features, StringComparer.OrdinalIgnoreCase ),
		Floats = new Dictionary<string, float>(),
		Vectors = new Dictionary<string, float[]>(),
		Textures = new Dictionary<string, string>(),
		IntAttributes = new Dictionary<string, int>(),
	};

	/// <summary>
	/// Each rule of complex (for materials naming complex or standard) and of generic (for generic's), on a material setting
	/// its features: every feature the surface has a field for draws as the rule leaves it.
	/// </summary>
	[TestMethod]
	public void EachRuleHoldsOnAStandardSurfaceMaterial()
	{
		using var backend = new BackendDevice( Device );
		var errors = new List<string>();
		backend.Surfaces.Error = errors.Add;
		var checkedFields = 0;
		var cases = StandardFeatureRuleTests.ComplexRules.SelectMany( r => new[] { ("shaders/complex.shader", r), ("shaders/standard.shader", r) } )
			.Concat( StandardFeatureRuleTests.GenericRules.Select( r => ("shaders/generic.shader", r) ) );
		var n = 0;
		foreach ( var (shader, rule) in cases )
		{
			var (set, expected) = StandardFeatureRuleTests.Case( rule );
			using var material = backend.LoadMaterial( $"materials/test_rules_{n++}.vmat", File( shader, set ) );
			var surface = material as ConvertedMaterial;
			Assert.IsNotNull( surface, $"{shader} draws as standard" );
			foreach ( var (feature, value) in expected )
			{
				if ( surface.FeatureValue( feature ) is not { } drawn ) continue;
				Assert.AreEqual( value, drawn, $"{shader} {rule}: {feature}" );
				checkedFields++;
			}
		}

		Assert.AreEqual( 0, errors.Count, string.Join( "\n", errors ) );
		Assert.IsTrue( checkedFields > 100, $"{checkedFields} fields checked" );
	}

	/// <summary>
	/// Where complex and generic differ (alpha test against translucency) each material keeps its own shader's choice, and
	/// generic's self-illumination turns its unlit off.
	/// </summary>
	[TestMethod]
	public void GenericMaterialsKeepGenericsRules()
	{
		using var backend = new BackendDevice( Device );
		var both = new Dictionary<string, int> { ["F_ALPHA_TEST"] = 1, ["F_TRANSLUCENT"] = 1 };
		using var complex = (ConvertedMaterial)backend.LoadMaterial( "materials/test_rules_complex.vmat", File( "shaders/complex.shader", both ) );
		using var generic = (ConvertedMaterial)backend.LoadMaterial( "materials/test_rules_generic.vmat", File( "shaders/generic.shader", both ) );
		Assert.AreEqual( 1, complex.FeatureValue( "F_ALPHA_TEST" ) );
		Assert.AreEqual( 0, complex.FeatureValue( "F_TRANSLUCENT" ) );
		Assert.AreEqual( 0, generic.FeatureValue( "F_ALPHA_TEST" ) );
		Assert.AreEqual( 1, generic.FeatureValue( "F_TRANSLUCENT" ) );

		using var lit = (ConvertedMaterial)backend.LoadMaterial( "materials/test_rules_lit.vmat", File( "shaders/generic.shader", new Dictionary<string, int> { ["F_UNLIT"] = 1, ["F_SELF_ILLUM"] = 1 } ) );
		Assert.AreEqual( 0, lit.FeatureValue( "F_UNLIT" ) );
		Assert.AreEqual( 1, lit.FeatureValue( "F_SELF_ILLUM" ) );
	}

	/// <summary>
	/// Features set in code go through the rules with the file's: alpha test set on a translucent material turns its
	/// translucency off, and back on once code stops setting it.
	/// </summary>
	[TestMethod]
	public void FeaturesSetInCodeGoThroughTheRules()
	{
		using var backend = new BackendDevice( Device );
		using var material = (ConvertedMaterial)backend.LoadMaterial( "materials/test_rules_code.vmat", File( "shaders/standard.shader", new Dictionary<string, int> { ["F_TRANSLUCENT"] = 1 } ) );
		Assert.AreEqual( 1, material.FeatureValue( "F_TRANSLUCENT" ) );

		material.SetFeatures( new Dictionary<string, int> { ["F_ALPHA_TEST"] = 1 } );
		Assert.AreEqual( 1, material.FeatureValue( "F_ALPHA_TEST" ) );
		Assert.AreEqual( 0, material.FeatureValue( "F_TRANSLUCENT" ) );

		material.SetFeatures( null );
		Assert.AreEqual( 0, material.FeatureValue( "F_ALPHA_TEST" ) );
		Assert.AreEqual( 1, material.FeatureValue( "F_TRANSLUCENT" ) );
	}
}
