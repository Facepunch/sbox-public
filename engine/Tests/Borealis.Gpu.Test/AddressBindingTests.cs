using Borealis.Gpu;

namespace BorealisGpuTests;

/// <summary>
/// The driver's address binding reports (VK_EXT_device_address_binding_report, with <see cref="DeviceOptions.ProgressMarkers"/>):
/// the device-loss report names the resource a fault address lies in, and one freed before the fault by the name it had.
/// </summary>
[TestClass]
public class AddressBindingTests
{
	public TestContext TestContext { get; set; }

	[TestMethod]
	public void FaultAddressNamesTheTextureItLiesIn()
	{
		if ( !Device.IsSupported ) Assert.Inconclusive( "No Vulkan loader is installed" );

		Device device = null;
		try
		{
			try
			{
				device = Device.Create( new DeviceOptions { ApplicationName = "Borealis.Gpu.Test", Presentation = false, ProgressMarkers = true } );
			}
			catch ( GpuException e )
			{
				Assert.Inconclusive( e.Message );
			}

			if ( !device.AddressReports ) Assert.Inconclusive( "The device has no VK_EXT_device_address_binding_report" );

			// Suballocated, as textures are: the driver's report during the bind is named after the texture
			const string Name = "Address binding test texture";
			var texture = device.CreateTexture( TextureInfo.Texture2D( 256, 256, Format.RGBA16Float, name: Name ) );
			var buffer = device.CreateBuffer( new BufferInfo { Size = 1 << 20, Usage = BufferUsage.Storage, Memory = MemoryLocation.Device, Name = "Address binding test buffer" } );
			var ranges = device.BoundRanges();
			TestContext.WriteLine( $"{ranges.Length} ranges bound: {string.Join( ", ", ranges.Select( r => $"{r.Type} '{r.Name}' at 0x{r.Base:X}+0x{r.Size:X}" ) )}" );
			var bound = device.AddressRangeOf( Name, unbound: false );
			Assert.IsNotNull( bound, "the texture's range is known while it's bound" );
			var inside = (bound.Value.Base + bound.Value.Size / 2) & ~0xFFFUL;
			var described = device.DescribeAddress( inside, 0x1000 );
			TestContext.WriteLine( described );
			StringAssert.Contains( described, $"Image '{Name}'" );
			StringAssert.Contains( described, ", bound " );

			// Destroyed, and freed once the frames after it are done: the address is named as taken back from it
			device.Destroy( texture );
			device.Destroy( buffer );
			for ( int i = 0; i < 4; i++ )
			{
				device.BeginFrame();
				device.WaitIdle();
			}

			Assert.IsNull( device.AddressRangeOf( Name, unbound: false ), "the texture's range isn't bound any more" );
			var unbound = device.AddressRangeOf( Name, unbound: true );
			Assert.IsNotNull( unbound, "the texture's range is kept once it's taken back" );
			described = device.DescribeAddress( inside, 0x1000 );
			TestContext.WriteLine( described );
			StringAssert.Contains( described, $"Image '{Name}'" );
			StringAssert.Contains( described, ", taken back " );
		}
		finally
		{
			device?.Dispose();
		}
	}

	/// <summary>With <see cref="DeviceOptions.BufferAddresses"/> the report names buffers too, which drivers don't report.</summary>
	[TestMethod]
	public void FaultAddressNamesTheBufferItLiesIn()
	{
		if ( !Device.IsSupported ) Assert.Inconclusive( "No Vulkan loader is installed" );

		Device device = null;
		try
		{
			try
			{
				device = Device.Create( new DeviceOptions { ApplicationName = "Borealis.Gpu.Test", Presentation = false, BufferAddresses = true } );
			}
			catch ( GpuException e )
			{
				Assert.Inconclusive( e.Message );
			}

			if ( !device.BufferAddresses ) Assert.Inconclusive( "The device has no buffer device addresses" );

			// A small buffer (suballocated in a shared block) and one with a block of its own
			const string Small = "Address test small buffer", Big = "Address test big buffer";
			var small = device.CreateBuffer( new BufferInfo { Size = 4096, Usage = BufferUsage.Storage, Memory = MemoryLocation.Device, Name = Small } );
			var big = device.CreateBuffer( new BufferInfo { Size = 64 << 20, Usage = BufferUsage.Storage | BufferUsage.Indirect, Memory = MemoryLocation.Device, Name = Big } );
			var upload = device.CreateBuffer( new BufferInfo { Size = 1 << 16, Usage = BufferUsage.Storage, Memory = MemoryLocation.Upload, Name = "Address test upload buffer" } );

			// Still usable as before
			var list = device.BeginCommandList( QueueType.Graphics, "Address test" );
			list.FillBuffer( big, 7 );
			list.CopyBuffer( small, 0, upload, 0, 4096 );
			device.Submit( list );
			device.WaitIdle();

			ulong inside = 0;
			foreach ( var name in new[] { Small, Big } )
			{
				var bound = device.AddressRangeOf( name, unbound: false );
				Assert.IsNotNull( bound, $"{name}'s range is known while it's alive" );
				inside = (bound.Value.Base + bound.Value.Size / 2) & ~0xFFFUL;
				var described = device.DescribeAddress( inside, 0x1000 );
				TestContext.WriteLine( described );
				StringAssert.Contains( described, $"Buffer '{name}'" );
				StringAssert.Contains( described, ", bound " );
			}

			// An address in nothing names the ranges nearest it, for a read past a buffer's end
			var nowhere = device.DescribeAddress( 0xFFFF_F000_0000_0000, 0x1000 );
			TestContext.WriteLine( nowhere );
			StringAssert.Contains( nowhere, "in no range" );
			StringAssert.Contains( nowhere, "nearest below ends 0x" );

			device.Destroy( small );
			device.Destroy( big );
			device.Destroy( upload );
			for ( int i = 0; i < 4; i++ )
			{
				device.BeginFrame();
				device.WaitIdle();
			}

			Assert.IsNull( device.AddressRangeOf( Big, unbound: false ), "the buffer's range isn't bound any more" );
			Assert.IsNotNull( device.AddressRangeOf( Big, unbound: true ), "the buffer's range is kept once it's taken back" );
			var freed = device.DescribeAddress( inside, 0x1000 );
			TestContext.WriteLine( freed );
			StringAssert.Contains( freed, $"Buffer '{Big}'" );
			StringAssert.Contains( freed, ", taken back " );
		}
		finally
		{
			device?.Dispose();
		}
	}
}
