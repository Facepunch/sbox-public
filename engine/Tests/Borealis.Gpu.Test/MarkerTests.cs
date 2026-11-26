using Borealis.Gpu;

namespace BorealisGpuTests;

/// <summary>
/// Progress markers (<see cref="DeviceOptions.ProgressMarkers"/>) on a device of their own: a finished list's markers are
/// all reached and done, labels write markers only with <see cref="DeviceOptions.PassProgressMarkers"/>, and the
/// device-loss report shows a list the GPU stopped part way through, however far back it is.
/// </summary>
[TestClass]
public class MarkerTests
{
	[TestMethod]
	public void FinishedListReachedEveryMarker()
	{
		WithDevice( device =>
		{
			var list = device.BeginCommandList( QueueType.Graphics, "Markers" );
			list.BeginLabel( "Outer" );
			list.Marker( "Inside" );
			list.EndLabel();
			device.Wait( device.Submit( list ) );

			var described = device.DescribeMarkers( list.MarkerSlot );
			StringAssert.StartsWith( described, " [markers " );
			Assert.IsFalse( described.Contains( "reached" ), described );
		} );
	}

	/// <summary>Without pass markers a list writes its beginning and end, and the markers asked for, not its labels'.</summary>
	[TestMethod]
	public void LabelsWriteNoMarkersWithoutPassMarkers()
	{
		WithDevice( device =>
		{
			var list = device.BeginCommandList( QueueType.Graphics, "Markers" );
			list.BeginLabel( "Outer" );
			list.Marker( "Inside" );
			list.EndLabel();
			device.Wait( device.Submit( list ) );

			Assert.AreEqual( " [markers 3/3]", device.DescribeMarkers( list.MarkerSlot ) );
		}, passMarkers: false );
	}

	/// <summary>
	/// A list the GPU stopped in, with many lists after it that finished on the GPU but weren't read as done (the frames
	/// recorded ahead of a hang), is in the report with the first marker it didn't finish.
	/// </summary>
	[TestMethod]
	public void LostReportShowsTheListTheGpuStoppedIn()
	{
		WithDevice( device =>
		{
			var stuck = device.BeginCommandList( QueueType.Graphics, "Stuck list" );
			stuck.Marker( "draw one" );
			stuck.Marker( "draw two" );
			device.Wait( device.Submit( stuck ) );

			for ( int i = 0; i < 80; i++ ) device.Wait( device.Submit( device.BeginCommandList( QueueType.Graphics, "Later list" ) ) );

			// begin, the list's label, draw one, draw two, its end and the end: the front end reached the end, the work after
			// "draw one" never finished
			device.SetMarkerProgress( stuck.MarkerSlot, 6, 3 );
			device.MarkLost( "Lost on purpose" );
			StringAssert.Contains( device.LostReport, "Stuck list" );
			StringAssert.Contains( device.LostReport, "first not done 'draw two'" );
		}, expectLoss: true );
	}

	static void WithDevice( Action<Device> test, bool expectLoss = false, bool passMarkers = true )
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
					ApplicationName = "Borealis.Gpu.Test",
					Validation = true,
					Presentation = false,
					ProgressMarkers = true,
					PassProgressMarkers = passMarkers,
					Message = m =>
					{
						if ( m.Severity == MessageSeverity.Error && !(expectLoss && m.Id == "Borealis.Gpu.DeviceLost") ) lock ( errors ) errors.Add( m.Text );
					},
				} );
			}
			catch ( GpuException e )
			{
				Assert.Inconclusive( e.Message );
			}

			if ( !device.ProgressMarkers ) Assert.Inconclusive( "The device has no VK_AMD_buffer_marker" );
			test( device );
		}
		finally
		{
			device?.Dispose();
		}

		Assert.AreEqual( 0, errors.Count, string.Join( "\n", errors ) );
	}
}
