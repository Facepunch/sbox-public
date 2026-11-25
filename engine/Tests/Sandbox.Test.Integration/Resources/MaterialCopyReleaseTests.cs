namespace ResourceTests;

/// <summary>
/// Runtime material copies are freed by what made them at the end of the frame (<see cref="Material.ReleaseCopy"/>).
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

			copy.ReleaseCopy();

			// Still this frame: a draw may use it
			Assert.IsTrue( copy.IsValid, "A released copy stays valid until frame end" );

			sceneModel.Delete();
			EngineLoop.DrainFrameEndDisposables();
			Assert.IsFalse( copy.IsValid, "A released copy goes at frame end" );
		}
		finally
		{
			world.Delete();
		}
	}
}
