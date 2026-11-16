using Sandbox.Rendering.Bridge;

namespace ResourceTests;

/// <summary>
/// Runtime material copies are freed by what made them at the end of the frame (<see cref="Material.ReleaseCopy"/>), and
/// Borealis's mirror forgets a material once no mesh it caches draws it.
/// </summary>
[TestClass]
public class MaterialCopyReleaseTests
{
	const string Source = "materials/dev/primary_white.vmat";

	[TestMethod]
	public void CopyIsFreedAtFrameEnd()
	{
		var source = Material.Load( Source );
		var copy = source.CreateCopy();
		Assert.IsTrue( copy.IsCopy );
		Assert.IsFalse( source.IsCopy );

		copy.ReleaseCopy();
		Assert.IsTrue( copy.IsValid, "A copy stays valid for the rest of its frame, which may still draw it" );

		EngineLoop.DrainFrameEndDisposables();
		Assert.IsFalse( copy.IsValid, "A released copy goes at frame end, not when the GC finalizes it" );

		copy.ReleaseCopy();
		EngineLoop.DrainFrameEndDisposables();
		Assert.IsFalse( copy.IsValid );

		// Loaded materials are shared: releasing one does nothing
		source.ReleaseCopy();
		EngineLoop.DrainFrameEndDisposables();
		Assert.IsTrue( source.IsValid );
		Assert.AreSame( source, Material.Load( Source ) );
	}

	/// <summary>
	/// Game code frees a copy with ReleaseCopy while a scene object still draws it: it stays valid for the rest of the frame,
	/// which may still draw it, and goes at frame end. Releasing a loaded material does nothing.
	/// </summary>
	[TestMethod]
	public void ReleasedCopyStaysValidForItsFrame()
	{
		Assert.IsTrue( typeof( Material ).GetMethod( nameof( Material.ReleaseCopy ) ).IsPublic, "Game code frees its copies" );

		var source = Material.Load( Source );
		var world = new SceneWorld();
		try
		{
			var copy = source.CreateCopy();
			var sceneModel = new SceneModel( world, "models/dev/box.vmdl", Transform.Zero );
			sceneModel.SetMaterialOverride( copy );

			using var camera = new SceneCamera { World = world };
			using var mirror = new SceneMirror( world );
			mirror.Sync( camera );
			var drawn = mirror.MaterialsDrawn;

			copy.ReleaseCopy();

			// Still this frame: a draw may use it
			Assert.IsTrue( copy.IsValid, "A released copy stays valid until frame end" );
			mirror.Sync( camera );
			Assert.AreEqual( drawn, mirror.MaterialsDrawn );

			sceneModel.Delete();
			EngineLoop.DrainFrameEndDisposables();
			Assert.IsFalse( copy.IsValid, "A released copy goes at frame end" );
			mirror.Sync( camera );
		}
		finally
		{
			world.Delete();
		}
	}

	/// <summary>
	/// A variant made for an override copy counts the copy; once the object goes and the variant has been idle long enough,
	/// the mirror drops it and forgets the copy, then the model's own mesh and its material. A copy freed while the mirror
	/// still caches a variant drawing it is forgotten safely.
	/// </summary>
	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void MirrorForgetsMaterialsNoMeshDraws( bool releaseFirst )
	{
		var world = new SceneWorld();
		var frame = Application.FrameCount;
		try
		{
			var copy = Material.Load( Source ).CreateCopy();
			var sceneModel = new SceneModel( world, "models/dev/box.vmdl", Transform.Zero );
			sceneModel.SetMaterialOverride( copy );

			using var camera = new SceneCamera { World = world };
			using var mirror = new SceneMirror( world );
			mirror.Sync( camera );

			var drawn = mirror.MaterialsDrawn;
			Assert.IsTrue( drawn >= 2, "The model's material and the copy its variant draws" );
			Assert.AreEqual( 0, mirror.MaterialsForgotten );

			sceneModel.Delete();
			if ( releaseFirst )
			{
				copy.ReleaseCopy();
				EngineLoop.DrainFrameEndDisposables();
				Assert.IsFalse( copy.IsValid );
			}

			mirror.Sync( camera );
			Assert.AreEqual( drawn, mirror.MaterialsDrawn, "An idle variant stays cached" );

			// Past the idle time: the variant goes, and its model is idle from then
			Application.FrameCount += 1000;
			mirror.Sync( camera );
			Assert.AreEqual( 1, mirror.MaterialsForgotten, "The copy only the variant drew is forgotten" );
			Assert.AreEqual( drawn - 1, mirror.MaterialsDrawn );

			Application.FrameCount += 1000;
			mirror.Sync( camera );
			Assert.AreEqual( 0, mirror.MaterialsDrawn );
			Assert.AreEqual( drawn, mirror.MaterialsForgotten );

			// Forgetting runs at frame end, after a copy freed meanwhile
			EngineLoop.DrainFrameEndDisposables();

			copy.ReleaseCopy();
			EngineLoop.DrainFrameEndDisposables();
			Assert.IsFalse( copy.IsValid );
		}
		finally
		{
			Application.FrameCount = frame;
			world.Delete();
		}
	}

	/// <summary>
	/// A line renderer makes its legacy material copy only while no material is set, and frees it when one is or when it's
	/// disabled.
	/// </summary>
	[TestMethod]
	public void LineRendererFreesItsCopy()
	{
		if ( !Graphics.IsAvailable ) Assert.Inconclusive( "Requires graphics (SBOX_TEST_GRAPHICS=1)." );

		var scene = new Scene();
		using var sceneScope = scene.Push();
		try
		{
			var line = scene.CreateObject().Components.Create<LineRenderer>( false );
			line.UseVectorPoints = true;
			line.VectorPoints = [Vector3.Zero, Vector3.Forward * 100];
			line.Enabled = true;

			var so = scene.SceneWorld.SceneObjects.OfType<SceneLineObject>().Single();
			scene.GameTick();
			var copy = so.Material;
			Assert.IsTrue( copy.IsCopy, "With no material it draws a copy of its own" );

			line.Texturing = line.Texturing with { Material = Material.Load( Source ) };
			scene.GameTick();
			Assert.AreSame( Material.Load( Source ), so.Material );
			EngineLoop.DrainFrameEndDisposables();
			Assert.IsFalse( copy.IsValid, "Setting a material frees the copy" );

			line.Texturing = line.Texturing with { Material = null };
			scene.GameTick();
			copy = so.Material;
			Assert.IsTrue( copy.IsCopy && copy.IsValid, "Clearing it makes another" );

			line.Enabled = false;
			EngineLoop.DrainFrameEndDisposables();
			Assert.IsFalse( copy.IsValid, "Disabling the component frees it" );
		}
		finally
		{
			scene.Destroy();
		}
	}
}
