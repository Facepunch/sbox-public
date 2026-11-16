using Borealis;
using Borealis.Backend;
using Borealis.Gpu;
using Sandbox;

namespace BorealisGpuTests;

/// <summary>
/// The frame buffer copy on Borealis.Gpu (<c>FrameBufferCopyTexture</c>, which refracting materials read: glass, water): what
/// the bound target holds, left readable so the draws after it bind it rather than a black stand-in.
/// </summary>
[TestClass]
[DoNotParallelize]
public class FrameBufferCopyTests : GpuTest
{
	const int Width = 64, Height = 32;

	[TestMethod]
	public void CopyHoldsTheTargetAndIsReadable()
	{
		using var backend = new BackendDevice( Device );
		var previousHost = RenderHost.Current;
		RenderHost.Current = new BackendRenderHost( backend );
		try
		{
			using var color = backend.CreateTexture( TextureDesc.Target( Width, Height, TextureFormat.RGBA16161616F ), "Frame" );
			// As RenderSystem.FrameBufferCopy makes it: every mip, writable by compute
			using var copy = backend.CreateTexture( TextureDesc.Target( Width, Height, TextureFormat.RGBA16161616F, mips: 6 ) with { Storage = true, GenerateMips = true }, "Frame buffer copy" );
			backend.Clear( color, new Color( 1, 0.5f, 0.25f, 1 ) );

			using var context = new BackendContext( backend, null );
			context.Begin();
			context.Bind( new StageTarget
			{
				Context = context,
				Attributes = context.Attributes,
				Color = color,
				Size = new Vector2( Width, Height ),
				Viewport = new Rect( 0, 0, Width, Height ),
				MaxZ = 1,
				ColorFormat = TextureFormat.RGBA16161616F,
				Samples = 1,
			} );
			context.BeginFrameBufferReads( copy, new Rect( 0, 0, Width, Height ) );
			context.CopyFrameBuffer();
			var layout = context.LayoutOf( (BackendTexture)copy );
			context.EndFrameBufferReads();
			context.Submit();
			Device.WaitIdle();

			Assert.AreEqual( ImageLayout.ShaderRead, layout, "the copy is readable by the draws after it" );
			var (handle, current) = BackendDevice.Unwrap( copy );
			var texels = ReadBack( handle, current );
			Assert.AreEqual( 1f, (float)BitConverter.ToHalf( texels, 0 ), "red copied" );
			Assert.AreEqual( 0.5f, (float)BitConverter.ToHalf( texels, 2 ), "green copied" );

			// The blurred mips refraction reads, made from it: a flat colour stays that colour
			var mip = ReadMip( handle, 3 );
			Assert.AreEqual( 0.5f, (float)BitConverter.ToHalf( mip, 2 ), 0.01f, "mip 3 made from the copy" );
			BackendDevice.SetLayout( copy, ImageLayout.CopySource );
		}
		finally
		{
			Device.WaitIdle();
			RenderHost.Current = previousHost;
		}
	}

	/// <summary>One mip of a texture left in <see cref="ImageLayout.CopySource"/> by <see cref="GpuTest.ReadBack"/>.</summary>
	byte[] ReadMip( TextureHandle texture, int mip )
	{
		var info = Device.InfoOf( texture );
		var size = Math.Max( info.Width >> mip, 1 ) * Math.Max( info.Height >> mip, 1 ) * info.Format.BytesPerBlock();
		var buffer = Device.CreateBuffer( new BufferInfo { Size = size, Memory = MemoryLocation.Readback, Name = "Mip readback" } );
		var list = Device.BeginCommandList( QueueType.Graphics, "Mip readback" );
		list.CopyTextureToBuffer( texture, mip, 0, buffer );
		list.Barrier( new BufferBarrier { Buffer = buffer, SourceStage = PipelineStage.Transfer, SourceAccess = MemoryAccess.TransferWrite, DestinationStage = PipelineStage.Host, DestinationAccess = MemoryAccess.HostRead } );
		Device.Wait( Device.Submit( list ) );
		var data = Device.Map( buffer ).ToArray();
		Device.Destroy( buffer );
		return data;
	}
}
