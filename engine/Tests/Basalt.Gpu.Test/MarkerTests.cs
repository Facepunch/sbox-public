using Basalt.Gpu;

namespace BasaltGpuTests;

/// <summary>
/// Progress markers (<see cref="DeviceOptions.ProgressMarkers"/>) on a device of their own: a finished list's markers are
/// all reached and done.
/// </summary>
[TestClass]
public class MarkerTests
{
	[TestMethod]
	public void FinishedListReachedEveryMarker()
	{
		if ( !Device.IsSupported ) Assert.Inconclusive( "No Vulkan loader is installed" );

		var errors = new List<string>();
		Device device = null;
		try
		{
			try
			{
				device = Device.Create( new DeviceOptions
				{
					ApplicationName = "Basalt.Gpu.Test",
					Validation = true,
					Presentation = false,
					ProgressMarkers = true,
					Message = m =>
					{
						if ( m.Severity == MessageSeverity.Error ) lock ( errors ) errors.Add( m.Text );
					},
				} );
			}
			catch ( GpuException e )
			{
				Assert.Inconclusive( e.Message );
			}

			if ( !device.ProgressMarkers ) Assert.Inconclusive( "The device has no VK_AMD_buffer_marker" );

			var list = device.BeginCommandList( QueueType.Graphics, "Markers" );
			list.BeginLabel( "Outer" );
			list.Marker( "Inside" );
			list.EndLabel();
			device.Wait( device.Submit( list ) );

			var described = device.DescribeMarkers( list.MarkerSlot );
			StringAssert.StartsWith( described, " [markers " );
			Assert.IsFalse( described.Contains( "reached" ), described );
		}
		finally
		{
			device?.Dispose();
		}

		Assert.AreEqual( 0, errors.Count, string.Join( "\n", errors ) );
	}
}
