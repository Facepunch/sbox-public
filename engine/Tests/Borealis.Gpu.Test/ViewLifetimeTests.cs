using Borealis.Backend;
using Borealis.Gpu;
using System.Runtime.CompilerServices;

namespace BorealisGpuTests;

/// <summary>
/// A view's GPU-driven visibility resources (ids target, mask, stamps, compacted triangles) go once the view has: a game
/// that loads and closes maps makes new views each time, and kept their resources for good (~650 MiB a Construct round).
/// </summary>
[TestClass]
[DoNotParallelize]
public class ViewLifetimeTests : GpuTest
{
	[MethodImpl( MethodImplOptions.NoInlining )]
	static void UseAndDropView( BackendDevice backend )
	{
		var view = new object();
		var state = backend.VisibilityFor( view, TextureInfo.RenderTarget( 64, 32, Format.D32Float ) );
		Assert.IsTrue( state.Mask.IsValid && state.Ids.IsValid );
	}

	[TestMethod]
	public void GoneViewsFreeTheirVisibility()
	{
		using var backend = new BackendDevice( Device );
		backend.BeginFrame();
		var (buffers, textures) = (Device.Buffers.Count, Device.Textures.Count);

		UseAndDropView( backend );
		Assert.IsTrue( Device.Buffers.Count > buffers, "the view made buffers" );

		GC.Collect();
		GC.WaitForPendingFinalizers();
		for ( int i = 0; i <= Device.FramesInFlight; i++ ) backend.BeginFrame();

		Assert.AreEqual( 1, backend.VisibilityViewsFreed );
		Assert.AreEqual( buffers, Device.Buffers.Count, "buffers back to before" );
		Assert.AreEqual( textures, Device.Textures.Count, "textures back to before" );
	}
}
