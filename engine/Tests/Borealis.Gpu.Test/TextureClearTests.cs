using Borealis;
using Borealis.Backend;
using Borealis.Gpu;

namespace BorealisGpuTests;

/// <summary>
/// <see cref="BackendDevice.Clear"/>: a texture cleared on its own, or inside a <see cref="Device.DeferUploads"/> scope (the
/// mirror applying a frame's changes, which clears a map's new capture cubes), holds the colour for work submitted after.
/// </summary>
[TestClass]
public class TextureClearTests : GpuTest
{
	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void ClearedTextureReadsBackItsColour( bool deferred )
	{
		using var backend = new BackendDevice( Device );
		using var texture = backend.CreateTexture( TextureDesc.Target( 8, 8, TextureFormat.RGBA8888 ), "Cleared" );
		using ( deferred ? Device.DeferUploads() : default )
		{
			backend.Clear( texture, new Color( 1, 0.5f, 0, 1 ) );
			Assert.AreEqual( deferred, Device.DeferredList is not null, "the clear went with the scope's list" );
		}

		var (handle, layout) = BackendDevice.Unwrap( texture );
		var texels = ReadBack( handle, layout );
		Assert.AreEqual( (byte)255, texels[0], "red" );
		Assert.IsTrue( Math.Abs( texels[1] - 128 ) <= 1, $"green {texels[1]}" );
		Assert.AreEqual( (byte)0, texels[2], "blue" );
		BackendDevice.SetLayout( texture, ImageLayout.CopySource );
	}
}
