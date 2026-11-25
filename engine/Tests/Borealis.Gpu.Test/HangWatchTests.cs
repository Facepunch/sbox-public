using Borealis.Gpu;

namespace BorealisGpuTests;

/// <summary>
/// The hang watch (<see cref="DeviceOptions.HangWatch"/>) says where a frame that went on too long had its thread, and
/// how far the queues had got, in a warning and among a loss report's events; frames that don't stop say nothing.
/// </summary>
[TestClass]
[DoNotParallelize]
public class HangWatchTests
{
	[TestMethod]
	[Timeout( 60_000 )]
	public void StoppedFrameSaysWhereItsThreadWas()
	{
		if ( !Device.IsSupported ) Assert.Inconclusive( "No Vulkan loader is installed" );
		var messages = new List<DeviceMessage>();
		using var device = WatchedDevice( messages );
		device.BeginFrame();
		device.Wait( device.Submit( device.BeginCommandList( QueueType.Graphics, "Quick frame" ) ) );
		device.BeginFrame();
		Assert.AreEqual( 0, device.FrameStalls );

		using ( device.Enter( "a test's long call" ) ) System.Threading.Thread.Sleep( 1600 );
		device.BeginFrame();

		Assert.AreEqual( 1, device.FrameStalls );
		string[] warnings;
		lock ( messages ) warnings = messages.Where( m => m.Id == "Borealis.Gpu.FrameStall" ).Select( m => m.Text ).ToArray();
		Assert.AreEqual( 1, warnings.Length );
		StringAssert.Contains( warnings[0], "frame thread in a test's long call" );
		// Everything submitted had finished: the queue reached what was flushed
		StringAssert.Matches( warnings[0], new System.Text.RegularExpressions.Regex( @"Graphics reached (\d+) of \1 flushed" ) );

		device.MarkLost( "Lost on purpose" );
		StringAssert.Contains( device.LostReport, "watch: frame thread in a test's long call" );
		StringAssert.Contains( device.LostReport, "watch: the frame ended" );
	}

	static Device WatchedDevice( List<DeviceMessage> messages )
	{
		try
		{
			return Device.Create( new DeviceOptions { ApplicationName = "Borealis.Gpu.Test", HangWatch = true, Message = m => { lock ( messages ) messages.Add( m ); } } );
		}
		catch ( GpuException e )
		{
			Assert.Inconclusive( e.Message );
			return null;
		}
	}
}
