using Borealis.Gpu;

namespace BorealisGpuTests;

/// <summary>
/// Volume textures (<see cref="TextureDimension.Texture3D"/>): uploads and read-backs move every slice of a mip, as a
/// light probe volume's texture needs.
/// </summary>
[TestClass]
public class VolumeTextureTests : GpuTest
{
	/// <summary>
	/// A 4x4x6 RGBA8 volume written whole reads back whole, slice by slice; its second mip too. Before, only the first
	/// slice was copied and the rest of a probe volume read as black.
	/// </summary>
	[TestMethod]
	public void EverySliceUploadsAndReadsBack()
	{
		const int Size = 4, Depth = 6;
		var texture = Device.CreateTexture( new TextureInfo
		{
			Width = Size,
			Height = Size,
			Depth = Depth,
			Layers = 1,
			Mips = 2,
			Samples = 1,
			Dimension = TextureDimension.Texture3D,
			Format = Format.RGBA8Unorm,
			Usage = TextureUsage.Sampled | TextureUsage.CopyDestination | TextureUsage.CopySource,
			Name = "Volume",
		} );

		for ( int mip = 0; mip < 2; mip++ )
		{
			int size = Size >> mip, depth = Depth >> mip;
			var data = new byte[size * size * depth * 4];
			for ( int i = 0; i < data.Length; i++ ) data[i] = (byte)(i / (size * size * 4) * 40 + mip * 7 + 1);
			Device.WriteTexture( texture, data, mip );

			var read = ReadVolume( texture, mip, data.Length );
			CollectionAssert.AreEqual( data, read, $"mip {mip}: every slice" );
		}

		Device.Destroy( texture );
	}

	/// <summary>
	/// A volume made through Borealis.Backend with initial pixels (the opaque black 2x2x2 fog fallback is one) holds them in
	/// every slice, ready to sample. Before, a volume's initial data was dropped and the texture left undefined.
	/// </summary>
	[TestMethod]
	public void BackendVolumeKeepsItsInitialData()
	{
		const int Size = 4, Depth = 6;
		var data = new byte[Size * Size * Depth * 4];
		for ( int i = 0; i < data.Length; i++ ) data[i] = (byte)(i / (Size * Size * 4) * 40 + i % 4 + 1);

		using var backend = new Borealis.Backend.BackendDevice( Device );
		var texture = (Borealis.Backend.BackendTexture)backend.CreateTexture( Borealis.TextureDesc.Volume( Size, Size, Depth, Borealis.TextureFormat.RGBA8888 ) with { Data = data }, "Volume with data" );
		Assert.AreEqual( ImageLayout.ShaderRead, texture.Layout, "ready to sample" );
		CollectionAssert.AreEqual( data, ReadVolume( texture.Handle, 0, data.Length ), "every slice" );
		texture.Dispose();
	}

	byte[] ReadVolume( TextureHandle texture, int mip, int bytes )
	{
		var buffer = Device.CreateBuffer( new BufferInfo { Size = bytes, Memory = MemoryLocation.Readback, Name = "Readback" } );
		var list = Device.BeginCommandList( QueueType.Graphics, "Read back" );
		list.Barrier( new TextureBarrier
		{
			Texture = texture,
			OldLayout = ImageLayout.ShaderRead,
			NewLayout = ImageLayout.CopySource,
			SourceStage = PipelineStage.AllCommands,
			SourceAccess = MemoryAccess.MemoryWrite,
			DestinationStage = PipelineStage.Transfer,
			DestinationAccess = MemoryAccess.TransferRead,
			BaseMip = mip,
			MipCount = 1,
			LayerCount = 1,
		} );
		list.CopyTextureToBuffer( texture, mip, 0, buffer );
		list.Barrier( BackToShaderRead( texture, mip ) );
		list.Barrier( new BufferBarrier { Buffer = buffer, SourceStage = PipelineStage.Transfer, SourceAccess = MemoryAccess.TransferWrite, DestinationStage = PipelineStage.Host, DestinationAccess = MemoryAccess.HostRead } );
		Device.Wait( Device.Submit( list ) );
		var data = Device.Map( buffer ).ToArray();
		Device.Destroy( buffer );
		return data;
	}

	static TextureBarrier BackToShaderRead( TextureHandle texture, int mip ) => new()
	{
		Texture = texture,
		OldLayout = ImageLayout.CopySource,
		NewLayout = ImageLayout.ShaderRead,
		SourceStage = PipelineStage.Transfer,
		SourceAccess = MemoryAccess.TransferRead,
		DestinationStage = PipelineStage.AllCommands,
		DestinationAccess = MemoryAccess.ShaderRead,
		BaseMip = mip,
		MipCount = 1,
		LayerCount = 1,
	};
}
