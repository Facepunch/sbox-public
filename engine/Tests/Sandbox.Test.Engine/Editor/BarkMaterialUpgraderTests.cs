using Editor;
using NativeEngine;
using Sandbox.Internal;
using System;
using System.Text.Json.Nodes;

namespace EditorTests;

[TestClass]
// The native KV1-to-JSON converter uses a shared output buffer.
[DoNotParallelize]
public class BarkMaterialUpgraderTests
{
	[TestMethod]
	[DataRow( 0, 0, null, 0.5f )]
	[DataRow( 1, 2, null, 0.5f )]
	[DataRow( 0, 0, "0.25", 0.25f )]
	[DataRow( 1, 2, "0.25", 0.25f )]
	public void BarkMigrationPreservesMaterialSettings( int barkAnimation, int foliageAnimation,
		string specularOcclusion, float expectedSpecularOcclusion )
	{
		var specularSetting = specularOcclusion is null
			? ""
			: $"\"g_flSpecularOcclusion\" \"{specularOcclusion}\"";
		var source = $$"""
			Layer0
			{
				"Shader" "SHADERS\BARK.SHADER_C"
				"F_BARK_ANIMATION" "{{barkAnimation}}"
				"TextureColor" "materials\trees\bark_color.tga"
				{{specularSetting}}
				ToolAttributes
				{
					"author" "test"
				}
			}
			""";

		Assert.IsTrue( AssetUpgrader.TryUpgrade( typeof( BarkMaterialUpgrader ).Assembly, ".VMAT", source, out var upgraded ) );
		var material = JsonNode.Parse( EditorUtility.KeyValues1ToJson( upgraded ),
			new JsonNodeOptions { PropertyNameCaseInsensitive = true } ).AsObject();
		Assert.AreEqual( "shaders/foliage.shader", material["Shader"].GetValue<string>() );
		Assert.AreEqual( foliageAnimation, material["F_FOLIAGE_ANIMATION"].GetValue<int>() );
		Assert.IsNull( material["F_BARK_ANIMATION"] );
		Assert.AreEqual( 0, material["g_flMinRoughness"].GetValue<int>() );
		Assert.AreEqual( 0, material["g_flNormalVariation"].GetValue<int>() );
		Assert.AreEqual( expectedSpecularOcclusion, material["g_flSpecularOcclusion"].GetValue<float>() );
		Assert.AreEqual( @"materials\trees\bark_color.tga", material["TextureColor"].GetValue<string>() );
		Assert.AreEqual( "test", material["ToolAttributes"]["author"].GetValue<string>() );
		Assert.AreEqual( 1, material["ToolAttributes"]["material_version:bark.shader"].GetValue<int>() );

		Assert.IsFalse( AssetUpgrader.TryUpgrade( typeof( BarkMaterialUpgrader ).Assembly, "vmat", upgraded, out var repeated ) );
		Assert.AreEqual( upgraded, repeated );
	}

	[TestMethod]
	public void UnrelatedMaterialIsUnchanged()
	{
		const string source = """
			Layer0
			{
				"shader" "shaders/foliage.shader"
				ToolAttributes
				{
					"previous_shader" "bark.shader"
				}
			}
			""";

		Assert.IsFalse( AssetUpgrader.TryUpgrade( typeof( BarkMaterialUpgrader ).Assembly, ".vmat", source, out var upgraded ) );
		Assert.AreEqual( source, upgraded );
	}

	[TestMethod]
	[DataRow( "m_intParams", "m_nValue", "0", null, 0, 0.5f )]
	[DataRow( "m_floatParams", "m_flValue", "1.0", "0.25", 2, 0.25f )]
	[DataRow( "m_vectorParams", "m_value", "[1, 1, 1, 1]", "[0.25, 0.25, 0.25, 0.25]", 2, 0.25f )]
	[DataRow( "m_intParams", "m_nValue", "1", "0", 2, 0.0f )]
	[DataRow( "m_intParams", "m_nValue", null, null, 0, 0.5f )]
	public void CompiledBarkMigrationPreservesMaterialSettings( string parameterArray, string valueName,
		string animation, string specularOcclusion, int expectedAnimation, float expectedSpecularOcclusion )
	{
		var source = JsonNode.Parse( """
			{
				"m_shaderName": "SHADERS\\BARK.SHADER_C",
				"m_intParams": [{ "m_name": "F_UNRELATED", "m_nValue": 7 }],
				"m_floatParams": [
					{ "m_name": "g_flUnrelated", "m_flValue": 0.75 },
					{ "m_name": "g_flMinRoughness", "m_flValue": 0.9 }],
				"m_vectorParams": [
					{ "m_name": "g_vUnrelated", "m_value": [1, 2, 3, 4] },
					{ "m_name": "g_flNormalVariation", "m_value": [0.8, 0.8, 0.8, 0.8] }],
				"m_textureParams": [{ "m_name": "g_tColor", "m_pValue": "materials/trees/bark_color.vtex" }],
				"m_stringAttributes": [{ "m_name": "PhysicsSurfaceProperties", "m_value": "wood" }],
				"m_floatAttributes": [{ "m_name": "custom", "m_flValue": 0.125 }]
			}
			""" ).AsObject();
		if ( animation is not null )
			source[parameterArray].AsArray().Add( new JsonObject { ["m_name"] = "F_BARK_ANIMATION", [valueName] = JsonNode.Parse( animation ) } );
		if ( specularOcclusion is not null )
			source[parameterArray].AsArray().Add( new JsonObject { ["m_name"] = "g_flSpecularOcclusion", [valueName] = JsonNode.Parse( specularOcclusion ) } );

		// Native reads the first matching parameter, with ints before floats before vectors.
		// Later duplicates must neither override that value nor survive the migration.
		if ( animation is not null )
			source["m_vectorParams"].AsArray().Add( JsonNode.Parse( """{ "m_name": "f_bark_animation", "m_value": [7, 7, 7, 7] }""" ) );
		if ( specularOcclusion is not null )
			source["m_vectorParams"].AsArray().Add( JsonNode.Parse( """{ "m_name": "G_FLSPECULAROCCLUSION", "m_value": [0.9, 0.9, 0.9, 0.9] }""" ) );

		var data = EngineGlue.JsonToKeyValues3( source.ToJsonString() );
		try
		{
			data.FindMember( "m_textureParams" ).GetArrayElement( 0 ).FindMember( "m_pValue" )
				.SetValueResourceString( "materials/trees/bark_color.vtex" );
			var preservedFields = new[] { "m_textureParams", "m_stringAttributes", "m_floatAttributes" };
			var preservedData = preservedFields.Select( name => Save( data.FindMember( name ) ) ).ToArray();
			var numericArrays = new[] { "m_intParams", "m_floatParams", "m_vectorParams" };
			var unrelatedParameters = numericArrays.Select( name => Save( data.FindMember( name ).GetArrayElement( 0 ) ) ).ToArray();

			Sandbox.Internal.CompiledAssetUpgrader.UpgradeCompiled( "materials/trees/bark.VMAT_C", data );

			var material = JsonNode.Parse( EngineGlue.KeyValues3ToJson( data ) ).AsObject();
			Assert.AreEqual( "shaders/foliage.shader", material["m_shaderName"].GetValue<string>() );
			var parameters = numericArrays.SelectMany( name => material[name].AsArray() ).ToArray();
			Assert.IsFalse( parameters.Any( x => string.Equals(
				x["m_name"].GetValue<string>(), "F_BARK_ANIMATION", StringComparison.OrdinalIgnoreCase ) ) );
			foreach ( var (name, expected) in new[]
			{
				("F_FOLIAGE_ANIMATION", (float)expectedAnimation),
				("g_flMinRoughness", 0.0f),
				("g_flNormalVariation", 0.0f),
				("g_flSpecularOcclusion", expectedSpecularOcclusion)
			} )
			{
				var parameter = parameters.Single( x => string.Equals(
					x["m_name"].GetValue<string>(), name, StringComparison.OrdinalIgnoreCase ) );
				Assert.AreEqual( expected, parameter["m_flValue"].GetValue<float>(), name );
			}

			for ( var i = 0; i < preservedFields.Length; i++ )
				CollectionAssert.AreEqual( preservedData[i], Save( data.FindMember( preservedFields[i] ) ), preservedFields[i] );
			for ( var i = 0; i < numericArrays.Length; i++ )
				CollectionAssert.AreEqual( unrelatedParameters[i], Save( data.FindMember( numericArrays[i] ).GetArrayElement( 0 ) ), numericArrays[i] );

			var upgraded = Save( data );
			Sandbox.Internal.CompiledAssetUpgrader.UpgradeCompiled( "materials/trees/bark.VMAT_C", data );
			CollectionAssert.AreEqual( upgraded, Save( data ) );
		}
		finally
		{
			data.DeleteThis();
		}
	}

	[TestMethod]
	[DataRow( "materials/test.vmat", "shaders/foliage.shader" )]
	[DataRow( "models/test.vmdl", "shaders/bark.shader" )]
	public void UnrelatedCompiledResourceIsUnchanged( string path, string shader )
	{
		var data = EngineGlue.JsonToKeyValues3( """{ "m_shaderName": "", "previous_shader": "bark.shader" }""" );
		try
		{
			data.SetMemberString( "m_shaderName", shader );
			var original = Save( data );
			Sandbox.Internal.CompiledAssetUpgrader.UpgradeCompiled( path, data );
			CollectionAssert.AreEqual( original, Save( data ) );
		}
		finally
		{
			data.DeleteThis();
		}
	}

	[TestMethod]
	[DataRow( "m_intParams" )]
	[DataRow( "m_floatParams" )]
	[DataRow( "m_vectorParams" )]
	public void MalformedCompiledMaterialIsUnchanged( string parameterArray )
	{
		var source = JsonNode.Parse( """
			{
				"m_shaderName": "shaders/bark.shader",
				"m_intParams": [{ "m_name": "F_BARK_ANIMATION", "m_nValue": 1 }],
				"m_floatParams": [],
				"m_vectorParams": []
			}
			""" ).AsObject();
		source[parameterArray] = "invalid parameter array";
		var data = EngineGlue.JsonToKeyValues3( source.ToJsonString() );
		try
		{
			var original = Save( data );
			Sandbox.Internal.CompiledAssetUpgrader.UpgradeCompiled( "materials/test.vmat", data );
			CollectionAssert.AreEqual( original, Save( data ) );
		}
		finally
		{
			data.DeleteThis();
		}
	}

	[TestMethod]
	public void CompiledUpgradersAreDiscoveredInOrder()
	{
		Assert.IsFalse( typeof( AssetUpgraderAttribute ).IsPublic );
		Assert.IsFalse( typeof( AssetUpgradeTarget ).IsPublic );
		var publicTypes = new TypeLibrary();
		publicTypes.AddAssembly( typeof( BarkMaterialUpgrader ).Assembly, false );
		Assert.IsNull( publicTypes.GetType( typeof( BarkMaterialUpgrader ) ) );

		var upgrades = CompiledAssetUpgrader.Discover( typeof( BarkMaterialUpgraderTests ).Assembly );
		Assert.IsTrue( upgrades.TryGetValue( ".VDATA", out var handlers ) );
		Assert.AreEqual( 2, handlers.Length );
		Assert.IsFalse( upgrades.ContainsKey( ".unrelated" ) );
		Assert.IsFalse( AssetUpgrader.TryUpgrade( typeof( BarkMaterialUpgraderTests ).Assembly, ".unrelated", "old", out _ ) );

		var method = typeof( FirstDataUpgrader ).GetMethod( nameof( FirstDataUpgrader.ApplySource ) );
		var declaration = (AssetUpgraderAttribute)Attribute.GetCustomAttribute( method, typeof( AssetUpgraderAttribute ) );
		Assert.AreEqual( "first.resource", declaration.SourceReference );
		Assert.IsTrue( AssetUpgrader.TryUpgrade( typeof( BarkMaterialUpgraderTests ).Assembly, ".vdata", "old", out var source ) );
		Assert.AreEqual( "new", source );

		var data = EngineGlue.JsonToKeyValues3( """{ "version": 0 }""" );
		try
		{
			foreach ( var upgrade in handlers )
				upgrade( data );

			Assert.AreEqual( 12, data.GetMemberInt( "version", -1 ) );
		}
		finally
		{
			data.DeleteThis();
		}
	}

	static class FirstDataUpgrader
	{
		[AssetUpgrader( ".vdata", Order = -10, SourceReference = "first.resource" )]
		public static string ApplySource( string source ) => source == "old" ? "middle" : null;

		[AssetUpgrader( ".vdata", Target = AssetUpgradeTarget.Compiled, Order = -10 )]
		public static void ApplyCompiled( KeyValues3 data )
		{
			data.SetMemberInt( "version", data.GetMemberInt( "version", 0 ) * 10 + 1 );
		}

		[AssetUpgrader( ".unrelated", Target = AssetUpgradeTarget.Compiled )]
		static string InvalidCompiledSignature( string source ) => source + " changed";
	}

	static class LegacyDataUpgrader
	{
		[AssetUpgrader( ".vdata" )]
		public static string Upgrade( string source ) => source == "middle" ? "legacy" : null;
	}

	static class LastDataUpgrader
	{
		[AssetUpgrader( ".VDATA", Order = 10, SourceReference = "second.resource" )]
		public static string ApplySource( string source ) => source == "legacy" ? "new" : null;

		[AssetUpgrader( ".VDATA", Target = AssetUpgradeTarget.Compiled, Order = 10 )]
		static void ApplyCompiled( KeyValues3 data )
		{
			data.SetMemberInt( "version", data.GetMemberInt( "version", 0 ) * 10 + 2 );
		}
	}

	static byte[] Save( KeyValues3 data )
	{
		using var buffer = data.Save();
		return buffer.ToArray();
	}
}
