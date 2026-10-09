using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace SceneTests.Components;

[TestClass]
[DoNotParallelize]
public class CloneCollectionRegressionTest : SceneTest
{
	[TestMethod]
	public void CollectionValuesKeepJsonRemappingAndIgnoredMembers()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var original = scene.CreateObject();
		var data = original.AddComponent<CloneCollectionData>();
		data.Ids = new() { original.Id };
		data.IdArray = new[] { original.Id };
		data.IdValues = new() { ["owner"] = original.Id };
		data.TextIds = new() { original.Id.ToString() };
		data.Payloads = new() { new() { Id = original.Id, Transient = 42 } };

		var clone = original.Clone();
		var copy = clone.GetComponent<CloneCollectionData>();
		Assert.AreEqual( clone.Id, copy.Ids[0] );
		Assert.AreEqual( clone.Id, copy.IdArray[0] );
		Assert.AreEqual( clone.Id, copy.IdValues["owner"] );
		Assert.AreEqual( clone.Id.ToString(), copy.TextIds[0] );
		Assert.AreEqual( clone.Id, copy.Payloads[0].Id );
		Assert.AreEqual( 0, copy.Payloads[0].Transient );
		Assert.AreEqual( original.Id, data.Ids[0] );
	}

	[TestMethod]
	public void ScalarCollectionsAndMappedReferencesAreIndependentCopies()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var original = scene.CreateObject();
		var data = original.AddComponent<CloneCollectionData>();
		data.Numbers = new[] { 1, 2, 3 };
		data.Weights = new() { ["a"] = 1.5f };
		data.References = new() { data, null };
		var clone = original.Clone();
		var copy = clone.GetComponent<CloneCollectionData>();

		Assert.AreSame( copy, copy.References[0] );
		Assert.IsNull( copy.References[1] );
		Assert.AreNotSame( data.References, copy.References );
		CollectionAssert.AreEqual( data.Numbers, copy.Numbers );
		Assert.AreEqual( 1.5f, copy.Weights["a"] );
		copy.Numbers[0] = 9;
		copy.Weights["a"] = 2;
		Assert.AreEqual( 1, data.Numbers[0] );
		Assert.AreEqual( 1.5f, data.Weights["a"] );
	}

	[TestMethod]
	public void SkippedComponentCollectionResolvesOnClonedOwner()
	{
		var source = new Scene();
		using var sourceScope = source.Push();
		var owner = source.CreateObject();
		var skipped = owner.AddComponent<CloneDataComponent>();
		skipped.Flags |= ComponentFlags.NotCloned;
		var original = owner.AddComponent<CloneCollectionData>();
		original.References = new() { skipped };

		var target = new Scene();
		using var targetScope = target.Push();
		var clonedOwner = target.CreateObject();
		var replacement = clonedOwner.AddComponent<CloneDataComponent>();
		var copy = clonedOwner.AddComponent<CloneCollectionData>();
		var context = new CloneContext( new() { [owner] = clonedOwner } );
		foreach ( var member in ReflectionQueryCache.ClonePlan( typeof( CloneCollectionData ) ) )
			member.Clone( copy, original, context );

		Assert.AreSame( replacement, copy.References[0] );
	}

	[TestMethod]
	public void PrefabMappingsReflectFlagChangesAndReplacements()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var template = new GameObject( false, "template" );
		var templateChild = new GameObject( template, false, "child" );
		templateChild.AddComponent<CloneDataComponent>();
		const string path = "clone_mapping_regression.prefab";
		using var registration = Helpers.RegisterPrefabFromJson( path, template.Serialize().ToJsonString() );
		var file = ResourceLibrary.Get<PrefabFile>( path );
		var prefab = SceneUtility.GetPrefabScene( file );
		var instance = GameObject.Clone( file );
		var data = instance.PrefabInstance;
		var child = prefab.Children[0];
		var component = child.Components.Get<CloneDataComponent>( FindMode.EverythingInSelf );
		var childInstanceId = data.PrefabToInstanceLookup[child.Id];
		var obsoleteId = Guid.NewGuid();
		var mappings = new Dictionary<Guid, Guid>( data.PrefabToInstanceLookup ) { [obsoleteId] = Guid.NewGuid() };
		data.InitLookups( mappings );
		Assert.IsFalse( data.PrefabToInstanceLookup.ContainsKey( obsoleteId ) );
		Assert.IsTrue( mappings.ContainsKey( obsoleteId ), "Validation must not modify the caller's mapping." );
		Assert.AreEqual( childInstanceId, data.PrefabToInstanceLookup[child.Id] );
		Assert.AreEqual( child.Id, data.InstanceToPrefabLookup[childInstanceId] );

		prefab.Flags |= GameObjectFlags.NotSaved;
		child.Flags |= GameObjectFlags.NotSaved;
		component.Flags |= ComponentFlags.NotSaved;
		data.ValidatePrefabLookup();
		Assert.AreEqual( instance.Id, data.PrefabToInstanceLookup[prefab.Id] );
		Assert.IsFalse( data.PrefabToInstanceLookup.ContainsKey( child.Id ) );
		Assert.IsFalse( data.PrefabToInstanceLookup.ContainsKey( component.Id ) );
		Assert.IsFalse( data.InstanceToPrefabLookup.ContainsKey( childInstanceId ) );

		child.Flags &= ~GameObjectFlags.NotSaved;
		component.Flags &= ~ComponentFlags.NotSaved;
		data.ValidatePrefabLookup();
		Assert.AreEqual( PrefabInstanceData.DeriveInstanceGuid( instance.Id, child.Id ), data.PrefabToInstanceLookup[child.Id] );
		Assert.AreEqual( PrefabInstanceData.DeriveInstanceGuid( instance.Id, component.Id ), data.PrefabToInstanceLookup[component.Id] );

		var oldId = child.Id;
		child.DestroyImmediate();
		using var prefabScope = prefab.Push();
		var replacement = new GameObject( prefab, false, "replacement" );
		replacement.AddComponent<CloneDataComponent>();
		data.ValidatePrefabLookup();
		Assert.IsFalse( data.PrefabToInstanceLookup.ContainsKey( oldId ) );
		var replacementId = data.PrefabToInstanceLookup[replacement.Id];
		Assert.AreEqual( PrefabInstanceData.DeriveInstanceGuid( instance.Id, replacement.Id ), replacementId );
		data.ValidatePrefabLookup();
		Assert.AreEqual( replacementId, data.PrefabToInstanceLookup[replacement.Id] );
	}
}

public struct CloneCollectionPayload
{
	public Guid Id { get; set; }
	[JsonIgnore] public int Transient { get; set; }
}

public class CloneCollectionData : Component
{
	[Property] public int[] Numbers { get; set; }
	[Property] public Dictionary<string, float> Weights { get; set; }
	[Property] public List<Guid> Ids { get; set; }
	[Property] public Guid[] IdArray { get; set; }
	[Property] public Dictionary<string, Guid> IdValues { get; set; }
	[Property] public List<string> TextIds { get; set; }
	[Property] public List<CloneCollectionPayload> Payloads { get; set; }
	[Property] public List<Component> References { get; set; }
}
