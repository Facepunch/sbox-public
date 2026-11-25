using Borealis;
using Borealis.Gpu;
using BorealisTests.Headless;
using System.Collections.Generic;
using System.Linq;
using static BorealisTests.TestScenes;

namespace BorealisTests;

/// <summary>
/// A child view rendered from a draw's callback in its parent's segment (a water body's refraction): left to finish after
/// its workers, so the parent's thread carries on, and finished, submitted and followed by what waits for it before the
/// parent submits (<see cref="RenderSystem.AfterSubmit"/>).
/// </summary>
// Frames go through Borealis's ShadowMapper, whose cache and frame counter are static
[TestClass]
[DoNotParallelize]
public class ChildViewTests
{
	static HeadlessDevice Device => HeadlessRenderHost.Headless;

	/// <summary>Renders the child view before the object's draws, once, noting what was submitted around it.</summary>
	sealed class ChildCallbacks( RenderSystem child, RenderWorld world, ViewTarget target ) : DrawCallbacks
	{
		public FrameCapture Capture;
		public int SubmittedBefore = -1, SubmittedAfter = -1, SubmittedAtReadable = -1;
		public bool LeftUnfinished, ReadableInCallback;

		internal override void Render( GpuContext context, ViewPass view, MeshLayer layer, bool after )
		{
			if ( after || SubmittedBefore >= 0 ) return;

			SubmittedBefore = Capture.Submissions.Count;
			child.MayFinishLater = true;
			child.Render( world, LookingDownX(), target );
			LeftUnfinished = child.IsUnfinished;
			child.AfterSubmit( _ => SubmittedAtReadable = Capture.Submissions.Count, target.Resolved );
			ReadableInCallback = SubmittedAtReadable >= 0;
			SubmittedAfter = Capture.Submissions.Count;
		}
	}

	[TestMethod]
	public void AChildViewInItsParentsSegmentIsSubmittedBeforeTheParent()
	{
		using var material = new HeadlessMaterial( "box", "complex" );
		var mesh = new RenderMesh( new HeadlessMesh( "box" ), UnitBox, [[new RenderMesh.Draw( 0, 0, 0, 36, 24, 0, material )]] );

		var world = new RenderWorld();
		var boxes = new List<MeshObject>();
		for ( int i = 0; i < 3; i++ )
		{
			var box = new MeshObject( mesh, new Transform( new Vector3( 30, i * 3 - 3, 0 ) ) );
			boxes.Add( box );
			world.Add( box );
		}

		using var target = ViewTarget.Create( new Vector2Int( 640, 480 ) );
		using var childTarget = ViewTarget.Create( new Vector2Int( 320, 240 ) );
		using var system = new RenderSystem();
		using var child = new RenderSystem();
		var callbacks = new ChildCallbacks( child, world, childTarget ) { Before = true };
		boxes[1].Callbacks = callbacks;

		IReadOnlyList<GpuSubmission> submissions;
		using ( var frame = Device.Capture() )
		{
			callbacks.Capture = frame;
			RenderHost.Current.NextFrame();
			system.Render( world, LookingDownX(), target );
			submissions = frame.Submissions.ToList();
		}

		// Left to finish inside the callback: nothing submitted there, and what waits for it not run yet
		Assert.IsTrue( callbacks.LeftUnfinished );
		Assert.IsFalse( callbacks.ReadableInCallback );
		Assert.AreEqual( callbacks.SubmittedBefore, callbacks.SubmittedAfter, "the child submitted inside its parent's segment" );

		// Finished with the parent: submitted, then what waited for it, then the parent's segments
		Assert.IsFalse( child.IsUnfinished );
		Assert.IsTrue( callbacks.SubmittedAtReadable > callbacks.SubmittedAfter, "the child's submissions before what reads it" );
		// The child's forward draws between the callback and what reads them, the parent's after: its segment holds the callback
		int ForwardDraws( int from, int to ) => submissions.Skip( from ).Take( to - from ).Sum( s => s.Commands.Count( c => c.IsDraw && c.In( "Opaque Forward" ) ) );
		Assert.IsTrue( ForwardDraws( callbacks.SubmittedAfter, callbacks.SubmittedAtReadable ) > 0, "the child's draws" );
		Assert.IsTrue( ForwardDraws( callbacks.SubmittedAtReadable, submissions.Count ) > 0, "the parent's draws" );
		Assert.AreEqual( 0, ForwardDraws( 0, callbacks.SubmittedAfter ) );
	}

	[TestMethod]
	public void WithTheSwitchOffAChildViewIsSubmittedInItsParentsSegment()
	{
		using var material = new HeadlessMaterial( "box", "complex" );
		var mesh = new RenderMesh( new HeadlessMesh( "box" ), UnitBox, [[new RenderMesh.Draw( 0, 0, 0, 36, 24, 0, material )]] );
		var world = new RenderWorld();
		var box = new MeshObject( mesh, new Transform( new Vector3( 30, 0, 0 ) ) );
		world.Add( box );

		using var target = ViewTarget.Create( new Vector2Int( 640, 480 ) );
		using var childTarget = ViewTarget.Create( new Vector2Int( 320, 240 ) );
		using var system = new RenderSystem();
		using var child = new RenderSystem();
		var callbacks = new ChildCallbacks( child, world, childTarget ) { Before = true };
		box.Callbacks = callbacks;

		RenderSystem.LeaveChildrenUnfinished = false;
		try
		{
			using var frame = Device.Capture();
			callbacks.Capture = frame;
			RenderHost.Current.NextFrame();
			system.Render( world, LookingDownX(), target );
		}
		finally
		{
			RenderSystem.LeaveChildrenUnfinished = true;
		}

		Assert.IsFalse( callbacks.LeftUnfinished );
		Assert.IsTrue( callbacks.ReadableInCallback );
		Assert.IsTrue( callbacks.SubmittedAfter > callbacks.SubmittedBefore );
	}
}
