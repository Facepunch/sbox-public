using Basalt;
using Basalt.Features;
using Basalt.Gpu;
using BasaltTests.Headless;
using System.Collections.Generic;
using System.Linq;
using static BasaltTests.TestScenes;

namespace BasaltTests;

/// <summary>
/// An object's own work around its draws (<see cref="DrawCallbacks"/>, a renderer's <c>ExecuteBefore</c> and
/// <c>ExecuteAfter</c> command lists): run in colour layers only, before and after the object's draws, on the main thread.
/// </summary>
// Frames go through Basalt's ShadowMapper, whose cache and frame counter are static
[TestClass]
[DoNotParallelize]
public class DrawCallbacksTests
{
	static HeadlessDevice Device => HeadlessRenderHost.Headless;

	/// <summary>
	/// Records each call, and marks it in the frame's log with a barrier on its own buffer.
	/// </summary>
	sealed class RecordingCallbacks : DrawCallbacks
	{
		public readonly List<(string Layer, bool After)> Calls = new();
		public readonly GpuBuffer<uint> Marker = new( 1, GpuBuffer.UsageFlags.Structured, "callback marker" );

		internal override void Render( GpuContext context, ViewPass view, MeshLayer layer, bool after )
		{
			Calls.Add( (layer.Name, after) );
			context.BufferBarrier( Marker, GpuStage.FragmentShader, after ? GpuAccess.IndexRead : GpuAccess.UniformRead, GpuStage.FragmentShader, GpuAccess.UniformRead );
		}
	}

	[TestMethod]
	public void CallbacksRunAroundTheObjectsDrawsInColourLayers()
	{
		using var material = new HeadlessMaterial( "box", "complex" );
		var mesh = new RenderMesh( new HeadlessMesh( "box" ), UnitBox, [[new RenderMesh.Draw( 0, 0, 0, 36, 24, 0, material )]] );

		var world = new RenderWorld();
		var callbacks = new RecordingCallbacks { Before = true, After = true };
		var boxes = new List<MeshObject>();
		for ( int i = 0; i < 3; i++ )
		{
			var box = new MeshObject( mesh, new Transform( new Vector3( 30, i * 3 - 3, 0 ) ) );
			boxes.Add( box );
			world.Add( box );
		}

		boxes[1].Callbacks = callbacks;

		using var target = ViewTarget.Create( new Vector2Int( 640, 480 ) );
		using var system = new RenderSystem();

		List<GpuCommand> commands;
		using ( var frame = Device.Capture() )
		{
			RenderHost.Current.NextFrame();
			system.Render( world, LookingDownX(), target );
			commands = frame.Commands.ToList();
		}

		// In a run of its own
		var opaque = system.GetFeature<MeshRenderFeature>().RunsFor( system.MainPass ).Opaque;
		Assert.IsTrue( opaque.Any( r => r.Callbacks == callbacks && r.Count == 1 ) );
		Assert.AreEqual( 2, opaque.Where( r => r.Callbacks is null ).Sum( r => r.Count ) );

		// Once before and once after, in the forward layer, never in the prepass
		CollectionAssert.AreEqual( new[] { ("Opaque Forward", false), ("Opaque Forward", true) }, callbacks.Calls );

		// Commands are records, so by position rather than by value
		var marks = Enumerable.Range( 0, commands.Count ).Where( i => commands[i].Op == GpuOp.BufferBarrier && commands[i].Buffer == callbacks.Marker ).ToList();
		var forward = Enumerable.Range( 0, commands.Count ).Where( i => commands[i].IsDraw && commands[i].In( "Opaque Forward" ) ).ToList();
		Assert.AreEqual( 2, marks.Count );

		// Exactly its one draw between them, and the other boxes' outside
		var between = forward.Where( i => i > marks[0] && i < marks[1] ).ToList();
		Assert.AreEqual( 1, between.Count );
		Assert.AreEqual( 1, commands[between[0]].Count );
		Assert.AreEqual( 2, forward.Count, "the other two instanced together, and it alone" );

		callbacks.Marker.Dispose();
	}

	/// <summary>
	/// An opaque custom object that draws nothing.
	/// </summary>
	sealed class OpaqueCustom : CustomObject
	{
		public OpaqueCustom( Vector3 position )
		{
			LocalBounds = UnitBox;
			Transform = new Transform( position );
			IsOpaque = true;
		}

		internal override void Render( GpuContext context, ViewPass view, MeshLayer layer ) { }
	}

	/// <summary>
	/// Custom objects record on the main thread, but the meshes sorted after them in the same layer still go to workers,
	/// in segments of their own.
	/// </summary>
	[TestMethod]
	public void CustomObjectsLeaveTheLayersMeshesToWorkers()
	{
		var world = new RenderWorld();
		for ( int i = 0; i < 200; i++ )
			world.Add( new MeshObject( TestMesh( false ), new Transform( new Vector3( 30 + i % 20, i / 20 - 5, 0 ) ) ) );
		for ( int i = 0; i < 3; i++ )
			world.Add( new OpaqueCustom( new Vector3( 25, i * 2 - 2, 0 ) ) );

		using var system = new RenderSystem();
		CollectAndPrepare( system, world, LookingDownX() );
		using var recorder = new FrameRecorder();
		recorder.Plan( system.Frame, parallel: true );
		Assert.IsTrue( recorder.Parallel );

		var plan = recorder.Snapshot( "test" );
		var layers = plan.Frames[0].Layers;
		var opaque = layers.FindIndex( l => l.Name == "Opaque Forward" );
		var first = layers[opaque].Segment;
		var end = layers.Skip( opaque + 1 ).Select( l => l.Segment ).First( s => s > first );
		var segments = plan.Segments.Skip( first ).Take( end - first ).ToList();

		// The custom objects sort first, on the main thread; the meshes after them on workers
		Assert.IsFalse( segments[0].Worker );
		Assert.IsTrue( segments.Skip( 1 ).Any( s => s.Worker ) );
	}

	[TestMethod]
	public void CallbacksKeepColourLayersOnTheMainThread()
	{
		var world = new RenderWorld();
		var box = new MeshObject( TestMesh( false ), new Transform( new Vector3( 30, 0, 0 ) ) );
		world.Add( box );

		using var system = new RenderSystem();
		CollectAndPrepare( system, world, LookingDownX() );
		Assert.AreEqual( (MeshRuns)0, system.GetFeature<MeshRenderFeature>().RunsFor( system.MainPass ).Callbacks );

		box.Callbacks = new RecordingCallbacks { Before = true };
		CollectAndPrepare( system, world, LookingDownX() );
		var lists = system.GetFeature<MeshRenderFeature>().RunsFor( system.MainPass );
		Assert.AreEqual( MeshRuns.Opaque, lists.Callbacks & MeshRuns.Opaque );
		Assert.AreSame( box.Callbacks, lists.Opaque.Single().Callbacks );
	}
}
