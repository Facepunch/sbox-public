using System;
using System.Collections.Generic;

namespace Prefab.Diff;

[TestClass]
public class PrefabInstance
{
	[TestMethod]
	[DataRow( 1, 10 )]
	[DataRow( 5, 20 )]
	[DataRow( 10, 100 )]
	[DataRow( 50, 1000 )]
	[DataRow( 50, 5000 )]
	public void InjectBasicPrefabInstancesIntoHierarchies(
		int prefabCount, int mutations )
	{
		var seed = (prefabCount << 20) | (mutations << 10);
		var random = new Random( seed );

		// Generate small initial hierarchy
		var source = GameObjectTestDataGenerator.GenerateStructured( 10, 20, seed );

		var prefabDisposables = new List<IDisposable>( prefabCount );

		// Generate a bunch of prefabs
		var prefabs = new List<string>();
		for ( int i = 0; i < prefabCount; i++ )
		{
			var resourceName = $"generated_{i}.prefab";
			var prefabJson = GameObjectTestDataGenerator.GenerateStructured( random.Int( 1, 5 ), random.Int( 2, 10 ), seed + i * i * 2556 );


			prefabDisposables.Add( Sandbox.SceneTests.Helpers.RegisterPrefabFromJson( resourceName, prefabJson.ToJsonString() ) );

			prefabs.Add( resourceName );
		}

		// Apply multiple mutations
		var target = source.DeepClone().AsObject();
		target = GameObjectTestDataGenerator.Mutate( target, mutations, seed * 1337, prefabs );

		JsonTestUtils.RunRoundTripTest( source, target, $"{prefabCount} Prefabs & {mutations} mutations", Sandbox.GameObject.DiffObjectDefinitions );

		prefabDisposables.ForEach( x => x.Dispose() );
	}

	[TestMethod]
	[DataRow( 1, 10 )]
	[DataRow( 5, 20 )]
	[DataRow( 10, 100 )]
	[DataRow( 50, 1000 )]
	[DataRow( 50, 5000 )]
	public void InjectPrefabsWithNestedPrefabsIntoHierarchy(
	int prefabCount, int mutations )
	{
		var seed = (prefabCount << 20) | (mutations << 10);
		var random = new Random( seed );

		// Generate small initial hierarchy
		var source = GameObjectTestDataGenerator.GenerateStructured( 10, 20, seed );

		var prefabDisposables = new List<IDisposable>( prefabCount );

		// Generate a bunch of prefabs
		var innerPrefabs = new List<string>();
		int prefabId = 0;
		for ( int i = 0; i < prefabCount; i++ )
		{
			var resourceName = $"generated_{prefabId}.prefab";
			var prefabJson = GameObjectTestDataGenerator.GenerateStructured( random.Int( 1, 5 ), random.Int( 2, 10 ), seed + i * i * 2556 );

			prefabDisposables.Add( Sandbox.SceneTests.Helpers.RegisterPrefabFromJson( resourceName, prefabJson.ToJsonString() ) );

			innerPrefabs.Add( resourceName );

			prefabId++;
		}

		var outerPrefabs = new List<string>();
		for ( int i = 0; i < prefabCount; i++ )
		{
			var resourceName = $"generated_{prefabId}.prefab";
			// not many components, primarily just container for previously generated prefabs.
			var outerPrefabJson = GameObjectTestDataGenerator.GenerateStructured( random.Int( 1, 4 ), random.Int( 1, 3 ), seed + i * i * 1234 );

			var combinedPrefabJson = GameObjectTestDataGenerator.Mutate( outerPrefabJson, 1, seed * 8989, innerPrefabs, GameObjectTestDataGenerator.MutationFlags.AddPrefab );
			prefabDisposables.Add( Sandbox.SceneTests.Helpers.RegisterPrefabFromJson( resourceName, combinedPrefabJson.ToJsonString() ) );

			outerPrefabs.Add( resourceName );

			prefabId++;
		}

		// Apply multiple mutations
		var target = source.DeepClone().AsObject();
		target = GameObjectTestDataGenerator.Mutate( target, mutations, seed * 1337, outerPrefabs );

		JsonTestUtils.RunRoundTripTest( source, target, $"{prefabCount} Prefabs & {mutations} mutations", Sandbox.GameObject.DiffObjectDefinitions );

		prefabDisposables.ForEach( x => x.Dispose() );
	}
}
