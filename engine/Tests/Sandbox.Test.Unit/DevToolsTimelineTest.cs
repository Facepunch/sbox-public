using Sandbox.Diagnostics;
using Sandbox.Rendering;
using System;
using System.Collections.Generic;
using System.Linq;

namespace EngineTests;

/// <summary>
/// The dev tools' GPU timelines: a layer recorded in pieces is one pass, chart scales hold still, and the Rendering tab's
/// pass tree matches passes to what drew them.
/// </summary>
// Publishing goes through ManagedFramePlan's statics - the latest plan and the ring of published ones - so these run
// one at a time rather than settling against a frame another test published
[TestClass, DoNotParallelize]
public class DevToolsTimelineTest
{
	static GpuFrameTimeline.Event Pass( string name, int parent, double start, double end, string queue = "graphics" ) =>
		new( name, parent, queue, start, end ) { Kind = "pass" };

	/// <summary>
	/// The managed renderer's pieces of one layer, back to back, become one pass from the first's start to the last's end,
	/// with what was under each piece under it; another layer between two runs of a name keeps them apart.
	/// </summary>
	[TestMethod]
	public void ConsecutivePiecesMerge()
	{
		GpuFrameTimeline.Event[] events =
		[
			Pass( "Depth Prepass", -1, 1, 2 ),
			Pass( "Depth Prepass", -1, 2, 3 ),
			Pass( "Ambient Occlusion", 1, 2.2, 2.8 ),
			Pass( "Opaque Forward", -1, 3, 4 ),
			Pass( "Depth Prepass", -1, 4, 5 ),
			Pass( "Hi-Z Depth Downsample", -1, 2, 2.5, "async-compute" ),
			new( "submit", -1, "graphics", 0.5, 0.5 ) { Kind = "host-submit" },
		];

		Assert.IsTrue( GpuFrameTimeline.BuildPaths( events ) );
		var merged = GpuFrameTimeline.MergePieces( events );
		var passes = merged.Where( x => x.Kind == "pass" ).ToArray();

		var depth = passes.Where( x => x.Name == "Depth Prepass" ).OrderBy( x => x.StartMs ).ToArray();
		Assert.AreEqual( 2, depth.Length, "the run of two pieces is one; the later one after Opaque Forward stays apart" );
		Assert.AreEqual( 1, depth[0].StartMs );
		Assert.AreEqual( 3, depth[0].EndMs );

		var ao = passes.Single( x => x.Name == "Ambient Occlusion" );
		Assert.AreEqual( "Depth Prepass", merged[ao.Parent].Name, "under the merged piece" );
		Assert.AreEqual( 1, merged[ao.Parent].StartMs );
		Assert.IsTrue( ao.Path.StartsWith( depth[0].Path + "/" ), "paths rebuilt under the merged pass" );

		Assert.AreEqual( 1, passes.Count( x => x.Queue == "async-compute" ), "other queues untouched" );
		Assert.AreEqual( 1, merged.Count( x => x.Kind == "host-submit" ), "submits kept" );
	}

	static DevTools.GpuSpan Span( string name, int n, float start, float end, string parent = null, bool async = false ) =>
		new( $"{parent}/{Uri.EscapeDataString( name )}#{n}", parent, name, parent is null ? 0 : 1, async, true, start, end, DevTools.PassKind.Other ) { AvgMs = end - start, MaxMs = end - start };

	static ManagedFramePlan.Layer Layer( string name, int order, int segment = 0, ManagedFramePlan.LayerState state = ManagedFramePlan.LayerState.Runs, int child = -1, double cpu = 0 ) =>
		new() { Name = name, State = state, Order = order, Segment = segment, ChildFrame = child, CpuMs = cpu };

	/// <summary>
	/// Through the managed renderer, a layer is the nth GPU pass of its name on its queue, in the order the layers ran: the
	/// 3D skybox's depth prepass, which runs after the camera's, is the second, and goes under a heading where the skybox
	/// ran. Passes no layer recorded stay where they ran; layers that didn't run are listed apart.
	/// </summary>
	[TestMethod]
	public void PassesMatchTheirLayers()
	{
		var timeline = new DevTools.GpuLayout();
		timeline.Spans.AddRange(
		[
			Span( "Setup", 1, 0, 0.1f ),
			Span( "Depth Prepass", 1, 0.1f, 1 ),
			Span( "Hi-Z Depth Downsample", 1, 1, 1.1f, async: true ),
			Span( "Opaque Forward", 1, 1.2f, 2 ),
			Span( "Depth Prepass", 2, 2, 2.2f ),
			Span( "Opaque Forward", 2, 2.2f, 2.5f ),
			Span( "Bloom", 1, 2.6f, 2.7f, parent: "/Managed%3A%20AfterOpaque#1" ),
			Span( "Managed: AfterOpaque", 1, 2.6f, 2.8f ),
		] );

		var plan = new ManagedFramePlan();
		plan.Segments.Add( new ManagedFramePlan.Segment() );
		plan.Segments.Add( new ManagedFramePlan.Segment { Async = true } );

		var camera = new ManagedFramePlan.Frame { Name = "Camera" };
		camera.Layers.Add( Layer( "Depth Prepass", 0, cpu: 1 ) );
		camera.Layers.Add( Layer( "Hi-Z Depth Downsample", 1, segment: 1 ) );
		camera.Layers.Add( Layer( "Contact Shadows", -1, state: ManagedFramePlan.LayerState.NotNeeded ) );
		camera.Layers.Add( Layer( "Opaque Forward", 2, cpu: 2 ) );
		camera.Layers.Add( Layer( "3D Skybox", 3, child: 1 ) );
		camera.Layers.Add( Layer( "Managed: AfterOpaque", 5 ) );
		plan.Frames.Add( camera );

		var sky = new ManagedFramePlan.Frame { Name = "3D Skybox", Skybox = true };
		sky.Layers.Add( Layer( "Depth Prepass", 3, cpu: 3 ) );
		sky.Layers.Add( Layer( "Opaque Forward", 4, cpu: 4 ) );
		plan.Frames.Add( sky );

		var passes = new List<DevTools.RenderPass>();
		var skipped = new List<DevTools.SkippedLayer>();
		DevTools.BuildPasses( timeline, plan, null, DevTools.PassOrder.Started, passes, skipped );

		string[] order = ["Setup", "Depth Prepass", "Hi-Z Depth Downsample", "Opaque Forward", "3D Skybox", "Depth Prepass", "Opaque Forward", "Managed: AfterOpaque", "Bloom"];
		CollectionAssert.AreEqual( order, passes.Select( x => x.Name ).ToArray(), "in the order they started, the skybox's in its place" );

		Assert.IsNull( passes[0].Layer, "Setup is no layer's" );
		Assert.AreEqual( 1, passes[1].Layer?.CpuMs, "the camera's prepass is the first of its name" );
		Assert.IsTrue( passes[2].Async && passes[2].Layer is not null, "async layers match async passes" );

		var heading = passes[4];
		Assert.IsTrue( heading.Heading );
		Assert.AreEqual( 0.5f, heading.GpuMs, 0.001f, "a heading's time is what's under it" );
		Assert.AreEqual( 3, passes[5].Layer?.CpuMs, "the skybox's prepass is the second of its name" );
		Assert.AreEqual( 4, passes[5].Parent, "under the heading" );
		Assert.AreEqual( 1, passes[5].Depth );
		Assert.AreEqual( 4, passes[6].Layer?.CpuMs );

		Assert.AreEqual( 7, passes[8].Parent, "a pass inside a layer's scope is under it" );

		Assert.AreEqual( 1, skipped.Count );
		Assert.AreEqual( "Contact Shadows", skipped[0].Name );

		DevTools.BuildPasses( timeline, plan, null, DevTools.PassOrder.Gpu, passes, skipped );
		Assert.AreEqual( "Depth Prepass", passes[0].Name, "by GPU time: the biggest first" );

		DevTools.BuildPasses( timeline, plan, null, DevTools.PassOrder.Recording, passes, skipped );
		Assert.AreEqual( "3D Skybox", passes[0].Name, "by recording: the skybox's layers between them recorded the most" );
	}

	/// <summary>
	/// Through native, each top level pass goes under the scene view the GPU profiler has it in, and a view's passes in a row
	/// share one heading. The profiler lists views in the order it first saw them, so a name two views share goes with the
	/// view of the pass next to it: the main view's light binner runs first though the skybox view is listed first. The
	/// global scope gets no heading.
	/// </summary>
	[TestMethod]
	public void NativePassesGroupByView()
	{
		var timeline = new DevTools.GpuLayout();
		timeline.Spans.AddRange(
		[
			Span( "LightBinner", 1, 0, 0.05f ),
			Span( "DrawDepthOpaque", 1, 0.05f, 0.1f ),
			Span( "DrawDepthOpaque", 2, 0.1f, 0.3f ),
			Span( "Clear Color", 1, 0.3f, 0.4f ),
			Span( "Depth Prepass", 1, 0.4f, 1 ),
			Span( "LightBinner", 2, 1, 1.05f ),
			Span( "Sky", 1, 1.05f, 1.1f ),
			Span( "Occlusion Stats", 1, 1.1f, 1.2f ),
		] );

		(string, string)[] views =
		[
			("CSM Cascade 0", "DrawDepthOpaque"),
			("CSM Cascade 1", "DrawDepthOpaque"),
			("3DSkyboxView", "LightBinner"),
			("3DSkyboxView", "Sky"),
			("RenderToSwapChain", "LightBinner"),
			("RenderToSwapChain", "Clear Color"),
			("RenderToSwapChain", "Depth Prepass"),
			("GLOBAL", "Occlusion Stats"),
		];

		var passes = new List<DevTools.RenderPass>();
		DevTools.BuildPasses( timeline, null, views, DevTools.PassOrder.Started, passes, new() );

		string[] order =
		[
			"RenderToSwapChain", "LightBinner",
			"CSM Cascade 0", "DrawDepthOpaque",
			"CSM Cascade 1", "DrawDepthOpaque",
			"RenderToSwapChain", "Clear Color", "Depth Prepass",
			"3DSkyboxView", "LightBinner", "Sky",
			"Occlusion Stats",
		];
		CollectionAssert.AreEqual( order, passes.Select( x => x.Name ).ToArray() );
		Assert.AreEqual( 0.2f, passes[5].GpuMs, 0.001f, "the second cascade's pass is the second of its name" );
		Assert.AreEqual( 6, passes[8].Parent, "a view's passes in a row share its heading" );
		Assert.AreEqual( -1, passes[12].Parent, "the global scope isn't a view" );
	}

	/// <summary>
	/// A pass's kind comes from its name, both renderers' names, and a name that says nothing is its parent's kind.
	/// </summary>
	[TestMethod]
	public void PassKindsByName()
	{
		Assert.AreEqual( DevTools.PassKind.Depth, DevTools.KindOf( "Depth Normal Prepass (Small)", DevTools.PassKind.Other ) );
		Assert.AreEqual( DevTools.PassKind.Depth, DevTools.KindOf( "CSM Cascade 2", DevTools.PassKind.Other ) );
		Assert.AreEqual( DevTools.PassKind.Opaque, DevTools.KindOf( "Static Opaque Forward", DevTools.PassKind.Other ) );
		Assert.AreEqual( DevTools.PassKind.Opaque, DevTools.KindOf( "Static World Overlays", DevTools.PassKind.Other ) );
		Assert.AreEqual( DevTools.PassKind.Translucent, DevTools.KindOf( "Translucent Forward", DevTools.PassKind.Other ) );
		Assert.AreEqual( DevTools.PassKind.Post, DevTools.KindOf( "AO Depth Chain", DevTools.PassKind.Other ), "AO before depth" );
		Assert.AreEqual( DevTools.PassKind.Post, DevTools.KindOf( "Managed: AfterDepthPrepass (async compute)", DevTools.PassKind.Other ), "a stage hook, not a depth pass" );
		Assert.AreEqual( DevTools.PassKind.Ui, DevTools.KindOf( "Managed: PlatformOverlay", DevTools.PassKind.Other ) );
		Assert.AreEqual( DevTools.PassKind.Other, DevTools.KindOf( "Clear Color Depth Stencil", DevTools.PassKind.Other ) );
		Assert.AreEqual( DevTools.PassKind.Ui, DevTools.KindOf( "CommandList", DevTools.PassKind.Ui ), "a command list is what it's in" );
	}

	/// <summary>
	/// A chart's scale grows at once to fit, holds while what it shows is between half and all of it, and lands on 1, 2 or 5.
	/// </summary>
	[TestMethod]
	public void ChartScaleHolds()
	{
		var top = DevToolsSparkline.HoldScale( 0, 13 );
		Assert.AreEqual( 20, top );
		Assert.AreEqual( 20, DevToolsSparkline.HoldScale( top, 11 ), "within the band: held" );
		Assert.AreEqual( 20, DevToolsSparkline.HoldScale( top, 19 ), "within the band: held" );
		Assert.AreEqual( 50, DevToolsSparkline.HoldScale( top, 21 ), "outgrown: up a step" );
		Assert.AreEqual( 5, DevToolsSparkline.HoldScale( top, 4 ), "well under: down to fit" );
	}

	/// <summary>
	/// A published plan stays as its frame was, found by its submission's tag for pairing with its GPU frame; the settled
	/// copy the overlay shows moves part way from the last one.
	/// </summary>
	[TestMethod]
	public void PlansKeepTheirFrameAsItWas()
	{
		var camera = $"PlansKeepTheirFrameAsItWas {Guid.NewGuid()}";
		ManagedFramePlan Plan( double ms )
		{
			var plan = new ManagedFramePlan { Camera = camera, RecordMs = ms };
			plan.Segments.Add( new ManagedFramePlan.Segment { Ms = ms } );
			var frame = new ManagedFramePlan.Frame { Name = "Camera" };
			frame.Layers.Add( Layer( "Opaque Forward", 0, cpu: ms ) );
			plan.Frames.Add( frame );
			return plan;
		}

		Plan( 10 ).Publish();
		var second = Plan( 20 );
		second.SubmitTag = long.MaxValue - 1;
		second.Publish();

		Assert.AreSame( second, ManagedFramePlan.WithTag( long.MaxValue - 1 ) );
		Assert.IsNull( ManagedFramePlan.WithTag( 0 ), "untagged submissions have no plan" );
		Assert.AreEqual( 20, second.RecordMs );
		Assert.AreEqual( 20, second.Segments[0].Ms );
		Assert.AreEqual( 20, second.Frames[0].Layers[0].CpuMs );

		var settled = ManagedFramePlan.Latest;
		Assert.AreNotSame( second, settled );
		Assert.AreEqual( 13, settled.RecordMs, 0.001 );
		Assert.AreEqual( 13, settled.Segments[0].Ms, 0.001 );
		Assert.AreEqual( 13, settled.Frames[0].Layers[0].CpuMs, 0.001 );
	}

	/// <summary>
	/// The render thread submits a frame a little after the managed renderer hands it over. A GPU frame's host submits,
	/// on the host clock, carry the tag of the frame they were handed over with, and every camera handed over in that
	/// engine frame is in that GPU frame, so each camera's delay is measured against its own handover even when a batched
	/// submission kept only the first camera's tag. Delays are settled for each camera, by its view rather than its name:
	/// cameras sit at different places in a GPU frame, so their offsets differ.
	/// </summary>
	[TestMethod]
	public void SubmitDelayIsFromTheTaggedFrame()
	{
		var frequency = System.Diagnostics.Stopwatch.Frequency;
		long Ms( double ms ) => (long)(ms * frequency / 1000.0);
		var a = new object();
		var b = new object();
		var zero = System.Diagnostics.Stopwatch.GetTimestamp();
		var engineFrame = Application.FrameCount;

		try
		{
			ManagedFramePlan.Want();
			long first = 0, second = 0;
			for ( int i = 0; i < 50; i++ )
			{
				// One engine frame, one GPU frame: A hands over at 0 and its submit starts the GPU frame 1 ms later; B, of the
				// same name, hands over at 10 ms. The render thread batched both, so only A's tag comes back
				Application.FrameCount = engineFrame + 1000 + (ulong)i;
				first = ManagedFramePlan.NoteHandedOver( zero, a );
				second = ManagedFramePlan.NoteHandedOver( zero + Ms( 10 ), b );
				ManagedFramePlan.NoteGpuFrame( first, zero + Ms( 1 ), 0 );
			}

			Assert.AreNotEqual( 0, first );
			Assert.AreNotEqual( first, second );
			Assert.AreEqual( 1, ManagedFramePlan.SubmitDelayTicks( a ) * 1000.0 / frequency, 0.01 );
			Assert.AreEqual( -9, ManagedFramePlan.SubmitDelayTicks( b ) * 1000.0 / frequency, 0.01, "B's plan starts 9 ms before its frame's handover" );

			// B's own tag coming back as well, in a later submit of the same GPU frame, doesn't count the frame twice
			ManagedFramePlan.NoteGpuFrame( second, zero + Ms( 11 ), 10 );
			Assert.AreEqual( -9, ManagedFramePlan.SubmitDelayTicks( b ) * 1000.0 / frequency, 0.01 );

			// A tag nothing was handed over with changes nothing
			ManagedFramePlan.NoteGpuFrame( long.MaxValue, zero, 0 );
			Assert.AreEqual( 1, ManagedFramePlan.SubmitDelayTicks( a ) * 1000.0 / frequency, 0.01 );

			// B's plan pairs with the GPU frame whose submission only carried A's tag
			var plan = new ManagedFramePlan { Camera = "Camera", View = b, SubmitTag = second };
			plan.Publish();
			Assert.AreSame( plan, ManagedFramePlan.WithTag( first ) );
		}
		finally
		{
			Application.FrameCount = engineFrame;
		}
	}
}
