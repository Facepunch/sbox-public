namespace SceneTests.GameObjects;

public partial class PrefabInstanceTest
{
	[TestMethod]
	public void PlantedDiffDefinitionIsNotUsedByEngine()
	{
		var calls = 0;
		var planted = new Json.TrackedObjectDefinition
		{
			Type = "Planted",
			AllowedAsRoot = true,
			MatchScore = _ => { calls++; return 0f; },
			ToId = _ => { calls++; return null; },
		};

		GameObject.DiffObjectDefinitions.Add( planted );

		try
		{
			using var registration = RegisterBasicPrefab( out var prefabScene );

			var scene = new Scene();
			using var sceneScope = scene.Push();

			var instance = prefabScene.Clone();
			var comp = instance.Components.Get<PrefabInstanceStatComponent>();

			comp.Number = 42;
			instance.PrefabInstance.RefreshPatch();

			Assert.IsTrue( instance.PrefabInstance.IsPropertyOverridden( comp, "Number" ) );
			Assert.AreEqual( 0, calls );
		}
		finally
		{
			GameObject.DiffObjectDefinitions.Remove( planted );
		}
	}
}
