using Sandbox.SceneRenderer;
using Sandbox.SceneRenderer.Gpu;
using System;
using System.Reflection;

namespace SceneRendererTests;

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

	static readonly FieldInfo AsyncSupport = typeof( RenderContext ).GetField( "supportsAsyncCompute", BindingFlags.Static | BindingFlags.NonPublic );
	static readonly FieldInfo Layers = typeof( RenderSystem ).GetField( "layers", BindingFlags.Instance | BindingFlags.NonPublic );
	static readonly FieldInfo Segments = typeof( FrameRecorder ).GetField( "segments", BindingFlags.Instance | BindingFlags.NonPublic );
	object _previousSupport;

	[TestInitialize]
	public void EnableCompute()
	{
		_previousSupport = AsyncSupport.GetValue( null );
		AsyncSupport.SetValue( null, true );
	}

	[TestCleanup]
	public void RestoreCompute() => AsyncSupport.SetValue( null, _previousSupport );

	static int WaitFor( FrameRecorder recorder, int index )
	{
		var segment = ((Array)Segments.GetValue( recorder )).GetValue( index );
		return (int)segment.GetType().GetField( "WaitFor" ).GetValue( segment );
	}

	static int SegmentFor( FrameRecorder recorder, string name ) => recorder.Snapshot( "test" ).Frames[0].Layers.Single( l => l.Name == name ).Segment;

	static void AssertSingleConsumers( FrameRecorder recorder )
	{
		var plan = recorder.Snapshot( "test" );
		var waits = Enumerable.Range( 0, plan.Segments.Count ).Select( i => WaitFor( recorder, i ) ).Where( i => i >= 0 ).ToArray();
		Assert.AreEqual( waits.Length, waits.Distinct().Count(), "binary semaphores must have one consumer" );
		Assert.IsTrue( waits.All( i => plan.Segments[i].Async ) );
		for ( int i = 0; i < plan.Segments.Count; i++ )
			Assert.IsTrue( WaitFor( recorder, i ) < i );
	}

	[TestMethod]
	public void IntermediateResultsReleaseGraphicsWithoutWaitingForTheRest()
	{
		using var system = new RenderSystem();
		Layers.SetValue( system, (RenderLayer[])[
			new Pass( "before" ),
			new Pass( "producer", writes: FrameResources.QuarterDepth, compute: true ),
			new Pass( "independent compute", compute: true ),
			new Pass( "reader", reads: FrameResources.QuarterDepth ),
			new Pass( "second reader", reads: FrameResources.QuarterDepth ),
			new UnknownPass()] );
		using var recorder = new FrameRecorder();
		recorder.Plan( system.Frame, parallel: true, asyncCompute: true );

		Assert.IsTrue( system.Frame.AsyncCompute );
		Assert.IsFalse( recorder.Parallel, "GPU overlap must not depend on CPU worker recording" );
		Assert.AreEqual( SegmentFor( recorder, "producer" ), WaitFor( recorder, SegmentFor( recorder, "reader" ) ) );
		Assert.AreEqual( SegmentFor( recorder, "reader" ), SegmentFor( recorder, "second reader" ), "same-queue ordering reuses the first acquisition" );
		Assert.AreEqual( SegmentFor( recorder, "independent compute" ), WaitFor( recorder, SegmentFor( recorder, "unknown access" ) ) );
		AssertSingleConsumers( recorder );
	}

	[TestMethod]
	public void GraphicsProducersSeparateComputeBatchesAutomatically()
	{
		using var system = new RenderSystem();
		Layers.SetValue( system, (RenderLayer[])[
			new Pass( "a", writes: FrameResources.QuarterDepth, compute: true ),
			new Pass( "b", writes: FrameResources.BloomObjects, compute: true ),
			new Pass( "tail", compute: true ),
			new Pass( "read a", reads: FrameResources.QuarterDepth ),
			new Pass( "read b", reads: FrameResources.BloomObjects ),
			new Pass( "graphics producer", writes: FrameResources.RefractionStencil ),
			new Pass( "second compute", reads: FrameResources.RefractionStencil, compute: true ),
			new Pass( "second graphics" )] );
		using var recorder = new FrameRecorder();
		recorder.Plan( system.Frame, parallel: true, asyncCompute: true );

		Assert.IsTrue( system.Frame.AsyncCompute );
		Assert.AreEqual( SegmentFor( recorder, "a" ), WaitFor( recorder, SegmentFor( recorder, "read a" ) ) );
		Assert.AreEqual( SegmentFor( recorder, "b" ), WaitFor( recorder, SegmentFor( recorder, "read b" ) ) );
		var plan = recorder.Snapshot( "test" );
		Assert.AreEqual( 4, plan.Segments.Count( s => s.Async ) );
		Assert.IsTrue( SegmentFor( recorder, "graphics producer" ) < SegmentFor( recorder, "second compute" ), "compute handoff follows its graphics producer" );
		Assert.AreEqual( SegmentFor( recorder, "tail" ), WaitFor( recorder, SegmentFor( recorder, "second compute" ) - 1 ) );
		Assert.AreEqual( SegmentFor( recorder, "second compute" ), WaitFor( recorder, plan.Segments.Count - 1 ), "the frame still joins outstanding compute" );
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
		Layers.SetValue( system, (RenderLayer[])[compute, new Pass( "tail", compute: true ),
			new Pass( "writer", writes: FrameResources.QuarterDepth ), new Pass( "consumer", reads: FrameResources.QuarterDepth )] );
		using var recorder = new FrameRecorder();
		recorder.Plan( system.Frame, parallel: true, asyncCompute: true );
		Assert.IsTrue( system.Frame.AsyncCompute );
		Assert.AreEqual( SegmentFor( recorder, "compute" ), WaitFor( recorder, SegmentFor( recorder, "writer" ) ) );
		AssertSingleConsumers( recorder );
	}

	[TestMethod]
	[DataRow( false, true, true )]
	[DataRow( true, false, true )]
	[DataRow( true, true, false )]
	public void DisabledOrUnsupportedSchedulingFallsBackAndClearsPreviousWaits( bool parallel, bool enabled, bool supported )
	{
		using var system = new RenderSystem();
		Layers.SetValue( system, (RenderLayer[])[new Pass( "compute", compute: true ), new Pass( "graphics" )] );
		using var recorder = new FrameRecorder();
		recorder.Plan( system.Frame, parallel: true, asyncCompute: true );
		Assert.IsTrue( system.Frame.AsyncCompute );
		AsyncSupport.SetValue( null, supported );
		recorder.Plan( system.Frame, parallel, enabled );
		Assert.IsFalse( system.Frame.AsyncCompute );
		Assert.IsFalse( recorder.SeparateContexts );
		Assert.IsTrue( recorder.Snapshot( "test" ).Segments.All( s => !s.Async ) );
		Assert.AreEqual( -1, WaitFor( recorder, 0 ) );
	}

	[TestMethod]
	public void CulledBranchesDoNotLeaveWaitsAndUnusedProducersStayCulled()
	{
		var graphics = new Pass( "graphics" );
		var unused = new Pass( "unused", writes: FrameResources.RefractionStencil, compute: true );
		using var system = new RenderSystem();
		Layers.SetValue( system, (RenderLayer[])[unused, new Pass( "compute", compute: true ), graphics] );
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
		Layers.SetValue( system, (RenderLayer[])[new Pass( "producer", writes: FrameResources.QuarterDepth, compute: true ),
			new Pass( "reader", reads: FrameResources.QuarterDepth ), new Pass( "later graphics" )] );
		using var recorder = new FrameRecorder();
		recorder.Plan( system.Frame, parallel: true, asyncCompute: true );
		Assert.IsFalse( system.Frame.AsyncCompute, "separate queues cannot overlap a single producer and its consumer" );
	}

	[TestMethod]
	public void ReadOnlyInputsDoNotSerializeQueues()
	{
		using var system = new RenderSystem();
		Layers.SetValue( system, (RenderLayer[])[new Pass( "compute", reads: FrameResources.Depth, compute: true ),
			new Pass( "graphics", reads: FrameResources.Depth )] );
		using var recorder = new FrameRecorder();
		recorder.Plan( system.Frame, parallel: true, asyncCompute: true );
		Assert.IsTrue( system.Frame.AsyncCompute );
		Assert.AreEqual( -1, WaitFor( recorder, SegmentFor( recorder, "graphics" ) ) );
		AssertSingleConsumers( recorder );
	}

	[TestMethod]
	public void UnknownAccessIsABoundaryEvenWithoutDeclaredInputs()
	{
		using var system = new RenderSystem();
		Layers.SetValue( system, (RenderLayer[])[new Pass( "compute", compute: true ), new UnknownPass(), new Pass( "graphics" )] );
		using var recorder = new FrameRecorder();
		recorder.Plan( system.Frame, parallel: true, asyncCompute: true );
		Assert.IsFalse( system.Frame.AsyncCompute, "work after the unknown pass cannot provide an overlap window" );
	}

	[TestMethod]
	public void SideEffectsAreKeptEvenWhenTheirOutputsAreUnused()
	{
		using var system = new RenderSystem();
		Layers.SetValue( system, (RenderLayer[])[new Pass( "output", writes: FrameResources.Depth ) { SideEffects = true }] );
		using var recorder = new FrameRecorder();
		recorder.Plan( system.Frame, parallel: true, asyncCompute: true );
		Assert.AreEqual( 1, recorder.Planned.Length );
	}

	[TestMethod]
	public void ComputeCannotCrossAChildFrame()
	{
		using var system = new RenderSystem();
		using var child = new RenderSystem();
		Layers.SetValue( child, (RenderLayer[])[new Pass( "child graphics" )] );
		Layers.SetValue( system, (RenderLayer[])[new Pass( "compute", compute: true ), new Pass( "independent" ),
			new ChildFramePass( child.Frame ), new Pass( "after child" )] );
		using var recorder = new FrameRecorder();
		recorder.Plan( system.Frame, parallel: true, asyncCompute: true );
		var plan = recorder.Snapshot( "test" );
		var childSegment = plan.Frames[1].Layers[0].Segment;
		Assert.IsTrue( system.Frame.AsyncCompute );
		Assert.IsFalse( child.Frame.AsyncCompute );
		Assert.AreEqual( SegmentFor( recorder, "compute" ), WaitFor( recorder, childSegment - 1 ) );
		AssertSingleConsumers( recorder );
	}

	[TestMethod]
	public void ComputeOutputPreparationCannotOverwriteEarlierInputs()
	{
		using var system = new RenderSystem();
		Layers.SetValue( system, (RenderLayer[])[new Pass( "reader", reads: FrameResources.QuarterDepth, compute: true ),
			new Pass( "writer", writes: FrameResources.QuarterDepth, compute: true ) { SideEffects = true },
			new Pass( "independent graphics" )] );
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

			Layers.SetValue( system, passes );
			recorder.Plan( system.Frame, parallel: true, asyncCompute: true );
			var plan = recorder.Snapshot( "test" );
			var count = plan.Segments.Count;
			var ordered = new bool[count, count];
			int graphics = -1, compute = -1;
			var segments = (Array)Segments.GetValue( recorder );
			for ( int i = 0; i < count; i++ )
			{
				var previous = plan.Segments[i].Async ? compute : graphics;
				if ( previous >= 0 ) ordered[previous, i] = true;
				var wait = WaitFor( recorder, i );
				if ( wait >= 0 ) ordered[wait, i] = true;
				var segment = segments.GetValue( i );
				if ( (int)segment.GetType().GetField( "HandOff" ).GetValue( segment ) >= 0 && graphics >= 0 )
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
		Layers.SetValue( system, (RenderLayer[])[new Pass( "producer", writes: FrameResources.QuarterDepth, compute: true ),
			new Pass( "tail", compute: true ), new Pass( "reader", reads: FrameResources.QuarterDepth )] );
		using var recorder = new FrameRecorder();
		for ( int i = 0; i < 100; i++ )
			recorder.Plan( system.Frame, parallel: true, asyncCompute: true );

		var before = GC.GetAllocatedBytesForCurrentThread();
		for ( int i = 0; i < 100; i++ )
			recorder.Plan( system.Frame, parallel: true, asyncCompute: true );

		Assert.AreEqual( 0L, GC.GetAllocatedBytesForCurrentThread() - before );
	}
}
