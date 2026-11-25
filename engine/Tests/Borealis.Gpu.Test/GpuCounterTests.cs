using Borealis.Backend;
using Borealis.Gpu;
using System.Numerics;

namespace BorealisGpuTests;

/// <summary>
/// Hardware counters per GPU scope (<see cref="GpuCounters"/>): off unless asked for, what a selection names, how results
/// put a scope's occurrences together, and (on an AMD GPU with GPUPerfAPI) a scope sampled over a few frames.
/// </summary>
[TestClass]
public class GpuCounterTests : GpuTest
{
	[TestMethod]
	public void CountersAreOffUnlessAskedFor()
	{
		Assert.IsNull( new DeviceOptions().GpuCounters, "no -gpucounters or BOREALIS_GPU_COUNTERS in the test process" );
		Assert.IsNull( Device.Counters );

		// And the pass timer samples nothing
		using var timer = new PassTimer( Device );
		timer.BeginFrame( 1 );
		var list = Device.BeginCommandList( QueueType.Graphics, "Timed" );
		timer.ResetIn( list );
		var scope = timer.Begin( list, "Layer", PassTimer.ScopeKind.Pass );
		timer.End( list, scope );
		Device.Submit( list );
		Device.WaitIdle();
	}

	[TestMethod]
	public void ASelectionNamesPresetsAndCounters()
	{
		var (names, list) = GpuCounters.Parse( null );
		CollectionAssert.AreEqual( GpuCounters.Presets["default"], names );
		Assert.IsFalse( list );

		(names, list) = GpuCounters.Parse( "list" );
		CollectionAssert.AreEqual( GpuCounters.Presets["default"], names );
		Assert.IsTrue( list );

		(names, list) = GpuCounters.Parse( "timing, L2CacheHit,GPUTime" );
		CollectionAssert.AreEqual( GpuCounters.Presets["timing"].Append( "L2CacheHit" ).ToList(), names, "named once each, in order" );
		Assert.IsFalse( list );
	}

	static readonly GpuCounterInfo[] Counters =
	[
		new( 0, "GPUTime", "Timing", "", GpuCounterUnit.Nanoseconds, true ),
		new( 1, "PSBusy", "Timing", "", GpuCounterUnit.Percentage, true ),
		new( 2, "PSPixelsOut", "PixelShader", "", GpuCounterUnit.Items, true ),
	];

	[TestMethod]
	public void AScopesOccurrencesAddUpAndItsSharesAreWeightedByTime()
	{
		// Two samples read; "Opaque" twice a frame (1 and 3 ms, 100% and 50% busy), "Post" once
		var results = GpuCounterResults.Build( Counters,
		[
			("Opaque", 0, [2e6, 200, 1000], 2),
			("Opaque", 1, [6e6, 100, 3000], 2),
			("Post", 0, [10e6, 40, 500], 2),
		], 2 );

		Assert.AreEqual( 2, results.Samples );
		CollectionAssert.AreEqual( new[] { "Post", "Opaque" }, results.Scopes.Select( s => s.Name ).ToArray(), "longest first" );
		var opaque = results.Scopes[1];
		Assert.AreEqual( 2, opaque.Occurrences );
		Assert.AreEqual( 4e6, opaque.Values[0], 1e-6, "times add up" );
		Assert.AreEqual( 2000, opaque.Values[2], 1e-9, "counts add up" );
		Assert.AreEqual( (100 * 1 + 50 * 3) / 4.0, opaque.Values[1], 1e-9, "percentages weighted by GPU time" );
		Assert.AreEqual( 20, results.Value( "Post", "PSBusy" ), 1e-9 );
		Assert.IsTrue( double.IsNaN( results.Value( "Post", "L2CacheHit" ) ) );
	}

	[TestMethod]
	public void TheTableAndCsvShowEveryScope()
	{
		var results = GpuCounterResults.Build( Counters, [("Opaque", 0, [2e6, 50, 1500], 1), ("Post \"x\"", 0, [1e6, 20, 0], 1)], 1 );
		var csv = results.ToCsv().Split( '\n', StringSplitOptions.RemoveEmptyEntries );
		Assert.AreEqual( "scope,occurrences,GPUTime (ns),PSBusy (%),PSPixelsOut (count)", csv[0].TrimEnd( '\r' ) );
		Assert.AreEqual( "\"Opaque\",1,2000000,50,1500", csv[1].TrimEnd( '\r' ) );
		Assert.AreEqual( "\"Post \"\"x\"\"\",1,1000000,20,0", csv[2].TrimEnd( '\r' ) );

		var table = results.ToTable();
		StringAssert.Contains( table, "GPUTime ms" );
		StringAssert.Contains( table, "2.000" );
		StringAssert.Contains( table, "1500" );
	}

	/// <summary>
	/// On an AMD GPU with GPUPerfAPI to hand (RenderDoc's copy, or BOREALIS_GPA_PATH): two scopes on a device of its own,
	/// sampled over the frames GPA needs, each come back with GPU time; the clear writes, the fill doesn't shade pixels.
	/// </summary>
	[TestMethod]
	public void ScopesAreSampledOverFrames()
	{
		if ( Device.Adapter.VendorId != 0x1002 ) Assert.Inconclusive( "GPU counters are AMD's" );
		if ( GpuCountersAvailable() is { } why ) Assert.Inconclusive( why );

		var notes = new List<string>();
		using var device = Borealis.Gpu.Device.Create( new DeviceOptions
		{
			ApplicationName = "Borealis.Gpu.Test counters",
			Presentation = false,
			GpuCounters = "GPUTime,CSBusy,PSPixelsOut,WriteSize",
			Message = m =>
			{
				lock ( notes ) notes.Add( $"{m.Severity} {m.Id}: {m.Text}" );
			},
		} );

		var counters = device.Counters;
		Assert.IsNotNull( counters, string.Join( "\n", notes ) );
		Assert.IsTrue( counters.PassCount >= 1 );
		Assert.AreEqual( "GPUTime", counters.Counters[0].Name );

		var texture = device.CreateTexture( TextureInfo.RenderTarget( 2048, 2048, Format.RGBA16Float ) );
		var buffer = device.CreateBuffer( new BufferInfo { Size = 64 << 20, Usage = BufferUsage.Storage } );
		using var timer = new PassTimer( device );
		counters.Capture( 2 );
		var layout = ImageLayout.Undefined;
		for ( long frame = 1; frame < 200 && counters.Capturing; frame++ )
		{
			device.BeginFrame();
			timer.BeginFrame( frame );
			var list = device.BeginCommandList( QueueType.Graphics, "Counted" );
			timer.ResetIn( list );
			list.Barrier( TextureBarrier.Transition( texture, layout, ImageLayout.CopyDestination, PipelineStage.AllCommands, MemoryAccess.MemoryWrite, PipelineStage.Transfer, MemoryAccess.TransferWrite ) );
			layout = ImageLayout.CopyDestination;

			var clear = timer.Begin( list, "Clear", PassTimer.ScopeKind.Pass );
			list.ClearTexture( texture, Vector4.One );
			timer.End( list, clear );

			var fill = timer.Begin( list, "Fill", PassTimer.ScopeKind.Own );
			list.FillBuffer( buffer, (uint)frame );
			timer.End( list, fill );

			device.Submit( list );
			device.WaitIdle();
		}

		Assert.IsFalse( counters.Capturing, $"the capture finished: {counters.Sessions}; {string.Join( "\n", notes )}" );
		var results = counters.Results;
		Assert.AreEqual( 2, results.Samples, string.Join( "\n", notes ) );
		Assert.IsTrue( results.Value( "Clear", "GPUTime" ) > 0 );
		Assert.IsTrue( results.Value( "Fill", "GPUTime" ) > 0 );
		Assert.AreEqual( 0, results.Value( "Fill", "PSPixelsOut" ), "a fill shades no pixels" );
		device.Destroy( texture );
		device.Destroy( buffer );
	}

	static string GpuCountersAvailable() => Gpa.Candidates().Any( System.IO.File.Exists ) ? null : "GPUPerfAPIVK-x64.dll isn't here";
}
