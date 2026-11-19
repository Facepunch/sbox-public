using Borealis.Gpu;
using BorealisGpuSample;

namespace BorealisGpuTests;

/// <summary>
/// Indirect draws as the GPU-driven lists record them: a plain multi-draw after a counted one in the same command list
/// draws all its commands, whatever count the earlier one read. Lavapipe's own plain indirect draws keep the earlier
/// count buffer; the device issues them counted there (Device.IndirectCounts).
/// </summary>
[TestClass]
[DoNotParallelize]
public class IndirectDrawTests : GpuTest
{
	const int Width = 64, Height = 64;

	static byte[] Load( string name )
	{
		using var stream = typeof( SampleScene ).Assembly.GetManifestResourceStream( name );
		var bytes = new byte[stream.Length];
		stream.ReadExactly( bytes );
		return bytes;
	}

	[TestMethod]
	[DataRow( false, true )]
	[DataRow( true, true )]
	[DataRow( true, false )]
	public void APlainMultiDrawAfterACountedOneDrawsAllItsCommands( bool counted, bool indexed )
	{
		if ( !Device.Capabilities.DrawIndirectCount ) Assert.Inconclusive( "The device has no indirect draw counts" );

		var vs = Device.CreateShader( Load( "triangle.vs.spv" ), "triangle.vs.spv" );
		var ps = Device.CreateShader( Load( "triangle.ps.spv" ), "triangle.ps.spv" );
		var pipeline = Device.CreateGraphicsPipeline( new GraphicsPipelineInfo
		{
			VertexShader = vs,
			VertexEntry = "MainVs",
			FragmentShader = ps,
			FragmentEntry = "MainPs",
			Topology = PrimitiveTopology.TriangleList,
			ColorFormats = [Format.RGBA8Unorm],
			Samples = 1,
			Name = "Triangle",
		} );

		var target = Device.CreateTexture( TextureInfo.RenderTarget( Width, Height, Format.RGBA8Unorm, name: "Target" ) );
		var indices = Device.CreateBuffer( new BufferInfo { Size = 6, Usage = BufferUsage.Index, Name = "Indices" } );
		Device.WriteBuffer<ushort>( indices, [0, 1, 2] );

		// Two indexed commands of one triangle each, then a count of none for the counted draw; two plain commands after them
		var arguments = Device.CreateBuffer( new BufferInfo { Size = 76, Usage = BufferUsage.Indirect, Name = "Arguments" } );
		Device.WriteBuffer<uint>( arguments, [3, 1, 0, 0, 0, 3, 1, 0, 0, 0, 0, 3, 1, 0, 0, 3, 1, 0, 0] );

		var list = Device.BeginCommandList( QueueType.Graphics, "Indirect draws" );
		list.Barrier( TextureBarrier.Transition( target, ImageLayout.Undefined, ImageLayout.ColorTarget, PipelineStage.ColorTargetOutput, MemoryAccess.None, PipelineStage.ColorTargetOutput, MemoryAccess.ColorTargetWrite ) );
		list.BeginRendering( [ColorTarget.Of( target, System.Numerics.Vector4.Zero )] );
		list.SetViewport( 0, 0, Width, Height );
		list.SetScissor( 0, 0, Width, Height );
		list.SetPipeline( pipeline );
		list.PushConstants( new System.Numerics.Vector4( 0, 0, 1, 0 ) );
		list.SetIndexBuffer( indices, IndexFormat.UInt16 );
		if ( counted ) list.DrawIndexedIndirectCount( arguments, 0, arguments, 40, 1 );
		if ( indexed ) list.DrawIndexedIndirect( arguments, 0, 2 );
		else list.DrawIndirect( arguments, 44, 2 );
		list.EndRendering();
		Device.Wait( Device.Submit( list ) );

		var pixels = ReadBack( target, ImageLayout.ColorTarget );
		var centre = Pixel( pixels, Width, Width / 2, Height / 2 );
		if ( centre.A == 0 ) Save( pixels, Width, Height, $"indirect-{counted}-{indexed}" );
		Assert.AreNotEqual( 0, centre.A, $"the plain multi-draw drew its triangle{(indexed ? "" : " (not indexed)")}{(counted ? " after a counted one" : "")} ({Device.Adapter.Name})" );

		Device.Destroy( pipeline );
		Device.Destroy( arguments );
		Device.Destroy( indices );
		Device.Destroy( target );
		Device.Destroy( vs );
		Device.Destroy( ps );
	}
}
