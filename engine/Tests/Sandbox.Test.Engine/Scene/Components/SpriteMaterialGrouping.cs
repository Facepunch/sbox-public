namespace SceneTests.Components;

/// <summary>
/// Sprites are collected into instanced batches keyed by their render state. A material has to
/// take part in that key, otherwise two sprites drawn with different materials would be uploaded
/// into the same batch and draw with whichever material the batch was created with.
/// </summary>
[TestClass]
[DoNotParallelize]
public class SpriteMaterialGroupingTest : SceneTest
{
	sealed class TestSpriteGroup : ISpriteRenderGroup
	{
		public bool Opaque => false;
		public bool Additive => false;
		public bool Shadows => false;
		public bool IsSorted => false;
	}

	[TestMethod]
	public void SpritesShareABatchPerMaterial()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var group = new TestSpriteGroup();
		var tags = scene.CreateObject().Tags as GameTags;
		var renderOptions = new RenderOptions( null );

		var noMaterial = SceneSpriteSystem.GetRenderGroupKey( group, tags, renderOptions );
		var materialA = SceneSpriteSystem.GetRenderGroupKey( group, tags, renderOptions, default, 111 );
		var materialAAgain = SceneSpriteSystem.GetRenderGroupKey( group, tags, renderOptions, default, 111 );
		var materialB = SceneSpriteSystem.GetRenderGroupKey( group, tags, renderOptions, default, 222 );

		Assert.AreEqual( noMaterial, SceneSpriteSystem.GetRenderGroupKey( group, tags, renderOptions ), "Sprites without a material keep sharing a batch" );
		Assert.AreEqual( materialA, materialAAgain, "Sprites with the same material share a batch" );
		Assert.AreNotEqual( noMaterial, materialA, "A material cannot share a batch with the default sprite shader" );
		Assert.AreNotEqual( materialA, materialB, "Different materials cannot share a batch" );
	}
}
