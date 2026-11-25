using Borealis.Backend;
using Borealis.Gpu;

namespace BorealisGpuTests;

/// <summary>
/// The pass timer's scopes belong to the frame they began in: a scene can start recording before the engine's frame
/// moves the device on (a scene panel drawn before the window acquires), so a scope can end after the next frame began.
/// </summary>
[TestClass]
public class PassTimerTests : GpuTest
{
	[TestMethod]
	public void AScopeEndsInTheFrameItBeganIn()
	{
		if ( !Device.Capabilities.Timestamps ) Assert.Inconclusive( "No timestamps on this device" );

		using var timer = new PassTimer( Device );
		timer.BeginFrame( 1 );
		var list = Device.BeginCommandList( QueueType.Graphics, "Timed" );
		timer.ResetIn( list );
		var scope = timer.Begin( list, "Layer", PassTimer.ScopeKind.Pass );

		timer.BeginFrame( 2 );
		timer.End( list, scope );
		Device.Submit( list );
		Device.WaitIdle();

		var timings = new List<PassTimer.Timing>();
		Assert.IsTrue( timer.TryRead( 1, timings ) );
		Assert.AreEqual( 1, timings.Count );
		Assert.AreEqual( "Layer", timings[0].Name );
		Assert.IsTrue( timings[0].Ms >= 0 );

		Assert.IsTrue( timer.TryRead( 2, timings ) );
		Assert.AreEqual( 0, timings.Count );
	}

	/// <summary>
	/// A frame's queries are reset as it begins, so a list that reaches the GPU before the one that would have reset them
	/// writes reset queries (validation fails the test otherwise).
	/// </summary>
	[TestMethod]
	public void ListsTimeInAnyOrder()
	{
		if ( !Device.Capabilities.Timestamps || !Device.Capabilities.HostQueryReset ) Assert.Inconclusive( "No timestamps or host query resets on this device" );

		using var timer = new PassTimer( Device );
		timer.BeginFrame( 1 );
		var first = Device.BeginCommandList( QueueType.Graphics, "Recorded first" );
		timer.ResetIn( first );
		var firstScope = timer.Begin( first, "First", PassTimer.ScopeKind.List );
		var second = Device.BeginCommandList( QueueType.Graphics, "Recorded second" );
		var secondScope = timer.Begin( second, "Second", PassTimer.ScopeKind.List );
		timer.End( second, secondScope );
		timer.End( first, firstScope );

		Device.Submit( second );
		Device.Submit( first );
		Device.WaitIdle();

		var timings = new List<PassTimer.Timing>();
		Assert.IsTrue( timer.TryRead( 1, timings ) );
		Assert.AreEqual( 2, timings.Count );
	}

	/// <summary>
	/// The caller's frames moving faster than the device's (the engine's, some of which draw nothing) bring a slot round
	/// again one device frame after it timed a list: the GPU finishes that list before the slot's queries are reset, as it
	/// has when the device's frames moved on with them. A slow list (fills of a large buffer), so it's still running then.
	/// </summary>
	[TestMethod]
	public void ASlotComingRoundEarlyWaitsForItsLastFrame()
	{
		if ( !Device.Capabilities.Timestamps ) Assert.Inconclusive( "No timestamps on this device" );

		using var timer = new PassTimer( Device );
		var buffer = Device.CreateBuffer( new BufferInfo { Size = 64 << 20, Usage = BufferUsage.Storage | BufferUsage.CopyDestination, Name = "Filled" } );

		Device.BeginFrame();
		timer.BeginFrame( 1 );
		var list = Device.BeginCommandList( QueueType.Graphics, "Timed" );
		timer.ResetIn( list );
		var scope = timer.Begin( list, "Fills", PassTimer.ScopeKind.List );
		for ( uint i = 0; i < 32; i++ )
		{
			list.FillBuffer( buffer, i );
			list.Barrier( new BufferBarrier { Buffer = buffer, SourceStage = PipelineStage.Transfer, SourceAccess = MemoryAccess.TransferWrite, DestinationStage = PipelineStage.Transfer, DestinationAccess = MemoryAccess.TransferWrite } );
		}

		timer.End( list, scope );
		var submitted = Device.Submit( list );

		// One device frame on, the caller's frame comes back to slot 1
		Device.BeginFrame();
		timer.BeginFrame( 1 + PassTimer.Slots );
		Assert.IsTrue( Device.IsComplete( submitted ), "the slot's last list finished before its queries were reset" );
		Assert.AreEqual( 1, timer.SlotWaits );

		// Its device frames moving on with it, it doesn't wait
		for ( int i = 2; i <= 2 * PassTimer.Slots; i++ )
		{
			Device.BeginFrame();
			timer.BeginFrame( PassTimer.Slots + i );
		}

		Assert.AreEqual( 1, timer.SlotWaits, "frames that moved on with the device's waited only once" );
		Device.WaitIdle();
		Device.Destroy( buffer );
	}
}
