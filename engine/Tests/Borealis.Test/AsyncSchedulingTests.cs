using Borealis;
using Borealis.Gpu;
using System;

namespace BorealisTests;

/// <summary>Queue dependencies with arbitrary resources, independent of camera effects and native rendering.</summary>
[TestClass]
[DoNotParallelize]
public class AsyncSchedulingTests
{
	sealed class Pass( string name, FrameResources reads = FrameResources.None, FrameResources writes = FrameResources.None, bool compute = false ) : RenderLayer( name )
	{
		public bool Enabled = true;
		public bool SideEffects;
		public override bool IsNeeded( RenderFrame frame ) => Enabled;
		public override FrameResources Reads( RenderFrame frame ) => reads;
		public override FrameResources Writes => writes;
		public override bool SupportsAsyncCompute => compute;
		public override bool HasSideEffects => SideEffects || base.HasSideEffects;
	}

	sealed class UnknownPass() : RenderLayer( "unknown access" ) { }

	sealed class ChildFramePass( RenderFrame child ) : RenderLayer( "child frame" )
	{
		public override void AddTo( FrameRecorder recorder, RenderFrame frame ) => recorder.AddFrame( child );
	}

	IDisposable _asyncCompute;

	[TestInitialize]
	public void EnableCompute() => _asyncCompute = HeadlessRenderHost.Headless.WithAsyncCompute( true );

	[TestCleanup]
	public void RestoreCompute() => _asyncCompute.Dispose();

	static int SegmentFor( FrameRecorder recorder, string name ) => recorder.Snapshot( "test" ).Frames[0].Layers.Single( l => l.Name == name ).Segment;

	static void AssertSingleConsumers( FrameRecorder recorder )
	{
		var plan = recorder.Snapshot( "test" );
		var waits = Enumerable.Range( 0, plan.Segments.Count ).Select( i => recorder.DependenciesOf( i ).WaitFor ).Where( i => i >= 0 ).ToArray();
		Assert.AreEqual( waits.Length, waits.Distinct().Count(), "binary semaphores must have one consumer" );
		Assert.IsTrue( waits.All( i => plan.Segments[i].Async ) );
		for ( int i = 0; i < plan.Segments.Count; i++ )
			Assert.IsTrue( recorder.DependenciesOf( i ).WaitFor < i );
	}

	[TestMethod]
	public void IntermediateResultsReleaseGraphicsWithoutWaitingForTheRest()
	{
		using var system = new RenderSystem();
		system.Layers = [
			new Pass( "before" ),
			new Pass( "producer", writes: FrameResources.QuarterDepth, compute: true ),
			new Pass( "independent compute", compute: true ),
			new Pass( "reader", reads: FrameResources.QuarterDepth ),
			new Pass( "second reader", reads: FrameResources.QuarterDepth ),
			new UnknownPass()];
		using var recorder = new FrameRecorder();
		recorder.Plan( system.Frame, parallel: true, asyncCompute: true );

		Assert.IsTrue( system.Frame.AsyncCompute );
		Assert.IsFalse( recorder.Parallel, "GPU overlap must not depend on CPU worker recording" );
		Assert.AreEqual( SegmentFor( recorder, "producer" ), recorder.DependenciesOf( SegmentFor( recorder, "reader" ) ).WaitFor );
		Assert.AreEqual( SegmentFor( recorder, "reader" ), SegmentFor( recorder, "second reader" ), "same-queue ordering reuses the first acquisition" );
		Assert.AreEqual( SegmentFor( recorder, "independent compute" ), recorder.DependenciesOf( SegmentFor( recorder, "unknown access" ) ).WaitFor );
		AssertSingleConsumers( recorder );
	}

	[TestMethod]
	public void GraphicsProducersSeparateComputeBatchesAutomatically()
	{
		using var system = new RenderSystem();
		system.Layers = [
			new Pass( "a", writes: FrameResources.QuarterDepth, compute: true ),
			new Pass( "b", writes: FrameResources.BloomObjects, compute: true ),
			new Pass( "tail", compute: true ),
			new Pass( "read a", reads: FrameResources.QuarterDepth ),
			new Pass( "read b", reads: FrameResources.BloomObjects ),
			new Pass( "graphics producer", writes: FrameResources.RefractionStencil ),
			new Pass( "second compute", reads: FrameResources.RefractionStencil, compute: true ),
			new Pass( "second graphics" )];
		using var recorder = new FrameRecorder();
		recorder.Plan( system.Frame, parallel: true, asyncCompute: true );

		Assert.IsTrue( system.Frame.AsyncCompute );
		Assert.AreEqual( SegmentFor( recorder, "a" ), recorder.DependenciesOf( SegmentFor( recorder, "read a" ) ).WaitFor );
		Assert.AreEqual( SegmentFor( recorder, "b" ), recorder.DependenciesOf( SegmentFor( recorder, "read b" ) ).WaitFor );
		var plan = recorder.Snapshot( "test" );
		Assert.AreEqual( 4, plan.Segments.Count( s => s.Async ) );
		Assert.IsTrue( SegmentFor( recorder, "graphics producer" ) < SegmentFor( recorder, "second compute" ), "compute handoff follows its graphics producer" );
		Assert.AreEqual( SegmentFor( recorder, "tail" ), recorder.DependenciesOf( SegmentFor( recorder, "second compute" ) - 1 ).WaitFor );
		Assert.AreEqual( SegmentFor( recorder, "second compute" ), recorder.DependenciesOf( plan.Segments.Count - 1 ).WaitFor, "the frame still joins outstanding compute" );
		AssertSingleConsumers( recorder );
	}

	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void GraphicsWritesWaitForComputeReadersAndWriters( bool computeWrites )
	{
		var compute = computeWrites
			? new Pass( "compute", writes: FrameResources.QuarterDepth, compute: true )
			: new Pass( "compute", reads: FrameResources.QuarterDepth, compute: true );
		using var system = new RenderSystem();
		system.Layers = [compute, new Pass( "tail", compute: true ),
			new Pass( "writer", writes: FrameResources.QuarterDepth ), new Pass( "consumer", reads: FrameResources.QuarterDepth )];
		using var recorder = new FrameRecorder();
		recorder.Plan( system.Frame, parallel: true, asyncCompute: true );
		Assert.IsTrue( system.Frame.AsyncCompute );
		Assert.AreEqual( SegmentFor( recorder, "compute" ), recorder.DependenciesOf( SegmentFor( recorder, "writer" ) ).WaitFor );
		AssertSingleConsumers( recorder );
	}

	[TestMethod]
	[DataRow( false, true, true )]
	[DataRow( true, false, true )]
	[DataRow( true, true, false )]
	public void DisabledOrUnsupportedSchedulingFallsBackAndClearsPreviousWaits( bool parallel, bool enabled, bool supported )
	{
		using var system = new RenderSystem();
		system.Layers = [new Pass( "compute", compute: true ), new Pass( "graphics" )];
		using var recorder = new FrameRecorder();
		recorder.Plan( system.Frame, parallel: true, asyncCompute: true );
		Assert.IsTrue( system.Frame.AsyncCompute );
		HeadlessRenderHost.Headless.AsyncCompute = supported;
		recorder.Plan( system.Frame, parallel, enabled );
		Assert.IsFalse( system.Frame.AsyncCompute );
		Assert.IsFalse( recorder.SeparateContexts );
		Assert.IsTrue( recorder.Snapshot( "test" ).Segments.All( s => !s.Async ) );
		Assert.AreEqual( -1, recorder.DependenciesOf( 0 ).WaitFor );
	}

	[TestMethod]
	public void CulledBranchesDoNotLeaveWaitsAndUnusedProducersStayCulled()
	{
		var graphics = new Pass( "graphics" );
		var unused = new Pass( "unused", writes: FrameResources.RefractionStencil, compute: true );
		using var system = new RenderSystem();
		system.Layers = [unused, new Pass( "compute", compute: true ), graphics];
		using var recorder = new FrameRecorder();
		recorder.Plan( system.Frame, parallel: true, asyncCompute: true );
		Assert.IsTrue( system.Frame.AsyncCompute );
		Assert.AreEqual( -1, SegmentFor( recorder, "unused" ) );
		graphics.Enabled = false;
		recorder.Plan( system.Frame, parallel: true, asyncCompute: true );
		Assert.IsFalse( system.Frame.AsyncCompute, "no independent graphics work to overlap" );
		Assert.IsFalse( recorder.SeparateContexts );
	}

	[TestMethod]
	public void FullyDependentBranchesStaySerial()
	{
		using var system = new RenderSystem();
		system.Layers = [new Pass( "producer", writes: FrameResources.QuarterDepth, compute: true ),
			new Pass( "reader", reads: FrameResources.QuarterDepth ), new Pass( "later graphics" )];
		using var recorder = new FrameRecorder();
		recorder.Plan( system.Frame, parallel: true, asyncCompute: true );
		Assert.IsFalse( system.Frame.AsyncCompute, "separate queues cannot overlap a single producer and its consumer" );
	}

	[TestMethod]
	public void ReadOnlyInputsDoNotSerializeQueues()
	{
		using var system = new RenderSystem();
		system.Layers = [new Pass( "compute", reads: FrameResources.Depth, compute: true ),
			new Pass( "graphics", reads: FrameResources.Depth )];
		using var recorder = new FrameRecorder();
		recorder.Plan( system.Frame, parallel: true, asyncCompute: true );
		Assert.IsTrue( system.Frame.AsyncCompute );
		Assert.AreEqual( -1, recorder.DependenciesOf( SegmentFor( recorder, "graphics" ) ).WaitFor );
		AssertSingleConsumers( recorder );
	}

	[TestMethod]
	public void UnknownAccessIsABoundaryEvenWithoutDeclaredInputs()
	{
		using var system = new RenderSystem();
		system.Layers = [new Pass( "compute", compute: true ), new UnknownPass(), new Pass( "graphics" )];
		using var recorder = new FrameRecorder();
		recorder.Plan( system.Frame, parallel: true, asyncCompute: true );
		Assert.IsFalse( system.Frame.AsyncCompute, "work after the unknown pass cannot provide an overlap window" );
	}

	[TestMethod]
	public void SideEffectsAreKeptEvenWhenTheirOutputsAreUnused()
	{
		using var system = new RenderSystem();
		system.Layers = [new Pass( "output", writes: FrameResources.Depth ) { SideEffects = true }];
		using var recorder = new FrameRecorder();
		recorder.Plan( system.Frame, parallel: true, asyncCompute: true );
		Assert.AreEqual( 1, recorder.Planned.Length );
	}

	[TestMethod]
	public void ComputeCannotCrossAChildFrame()
	{
		using var system = new RenderSystem();
		using var child = new RenderSystem();
		child.Layers = [new Pass( "child graphics" )];
		system.Layers = [new Pass( "compute", compute: true ), new Pass( "independent" ),
			new ChildFramePass( child.Frame ), new Pass( "after child" )];
		using var recorder = new FrameRecorder();
		recorder.Plan( system.Frame, parallel: true, asyncCompute: true );
		var plan = recorder.Snapshot( "test" );
		var childSegment = plan.Frames[1].Layers[0].Segment;
		Assert.IsTrue( system.Frame.AsyncCompute );
		Assert.IsFalse( child.Frame.AsyncCompute );
		Assert.AreEqual( SegmentFor( recorder, "compute" ), recorder.DependenciesOf( childSegment - 1 ).WaitFor );
		AssertSingleConsumers( recorder );
	}

	/// <summary>
	/// With lists of their own, main-thread segments straight after one another record into the first one's list; a
	/// segment that waits for compute, compute itself, or anything with separate lists turned off keeps a list of its own.
	/// </summary>
	[TestMethod]
	public void MainSegmentsAfterOneAnotherShareAList()
	{
		using var system = new RenderSystem();
		using var child = new RenderSystem();
		child.Layers = [new Pass( "child graphics" )];
		system.Layers = [new Pass( "compute", compute: true ), new Pass( "independent" ),
			new ChildFramePass( child.Frame ), new Pass( "after child" )];
		using var recorder = new FrameRecorder();
		recorder.Plan( system.Frame, parallel: true, asyncCompute: true );

		var plan = recorder.Snapshot( "test" );
		var childSegment = plan.Frames[1].Layers[0].Segment;
		var after = SegmentFor( recorder, "after child" );
		var join = childSegment - 1;
		Assert.IsTrue( recorder.DependenciesOf( join ).WaitFor >= 0, "the segment before the child frame waits for compute" );
		Assert.AreEqual( -1, recorder.DependenciesOf( join ).Borrows, "so it has a list of its own" );
		Assert.AreEqual( -1, recorder.DependenciesOf( SegmentFor( recorder, "compute" ) ).Borrows, "as compute does" );
		Assert.AreEqual( join, recorder.DependenciesOf( childSegment ).Borrows, "the child frame records into it" );
		Assert.AreEqual( join, recorder.DependenciesOf( after ).Borrows, "and so does the segment after, into the first of the run" );
		Assert.IsTrue( recorder.SharedLists >= 2 );

		FrameRecorder.ShareMainLists = false;
		try
		{
			recorder.Plan( system.Frame, parallel: true, asyncCompute: true );
			Assert.AreEqual( 0, recorder.SharedLists, "-separate-main-lists keeps a list per segment" );
		}
		finally
		{
			FrameRecorder.ShareMainLists = true;
		}
	}

	[TestMethod]
	public void ComputeOutputPreparationCannotOverwriteEarlierInputs()
	{
		using var system = new RenderSystem();
		system.Layers = [new Pass( "reader", reads: FrameResources.QuarterDepth, compute: true ),
			new Pass( "writer", writes: FrameResources.QuarterDepth, compute: true ) { SideEffects = true },
			new Pass( "independent graphics" )];
		using var recorder = new FrameRecorder();
		recorder.Plan( system.Frame, parallel: true, asyncCompute: true );
		var plan = recorder.Snapshot( "test" );
		var reader = SegmentFor( recorder, "reader" );
		var writer = SegmentFor( recorder, "writer" );
		Assert.IsTrue( reader < writer );
		Assert.IsFalse( plan.Segments[reader].Async, "finish the reader before the writer's graphics preparation" );
		Assert.IsTrue( plan.Segments[writer].Async );
		AssertSingleConsumers( recorder );
	}

	[TestMethod]
	public void MixedPassOrdersPreserveAllResourceHazards()
	{
		var random = new Random( 731 );
		using var system = new RenderSystem();
		using var recorder = new FrameRecorder();
		for ( int sample = 0; sample < 64; sample++ )
		{
			var passes = new Pass[24];
			for ( int i = 0; i < passes.Length; i++ )
			{
				var reads = (FrameResources)random.Next( 8 );
				if ( random.Next( 8 ) == 0 ) reads |= FrameResources.Untracked;
				passes[i] = new Pass( $"pass {i}", reads, (FrameResources)random.Next( 8 ), random.Next( 2 ) == 0 ) { SideEffects = true };
			}

			system.Layers = passes;
			recorder.Plan( system.Frame, parallel: true, asyncCompute: true );
			var plan = recorder.Snapshot( "test" );
			var count = plan.Segments.Count;
			var ordered = new bool[count, count];
			int graphics = -1, compute = -1;
			for ( int i = 0; i < count; i++ )
			{
				var previous = plan.Segments[i].Async ? compute : graphics;
				if ( previous >= 0 ) ordered[previous, i] = true;
				var (wait, _, handOff) = recorder.DependenciesOf( i );
				if ( wait >= 0 ) ordered[wait, i] = true;
				if ( handOff >= 0 && graphics >= 0 )
					ordered[graphics, i] = true;
				if ( plan.Segments[i].Async ) compute = i;
				else graphics = i;
			}

			for ( int k = 0; k < count; k++ )
				for ( int i = 0; i < count; i++ )
					for ( int j = 0; j < count; j++ )
						ordered[i, j] |= ordered[i, k] && ordered[k, j];

			for ( int i = 0; i < passes.Length; i++ )
			{
				for ( int j = i + 1; j < passes.Length; j++ )
				{
					var a = passes[i];
					var b = passes[j];
					var ar = a.Reads( system.Frame );
					var br = b.Reads( system.Frame );
					if ( ((ar | br) & FrameResources.Untracked) == 0 && (a.Writes & (br | b.Writes)) == 0 && (ar & b.Writes) == 0 ) continue;
					var from = plan.Frames[0].Layers[i].Segment;
					var to = plan.Frames[0].Layers[j].Segment;
					Assert.IsTrue( from == to || ordered[from, to], $"sample {sample}: {a.Name} must complete before {b.Name}" );
				}
			}

			AssertSingleConsumers( recorder );
		}
	}

	[TestMethod]
	public void WarmPlanningDoesNotAllocate()
	{
		using var system = new RenderSystem();
		system.Layers = [new Pass( "producer", writes: FrameResources.QuarterDepth, compute: true ),
			new Pass( "tail", compute: true ), new Pass( "reader", reads: FrameResources.QuarterDepth )];
		using var recorder = new FrameRecorder();
		for ( int i = 0; i < 100; i++ )
			recorder.Plan( system.Frame, parallel: true, asyncCompute: true );

		var before = GC.GetAllocatedBytesForCurrentThread();
		for ( int i = 0; i < 100; i++ )
			recorder.Plan( system.Frame, parallel: true, asyncCompute: true );

		Assert.AreEqual( 0L, GC.GetAllocatedBytesForCurrentThread() - before );
	}
}
