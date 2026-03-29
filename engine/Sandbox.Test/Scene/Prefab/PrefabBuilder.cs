using Sandbox.Mounting;
using System;

namespace Prefab;

[TestClass]
public class PrefabBuilderTests
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
}
