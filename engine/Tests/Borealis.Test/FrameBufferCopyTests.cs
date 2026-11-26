using Borealis;
using Borealis.Gpu;
using BorealisTests.Headless;
using System.Linq;
using static BorealisTests.TestScenes;

namespace BorealisTests;

/// <summary>
/// The translucent layer's frame copy: made at the layer's first frame buffer reader, as native's
/// <c>CRenderBatchList::DrawCurrentPrimitives</c> makes it, however many record workers the layer is split across.
/// </summary>
[TestClass]
[DoNotParallelize]
public class FrameBufferCopyTests
{
	static HeadlessDevice Device => HeadlessRenderHost.Headless;

	static RenderMesh Glass( HeadlessMaterial material ) => new( new HeadlessMesh( "glass" ), UnitBox, [[new RenderMesh.Draw( 0, 0, 0, 36, 24, 0, material, Translucent: true )]] );

	[TestMethod]
	public void SplitTranslucentLayerCopiesTheFrameOnce()
	{
		var world = new RenderWorld();
		using var material = new HeadlessMaterial( "glass", "complex" );
		RenderMesh[] glass = [Glass( material ), Glass( material )];
		for ( int i = 0; i < 200; i++ )
			world.Add( new MeshObject( glass[i % 2], new Transform( new Vector3( 20 + i, (i % 9) - 4, 0 ) ) ) { WantsFrameBufferCopy = true } );

		using var target = ViewTarget.Create( new Vector2Int( 640, 480 ) );
		using var system = new RenderSystem { ParallelRecording = true };

		using var frame = Device.Capture();
		RenderHost.Current.NextFrame();
		system.Render( world, LookingDownX(), target );

		var translucentLists = frame.Commands.Count( c => c.Op == GpuOp.Bind && c.Scope.Contains( "Translucent Forward" ) );
		if ( System.Environment.ProcessorCount > 2 )
			Assert.IsTrue( translucentLists > 1, $"the layer splits across record workers ({translucentLists} parts)" );

		Assert.AreEqual( 1, frame.Commands.Count( c => c.Op == GpuOp.CopyFrameBuffer ), "one copy, at the layer's first reader" );
	}

	/// <summary>
	/// A view full of glass records the refraction stencil's draws on a record worker, in one segment: its target cleared
	/// once and every glass mesh drawn into it.
	/// </summary>
	[TestMethod]
	public void RefractionStencilRecordsOnOneWorker()
	{
		var world = new RenderWorld();
		using var material = new HeadlessMaterial( "glass", "complex" );
		var meshes = new RenderMesh[200];
		for ( int i = 0; i < meshes.Length; i++ )
		{
			meshes[i] = Glass( material );
			world.Add( new MeshObject( meshes[i], new Transform( new Vector3( 20 + i, (i % 9) - 4, 0 ) ) ) { WantsFrameBufferCopy = true } );
		}

		using var target = ViewTarget.Create( new Vector2Int( 640, 480 ) );
		using var system = new RenderSystem { ParallelRecording = true };
		HeadlessRenderHost.WantsPlans = true;
		try
		{
			using var frame = Device.Capture();
			RenderHost.Current.NextFrame();
			system.Render( world, LookingDownX(), target, null, window: true );

			const string stencil = "Refraction Stencil Layer";
			var segments = HeadlessRenderHost.LastPlan.Segments.Where( s => s.First == stencil ).ToArray();
			Assert.AreEqual( 1, segments.Length, "one segment" );
			if ( System.Environment.ProcessorCount > 2 ) Assert.IsTrue( segments[0].Worker, "recorded on a worker" );

			Assert.AreEqual( 1, frame.Commands.Count( c => c.Op == GpuOp.Clear && c.In( stencil ) ), "its target cleared once" );
			var drawn = frame.Commands.Where( c => c.IsDraw && c.In( stencil ) ).Select( c => c.Mesh ).ToHashSet();
			Assert.IsTrue( meshes.All( drawn.Contains ), "every glass mesh drawn into it" );
		}
		finally
		{
			HeadlessRenderHost.WantsPlans = false;
			HeadlessRenderHost.LastPlan = null;
		}
	}

	/// <summary>
	/// Where a reader is on the screen (<see cref="FrameBufferReadArea"/>): in front, the rectangle around its bounds'
	/// projection; crossing the camera's plane, the whole screen; behind it, past the far plane or off to the side, nowhere.
	/// </summary>
	[TestMethod]
	public void ReadAreaIsWhereTheReadersAreOnScreen()
	{
		var view = LookingDownX();
		var toProjection = view.WorldToProjection;

		var area = FrameBufferReadArea.Empty;
		area.Add( new Vector3( 100, 0, 0 ), new Vector3( 1, 10, 10 ), toProjection );
		Assert.IsFalse( area.Whole || area.IsEmpty, "a reader in front is somewhere" );
		Assert.AreEqual( -10f / 99, area.Min.x, 0.002f, "its left at the near face's edge" );
		Assert.AreEqual( 10f / 99, area.Max.x, 0.002f );
		Assert.AreEqual( -10f / 99, area.Min.y, 0.002f );
		Assert.AreEqual( 10f / 99, area.Max.y, 0.002f );

		// A second widens it
		area.Add( new Vector3( 100, -40, 0 ), new Vector3( 1, 10, 10 ), toProjection );
		Assert.AreEqual( 50f / 99, area.Max.x, 0.002f, "right of the first: -y is to the right looking down +x" );

		foreach ( var (center, what) in new[] { (new Vector3( -100, 0, 0 ), "behind"), (new Vector3( 2000, 0, 0 ), "past the far plane"), (new Vector3( 100, 500, 0 ), "off the side") } )
		{
			var nowhere = FrameBufferReadArea.Empty;
			nowhere.Add( center, new Vector3( 1, 10, 10 ), toProjection );
			Assert.IsTrue( nowhere.IsEmpty, $"a reader {what} is nowhere" );
		}

		var around = FrameBufferReadArea.Empty;
		around.Add( Vector3.Zero, new Vector3( 5, 5, 5 ), toProjection );
		Assert.IsTrue( around.Whole, "a reader round the camera may be read anywhere" );
	}
}
