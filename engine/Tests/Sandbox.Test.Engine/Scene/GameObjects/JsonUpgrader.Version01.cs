using Sandbox.Internal;
using SceneTests;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using static Sandbox.Json;

namespace SceneTests.GameObjects;

[TestClass]
[DoNotParallelize]
public class JsonUpgrader01Test : SceneTest
{
	TypeLibrary TypeLibrary;

	private TypeLibrary _oldTypeLibrary;

	[TestInitialize]
	public void TestInitialize()
	{
		// Replace TypeLibrary / NodeLibrary with mocked ones, store the originals

		_oldTypeLibrary = Game.TypeLibrary;

		TypeLibrary = new Sandbox.Internal.TypeLibrary();
		TypeLibrary.AddAssembly( typeof( ModelRenderer ).Assembly, false );
		TypeLibrary.AddAssembly( typeof( PrefabFile ).Assembly, false );
		JsonUpgrader.UpdateUpgraders( TypeLibrary );

		Game.TypeLibrary = TypeLibrary;
	}

	[TestCleanup]
	public void Cleanup()
	{
		// Make sure our mocked TypeLibrary doesn't leak out, restore old ones
		Game.TypeLibrary = _oldTypeLibrary;
	}

	[TestMethod]
	public void UpgraderConvertsLegacyPrefabInstanceToNewFormat()
	{
		// Arrange - Setup an old format GameObject with prefab reference
		string oldFormatJson = """
		{
			"__guid": "5d6dad9b-96d1-45c3-a7c4-1412b8570422",
			"Flags": 0,
			"Name": "piss (1)",
			"Position": "4.408689,0.0000001473241,32.37395",
			"Tags": "particles",
			"Enabled": true,
			"Rotation": "0,0,0,1",
			"Scale": "1,1,1",
			"__Prefab": "prefabs/particles/piss.prefab",
			"__PrefabVariables": {}
		}
		""";

		// Create a mock prefab file with a root GameObject
		string prefabContent = """
		{
			"__guid": "fab370f8-2e2c-48cf-a523-e4be49723490",
			"Name": "Object",
			"Position": "0,0,0",
			"Rotation": "0,0,0,1",
			"Scale": "1,1,1",
			"Enabled": true,
			"Components": []
		}
		""";

		string prefabPath = "prefabs/particles/piss.prefab";
		using var prefabRegistration = SceneTests.Helpers.RegisterPrefabFromJson( prefabPath, prefabContent );

		// Parse the JSON into a JsonObject that we can pass to the upgrader
		var jsonObject = JsonNode.Parse( oldFormatJson ).AsObject();

		// Act - Call the upgrader
		GameObject.Upgrader_v1( jsonObject );

		// Assert - Verify the JSON has been properly upgraded

		// 1. Verify the essential properties are preserved
		Assert.AreEqual( "5d6dad9b-96d1-45c3-a7c4-1412b8570422", jsonObject["__guid"].Deserialize<Guid>().ToString() );
		Assert.AreEqual( "prefabs/particles/piss.prefab", jsonObject["__Prefab"].GetValue<string>() );

		var rootLevelProperties = new[] { GameObject.JsonKeys.Position, GameObject.JsonKeys.Name, GameObject.JsonKeys.Flags, GameObject.JsonKeys.Tags, GameObject.JsonKeys.Enabled, GameObject.JsonKeys.Rotation, GameObject.JsonKeys.Scale };

		// 2. Verify old root-level properties no longer exist
		foreach ( var propName in rootLevelProperties )
		{
			Assert.IsFalse( jsonObject.ContainsKey( propName ), $"Expected property '{propName}' to be removed" );
		}

		// 3. Verify the new patch structure exists
		Assert.IsTrue( jsonObject.ContainsKey( GameObject.JsonKeys.PrefabInstancePatch ) );
		Assert.IsTrue( jsonObject.ContainsKey( GameObject.JsonKeys.PrefabIdToInstanceId ) );

		// 4. Verify the patch contains the expected overrides
		var prefabOverrides = jsonObject[GameObject.JsonKeys.PrefabInstancePatch].Deserialize<Json.Patch>();
		Assert.IsNotNull( prefabOverrides );
		Assert.IsNotNull( prefabOverrides.PropertyOverrides );

		// 5. Verify all the original properties are now properly overridden
		foreach ( var propName in rootLevelProperties )
		{
			var override_ = prefabOverrides.PropertyOverrides.FirstOrDefault( p =>
				p.Property == propName && p.Target.Type == "GameObject" );

			Assert.IsNotNull( override_, $"Expected property override for '{propName}' not found" );
		}

		// 6. Verify specific property values are preserved
		var positionOverride = prefabOverrides.PropertyOverrides.First( p => p.Property == GameObject.JsonKeys.Position );
		Assert.AreEqual( "4.408689,0.0000001473241,32.37395", positionOverride.Value.ToString() );

		var nameOverride = prefabOverrides.PropertyOverrides.First( p => p.Property == GameObject.JsonKeys.Name );
		Assert.AreEqual( "piss (1)", nameOverride.Value.GetValue<string>() );

		var tagsOverride = prefabOverrides.PropertyOverrides.First( p => p.Property == GameObject.JsonKeys.Tags );
		Assert.AreEqual( "particles", tagsOverride.Value.GetValue<string>() );

		// 7. Check that GUID mapping exists
		var idLookup = jsonObject[GameObject.JsonKeys.PrefabIdToInstanceId].Deserialize<Dictionary<string, string>>();
		Assert.IsNotNull( idLookup );
		Assert.IsTrue( idLookup.Count > 0 );

		// 8. Verify that all overrides target the prefab's root GameObject ID
		var prefabRootId = "fab370f8-2e2c-48cf-a523-e4be49723490";
		foreach ( var override_ in prefabOverrides.PropertyOverrides )
		{
			Assert.AreEqual( prefabRootId, override_.Target.IdValue, "All overrides should target the prefab's root GameObject ID" );
		}
	}

	[TestMethod]
	public void UpgraderHandlesPrefabVariablesCorrectly()
	{
		// Arrange - Setup an old format GameObject with prefab variables
		string oldFormatJsonWithVariables = """
		{
			"__guid": "5d6dad9b-96d1-45c3-a7c4-1412b8570422",
			"Name": "particle_instance",
			"Position": "0,0,0",
			"__Prefab": "prefabs/particles/variable_test.prefab",
			"__PrefabVariables": {
				"color": "0,1,0,1",
				"speed": 5.5,
				"emit": true
			}
		}
		""";

		// Create a prefab with component and variables
		var componentId = Guid.NewGuid();
		var prefabRootId = Guid.NewGuid();

		// Build the prefab JSON programmatically - the registered prefab JSON (including its
		// "__variables" array) is all the upgrader reads
		var prefabJson = new JsonObject
		{
			[GameObject.JsonKeys.Id] = prefabRootId.ToString(),
			[GameObject.JsonKeys.Name] = "Particle",
			[GameObject.JsonKeys.Position] = "0,0,0",
			[GameObject.JsonKeys.Components] = new JsonArray
			{
				new JsonObject
				{
					[Component.JsonKeys.Id] = componentId.ToString(),
					[Component.JsonKeys.Type] = "ParticleSystem",
					["Color"] = "1,0,0,1",
					["Speed"] = 1.0,
					["Emit"] = false
				}
			},
			["__variables"] = new JsonArray
			{
				new JsonObject
				{
					["Id"] = "color",
					["Title"] = "Color",
					["Targets"] = new JsonArray
					{
						new JsonObject
						{
							["Id"] = componentId.ToString(),
							["Property"] = "Color"
						}
					}
				},
				new JsonObject
				{
					["Id"] = "speed",
					["Title"] = "Speed",
					["Targets"] = new JsonArray
					{
						new JsonObject
						{
							["Id"] = componentId.ToString(),
							["Property"] = "Speed"
						}
					}
				},
				new JsonObject
				{
					["Id"] = "emit",
					["Title"] = "Emit",
					["Targets"] = new JsonArray
					{
						new JsonObject
						{
							["Id"] = componentId.ToString(),
							["Property"] = "Emit"
						}
					}
				}
			}
		};

		string prefabPathWithVars = "prefabs/particles/variable_test.prefab";
		using var prefabRegistration = SceneTests.Helpers.RegisterPrefabFromJson(
			prefabPathWithVars,
			prefabJson.ToJsonString()
		);

		// Parse input JSON
		var jsonObject = JsonNode.Parse( oldFormatJsonWithVariables ).AsObject();

		// Act - Call the upgrader
		GameObject.Upgrader_v1( jsonObject );

		// Assert
		Assert.IsTrue( jsonObject.ContainsKey( GameObject.JsonKeys.PrefabInstancePatch ) );
		var prefabOverrides = jsonObject[GameObject.JsonKeys.PrefabInstancePatch].Deserialize<Json.Patch>();

		// Check that variables were converted to component property overrides
		var componentOverrides = prefabOverrides.PropertyOverrides
			.Where( p => p.Target.Type == "Component" )
			.ToList();

		Assert.AreEqual( 3, componentOverrides.Count, "Should have 3 component property overrides from the variables" );

		// Check individual overrides match the expected values from __PrefabVariables
		var colorOverride = componentOverrides.FirstOrDefault( p => p.Property == "Color" );
		Assert.IsNotNull( colorOverride, "Color override not found" );
		Assert.AreEqual( componentId.ToString(), colorOverride.Target.IdValue );
		Assert.AreEqual( "0,1,0,1", colorOverride.Value.ToString() );

		var speedOverride = componentOverrides.FirstOrDefault( p => p.Property == "Speed" );
		Assert.IsNotNull( speedOverride, "Speed override not found" );
		Assert.AreEqual( componentId.ToString(), speedOverride.Target.IdValue );
		Assert.AreEqual( 5.5, speedOverride.Value.GetValue<double>() );

		var emitOverride = componentOverrides.FirstOrDefault( p => p.Property == "Emit" );
		Assert.IsNotNull( emitOverride, "Emit override not found" );
		Assert.AreEqual( componentId.ToString(), emitOverride.Target.IdValue );
		Assert.IsTrue( emitOverride.Value.GetValue<bool>() );

		// Verify the old __PrefabVariables is gone
		Assert.IsFalse( jsonObject.ContainsKey( GameObject.JsonKeys.PrefabInstanceVariables ) );

		// Verify ID lookup was created correctly
		var idLookup = jsonObject[GameObject.JsonKeys.PrefabIdToInstanceId].Deserialize<Dictionary<string, string>>();
		Assert.IsNotNull( idLookup );
		Assert.IsTrue( idLookup.ContainsKey( prefabRootId.ToString() ) );
		Assert.IsTrue( idLookup.ContainsKey( componentId.ToString() ) );
	}

	[TestMethod]
	public void UpgraderSkipsAlreadyUpgradedObjects()
	{
		// Arrange - Setup an already upgraded GameObject
		string alreadyUpgradedJson = """
		{
			"__guid": "5d6dad9b-96d1-45c3-a7c4-1412b8570422",
			"__Prefab": "prefabs/particles/piss.prefab",
			"__PrefabInstancePatch": {
				"PropertyOverrides": [
					{
						"Target": {
							"Type": "GameObject",
							"IdValue": "fab370f8-2e2c-48cf-a523-e4be49723490"
						},
						"Property": "Name",
						"Value": "Already Upgraded"
					}
				]
			},
			"__PrefabIdToInstanceId": {
				"fab370f8-2e2c-48cf-a523-e4be49723490": "5d6dad9b-96d1-45c3-a7c4-1412b8570422"
			}
		}
		""";

		// Create a mock prefab file
		string prefabContent = """
		{
			"__guid": "fab370f8-2e2c-48cf-a523-e4be49723490",
			"Name": "Object",
			"Position": "0,0,0",
			"Enabled": true,
			"Components": []
		}
		""";

		string prefabPath = "prefabs/particles/piss.prefab";
		using var prefabRegistration = SceneTests.Helpers.RegisterPrefabFromJson( prefabPath, prefabContent );

		// Create a deep copy of the original JSON to compare later
		var originalJson = JsonNode.Parse( alreadyUpgradedJson ).AsObject();
		var jsonObject = JsonNode.Parse( alreadyUpgradedJson ).AsObject();

		// Act - Call the upgrader
		GameObject.Upgrader_v1( jsonObject );

		// Assert - The object should remain unchanged since it's already upgraded
		var originalText = originalJson.ToJsonString();
		var afterText = jsonObject.ToJsonString();

		Assert.AreEqual( originalText, afterText, "Already upgraded objects should not be modified" );

		// Specifically check that __PrefabOverrides and __PrefabIdToInstanceId remain untouched
		var originalOverrides = originalJson[GameObject.JsonKeys.PrefabInstancePatch].ToJsonString();
		var afterOverrides = jsonObject[GameObject.JsonKeys.PrefabInstancePatch].ToJsonString();
		Assert.AreEqual( originalOverrides, afterOverrides );

		var originalLookup = originalJson[GameObject.JsonKeys.PrefabIdToInstanceId].ToJsonString();
		var afterLookup = jsonObject[GameObject.JsonKeys.PrefabIdToInstanceId].ToJsonString();
		Assert.AreEqual( originalLookup, afterLookup );
	}

	[TestMethod]
	public void UpgraderHandlesEmptyPrefabVariables()
	{
		// Arrange - Setup an old format GameObject with empty prefab variables
		string oldFormatJsonEmptyVars = """
		{
			"__guid": "5d6dad9b-96d1-45c3-a7c4-1412b8570422",
			"Flags": 0,
			"Name": "empty_vars_test",
			"Position": "4.408689,0.0000001473241,32.37395",
			"Tags": "particles",
			"Enabled": true,
			"__Prefab": "prefabs/particles/piss.prefab",
			"__PrefabVariables": {}
		}
		""";

		// Create a mock prefab file
		string prefabContent = """
		{
			"__guid": "fab370f8-2e2c-48cf-a523-e4be49723490",
			"Name": "Object",
			"Position": "0,0,0",
			"Enabled": true,
			"Components": []
		}
		""";

		string prefabPath = "prefabs/particles/piss.prefab";
		using var prefabRegistration = SceneTests.Helpers.RegisterPrefabFromJson( prefabPath, prefabContent );

		// Parse input JSON
		var jsonObject = JsonNode.Parse( oldFormatJsonEmptyVars ).AsObject();

		// Act - Call the upgrader
		GameObject.Upgrader_v1( jsonObject );

		// Assert
		Assert.IsTrue( jsonObject.ContainsKey( GameObject.JsonKeys.PrefabInstancePatch ) );
		Assert.IsFalse( jsonObject.ContainsKey( GameObject.JsonKeys.PrefabInstanceVariables ) );

		var prefabOverrides = jsonObject[GameObject.JsonKeys.PrefabInstancePatch].Deserialize<Json.Patch>();

		// Should only have GameObject property overrides, no component ones
		var componentOverrides = prefabOverrides.PropertyOverrides
			.Where( p => p.Target.Type == "Component" )
			.ToList();

		Assert.AreEqual( 0, componentOverrides.Count, "Should have no component property overrides from empty variables" );

		// Root overrides should still be there
		var gameObjectOverrides = prefabOverrides.PropertyOverrides
			.Where( p => p.Target.Type == "GameObject" )
			.ToList();

		Assert.IsTrue( gameObjectOverrides.Count > 0, "Should have GameObject property overrides" );
	}

	[TestMethod]
	public void UpgraderFailsGracefullyWithMissingPrefab()
	{
		// Arrange - Setup an old format GameObject with a non-existing prefab
		string jsonWithMissingPrefab = """
		{
			"__guid": "5d6dad9b-96d1-45c3-a7c4-1412b8570422",
			"Name": "missing_prefab_test",
			"Position": "0,0,0",
			"__Prefab": "prefabs/does_not_exist/missing.prefab",
			"__PrefabVariables": {}
		}
		""";

		var jsonObject = JsonNode.Parse( jsonWithMissingPrefab ).AsObject();

		// Act - Call the upgrader without registering the prefab so that it does not exist
		try
		{
			GameObject.Upgrader_v1( jsonObject );
		}
		catch ( Exception ex )
		{
			Assert.Fail( "Upgrader threw an exception: " + ex.Message );
		}

		// Assert - Verify that no upgrade occurred and the __Prefab key remains unchanged
		Assert.IsTrue( jsonObject.ContainsKey( GameObject.JsonKeys.PrefabInstanceSource ), "The __Prefab key should remain present" );
		Assert.IsFalse( jsonObject.ContainsKey( GameObject.JsonKeys.PrefabInstancePatch ), "Should not create a PrefabInstancePatch for missing prefab" );
		Assert.IsFalse( jsonObject.ContainsKey( GameObject.JsonKeys.PrefabIdToInstanceId ), "Should not create a PrefabIdToInstanceId mapping for missing prefab" );
	}

	[TestMethod]
	public void UpgraderConvertsMissingEnabledPropertyCorrectly()
	{
		// Arrange - Setup an old format GameObject with prefab reference
		// Note: We're intentionally omitting most properties to test defaults
		string oldFormatJson = """
		{
			"__guid": "5d6dad9b-96d1-45c3-a7c4-1412b8570422",
			"Name": "piss (1)",
			"Position": "4.408689,0.0000001473241,32.37395",
			"__Prefab": "prefabs/particles/piss.prefab",
			"__PrefabVariables": {}
		}
		""";

		// Create a mock prefab file with a root GameObject
		string prefabContent = """
		{
			"__guid": "fab370f8-2e2c-48cf-a523-e4be49723490",
			"Name": "Object",
			"Position": "0,0,0",
			"Rotation": "0,0,0,1",
			"Scale": "1,1,1",
			"Enabled": true,
			"Components": []
		}
		""";

		string prefabPath = "prefabs/particles/piss.prefab";
		using var prefabRegistration = SceneTests.Helpers.RegisterPrefabFromJson( prefabPath, prefabContent );

		// Parse the JSON into a JsonObject that we can pass to the upgrader
		var jsonObject = JsonNode.Parse( oldFormatJson ).AsObject();

		// Act - Call the upgrader
		GameObject.Upgrader_v1( jsonObject );

		// Assert - Verify the JSON has been properly upgraded
		Assert.IsTrue( jsonObject.ContainsKey( GameObject.JsonKeys.PrefabInstancePatch ), "Patch should be created" );
		var prefabOverrides = jsonObject[GameObject.JsonKeys.PrefabInstancePatch].Deserialize<Json.Patch>();
		Assert.IsNotNull( prefabOverrides );

		// Helper function to test property overrides
		PropertyOverride FindOverride( string propertyName ) =>
			prefabOverrides.PropertyOverrides.FirstOrDefault( p =>
				p.Property == propertyName &&
				p.Target.Type == "GameObject" );

		// Test each missing property gets proper default
		var enabledOverride = FindOverride( GameObject.JsonKeys.Enabled );
		Assert.IsNotNull( enabledOverride, "Enabled property override should be created" );
		Assert.IsFalse( enabledOverride.Value.GetValue<bool>(), "Missing Enabled property should default to false" );

		var rotationOverride = FindOverride( GameObject.JsonKeys.Rotation );
		Assert.IsNotNull( rotationOverride, "Rotation property override should be created" );
		Assert.AreEqual( "0,0,0,1", rotationOverride.Value.ToString(), "Default rotation should be Identity" );

		var scaleOverride = FindOverride( GameObject.JsonKeys.Scale );
		Assert.IsNotNull( scaleOverride, "Scale property override should be created" );
		Assert.AreEqual( "1,1,1", scaleOverride.Value.ToString(), "Default scale should be Vector3.One" );

		var flagsOverride = FindOverride( GameObject.JsonKeys.Flags );
		Assert.IsNotNull( flagsOverride, "Flags property override should be created" );
		Assert.AreEqual( 0, flagsOverride.Value.GetValue<int>(), "Default flags should be 0" );

		var tagsOverride = FindOverride( GameObject.JsonKeys.Tags );
		Assert.IsNotNull( tagsOverride, "Tags property override should be created" );
		Assert.AreEqual( "", tagsOverride.Value.GetValue<string>(), "Default tags should be empty string" );

		var networkModeOverride = FindOverride( GameObject.JsonKeys.NetworkMode );
		Assert.IsNotNull( networkModeOverride, "NetworkMode property override should be created" );
		Assert.AreEqual( (int)NetworkMode.Snapshot, networkModeOverride.Value.GetValue<int>(), "Default NetworkMode should be Snapshot" );

		var networkOrphanedOverride = FindOverride( GameObject.JsonKeys.NetworkOrphaned );
		Assert.IsNotNull( networkOrphanedOverride, "NetworkOrphaned property override should be created" );
		Assert.AreEqual( (int)NetworkOrphaned.Destroy, networkOrphanedOverride.Value.GetValue<int>(), "Default NetworkOrphaned should be Destroy" );

		var ownerTransferOverride = FindOverride( GameObject.JsonKeys.OwnerTransfer );
		Assert.IsNotNull( ownerTransferOverride, "OwnerTransfer property override should be created" );
		Assert.AreEqual( (int)OwnerTransfer.Fixed, ownerTransferOverride.Value.GetValue<int>(), "Default OwnerTransfer should be Fixed" );

		var networkInterpolationOverride = FindOverride( GameObject.JsonKeys.NetworkInterpolation );
		Assert.IsNotNull( networkInterpolationOverride, "NetworkInterpolation property override should be created" );
		Assert.IsFalse( networkInterpolationOverride.Value.GetValue<bool>(), "Default NetworkInterpolation should be false" );

		// Verify the prefab's root GameObject ID is targeted for all overrides
		var prefabRootId = "fab370f8-2e2c-48cf-a523-e4be49723490";
		foreach ( var override_ in prefabOverrides.PropertyOverrides )
		{
			Assert.AreEqual( prefabRootId, override_.Target.IdValue,
				$"The {override_.Property} override should target the prefab's root GameObject" );
		}
	}

	[TestMethod]
	public void UpgraderUsesPrefabPropertiesWhenMissing()
	{
		// Arrange - Setup an old format GameObject with minimal properties
		string oldFormatJson = """
	{
		"__guid": "5d6dad9b-96d1-45c3-a7c4-1412b8570422",
		"Name": "prefab_instance",
		"Position": "10,10,10",
		"__Prefab": "prefabs/custom_values.prefab",
		"__PrefabVariables": {}
	}
	""";

		// Create a prefab file with custom non-default values for network properties
		string prefabContentWithCustomValues = """
	{
		"__guid": "fab370f8-2e2c-48cf-a523-e4be49723490",
		"Name": "Custom Values Object",
		"Position": "0,0,0",
		"Rotation": "0,0,0,1",
		"Scale": "1,1,1",
		"Enabled": true,
		"Tags": "custom prefab tag",
		"NetworkMode": 2,
		"NetworkOrphaned": 2,
		"OwnerTransfer": 2,
		"NetworkInterpolation": true,
		"Components": []
	}
	""";

		string prefabPath = "prefabs/custom_values.prefab";
		using var prefabRegistration = SceneTests.Helpers.RegisterPrefabFromJson(
			prefabPath,
			prefabContentWithCustomValues
		);

		// Parse the JSON into a JsonObject
		var jsonObject = JsonNode.Parse( oldFormatJson ).AsObject();

		// Act - Call the upgrader
		GameObject.Upgrader_v1( jsonObject );

		// Assert - Verify the JSON has been properly upgraded
		Assert.IsTrue( jsonObject.ContainsKey( GameObject.JsonKeys.PrefabInstancePatch ) );
		var prefabOverrides = jsonObject[GameObject.JsonKeys.PrefabInstancePatch].Deserialize<Json.Patch>();
		Assert.IsNotNull( prefabOverrides );

		// Helper function to find specific property overrides
		PropertyOverride FindOverride( string propertyName ) =>
			prefabOverrides.PropertyOverrides.FirstOrDefault( p =>
				p.Property == propertyName &&
				p.Target.Type == "GameObject" );

		// Verify that the special properties use the prefab's values, not the defaults
		var tagsOverride = FindOverride( GameObject.JsonKeys.Tags );
		Assert.IsNotNull( tagsOverride, "Tags property override should be created" );
		Assert.AreEqual( "custom prefab tag", tagsOverride.Value.GetValue<string>(),
			"Tags should use prefab's value" );

		var networkModeOverride = FindOverride( GameObject.JsonKeys.NetworkMode );
		Assert.IsNotNull( networkModeOverride, "NetworkMode property override should be created" );
		Assert.AreEqual( 2, networkModeOverride.Value.GetValue<int>(),
			"NetworkMode should use prefab's value" );

		var networkOrphanedOverride = FindOverride( GameObject.JsonKeys.NetworkOrphaned );
		Assert.IsNotNull( networkOrphanedOverride, "NetworkOrphaned property override should be created" );
		Assert.AreEqual( 2, networkOrphanedOverride.Value.GetValue<int>(),
			"NetworkOrphaned should use prefab's value" );

		var ownerTransferOverride = FindOverride( GameObject.JsonKeys.OwnerTransfer );
		Assert.IsNotNull( ownerTransferOverride, "OwnerTransfer property override should be created" );
		Assert.AreEqual( 2, ownerTransferOverride.Value.GetValue<int>(),
			"OwnerTransfer should use prefab's value" );

		var networkInterpolationOverride = FindOverride( GameObject.JsonKeys.NetworkInterpolation );
		Assert.IsNotNull( networkInterpolationOverride, "NetworkInterpolation property override should be created" );
		Assert.IsTrue( networkInterpolationOverride.Value.GetValue<bool>(),
			"NetworkInterpolation should use prefab's value" );

		// Verify these properties still get regular default treatment when missing from both
		var enabledOverride = FindOverride( GameObject.JsonKeys.Enabled );
		Assert.IsNotNull( enabledOverride, "Enabled property override should be created" );
		Assert.IsFalse( enabledOverride.Value.GetValue<bool>(),
			"Missing Enabled property should default to false" );
	}
}

