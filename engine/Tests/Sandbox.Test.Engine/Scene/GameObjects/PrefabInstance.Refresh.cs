using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;

namespace SceneTests.GameObjects;

public partial class PrefabInstanceTest
{
	[TestMethod]
	public void UpdatePrefabMemberPreservesItsComponentsAndChildren()
	{
		using var registration = RegisterBasicPrefab( out var prefab );
		var scene = new Scene();
		using var scope = scene.Push();
		var instance = prefab.Clone();
		var child = GetChild( instance, "ChildA" );
		var grandchild = new GameObject( child ) { Name = "AddedGrandchild" };
		var grandchildId = grandchild.Id;
		child.Components.Get<PrefabInstanceStatComponent>().Text = "override";
		instance.PrefabInstance.RefreshPatch();

		GetChild( prefab, "ChildA" ).Components.Get<PrefabInstanceStatComponent>().Number = 42;
		prefab.ToPrefabFile();
		child.UpdateFromPrefab();

		Assert.AreEqual( 42, child.Components.Get<PrefabInstanceStatComponent>().Number );
		Assert.AreEqual( "override", child.Components.Get<PrefabInstanceStatComponent>().Text );
		Assert.AreEqual( grandchildId, GetChild( child, "AddedGrandchild" ).Id );
		Assert.IsTrue( child.IsPrefabInstance );
	}

	[TestMethod]
	public void ApplyGameObjectRootPreservesIndependentInstanceOwnership()
	{
		using var registration = RegisterBasicPrefab( out var prefab );
		using var containerRegistration = Helpers.RegisterPrefabFromJson( ContainerPrefabPath, ContainerPrefabJson );
		var scene = new Scene();
		using var scope = scene.Push();
		var instance = prefab.Clone();
		instance.Components.Get<PrefabInstanceStatComponent>().Number = 42;
		instance.PrefabInstance.RefreshPatch();
		instance.PrefabInstance.ApplyGameObjectChangesToPrefab( instance );
		Assert.IsFalse( instance.PrefabInstance.IsNested );

		var container = GameObject.GetPrefab( ContainerPrefabPath ).Clone();
		instance.SetParent( GetChild( container, "Slot" ) );
		Assert.IsTrue( instance.IsOutermostPrefabInstanceRoot );
		instance.PrefabInstance.ApplyGameObjectChangesToPrefab( instance );
		Assert.IsFalse( instance.PrefabInstance.IsNested );
		Assert.IsTrue( instance.IsOutermostPrefabInstanceRoot );

		container.PrefabInstance.RefreshPatch();
		Assert.IsTrue( container.PrefabInstance.IsAddedGameObject( instance ) );
		var restored = RestoreIntoFreshScene( container.Serialize() );
		var restoredItem = GetChild( GetChild( restored, "Slot" ), "BasicRoot" );
		Assert.IsTrue( restoredItem.IsOutermostPrefabInstanceRoot );
		Assert.AreEqual( 42, restoredItem.Components.Get<PrefabInstanceStatComponent>().Number );
	}

	[TestMethod]
	public void PrefabRootGuidReplacementRemovesOldRegistration()
	{
		var oldId = Guid.Parse( RootGuid );
		var newId = Guid.NewGuid();
		using ( Helpers.RegisterPrefabFromJson( BasicPrefabPath, BasicPrefabJson ) )
		{
			var file = ResourceLibrary.Get<PrefabFile>( BasicPrefabPath );
			Assert.IsTrue( file.PostLoadInternal() );
			Assert.AreSame( file, PrefabFile.FindByGuid( oldId ) );
			file.RootObject["__guid"] = newId;
			Assert.IsTrue( file.PostReloadInternal() );
			Assert.IsNull( PrefabFile.FindByGuid( oldId ) );
			Assert.AreSame( file, PrefabFile.FindByGuid( newId ) );
			file.DestroyInternal();
		}
		Assert.IsNull( PrefabFile.FindByGuid( oldId ) );
		Assert.IsNull( PrefabFile.FindByGuid( newId ) );
	}

	[TestMethod]
	[DataRow( 0, 1, 2 )]
	[DataRow( 0, 2, 1 )]
	[DataRow( 1, 0, 2 )]
	[DataRow( 1, 2, 0 )]
	[DataRow( 2, 0, 1 )]
	[DataRow( 2, 1, 0 )]
	public void PrefabCacheRefreshOrdersDependencies( int first, int second, int third )
	{
		const string topPath = "test_prefab_refresh_top.prefab";
		const string middlePath = "test_prefab_refresh_middle.prefab";
		var middleId = Guid.NewGuid();
		var definitions = new[]
		{
			(topPath, MakeContainingPrefab( Guid.NewGuid(), "Top", middlePath, middleId )),
			(middlePath, MakeContainingPrefab( middleId, "Middle", BasicPrefabPath, Guid.Parse( RootGuid ) )),
			(BasicPrefabPath, BasicPrefabJson)
		};
		var registrations = new List<IDisposable>();
		try
		{
			foreach ( var index in new[] { first, second, third } )
				registrations.Add( Helpers.RegisterPrefabFromJson( definitions[index].Item1, definitions[index].Item2 ) );

			var top = GameObject.GetPrefab( topPath );
			Assert.AreEqual( 5, GetChild( GetChild( top, "Middle" ), "BasicRoot" ).Components.Get<PrefabInstanceStatComponent>().Number );
			var leaf = ResourceLibrary.Get<PrefabFile>( BasicPrefabPath );
			leaf.RootObject["Components"][0]["Number"] = 42;
			leaf.CachedScene.Refresh( leaf );

			Assert.AreEqual( 42, GetChild( GetChild( top, "Middle" ), "BasicRoot" ).Components.Get<PrefabInstanceStatComponent>().Number );
			var scene = new Scene();
			using var scope = scene.Push();
			var clone = top.Clone();
			Assert.AreEqual( 42, GetChild( GetChild( clone, "Middle" ), "BasicRoot" ).Components.Get<PrefabInstanceStatComponent>().Number );
			clone.PrefabInstance.RefreshPatch();
			Assert.IsFalse( clone.PrefabInstance.Patch.PropertyOverrides.Exists( x => x.Property == "Number" ) );
		}
		finally
		{
			for ( var i = registrations.Count - 1; i >= 0; i-- )
				registrations[i].Dispose();
		}
	}

	private static string MakeContainingPrefab( Guid rootId, string name, string childPath, Guid childPrefabId )
	{
		var childInstanceId = Guid.NewGuid();
		return new JsonObject
		{
			["__guid"] = rootId,
			["__version"] = 2,
			["Name"] = name,
			["Flags"] = 0,
			["Enabled"] = true,
			["Components"] = new JsonArray(),
			["Children"] = new JsonArray( new JsonObject
			{
				["__guid"] = childInstanceId,
				["__version"] = 2,
				["__Prefab"] = childPath,
				["__PrefabInstancePatch"] = Json.ToNode( new Json.Patch() ),
				["__PrefabIdToInstanceId"] = new JsonObject { [childPrefabId.ToString()] = childInstanceId }
			} )
		}.ToJsonString();
	}
}
