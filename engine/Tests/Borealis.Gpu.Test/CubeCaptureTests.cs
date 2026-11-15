using Borealis;
using Borealis.Backend;
using Borealis.Gpu;
using Sandbox;

namespace BorealisGpuTests;

/// <summary>
/// Environment map probes captured on Borealis.Gpu (<see cref="CubeCapture"/>): the faces land in their slices as the engine
/// turns its cube camera, and the filter fills every mip.
/// </summary>
[TestClass]
[DoNotParallelize]
public class CubeCaptureTests : GpuTest
{
	/// <summary>The smallest cube with the filter's seven mips.</summary>
	const int Size = 64;

	/// <summary>
	/// A lit box in front of the probe (+X, the second face's direction) shows in the middle of slice 1 and not in slice 0,
	/// which looks the other way; every mip of every face is filled from the faces, none left red or black.
	/// </summary>
	[TestMethod]
	public void FacesLandInTheirSlicesAndEveryMipIsFiltered()
	{
		using var backend = new BackendDevice( Device );
		var previousHost = RenderHost.Current;
		RenderHost.Current = new BackendRenderHost( backend );
		try
		{
			using var capture = new CubeCapture( backend, Size );
			if ( !capture.CanFilter ) Assert.Inconclusive( "envmap_filtering_cs isn't built" );

			var world = new RenderWorld();
			world.Lighting.SunDirection = new Vector3( 1, 0.2f, -0.3f ).Normal;
			world.Lighting.SunColor = new Color( 1, 1, 1 );
			world.Lighting.AmbientColor = new Color( 0.5f, 0.5f, 0.5f, 1 );
			world.Lighting.SunShadowCascades = 0;
			world.Add( new MeshObject( ObjectAttributeTests.BoxMesh( backend, backend.CreateMaterial( "Cube capture box", Color.White ) ), new Transform( new Vector3( 300, 0, 0 ) ) ) );

			using var cube = CubeCapture.CreateCube( backend, Size, "Cube capture test" );
			backend.BeginFrame();
			RenderHost.Current.NextFrame();
			capture.Render( world, Vector3.Zero, Rotation.Identity, 16, 4096, cube );
			Device.WaitIdle();

			var handle = BackendDevice.Unwrap( cube ).Texture;
			var front = Read( handle, 0, 1 );
			var back = Read( handle, 0, 0 );
			var middle = (Size / 2 * Size + Size / 2) * 4;
			Assert.IsTrue( front[middle] > 0.05f, $"the box shows in slice 1 ({front[middle]})" );
			Assert.IsTrue( back[middle] < front[middle] * 0.1f, $"slice 0 looks away from it ({back[middle]})" );

			for ( int mip = 1; mip < CubeCapture.Mips; mip++ )
			{
				for ( int face = 0; face < 6; face++ )
				{
					var texels = Read( handle, mip, face );
					var red = texels.Where( ( _, i ) => i % 4 == 0 ).Average();
					var green = texels.Where( ( _, i ) => i % 4 == 1 ).Average();
					Assert.IsTrue( red < 0.9f || green > 0.01f, $"mip {mip} of face {face} isn't the red clear" );
					Assert.IsTrue( texels.All( float.IsFinite ), $"mip {mip} of face {face} is finite" );
					Assert.AreEqual( 1f, texels[3], $"mip {mip} of face {face} was written by the filter (alpha 1)" );
				}
			}

			// The box's light reaches the filtered mips on its side
			var roughFront = Read( handle, 3, 1 );
			var roughBack = Read( handle, 3, 0 );
			Assert.IsTrue( roughFront.Average() > roughBack.Average(), "the box brightens slice 1's rough mips" );
		}
		finally
		{
			Device.WaitIdle();
			RenderHost.Current = previousHost;
		}
	}

	/// <summary>A cube's mip and face as floats, RGBA.</summary>
	float[] Read( TextureHandle texture, int mip, int face )
	{
		var size = Math.Max( Size >> mip, 1 );
		var buffer = Device.CreateBuffer( new BufferInfo { Size = size * size * 8, Memory = MemoryLocation.Readback, Name = "Cube readback" } );
		var list = Device.BeginCommandList( QueueType.Graphics, "Cube readback" );
		list.Barrier( new TextureBarrier { Texture = texture, OldLayout = ImageLayout.ShaderRead, NewLayout = ImageLayout.CopySource, SourceStage = PipelineStage.AllCommands, SourceAccess = MemoryAccess.MemoryWrite, DestinationStage = PipelineStage.Transfer, DestinationAccess = MemoryAccess.TransferRead, BaseMip = mip, MipCount = 1, BaseLayer = face, LayerCount = 1 } );
		list.CopyTextureToBuffer( texture, mip, face, buffer );
		list.Barrier( new TextureBarrier { Texture = texture, OldLayout = ImageLayout.CopySource, NewLayout = ImageLayout.ShaderRead, SourceStage = PipelineStage.Transfer, SourceAccess = MemoryAccess.TransferRead, DestinationStage = PipelineStage.AllCommands, DestinationAccess = MemoryAccess.ShaderRead, BaseMip = mip, MipCount = 1, BaseLayer = face, LayerCount = 1 } );
		list.Barrier( new BufferBarrier { Buffer = buffer, SourceStage = PipelineStage.Transfer, SourceAccess = MemoryAccess.TransferWrite, DestinationStage = PipelineStage.Host, DestinationAccess = MemoryAccess.HostRead } );
		Device.Wait( Device.Submit( list ) );

		var bytes = Device.Map( buffer ).ToArray();
		Device.Destroy( buffer );
		var floats = new float[size * size * 4];
		for ( int i = 0; i < floats.Length; i++ ) floats[i] = (float)BitConverter.ToHalf( bytes, i * 2 );
		return floats;
	}
}
