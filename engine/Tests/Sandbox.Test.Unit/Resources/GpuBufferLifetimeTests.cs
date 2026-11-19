using System;
using System.Reflection;

namespace ResourceTests;

[TestClass]
public class GpuBufferLifetimeTests
{
	class FakeBuffer : GpuBuffer
	{
		public FakeBuffer( IntPtr handle ) => native = handle;
	}

	static IntPtr Pending( GpuBuffer buffer ) => (NativeEngine.RenderBufferHandle_t)typeof( GpuBuffer )
		.GetField( "pendingDestroy", BindingFlags.Instance | BindingFlags.NonPublic )
		.GetValue( buffer );

	[TestMethod]
	public void DisposeWhileBoundDefersDestroy()
	{
		ThreadSafe.MarkMainThread();
		var buffer = new FakeBuffer( 0x1234 );

		Assert.AreEqual( (IntPtr)0x1234, (IntPtr)buffer.AddAttributeBinding() );
		buffer.Dispose();

		Assert.IsFalse( buffer.IsValid );
		Assert.AreEqual( (IntPtr)0x1234, Pending( buffer ) );
		Assert.AreEqual( IntPtr.Zero, (IntPtr)buffer.AddAttributeBinding() );
	}
}
