using Borealis;
using Borealis.Features;
using Borealis.Gpu;
using BorealisTests.Headless;
using System.Collections.Generic;
using System.Linq;
using static BorealisTests.TestScenes;

namespace BorealisTests;

/// <summary>
/// An object's own work around its draws (<see cref="DrawCallbacks"/>, a renderer's <c>ExecuteBefore</c> and
/// <c>ExecuteAfter</c> command lists): run in colour layers only, before and after the object's draws, on the main thread.
/// </summary>
// Frames go through Borealis's ShadowMapper, whose cache and frame counter are static
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
	/// Custom objects record on the main thread, but the meshes drawn before them in the same layer still go to workers,
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

		// The meshes first, on workers; the custom objects after them, on the main thread
		Assert.IsTrue( segments[0].Worker );
		Assert.IsFalse( segments[^1].Worker );
	}

	/// <summary>
	/// An opaque custom object that marks where it draws in the frame's log with a barrier on its own buffer.
	/// </summary>
	sealed class MarkingCustom : CustomObject
	{
		public readonly GpuBuffer<uint> Marker = new( 1, GpuBuffer.UsageFlags.Structured, "custom marker" );

		public MarkingCustom( Vector3 position )
		{
			LocalBounds = UnitBox;
			Transform = new Transform( position );
			IsOpaque = true;
		}

		internal override void Render( GpuContext context, ViewPass view, MeshLayer layer ) =>
			context.BufferBarrier( Marker, GpuStage.FragmentShader, GpuAccess.UniformRead, GpuStage.FragmentShader, GpuAccess.UniformRead );
	}

	/// <summary>
	/// An opaque custom object draws after the opaque layer's meshes, as native draws the world before dynamic objects: one
	/// that blends without writing depth (a sprite batch drawn opaque and additive) in front of a mesh isn't painted over by it.
	/// </summary>
	[TestMethod]
	public void OpaqueCustomObjectsDrawAfterTheMeshes()
	{
		using var material = new HeadlessMaterial( "box", "complex" );
		var mesh = new RenderMesh( new HeadlessMesh( "box" ), UnitBox, [[new RenderMesh.Draw( 0, 0, 0, 36, 24, 0, material )]] );

		var world = new RenderWorld();
		for ( int i = 0; i < 3; i++ )
			world.Add( new MeshObject( mesh, new Transform( new Vector3( 30, i * 3 - 3, 0 ) ) ) );
		var custom = new MarkingCustom( new Vector3( 20, 0, 0 ) );
		world.Add( custom );

		using var target = ViewTarget.Create( new Vector2Int( 640, 480 ) );
		using var system = new RenderSystem();

		List<GpuCommand> commands;
		using ( var frame = Device.Capture() )
		{
			RenderHost.Current.NextFrame();
			system.Render( world, LookingDownX(), target );
			commands = frame.Commands.ToList();
		}

		var marks = Enumerable.Range( 0, commands.Count ).Where( i => commands[i].Op == GpuOp.BufferBarrier && commands[i].Buffer == custom.Marker && commands[i].In( "Opaque Forward" ) ).ToList();
		var forward = Enumerable.Range( 0, commands.Count ).Where( i => commands[i].IsDraw && commands[i].In( "Opaque Forward" ) ).ToList();
		Assert.AreEqual( 1, marks.Count );
		Assert.IsTrue( forward.Count > 0 );
		Assert.IsTrue( forward.All( i => i < marks[0] ), "every mesh draw before the custom object's" );

		custom.Marker.Dispose();
	}

	/// <summary>
	/// An opaque custom object that notes the tools pass each of its draws is given.
	/// </summary>
	sealed class ToolsPassCustom : CustomObject
	{
		public readonly List<ToolsPass> Seen = new();

		public ToolsPassCustom( Vector3 position )
		{
			LocalBounds = UnitBox;
			Transform = new Transform( position );
			IsOpaque = true;
		}

		internal override void Render( GpuContext context, ViewPass view, MeshLayer layer ) => Seen.Add( context.ToolsPass );
	}

	/// <summary>
	/// The wireframe view mode is the scene's meshes': a custom object (a tool view's overlay lines) draws as it asks, as
	/// native's wireframe override replaces only scene objects' material modes, so tool lines keep their colours.
	/// </summary>
	[TestMethod]
	public void CustomObjectsDrawTheirOwnWayInWireframe()
	{
		var world = new RenderWorld();
		world.Add( new MeshObject( TestMesh( false ), new Transform( new Vector3( 30, 0, 0 ) ) ) );
		var custom = new ToolsPassCustom( new Vector3( 20, 0, 0 ) );
		world.Add( custom );

		using var target = ViewTarget.Create( new Vector2Int( 64, 64 ) );
		using var system = new RenderSystem();
		var view = LookingDownX();
		view.Wireframe = true;
		RenderHost.Current.NextFrame();
		system.Render( world, view, target );

		Assert.IsTrue( custom.Seen.Count > 0 );
		Assert.IsTrue( custom.Seen.All( x => x == ToolsPass.None ), string.Join( ", ", custom.Seen ) );
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
