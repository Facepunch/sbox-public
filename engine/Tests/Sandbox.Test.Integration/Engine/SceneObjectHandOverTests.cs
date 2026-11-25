using System;
using Sandbox.Rendering;

namespace EngineTests;

/// <summary>
/// A scene object deleted across a hand-over (docs/reference/borealis-render-thread.md): while the render thread draws the
/// frame handed over with it, the main thread deleting it for the next frame leaves it valid, with its attributes, for that
/// frame's drawing, and its world keeps it until the frame is drawn.
/// </summary>
[TestClass, DoNotParallelize]
public class SceneObjectHandOverTests
{
	[TestMethod]
	public void AFrameHandedOverDrawsAnObjectDeletedSince()
	{
		ThreadSafe.MarkMainThread();
		var overlapping = RenderSync.Overlapping;
		var world = new SceneWorld();
		try
		{
			var custom = new SceneCustomObject( world );
			custom.Attributes.Set( "Probe", 3 );

			// A frame is handed over with it; the game deletes it (a debug overlay's one-frame object) and flushes its world
			RenderSync.Overlapping = true;
			RenderSync.HandOver();
			RenderSync.RenderThreadDrawing = true;
			custom.Delete();
			world.DeletePendingObjects();

			Assert.IsFalse( custom.IsValid(), "deleted for the main thread" );
			Assert.IsNull( custom.Attributes );
			var (valid, probe) = OnRenderThread( () => (custom.IsValid(), custom.Attributes?.GetInt( "Probe" ) ?? -1) );
			Assert.IsTrue( valid, "the frame handed over still draws it" );
			Assert.AreEqual( 3, probe, "with its attributes" );
			Assert.IsTrue( world.InternalSceneObjects.Contains( custom ), "kept in its world until the frame is drawn" );

			// The frame is drawn: the next flush lets it go
			RenderSync.RenderThreadDrawing = false;
			world.DeletePendingObjects();
			Assert.IsFalse( world.InternalSceneObjects.Contains( custom ) );
			Assert.IsFalse( OnRenderThread( () => custom.IsValid() ), "gone for later frames" );
		}
		finally
		{
			RenderSync.RenderThreadDrawing = false;
			RenderSync.Overlapping = overlapping;
			world.Delete();
		}
	}

	[TestMethod]
	public void SerialModeDeletesAtOnce()
	{
		ThreadSafe.MarkMainThread();
		var overlapping = RenderSync.Overlapping;
		var world = new SceneWorld();
		try
		{
			RenderSync.Overlapping = false;
			var custom = new SceneCustomObject( world );
			custom.Delete();
			world.DeletePendingObjects();
			Assert.IsFalse( world.InternalSceneObjects.Contains( custom ) );
			Assert.IsFalse( OnRenderThread( () => custom.IsValid() ) );
		}
		finally
		{
			RenderSync.Overlapping = overlapping;
			world.Delete();
		}
	}

	/// <summary>
	/// A terrain's normal bake queued while the render thread draws is the next frame's: that frame's height map updates are
	/// written once the frame drawing is done, so the frame drawing runs only a bake queued before it was handed over.
	/// </summary>
	[TestMethod]
	public void ATerrainBakeQueuedSinceIsTheNextFrames()
	{
		ThreadSafe.MarkMainThread();
		var overlapping = RenderSync.Overlapping;
		var world = new SceneWorld();
		try
		{
			RenderSync.Overlapping = true;
			var terrain = new TerrainClipmapSceneObject( world );
			var before = new CommandList( "bake before" );
			var since = new CommandList( "bake since" );

			terrain.QueueBake( before );
			RenderSync.HandOver();
			terrain.QueueBake( since );
			Assert.IsNull( OnRenderThread( terrain.TakeBake ), "the frame drawing leaves the next frame's bake" );

			RenderSync.HandOver();
			Assert.AreSame( since, OnRenderThread( terrain.TakeBake ), "which the next frame runs" );
			Assert.IsNull( OnRenderThread( terrain.TakeBake ), "once" );

			terrain.QueueBake( before );
			Assert.AreSame( before, terrain.TakeBake(), "a frame drawn on the main thread runs what's queued" );
		}
		finally
		{
			RenderSync.Overlapping = overlapping;
			world.Delete();
		}
	}

	/// <summary>
	/// A dynamic object rebuilt while the render thread draws (a debug overlay, a game's own lines): the frame drawing reads the
	/// vertices, primitive type and material it was handed over with, not the half-built next ones.
	/// </summary>
	[TestMethod]
	public void ADynamicObjectRebuiltSinceDrawsAsHandedOver()
	{
		ThreadSafe.MarkMainThread();
		var overlapping = RenderSync.Overlapping;
		var world = new SceneWorld();
		try
		{
			RenderSync.Overlapping = true;
			var dynamic = new SceneDynamicObject( world );
			dynamic.Init( Graphics.PrimitiveType.Triangles );
			for ( int i = 0; i < 3; i++ ) dynamic.AddVertex( new Vertex( new Vector3( i, 0, 0 ) ) );

			RenderSync.HandOver();
			dynamic.Init( Graphics.PrimitiveType.Lines );
			for ( int i = 0; i < 4; i++ ) dynamic.AddVertex( new Vertex( new Vector3( 0, i, 0 ) ) );

			var drawing = OnRenderThread( () => (dynamic.ManagedVertices.Count, dynamic.ManagedPrimitiveType) );
			Assert.AreEqual( (3, Graphics.PrimitiveType.Triangles), drawing, "the frame handed over draws what it was handed over with" );
			Assert.AreEqual( 4, dynamic.ManagedVertices.Count, "the main thread builds the next" );

			RenderSync.HandOver();
			Assert.AreEqual( (4, Graphics.PrimitiveType.Lines), OnRenderThread( () => (dynamic.ManagedVertices.Count, dynamic.ManagedPrimitiveType) ) );

			// Added to without a rebuild: the next frame keeps what was there
			dynamic.AddVertex( new Vertex( Vector3.One ) );
			Assert.AreEqual( 4, OnRenderThread( () => dynamic.ManagedVertices.Count ) );
			Assert.AreEqual( 5, dynamic.ManagedVertices.Count );
		}
		finally
		{
			RenderSync.Overlapping = overlapping;
			world.Delete();
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
