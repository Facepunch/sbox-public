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
		var materialA = SceneSpriteSystem.GetRenderGroupKey( group, tags, renderOptions, default, 1 );
		var materialB = SceneSpriteSystem.GetRenderGroupKey( group, tags, renderOptions, default, 2 );

		Assert.AreEqual( noMaterial, SceneSpriteSystem.GetRenderGroupKey( group, tags, renderOptions ), "Sprites without a material keep sharing a batch" );
		Assert.AreEqual( materialA, SceneSpriteSystem.GetRenderGroupKey( group, tags, renderOptions, default, 1 ), "Sprites with the same material share a batch" );
		Assert.AreNotEqual( noMaterial, materialA, "A material can't share a batch with the built-in sprite material" );
		Assert.AreNotEqual( materialA, materialB, "Different materials can't share a batch" );
	}

	[TestMethod]
	public void NoMaterialHasNoMaterialId()
	{
		Assert.AreEqual( 0, SceneSpriteSystem.GetMaterialId( null ) );
	}
}
