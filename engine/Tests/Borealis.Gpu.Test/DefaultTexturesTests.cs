using Borealis.Gpu;
using Borealis.Source2.Shaders;

namespace BorealisGpuTests;

/// <summary>
/// A storage binding nothing fills gets a default of its own, kept in the general layout, not the black texture sampled
/// bindings read: a compute shader writing through it changed that black, and bound it in a layout it wasn't in.
/// </summary>
[TestClass]
public class DefaultTexturesTests : GpuTest
{
	[TestMethod]
	public void StorageDefaultsAreTheirOwn()
	{
		using var defaults = new ShaderDefaults( Device );
		foreach ( var dimension in (SpirvImageDimension[])[SpirvImageDimension.Texture2D, SpirvImageDimension.Texture3D, SpirvImageDimension.Texture2DArray] )
		{
			var storage = defaults.StorageTexture( dimension );
			Assert.AreNotEqual( defaults.Texture( dimension ), storage, $"{dimension}" );
			Assert.AreEqual( storage, defaults.StorageTexture( dimension ), "made once" );
			Assert.IsTrue( Device.InfoOf( storage ).Usage.HasFlag( TextureUsage.Storage ) );
		}
	}
}
