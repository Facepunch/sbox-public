using Borealis.Gpu;
using BorealisGpuSample;

namespace BorealisGpuTests;

/// <summary>
/// Material objects (<see cref="Borealis.Backend.BackendDevice.MaterialObjects"/>) against binding by reflection every draw.
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
		using var scene = new BorealisScene( Device ) { GpuDriven = false };

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

		// Borealis's per-frame buffers (transforms, lights) go round a ring of three, so sets exist once per ring slot
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
		using var scene = new BorealisScene( Device ) { GpuDriven = false };
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

	/// <summary>
	/// Changing a material's value (as <c>Material.Set</c> does) draws it with the new value on the next frame, without
	/// anything invalidating it by hand; setting the old value back draws the first frame again.
	/// </summary>
	[TestMethod]
	public void ChangedMaterialsDrawTheirNewValues()
	{
		const int width = 320, height = 200;
		using var scene = new BorealisScene( Device );
		scene.Backend.MaterialObjects = true;

		byte[] Frame()
		{
			Device.BeginFrame();
			var (texture, layout, color) = scene.Render( width, height, 1.0f );
			Device.WaitIdle();
			return Read( texture, layout, color );
		}

		var before = Frame();
		var material = scene.Backend.MaterialCache.Materials.First( m => m.Value( "g_vColorTint" ) is not null );
		var tint = material.Value( "g_vColorTint" );
		var objects = scene.Backend.MaterialObjectStats.Objects;

		material.Set( "g_vColorTint", [0, 1, 0, 0] );
		var changed = Frame();
		material.Set( "g_vColorTint", tint );
		var restored = Frame();

		Assert.IsTrue( Different( before, changed ) > 0, $"{material.Name} drew with its new tint" );
		CollectionAssert.AreEqual( before, restored, "setting the old tint back draws the first frame again" );
		Assert.AreEqual( objects, scene.Backend.MaterialObjectStats.Objects, "the changed material's objects were made again, not added to" );
	}

	/// <summary>
	/// Recording the frame's segments on several threads, each into a list of its own with layouts tracked per list and
	/// fixed up at submission, draws the pixels recording on one thread does, through the same material objects.
	/// </summary>
	[TestMethod]
	public void ParallelRecordingDrawsTheSamePixels()
	{
		const int width = 640, height = 400;
		using var scene = new BorealisScene( Device ) { GpuDriven = false };
		scene.Backend.MaterialObjects = true;

		(byte[] Pixels, double WorkerMs) Frame( bool parallel )
		{
			scene.System.ParallelRecording = parallel;
			Device.BeginFrame();
			var (texture, layout, color) = scene.Render( width, height, 1.0f );
			var workers = scene.Stats.WorkerRecordMs;
			Device.WaitIdle();
			return (Read( texture, layout, color ), workers);
		}

		var serial = Frame( false );
		var parallel = Frame( true );
		var again = Frame( true );
		var serialAgain = Frame( false );

		Assert.IsTrue( parallel.WorkerMs > 0 && again.WorkerMs > 0, "segments recorded on workers" );
		CollectionAssert.AreEqual( serial.Pixels, serialAgain.Pixels, "serial recording draws the same frame twice" );
		Assert.AreEqual( 0, Different( serial.Pixels, parallel.Pixels ), "parallel recording draws what serial recording does" );
		CollectionAssert.AreEqual( parallel.Pixels, again.Pixels, "parallel recording draws the same frame twice" );
	}

	/// <summary>
	/// A frame recorded on several threads, a list per segment, reaches the GPU as one submission once the render system
	/// has submitted it, and draws what a submission per list does.
	/// </summary>
	[TestMethod]
	public void ParallelRecordingSubmitsTheFrameOnce()
	{
		const int width = 640, height = 400;
		using var scene = new BorealisScene( Device );
		scene.Backend.MaterialObjects = true;
		scene.System.ParallelRecording = true;

		(byte[] Pixels, long Submissions) Frame( bool batch )
		{
			scene.Backend.BatchSubmits = batch;
			Device.BeginFrame();
			var before = Device.Submissions;
			var (texture, layout, color) = scene.Render( width, height, 1.0f );
			var submissions = Device.Submissions - before;
			Device.WaitIdle();
			return (Read( texture, layout, color ), submissions);
		}

		var separate = Frame( false );
		var batched = Frame( true );
		var again = Frame( true );

		Assert.IsTrue( separate.Submissions > 2, $"recording on workers made {separate.Submissions} lists" );
		Assert.AreEqual( 1, again.Submissions, "a warm frame went in one submission" );
		Assert.AreEqual( 0, Different( separate.Pixels, batched.Pixels ), "one submission draws what a submission per list does" );
		CollectionAssert.AreEqual( batched.Pixels, again.Pixels );
	}

	/// <summary>
	/// A frame recorded on workers draws the same with exact barriers as with ones that wait for everything, and with
	/// rendering scopes carried across lists as without, batched or submitted a list at a time, and no list is added to pair
	/// scopes. (This scene's layers don't split inside a view's work, so nothing resumes here: see
	/// <c>DeviceTests.SuspendedScopeResumesInTheNextList</c>.)
	/// </summary>
	[TestMethod]
	public void ParallelRecordingDrawsTheSameWithExactBarriersAndCarriedScopes()
	{
		const int width = 640, height = 400;
		using var scene = new BorealisScene( Device );
		scene.Backend.MaterialObjects = true;
		scene.System.ParallelRecording = true;

		(byte[] Pixels, long Pairings) Frame( bool carry, bool exact, bool batch = true )
		{
			scene.Backend.CarryScopes = carry;
			scene.Backend.ExactBarriers = exact;
			scene.Backend.BatchSubmits = batch;
			Device.BeginFrame();
			var pairings = Device.ScopePairings;
			var (texture, layout, color) = scene.Render( width, height, 1.0f );
			Device.Flush();
			pairings = Device.ScopePairings - pairings;
			Device.WaitIdle();
			return (Read( texture, layout, color ), pairings);
		}

		// A scene's first frame isn't compared: it can differ from the ones after
		Frame( carry: false, exact: false );
		var full = Frame( carry: false, exact: false );
		var exact = Frame( carry: false, exact: true );
		var carried = Frame( carry: true, exact: true );
		var separate = Frame( carry: true, exact: true, batch: false );

		Assert.AreEqual( 0, carried.Pairings, "no list was added to pair scopes" );
		Assert.AreEqual( 0, Different( full.Pixels, exact.Pixels ), "exact barriers draw what full ones do" );
		Assert.AreEqual( 0, Different( full.Pixels, carried.Pixels ), "scopes carried across lists draw what a scope per list does" );
		Assert.AreEqual( 0, Different( full.Pixels, separate.Pixels ), "a submission per list draws the same" );
	}

	/// <summary>Read a frame back, and tell the backend where that left its target.</summary>
	byte[] Read( TextureHandle texture, ImageLayout layout, Borealis.GpuTexture color )
	{
		var pixels = ReadBack( texture, layout );
		Borealis.Backend.BackendDevice.SetLayout( color, ImageLayout.CopySource );
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
