using System;

namespace EngineTests;

[TestClass]
public class GpuBufferBoundsTests
{
	[TestMethod]
	public void RangesPastEndAreRejected()
	{
		using var buffer = new GpuBuffer<int>( 4, GpuBuffer.UsageFlags.Structured );
		using var append = new GpuBuffer<int>( 4, GpuBuffer.UsageFlags.Append );
		if ( !buffer.IsValid || !append.IsValid )
			Assert.Inconclusive( "Requires a render device that creates GPU buffers." );

		Assert.ThrowsException<ArgumentOutOfRangeException>( () => buffer.GetData( new int[4], 2, 4 ) );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => buffer.GetDataAsync( _ => { }, 3, 2 ) );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => buffer.SetData( new int[2], 3 ) );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => append.CopyStructureCount( buffer, 16 ) );
	}

	class ReinitializedBuffer : GpuBuffer<Matrix>
	{
		public ReinitializedBuffer() : base( 1 ) => Initialize( 1024, 4 );
	}

	[TestMethod]
	public void InitializeTwiceThrows()
	{
		Assert.ThrowsException<InvalidOperationException>( () => new ReinitializedBuffer() );
	}

	[TestMethod]
	public void OversizedConstantBufferIsRejected()
	{
		var attributes = new RenderAttributes();
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => attributes.SetData( "Data", new byte[RenderAttributes.MaxConstantBufferSize + 16] ) );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => attributes.SetData( "Data", new Vector4[4096] ) );
	}
}
