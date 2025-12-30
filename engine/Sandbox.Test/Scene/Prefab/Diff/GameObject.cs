namespace Prefab.Diff;

[TestClass]
public class GeneratedGameObject
{
	[TestMethod]
	[DataRow( 2, 1, 1 )]
	[DataRow( 2, 2, 5 )]
	[DataRow( 3, 2, 1 )]
	[DataRow( 3, 3, 5 )]
	[DataRow( 5, 3, 1 )]
	[DataRow( 5, 5, 5 )]
	[DataRow( 10, 5, 1 )]
	[DataRow( 10, 10, 5 )]
	[DataRow( 10, 10, 10 )]
	[DataRow( 20, 20, 20 )]
	[DataRow( 20, 20, 20 )]
	[DataRow( 30, 30, 30 )]
	[DataRow( 40, 40, 40 )]
	[DataRow( 50, 50, 50 )]
	[DataRow( 60, 60, 60 )]
	[DataRow( 70, 70, 70 )]
	[DataRow( 80, 80, 80 )]
	[DataRow( 90, 90, 90 )]
	[DataRow( 100, 100, 100 )]
	[DataRow( 100, 200, 200 )]
	[DataRow( 100, 300, 300 )]
	[DataRow( 100, 400, 400 )]
	[DataRow( 100, 500, 500 )]
	[DataRow( 5000, 5000, 5000 )] // stress test let's go
	public void StructuredGameObjects(
		int gameObjectCount, int componentCount, int mutationCount )
	{
		var seed = (gameObjectCount << 20) | (componentCount << 10) | mutationCount;
		var source = GameObjectTestDataGenerator.GenerateStructured( gameObjectCount, componentCount, seed );

		// Apply multiple mutations
		var target = source.DeepClone().AsObject();
		target = GameObjectTestDataGenerator.Mutate( target, mutationCount, seed + 1 );

		JsonTestUtils.RunRoundTripTest( source, target, $"GameObject hierarchy with {gameObjectCount} GameObjects, {componentCount} Components & {mutationCount} mutations", Sandbox.GameObject.DiffObjectDefinitions );
	}
}
