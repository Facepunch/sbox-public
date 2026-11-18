using NativeEngine;

namespace BorealisTests;

/// <summary>
/// On Borealis.Gpu (-borealisgpu) the multisampling the game window and the settings get is what the Borealis.Gpu device
/// supports (<see cref="BorealisGpuRenderBackend.BestMultisampleType(RenderMultisampleType, int)"/>), not what Source 2's
/// empty render system says (none): a game whose settings ask for 4x renders with 4x, as on native.
/// </summary>
[TestClass]
public class MultisampleTypeTests
{
	[TestMethod]
	public void BestMultisampleTypeIsTheMostTheDeviceSupportsUpToTheAsk()
	{
		const int Upto8 = 1 | 2 | 4 | 8, Upto4 = 1 | 2 | 4, Single = 1;
		Assert.AreEqual( RenderMultisampleType.RENDER_MULTISAMPLE_4X, BorealisGpuRenderBackend.BestMultisampleType( RenderMultisampleType.RENDER_MULTISAMPLE_4X, Upto8 ) );
		Assert.AreEqual( RenderMultisampleType.RENDER_MULTISAMPLE_2X, BorealisGpuRenderBackend.BestMultisampleType( RenderMultisampleType.RENDER_MULTISAMPLE_2X, Upto8 ) );
		Assert.AreEqual( RenderMultisampleType.RENDER_MULTISAMPLE_8X, BorealisGpuRenderBackend.BestMultisampleType( RenderMultisampleType.RENDER_MULTISAMPLE_16X, Upto8 ), "16x draws 8 samples" );
		Assert.AreEqual( RenderMultisampleType.RENDER_MULTISAMPLE_4X, BorealisGpuRenderBackend.BestMultisampleType( RenderMultisampleType.RENDER_MULTISAMPLE_8X, Upto4 ) );
		Assert.AreEqual( RenderMultisampleType.RENDER_MULTISAMPLE_NONE, BorealisGpuRenderBackend.BestMultisampleType( RenderMultisampleType.RENDER_MULTISAMPLE_4X, Single ) );
		Assert.AreEqual( RenderMultisampleType.RENDER_MULTISAMPLE_NONE, BorealisGpuRenderBackend.BestMultisampleType( RenderMultisampleType.RENDER_MULTISAMPLE_NONE, Upto8 ) );
	}
}
