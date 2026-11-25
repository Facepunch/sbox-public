using System;
using Sandbox.Rendering;

namespace EngineTests;

/// <summary>
/// A sprite batch across a hand-over (docs/reference/borealis-render-thread.md): while a frame handed over may be drawing on
/// the render thread, the batch growing or going away for the next frame leaves that frame its own buffers, drawn as uploaded,
/// and disposes them once the next frame is handed over.
/// </summary>
[TestClass, DoNotParallelize]
public class SpriteBatchHandOverTests
{
	[TestMethod]
	public void AFrameHandedOverKeepsItsBuffersWhenTheBatchGrows()
	{
		if ( !Graphics.IsAvailable ) Assert.Inconclusive( "Requires graphics." );

		var overlapping = RenderSync.Overlapping;
		var scene = new Scene();
		using var sceneScope = scene.Push();
		try
		{
			scene.CreateObject().Components.Create<SpriteRenderer>();
			scene.GameTick();
			var batch = scene.SceneWorld.SceneObjects.OfType<SpriteBatchSceneObject>().Single();

			// A frame is handed over with one sprite; the game then adds enough for the batch to grow its buffers
			RenderSync.Overlapping = true;
			RenderSync.HandOver();
			var handedOver = batch.DrawnForTests;
			Assert.AreEqual( 1, handedOver.SpriteCount );

			for ( int i = 0; i < 40; i++ ) scene.CreateObject().Components.Create<SpriteRenderer>();
			scene.GameTick();

			var drawing = OnRenderThread( () => batch.DrawnForTests );
			Assert.AreSame( handedOver.Sprites, drawing.Sprites, "the frame handed over draws with the buffer it was recorded with" );
			Assert.AreEqual( 1, drawing.SpriteCount, "and the sprites it was handed over with" );
			Assert.IsTrue( handedOver.Sprites.IsValid, "which isn't disposed under it" );
			Assert.AreNotSame( handedOver.Sprites, batch.DrawnForTests.Sprites, "the next frame uploads into a bigger one" );
			Assert.AreEqual( 41, batch.DrawnForTests.SpriteCount );
			Assert.IsTrue( SpriteBatchSceneObject.RetiredCount > 0 );

			// The next hand-over: the old frame is done with them
			RenderSync.HandOver();
			scene.GameTick();
			Assert.IsFalse( handedOver.Sprites.IsValid, "disposed once no frame draws with it" );
			Assert.AreEqual( 0, SpriteBatchSceneObject.RetiredCount );
			Assert.AreEqual( 41, OnRenderThread( () => batch.DrawnForTests ).SpriteCount );
		}
		finally
		{
			RenderSync.Overlapping = overlapping;
			scene.Destroy();
			SpriteBatchSceneObject.DisposeRetired();
		}
	}

	static T OnRenderThread<T>( Func<T> read )
	{
		RenderSync.MarkRenderThread( true );
		try
		{
			return read();
		}
		finally
		{
			RenderSync.MarkRenderThread( false );
		}
	}
}
