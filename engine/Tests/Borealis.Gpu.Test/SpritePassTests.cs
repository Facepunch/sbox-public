using Borealis.Backend;
using Borealis.Gpu;

namespace BorealisGpuTests;

/// <summary>
/// sprite_cs's module keeps the last group's spare threads inside its buffers, checked on the GPU by GPU-assisted
/// validation: Borealis.Gpu's device has no robust buffer access, so a read or write past a buffer's end is undefined there
/// (Source 2's device makes it defined).
/// </summary>
[TestClass]
[DoNotParallelize]
public class SpritePassTests : GpuTest
{
	protected override bool GpuAssistedValidation => true;

	/// <summary>
	/// One sprite in buffers sized as a sprite batch's smallest (the distances 64 bytes): the group's other 63 threads read
	/// and write nothing, and the sprite's own distance and copy are written.
	/// </summary>
	[TestMethod]
	public void SpareThreadsStayInsideTheBuffers()
	{
		using var backend = new BackendDevice( Device );
		var errors = new List<string>();
		backend.Passes.Error = e => errors.Add( e );
		using var drawer = new ShaderDrawer( backend );
		var sprite = backend.Shaders.Load( "shaders/sprite/sprite_cs.shader" );
		if ( sprite is null ) Assert.Inconclusive( "sprite_cs isn't compiled" );

		// A buffer holds one sprite, whatever its stride (under 256 bytes): its position first, 10 units along x
		var sprites = Device.CreateBuffer( new BufferInfo { Size = 256, Usage = BufferUsage.Storage, Name = "Sprites" } );
		var spritesOut = Device.CreateBuffer( new BufferInfo { Size = 256, Usage = BufferUsage.Storage, Name = "Sprites out" } );
		var distances = Device.CreateBuffer( new BufferInfo { Size = 64, Usage = BufferUsage.Storage | BufferUsage.CopySource, Name = "Distances" } );
		var counter = Device.CreateBuffer( new BufferInfo { Size = 4, Usage = BufferUsage.Storage, Name = "Counter" } );
		var data = new float[64];
		data[0] = 10;
		Device.WriteBuffer<float>( sprites, data );
		Device.WriteBuffer<float>( distances, new float[16] );
		Device.WriteBuffer<uint>( counter, [0] );

		var view = new Borealis.Gpu.ViewConstants { CameraDirWs = new System.Numerics.Vector3( 1, 0, 0 ), CameraUpDirWs = new System.Numerics.Vector3( 0, 0, 1 ) };
		var viewBytes = new byte[Borealis.Gpu.ViewConstants.NativeSize];
		System.Runtime.InteropServices.MemoryMarshal.Write( viewBytes, in view );

		var attributes = new ShaderAttributes();
		attributes.SetData( "PerViewConstantBuffer_t", viewBytes );
		attributes.Set( "Sprites", sprites );
		attributes.Set( "SpriteBufferOut", spritesOut );
		attributes.Set( "DistanceBuffer", distances );
		attributes.Set( "AtomicCounter", counter );
		attributes.Set( "SpriteCount", 1 );
		attributes.Set( "CameraPosition", 0, 0, 0 );

		Device.BeginFrame();
		var list = Device.BeginCommandList( QueueType.Graphics, "Sprites" );
		Assert.IsTrue( drawer.Dispatch( list, sprite, attributes, 1, 1, 1 ), "sprite_cs dispatches" );
		var readback = Device.CreateBuffer( new BufferInfo { Size = 64, Memory = MemoryLocation.Readback, Name = "Distances readback" } );
		list.Barrier( PipelineStage.ComputeShader, MemoryAccess.ShaderWrite, PipelineStage.Transfer, MemoryAccess.TransferRead );
		list.CopyBuffer( distances, 0, readback, 0, 64 );
		Device.Wait( Device.Submit( list ) );
		Device.WaitIdle();

		var reported = TakeValidationErrors();
		Assert.AreEqual( 0, reported.Length, "GPU-assisted validation saw an access past a buffer: " + string.Join( ", ", reported ) );
		Assert.AreEqual( 0, errors.Count, string.Join( "\n", errors ) );
		var written = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, float>( Device.Map( readback )[..64] ).ToArray();
		Assert.AreEqual( 100f, written[0], "the sprite's squared distance" );
		Assert.IsTrue( written[1..].All( d => d == float.MaxValue ), "the spare threads inside the buffer mark theirs as far" );

		foreach ( var buffer in (ReadOnlySpan<BufferHandle>)[sprites, spritesOut, distances, counter, readback] ) Device.Destroy( buffer );
	}
}
