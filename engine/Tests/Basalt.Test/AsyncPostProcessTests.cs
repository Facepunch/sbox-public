using Basalt;
using Basalt.Gpu;
using System;
using System.Reflection;
using static BasaltTests.TestScenes;

namespace BasaltTests;

[TestClass]
[DoNotParallelize]
public class AsyncPostProcessTests
{
	sealed class Stages( bool reflections ) : IRenderStages
	{
		public void Render( RenderStage stage, in StageTarget target ) { }
		public bool HasEarlyPostProcess( RenderStage stage, StageQueue queue ) => stage == RenderStage.AfterDepthPrepass
			&& (queue == StageQueue.AsyncCompute || reflections);
	}

	static readonly FieldInfo Segments = typeof( FrameRecorder ).GetField( "segments", BindingFlags.Instance | BindingFlags.NonPublic );

	static int WaitFor( FrameRecorder recorder, int index )
	{
		var segment = ((Array)Segments.GetValue( recorder )).GetValue( index );
		return (int)segment.GetType().GetField( "WaitFor" ).GetValue( segment );
	}

	[TestMethod]
	[DataRow( true, true, true, true, 2, 80 )]
	[DataRow( false, true, true, true, 1, 80 )]
	[DataRow( true, false, true, true, 0, 80 )]
	[DataRow( true, true, false, true, 0, 80 )]
	[DataRow( true, true, true, false, 0, 80 )]
	[DataRow( true, true, true, true, 2, 1 )]
	[DataRow( false, true, true, true, 1, 1 )]
	[DataRow( true, false, true, true, 0, 1 )]
	public void WaitsForDepthBeforeReflectionsAndAllComputeBeforeNormalHooks( bool reflections, bool enabled, bool parallel, bool supported, int computeSegments, int objects )
	{
		using ( HeadlessRenderHost.Headless.WithAsyncCompute( supported ) )
		{
			var world = new RenderWorld();
			for ( int i = 0; i < objects; i++ )
				world.Add( new MeshObject( TestMesh( false ), new Transform( new Vector3( 30, 0, 0 ) ) ) );

			using var system = new RenderSystem();
			CollectAndPrepare( system, world, LookingDownX() );
			system.Frame.Stages = new Stages( reflections );
			using var recorder = new FrameRecorder();
			recorder.Plan( system.Frame, parallel, enabled );
			var plan = recorder.Snapshot( "test" );
			Assert.AreEqual( computeSegments, plan.Segments.Count( s => s.Async ) );
			if ( objects == 1 ) Assert.IsFalse( recorder.Parallel, "GPU overlap must not require CPU worker recording" );
			if ( computeSegments > 0 ) Assert.IsTrue( recorder.SeparateContexts );
			var waits = Enumerable.Range( 0, plan.Segments.Count ).Select( i => WaitFor( recorder, i ) ).Where( i => i >= 0 ).ToArray();
			Assert.AreEqual( computeSegments, waits.Length );
			Assert.AreEqual( waits.Length, waits.Distinct().Count(), "a binary semaphore must never have two consumers" );
			Assert.IsTrue( waits.All( i => plan.Segments[i].Async ) );

			if ( computeSegments == 2 )
			{
				var layers = plan.Frames[0].Layers;
				var depth = layers.Single( l => l.Name == "Hi-Z Depth Downsample" ).Segment;
				var ao = layers.Single( l => l.Name.EndsWith( "(async compute)" ) ).Segment;
				var ssr = layers.Single( l => l.Name.EndsWith( "(early graphics)" ) ).Segment;
				var hooks = layers.Single( l => l.Name == "Managed: AfterDepthPrepass" ).Segment;
				Assert.IsTrue( layers.FindIndex( l => l.Name.EndsWith( "(async compute)" ) ) < layers.FindIndex( l => l.Name == "Contact Shadows" ), "AO gets the overlap window before contact shadows" );
				Assert.AreEqual( depth, WaitFor( recorder, ssr ) );
				Assert.AreNotEqual( ao, WaitFor( recorder, ssr ), "SSR must not wait for AO" );
				Assert.IsTrue( Enumerable.Range( ssr + 1, hooks - ssr ).Any( i => WaitFor( recorder, i ) == ao ), "ordinary camera hooks must acquire AO" );
			}

			// Reusing a recorder after an async frame must not retain waits or early-effect execution.
			recorder.Plan( system.Frame, parallel: false, asyncCompute: false );
			Assert.IsFalse( recorder.Snapshot( "serial" ).Segments.Any( s => s.Async ) );
		}
	}
}
