using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NativeEngine;
using Sandbox.ShaderCompiler;

namespace EngineTests;

/// <summary>
/// The material editor reads a shader only a Slang module describes from the module's MATERIAL_INPUTS block
/// (MaterialEditorDescription, through IAssetSystem::DescribeModuleShader). While a surface's .shader is still compiled, its
/// CVfx is the reference: the same features and rules, and in every feature state checked (each feature's values alone,
/// and every pair) the same UI variables with the same types, UI, groups, defaults, ranges and texture inputs, and the
/// same literal attributes.
/// </summary>
[TestClass]
public class MaterialEditorDescriptionTests
{
	static string Game => Environment.GetEnvironmentVariable( "FACEPUNCH_ENGINE" );

	[TestMethod]
	[DataRow( "core/shaders/skin" )]
	[DataRow( "core/shaders/blendable" )]
	[DataRow( "core/shaders/foliage" )]
	[DataRow( "core/shaders/fur" )]
	[DataRow( "core/shaders/vertex_color" )]
	[DataRow( "core/shaders/static_overlay" )]
	[DataRow( "core/shaders/water_simple" )]
	[DataRow( "core/shaders/terrain_preview" )]
	[DataRow( "core/shaders/glass" )]
	[DataRow( "core/shaders/eyeball" )]
	[DataRow( "core/shaders/terrain" )]
	[DataRow( "core/shaders/water" )]
	[DataRow( "core/shaders/sky" )]
	[DataRow( "core/shaders/unlit" )]
	[DataRow( "core/shaders/line" )]
	[DataRow( "addons/citizen/Assets/shaders/citizen_eye" )]
	public void ModulesDescribeTheirMaterialsAsTheirShaders( string shaderPath )
	{
		var stem = Path.Combine( Game, shaderPath );
		Check( stem + ".slang", stem, [], [] );
	}

	/// <summary>
	/// standard.slang describes the materials of complex, simple and generic, which stand for it (ShaderAliases), with
	/// complex's inputs: while complex.shader is still compiled (a checkout from before it went), its CVfx is the reference,
	/// less the features the standard surface doesn't do and what only they use.
	/// </summary>
	[TestMethod]
	public void StandardDescribesComplexMaterialsLessWhatItDoesNotDo()
	{
		var shaders = Path.Combine( Game, "core", "shaders" );
		Check( Path.Combine( shaders, "standard.slang" ), Path.Combine( shaders, "complex" ),
			["F_ANISOTROPIC_GLOSS", "F_RETRO_REFLECTIVE", "F_ENABLE_NORMAL_SELF_SHADOW", "F_USE_BENT_NORMALS", "F_SECONDARY_UV", "F_DIFFUSE_WRAP", "F_CLOTH_SHADING", "F_PARALLAX_OCCLUSION"],
			["g_flLightRangeForSelfShadowNormals"] );
	}

	/// <summary>
	/// The module at <paramref name="modulePath"/> described as the compiled shader at <paramref name="referenceStem"/> is,
	/// less <paramref name="notOffered"/> features (their rules too) and <paramref name="notOfferedVariables"/>.
	/// </summary>
	static void Check( string modulePath, string referenceStem, string[] notOffered, string[] notOfferedVariables )
	{
		var name = Path.GetFileName( referenceStem );
		if ( !File.Exists( referenceStem + ".shader_c" ) ) Assert.Inconclusive( $"{name}.shader isn't compiled: nothing to check the module against" );
		var module = PassModule.Read( modulePath, [Path.GetDirectoryName( modulePath ), Path.Combine( Game, "core", "shaders" )] );
		bool Offered( string line ) => !notOffered.Any( f => line.Split( '\t', ' ', '=' ).Contains( f ) ) && !notOfferedVariables.Any( v => line.Split( '\t' )[0] == v );

		var shader = new Shader();
		try
		{
			Assert.IsTrue( shader.LoadCompiledForRecompile( referenceStem + ".shader" ) );

			// Features and rules: the module's FEATURES lines, read as the editor reads them
			var reference = Records( shader.native.DescribeUi( "" ) );
			var featureLines = MaterialEditorDescription.Describe( module, new Dictionary<string, int>() ).Split( '\n' ).First( l => l.StartsWith( "featurelines\t" ) )["featurelines\t".Length..].Replace( "\\n", "\n" );
			var features = Records( CVfx.DescribeFeatureText( name, featureLines ) );
			CollectionAssert.AreEquivalent( Lines( reference, "feature" ).Where( Offered ).ToList(), Lines( features, "feature" ), "features" );
			CollectionAssert.AreEquivalent( Lines( reference, "rule" ).Where( Offered ).ToList(), Lines( features, "rule" ), "rules" );

			var mismatches = new List<string>();
			foreach ( var state in States( features ) )
			{
				var expected = Records( shader.native.DescribeUi( state ) );
				var actual = Records( MaterialEditorDescription.Describe( module, MaterialEditorDescription.ParseFeatureValues( state ) ) );
				foreach ( var kind in new[] { "variable", "attribute" } )
				{
					var e = Lines( expected, kind ).Where( Offered ).ToList();
					var a = Lines( actual, kind );
					foreach ( var missing in e.Except( a ) ) mismatches.Add( $"[{state}] {kind} the module lacks or differs: {missing}" );
					foreach ( var extra in a.Except( e ) ) mismatches.Add( $"[{state}] {kind} only the module has: {extra}" );
				}
			}

			Assert.AreEqual( 0, mismatches.Count, string.Join( "\n", mismatches.Distinct().Take( 60 ) ) );
		}
		finally { shader.Destroy(); }
	}

	/// <summary>Each feature's values alone and every pair of features, from the module's features (the reference's, less those not offered).</summary>
	static IEnumerable<string> States( List<string[]> reference )
	{
		// Not pairs an Allow1 rule forbids: the editor applies rules before it asks, as the CVfx does inside
		var exclusive = reference.Where( r => r[0] == "rule" && r[1].StartsWith( "Allow1 ", StringComparison.Ordinal ) )
			.Select( r => r[1].Split( ' ' ).Skip( 1 ).Select( t => t.Split( '=' )[0] ).ToHashSet() ).ToList();
		bool Allowed( string a, string b ) => !exclusive.Any( g => g.Contains( a.Split( '=' )[0] ) && g.Contains( b.Split( '=' )[0] ) );

		var singles = new List<string>();
		foreach ( var f in reference.Where( r => r[0] == "feature" ) )
		{
			int min = int.Parse( Value( f, "min" ) ), max = int.Parse( Value( f, "max" ) );
			for ( int v = min + 1; v <= max; v++ ) singles.Add( $"{f[1]}={v}" );
		}

		yield return "";
		foreach ( var s in singles ) yield return s;
		for ( int i = 0; i < singles.Count; i++ )
			for ( int j = i + 1; j < singles.Count; j++ )
				if ( singles[i].Split( '=' )[0] != singles[j].Split( '=' )[0] && Allowed( singles[i], singles[j] ) ) yield return singles[i] + " " + singles[j];
	}

	static List<string[]> Records( string text ) => [.. text.Split( '\n', StringSplitOptions.RemoveEmptyEntries ).Select( l => l.TrimEnd( '\r' ).Split( '\t' ) )];

	static string Value( string[] record, string key ) => record.Skip( 2 ).FirstOrDefault( f => f.StartsWith( key + "=", StringComparison.Ordinal ) )?[(key.Length + 1)..] ?? "";

	/// <summary>A kind's records as comparable lines: name, then the fields in order, numbers normalised.</summary>
	static List<string> Lines( List<string[]> records, string kind ) => [.. records.Where( r => r[0] == kind ).Select( r => string.Join( "\t", r.Skip( 1 ).Select( Normalise ) ) )];

	static string Normalise( string field )
	{
		var eq = field.IndexOf( '=' );
		if ( eq < 0 ) return field;
		var parts = field[(eq + 1)..].Split( ' ' );
		if ( parts.Length == 0 || !parts.All( p => float.TryParse( p, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _ ) ) ) return field;
		return field[..(eq + 1)] + string.Join( " ", parts.Select( p => float.Parse( p, System.Globalization.CultureInfo.InvariantCulture ).ToString( "0.#####", System.Globalization.CultureInfo.InvariantCulture ) ) );
	}
}
