using Sandbox.Mounting;
using System;

namespace SceneTests.Prefab;

[TestClass]
public class PrefabBuilderTest
{
	[TestMethod]
	public void BasicPrefabIsRegisteredAndRetrievable()
	{
		var builder = new PrefabBuilder()
			.WithName( "test/basic_prefab.prefab" );

		using ( builder.Scope() )
		{
			var root = new GameObject();
			root.Name = "Basic Prefab";
			root.AddComponent<ModelRenderer>();
		}

		var prefab = builder.Create();

		Assert.IsNotNull( prefab );
		Assert.IsNotNull( prefab.RootObject );

		// Should be retrievable from the resource system
		var fixedPath = Resource.FixPath( "test/basic_prefab.prefab" );
		var found = Game.Resources.GetByIdLong<PrefabFile>( fixedPath.FastHash64() );
		Assert.AreSame( prefab, found );

		// Prefab scene should contain a ModelRenderer
		var prefabScene = SceneUtility.GetPrefabScene( prefab );
		Assert.IsNotNull( prefabScene );
		Assert.IsNotNull( prefabScene.Components.Get<ModelRenderer>() );

		PrefabBuilder.Destroy( prefab );
	}

	[TestMethod]
	public void PrefabCanBeClonedIntoScene()
	{
		var builder = new PrefabBuilder()
			.WithName( "test/clonable.prefab" );

		using ( builder.Scope() )
		{
			var root = new GameObject();
			root.Name = "Clonable";

			var renderer = root.AddComponent<ModelRenderer>();
			renderer.Tint = Color.Red;
		}

		var prefab = builder.Create();
		var prefabScene = SceneUtility.GetPrefabScene( prefab );

		var scene = new Scene();
		using var sceneScope = scene.Push();

		var clone = prefabScene.Clone( Vector3.Zero );

		Assert.IsNotNull( clone );
		// PrefabFile.PostLoad syncs root name to the resource name (filename stem)
		Assert.AreEqual( "clonable", clone.Name );
		Assert.IsNotNull( clone.Components.Get<ModelRenderer>() );
		Assert.AreEqual( Color.Red, clone.Components.Get<ModelRenderer>().Tint );

		PrefabBuilder.Destroy( prefab );
	}

	[TestMethod]
	public void SceneInformationOnTheRootIsReadableAsMetadata()
	{
		var builder = new PrefabBuilder()
			.WithName( "test/with_metadata.prefab" );

		using ( builder.Scope() )
		{
			var root = new GameObject();
			root.AddComponent<ModelRenderer>();

			var information = root.AddComponent<SceneInformation>();
			information.Title = "Crate";
			information.Description = "A wooden crate";
			information.Group = "Props";
		}

		var prefab = builder.Create();

		Assert.AreEqual( "Crate", prefab.GetMetadata( "Title" ) );
		Assert.AreEqual( "A wooden crate", prefab.GetMetadata( "Description" ) );
		Assert.AreEqual( "Props", prefab.GetMetadata( "Group" ) );

		var scene = new Scene();
		using var sceneScope = scene.Push();

		var clone = SceneUtility.GetPrefabScene( prefab ).Clone( Vector3.Zero );

		Assert.IsNotNull( clone );
		Assert.IsNotNull( clone.Components.Get<ModelRenderer>() );
		Assert.AreEqual( "Crate", clone.Components.Get<SceneInformation>().Title );

		PrefabBuilder.Destroy( prefab );
	}

	[TestMethod]
	public void APrefabWithoutSceneInformationHasNoMetadata()
	{
		var builder = new PrefabBuilder()
			.WithName( "test/without_metadata.prefab" );

		using ( builder.Scope() )
		{
			var root = new GameObject();
			root.AddComponent<ModelRenderer>();
		}

		var prefab = builder.Create();

		Assert.IsNull( prefab.GetMetadata( "Title" ) );
		Assert.IsNull( prefab.GetMetadata( "Description" ) );
		Assert.IsNull( prefab.GetMetadata( "Group" ) );
		Assert.AreEqual( "Unnamed", prefab.GetMetadata( "Title", "Unnamed" ) );

		// Only what a saved prefab has beside its root
		Assert.IsFalse( prefab.RootObject["__properties"].AsObject().ContainsKey( "GameObjectSystems" ) );

		PrefabBuilder.Destroy( prefab );
	}

	[TestMethod]
	public void ChildObjectsArePresentInPrefabScene()
	{
		var builder = new PrefabBuilder()
			.WithName( "test/with_children.prefab" );

		using ( builder.Scope() )
		{
			var root = new GameObject();
			root.Name = "Parent";

			var child = new GameObject();
			child.Name = "Child";
			child.Parent = root;
			child.AddComponent<ModelRenderer>();
		}

		var prefab = builder.Create();
		var prefabScene = SceneUtility.GetPrefabScene( prefab );

		Assert.IsNotNull( prefabScene );
		// PrefabFile.PostLoad syncs root name to the resource name (filename stem)
		Assert.AreEqual( "with_children", prefabScene.Name );
		Assert.AreEqual( 1, prefabScene.Children.Count );
		Assert.AreEqual( "Child", prefabScene.Children[0].Name );
		Assert.IsNotNull( prefabScene.Children[0].Components.Get<ModelRenderer>() );

		PrefabBuilder.Destroy( prefab );
	}

	[TestMethod]
	public void DestroyRemovesPrefabFromResourceSystem()
	{
		var builder = new PrefabBuilder()
			.WithName( "test/destroy_test.prefab" );

		using ( builder.Scope() )
		{
			var root = new GameObject();
			root.Name = "Temporary";
		}

		var prefab = builder.Create();
		Assert.IsNotNull( prefab );

		var fixedPath = Resource.FixPath( "test/destroy_test.prefab" );
		Assert.IsNotNull( Game.Resources.GetByIdLong<PrefabFile>( fixedPath.FastHash64() ) );

		PrefabBuilder.Destroy( prefab );

		// After destroy, should no longer be in the resource system
		Assert.IsNull( Game.Resources.GetByIdLong<PrefabFile>( fixedPath.FastHash64() ) );
	}

	[TestMethod]
	public void CallingCreateWithoutScopeThrows()
	{
		var builder = new PrefabBuilder()
			.WithName( "test/no_scope.prefab" );

		Assert.ThrowsException<InvalidOperationException>( () => builder.Create() );
	}

	[TestMethod]
	public void EmptyScopePrefabReturnsNull()
	{
		var builder = new PrefabBuilder()
			.WithName( "test/empty.prefab" );

		using ( builder.Scope() )
		{
			// No objects created
		}

		var prefab = builder.Create();
		Assert.IsNull( prefab );
	}

	[TestMethod]
	public void RebuildingAtSamePathReusesTheSamePrefabFile()
	{
		var path = "test/remount.prefab";

		// First creation
		var builder1 = new PrefabBuilder().WithName( path );
		using ( builder1.Scope() )
		{
			var root = new GameObject();
			root.Name = "Version1";
		}
		var prefab1 = builder1.Create();

		// Second creation at same path should reuse
		var builder2 = new PrefabBuilder().WithName( path );
		using ( builder2.Scope() )
		{
			var root = new GameObject();
			root.Name = "Version2";
			// Add a distinguishing component so we can verify the content was updated
			root.AddComponent<ModelRenderer>();
		}
		var prefab2 = builder2.Create();

		Assert.AreSame( prefab1, prefab2, "Re-creating at the same path should reuse the PrefabFile instance" );

		// Verify it reflects the updated content (name is always the resource name stem)
		var prefabScene = SceneUtility.GetPrefabScene( prefab2 );
		Assert.IsNotNull( prefabScene.Components.Get<ModelRenderer>(), "Updated prefab content should be reflected" );

		PrefabBuilder.Destroy( prefab2 );
	}

	[TestMethod]
	public void MetadataSetOnTheBuilderIsReadableAndWinsOverAComponents()
	{
		var builder = new PrefabBuilder()
			.WithName( "test/builder_metadata.prefab" )
			.WithMetadata( "Title", "Supply Crate" )
			.WithMetadata( "Group", "Props" )
			.WithMetadata( "Author", "someone" )
			.WithMetadata( "Author", null )
			.WithMetadata( "", "ignored" )
			.WithMetadata( null, "ignored" );

		using ( builder.Scope() )
		{
			var root = new GameObject();
			var information = root.AddComponent<SceneInformation>();
			information.Title = "Crate";
			information.Description = "A wooden crate";
		}

		var prefab = builder.Create();

		Assert.AreEqual( "Supply Crate", prefab.GetMetadata( "Title" ), "The builder's value wins over the component's" );
		Assert.AreEqual( "A wooden crate", prefab.GetMetadata( "Description" ), "What only the component has is kept" );
		Assert.AreEqual( "Props", prefab.GetMetadata( "Group" ) );
		Assert.IsNull( prefab.GetMetadata( "Author" ), "A null value takes back one set earlier" );
		Assert.AreEqual( 3, prefab.RootObject["__properties"]["Metadata"].AsObject().Count );

		PrefabBuilder.Destroy( prefab );
	}

	[TestMethod]
	public void MetadataSetOnTheBuilderWorksInADisabledBuilderScene()
	{
		var builder = new PrefabBuilder()
			.WithName( "test/builder_metadata_disabled.prefab" )
			.WithMetadata( "Title", "Health Kit" )
			.WithMetadata( "Group", "Half-Life 2" );

		using ( builder.Scope() )
		{
			// A mount builds in a disabled scene, so nothing it makes starts running
			Game.ActiveScene.Enabled = false;

			var root = new GameObject();
			root.AddComponent<ModelRenderer>();
			root.AddComponent<SceneInformation>().Description = "Not collected: its scene is disabled";
		}

		var prefab = builder.Create();

		Assert.AreEqual( "Health Kit", prefab.GetMetadata( "Title" ) );
		Assert.AreEqual( "Half-Life 2", prefab.GetMetadata( "Group" ) );
		Assert.IsNull( prefab.GetMetadata( "Description" ), "Components are only collected from an enabled builder scene" );
		Assert.IsNotNull( SceneUtility.GetPrefabScene( prefab ).Components.Get<ModelRenderer>() );

		PrefabBuilder.Destroy( prefab );
	}

	static System.Text.Json.Nodes.JsonObject BodyAndAttachment( Color tint )
	{
		var scene = new Scene();
		using var scope = scene.Push();

		var root = new GameObject();
		var body = root.AddComponent<SkinnedModelRenderer>();
		body.Tint = tint;

		var attachment = new GameObject( root, true, "Attachment" );
		attachment.AddComponent<SkinnedModelRenderer>().BoneMergeTarget = body;

		var json = root.Serialize();
		scene.Destroy();
		return json;
	}

	// Built the way a mount builds one from a definition: the objects are read from text inside the scope
	static PrefabFile BuildFromText( string path, Color tint, Action<GameObject> more = null )
	{
		var json = BodyAndAttachment( tint );
		var builder = new PrefabBuilder().WithName( path );

		using ( builder.Scope() )
		{
			var root = new GameObject();
			root.Deserialize( json );
			more?.Invoke( root );
		}

		return builder.Create();
	}

	static void AssertComplete( PrefabFile prefab, Color tint )
	{
		Assert.IsNotNull( prefab );

		var root = SceneUtility.GetPrefabScene( prefab );
		var body = root.Components.Get<SkinnedModelRenderer>();
		Assert.IsNotNull( body );
		Assert.AreEqual( tint, body.Tint );

		var attachment = root.Children.First( x => x.Name == "Attachment" ).Components.Get<SkinnedModelRenderer>();
		Assert.AreEqual( body, attachment.BoneMergeTarget );
	}

	[TestMethod]
	public void BuiltInsideAnotherObjectsDeserializeItKeepsItsProperties()
	{
		var plain = BuildFromText( "test/from_text.prefab", Color.Red );
		AssertComplete( plain, Color.Red );
		PrefabBuilder.Destroy( plain );

		// Leave another object's deserialize callback queued during the prefab build.
		var scene = new Scene();
		using var scope = scene.Push();
		var waiting = BodyAndAttachment( Color.Blue );

		using ( CallbackBatch.Batch() )
		{
			var other = new GameObject();
			other.Deserialize( waiting );
			Assert.AreNotEqual( Color.Blue, other.Components.Get<SkinnedModelRenderer>().Tint, "The other object's properties are still waiting for the batch" );

			var prefab = BuildFromText( "test/from_text_nested.prefab", Color.Green );
			AssertComplete( prefab, Color.Green );

			var clone = SceneUtility.GetPrefabScene( prefab ).Clone( Vector3.Zero );
			Assert.AreEqual( Color.Green, clone.Components.Get<SkinnedModelRenderer>().Tint );

			Assert.AreNotEqual( Color.Blue, other.Components.Get<SkinnedModelRenderer>().Tint, "Building it did not run the other object's callbacks early" );
			PrefabBuilder.Destroy( prefab );
		}

		Assert.AreEqual( Color.Blue, scene.Children.First().Components.Get<SkinnedModelRenderer>().Tint );
	}

	[TestMethod]
	public void ABuildThatTriggersAnotherBuildCompletesBoth()
	{
		foreach ( var insideBatch in new[] { false, true } )
		{
			var scene = new Scene();
			using var scope = scene.Push();
			var batch = insideBatch ? CallbackBatch.Batch() : null;

			PrefabFile inner = null;
			var outer = BuildFromText( "test/outer.prefab", Color.Red, root =>
			{
				// The outer prefab refers to another one that is built on demand, part way through its own build
				inner = BuildFromText( "test/inner.prefab", Color.Yellow );
				var instance = SceneUtility.GetPrefabScene( inner ).Clone( Vector3.Zero );
				instance.Name = "Inner";
				instance.Parent = root;
			} );

			AssertComplete( inner, Color.Yellow );
			AssertComplete( outer, Color.Red );

			var nested = SceneUtility.GetPrefabScene( outer ).Children.First( x => x.Name == "Inner" );
			Assert.AreEqual( Color.Yellow, nested.Components.Get<SkinnedModelRenderer>().Tint, "The inner prefab's instance inside the outer one is whole" );

			batch?.Dispose();
			PrefabBuilder.Destroy( outer );
			PrefabBuilder.Destroy( inner );
		}
	}
}
