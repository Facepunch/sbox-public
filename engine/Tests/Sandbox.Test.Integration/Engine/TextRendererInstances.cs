namespace EngineTests;

/// <summary>
/// A text renderer's block has its glyph instances built when its command list is built (on the main thread, as its text
/// is set), so the first view drawing it (an envmap capture face, on the render thread) doesn't build them in its frame.
/// </summary>
[TestClass]
public class TextRendererInstancesTest
{
	[TestMethod]
	public void BuildingTheCommandListBuildsTheGlyphInstances()
	{
		TestGraphics.Require();

		var world = new SceneWorld();
		try
		{
			var text = new TextRenderer.TextSceneObject( world );
			var scope = new TextRendering.Scope( $"Instances {System.Guid.NewGuid():n}", Color.White, 32 );
			text.TextScope = scope;
			text.BuildCommandList();
			var block = TextRendering.GetOrCreateTextBlock( scope, text.TextFlags );
			Assert.IsNotNull( block );
			Assert.IsTrue( block.HasInstances, "the glyph instances were left for the first draw" );
		}
		finally
		{
			world.Delete();
		}
	}
}
