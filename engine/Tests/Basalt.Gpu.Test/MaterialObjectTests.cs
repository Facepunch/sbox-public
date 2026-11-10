using Basalt.Gpu;
using BasaltGpuSample;

namespace BasaltGpuTests;

/// <summary>
/// Material objects (<see cref="Basalt.Backend.BackendDevice.MaterialObjects"/>) against binding by reflection every draw.
/// </summary>
[TestClass]
[DoNotParallelize]
public class MaterialObjectTests : GpuTest
{
	/// <summary>
	/// The sample's world, depth prepass, shadow maps and forward, draws the same pixels through material objects as through
	/// binding every draw by reflection, and once the per-frame buffers have gone round their ring, frames write no
	/// descriptor sets.
	/// </summary>
	[TestMethod]
	public void DrawTheSamePixelsAsBindingByReflection()
	{
		const int width = 640, height = 400;
		using var scene = new BasaltScene( Device );

		byte[] Frame( bool materialObjects )
		{
			scene.Backend.MaterialObjects = materialObjects;
			Device.BeginFrame();
			var (texture, layout, color) = scene.Render( width, height, 1.0f );
			Device.WaitIdle();
			return Read( texture, layout, color );
		}

		var reflection = Frame( false );
		var objects = Frame( true );

		// Basalt's per-frame buffers (transforms, lights) go round a ring of three, so sets exist once per ring slot
		Frame( true );
		Frame( true );
		var written = scene.Backend.MaterialObjectStats.SetsWritten;
		var again = Frame( true );
		var steady = scene.Backend.MaterialObjectStats;
		var reflectionAgain = Frame( false );

		CollectionAssert.AreEqual( reflection, reflectionAgain, "binding by reflection draws the same frame twice" );
		Assert.IsTrue( steady.Objects > 0, "material objects drew" );
		Assert.AreEqual( Different( reflection, objects ), 0, "material objects draw the pixels binding by reflection does" );
		CollectionAssert.AreEqual( objects, again, "material objects draw the same frame twice" );
		Assert.AreEqual( written, steady.SetsWritten, "a warm frame writes no descriptor sets" );
	}

	/// <summary>
	/// Invalidating a material drops its objects, and the next frame makes them again with the same result.
	/// </summary>
	[TestMethod]
	public void InvalidatedMaterialsAreMadeAgain()
	{
		const int width = 320, height = 200;
		using var scene = new BasaltScene( Device );
		scene.Backend.MaterialObjects = true;

		Device.BeginFrame();
		var (texture, layout, color) = scene.Render( width, height, 1.0f );
		Device.WaitIdle();
		var before = Read( texture, layout, color );
		var objects = scene.Backend.MaterialObjectStats.Objects;

		foreach ( var material in scene.Backend.MaterialCache.Materials ) scene.Backend.InvalidateMaterial( material );
		Assert.IsTrue( scene.Backend.MaterialObjectStats.Objects < objects, "invalidating dropped objects" );

		Device.BeginFrame();
		(texture, layout, color) = scene.Render( width, height, 1.0f );
		Device.WaitIdle();
		CollectionAssert.AreEqual( before, Read( texture, layout, color ) );
		Assert.AreEqual( objects, scene.Backend.MaterialObjectStats.Objects, "the objects were made again" );
	}

	/// <summary>Read a frame back, and tell the backend where that left its target.</summary>
	byte[] Read( TextureHandle texture, ImageLayout layout, Basalt.GpuTexture color )
	{
		var pixels = ReadBack( texture, layout );
		Basalt.Backend.BackendDevice.SetLayout( color, ImageLayout.CopySource );
		return pixels;
	}

	static int Different( byte[] a, byte[] b )
	{
		Assert.AreEqual( a.Length, b.Length );
		var count = 0;
		for ( int i = 0; i < a.Length; i++ )
		{
			if ( a[i] != b[i] ) count++;
		}

		return count;
	}
}
