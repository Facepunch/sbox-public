using Borealis;
using Borealis.Features;
using BorealisTests.Headless;
using System;
using System.Linq;
using static BorealisTests.TestScenes;

namespace BorealisTests;

/// <summary>
/// The texture sizes draws ask streaming for (<see cref="TextureSizes"/>), worked out as native's
/// <c>GeneratePrimitivesArgs_t</c> does: UV density times the view's pixels per world unit at the object's distance, or
/// the object's screen size scaled up when the draw has no UV density; a run asks for its largest object's.
/// </summary>
// Frames go through Borealis's ShadowMapper, whose cache and frame counter are static
[TestClass]
[DoNotParallelize]
public class TextureSizeTests
{
	static HeadlessDevice Device => HeadlessRenderHost.Headless;

	[TestMethod]
	public void DrawSizesFollowUvDensityOrScreenSize()
	{
		// 1000 pixels wide at 90 degrees: 500 pixels per world unit at distance 1
		var sizes = new TextureSizes( LookingDownX(), LookingDownX() );

		// A unit box 30 away: 29 to its nearest face
		var (scale, fallback) = sizes.ForObject( new Vector3( 30, 0, 0 ), new Vector3( 1 ) );
		Assert.AreEqual( 500.0f / 29.0f, scale, 1e-3f );
		Assert.AreEqual( 4 * 500 / 29, TextureSizes.ForDraw( 4, scale, fallback ) );

		// Without a UV density: its bounding sphere's screen size (radius over distance), times the viewport width, times 2
		Assert.AreEqual( (int)(MathF.Sqrt( 3 ) / 30.0f * 1000 * 2), TextureSizes.ForDraw( 0, scale, fallback ) );

		// Inside it, the nearest face is no nearer than 1, and the whole view
		var (insideScale, insideFallback) = sizes.ForObject( Vector3.Zero, new Vector3( 1 ) );
		Assert.AreEqual( 500.0f, insideScale, 1e-3f );
		Assert.AreEqual( 2000, TextureSizes.ForDraw( 0, insideScale, insideFallback ) );

		// No more than a uint16 holds
		Assert.AreEqual( ushort.MaxValue, TextureSizes.ForDraw( 1000, insideScale, insideFallback ) );
	}

	[TestMethod]
	public void OrthographicViewsAskForEveryMip()
	{
		var view = LookingDownX();
		view.Orthographic = true;
		view.OrthoSize = new Vector2( 100, 100 );
		view.Update();

		// Native's orthographic frustum has a negative field of view: draws with a UV density ask for all mips, and an
		// aggregate's fragments with one don't raise its size
		var sizes = new TextureSizes( view, view );
		var (scale, fallback) = sizes.ForObject( new Vector3( 30, 0, 0 ), new Vector3( 1 ) );
		Assert.AreEqual( TextureSizes.All, TextureSizes.ForDraw( 4, scale, fallback ) );
		Assert.AreEqual( 0.0f, sizes.ForFragment( 4, new Vector3( 30, 0, 0 ), new Vector3( 1 ), view.Position, fallback ) );

		// The fallback's screen size is the sphere's diameter over the view's width
		Assert.AreEqual( (int)(2 * MathF.Sqrt( 3 ) / 100 * 1000 * 2), TextureSizes.ForDraw( 0, scale, fallback ) );
	}

	[TestMethod]
	public void InstancedRunAsksForItsNearestObject()
	{
		using var material = new HeadlessMaterial( "box", "complex" );
		var mesh = new RenderMesh( new HeadlessMesh( "box" ), UnitBox, [[new RenderMesh.Draw( 0, 0, 0, 36, 24, 0, material, UvDensity: 4 )]] );

		var world = new RenderWorld();
		world.Add( new MeshObject( mesh, new Transform( new Vector3( 30, 4, 0 ) ) ) );
		world.Add( new MeshObject( mesh, new Transform( new Vector3( 20, 0, 0 ) ) ) );
		world.Add( new MeshObject( mesh, new Transform( new Vector3( 50, -4, 0 ) ) ) );

		using var target = ViewTarget.Create( new Vector2Int( 1000, 1000 ) );
		using var system = new RenderSystem();

		GpuCommand[] forward;
		using ( var frame = Device.Capture() )
		{
			RenderHost.Current.NextFrame();
			system.Render( world, LookingDownX(), target );
			forward = frame.Commands.Where( c => c.IsDraw && c.In( "Opaque Forward" ) ).ToArray();
		}

		// One draw of the run of all three (an occlusion-culled one, whose instances the GPU counts), asking for what the
		// nearest one needs: its nearest face is 19 away
		Assert.AreEqual( 1, forward.Length );
		Assert.AreEqual( 3, system.GetFeature<MeshRenderFeature>().RunsFor( system.MainPass ).Opaque.Single().Count );
		Assert.AreEqual( (int)(4 * (500.0f / 19.0f)), forward[0].TextureSize );
	}

	/// <summary>
	/// Aggregates the GPU culls are sized on a worker (<see cref="AggregateSizes"/>) as the CPU's own cull sizes them: the
	/// largest size of the fragments in view, one answer per material, taken once.
	/// </summary>
	[TestMethod]
	public void WorkerSizesAggregatesAsTheCpuCullDoes()
	{
		using var material = new HeadlessMaterial( "ground", "complex" );
		var mesh = new RenderMesh( new HeadlessMesh( "ground" ), UnitBox, [[new RenderMesh.Draw( 0, 0, 0, 36, 24, 0, material )]] );
		foreach ( var density in new[] { 0.0f, 4.0f } )
		{
			var world = new RenderWorld();
			var aggregate = new AggregateObject( mesh,
			[
				AggregateTests.Fragment( 0, new Vector3( 20, 0, 0 ) ) with { UvDensity = density },
				AggregateTests.Fragment( 1, new Vector3( 60, 2, 0 ) ) with { UvDensity = density * 2 },
				AggregateTests.Fragment( 2, new Vector3( -20, 0, 0 ) ) with { UvDensity = density * 8 },
			] );
			world.Add( aggregate );

			using var system = new RenderSystem();
			Borealis.Shadows.ShadowMapper.ResetCache();
			CollectAndPrepare( system, world, LookingDownX() );
			var feature = system.GetFeature<AggregateRenderFeature>();
			var expected = feature.BatchesFor( system.MainPass ).Single().TextureSize;
			Assert.IsTrue( expected > 0 );

			var sizes = new AggregateSizes();
			sizes.Start( world, system.MainPass, system.MainPass.Visible( feature ) );
			var asked = new System.Collections.Generic.List<(GpuMaterial Material, int Size)>();
			for ( int i = 0; i < 1000 && asked.Count == 0; i++ )
			{
				sizes.Take( ( m, s ) => asked.Add( (m, s) ) );
				if ( asked.Count == 0 ) System.Threading.Thread.Sleep( 5 );
			}

			Assert.AreSame( aggregate.Mesh.DrawsForLod( 0 )[0].Material, asked.Single().Material );
			Assert.AreEqual( expected, asked.Single().Size, $"UV density {density}" );

			sizes.Take( ( m, s ) => asked.Add( (m, s) ) );
			Assert.AreEqual( 1, asked.Count, "an answer is taken once" );
		}
	}

	/// <summary>
	/// A view's resident aggregates, which ask streaming for no size of their own, are sized on the worker every few of its
	/// frames whatever frame generations its system gets: those come from one counter every system shares, and with another
	/// system rendering each frame (another camera, a 3D skybox) a system's can all be odd.
	/// </summary>
	[TestMethod]
	public void ResidentAggregatesAreSizedWhateverTheFrameGenerations()
	{
		var device = HeadlessRenderHost.Headless;
		device.ResidentAggregates = true;
		device.StreamTextures = true;
		try
		{
			using var material = new HeadlessMaterial( "ground", "complex" );
			var mesh = new RenderMesh( new HeadlessMesh( "ground" ), UnitBox, [[new RenderMesh.Draw( 0, 0, 0, 36, 24, 0, material )]] );
			var world = new RenderWorld();
			var aggregate = new AggregateObject( mesh, [AggregateTests.Fragment( 0, new Vector3( 20, 0, 0 ) ) with { UvDensity = 4 }] ) { IsStatic = true };
			world.Add( aggregate );

			// No depth prepass, so no occlusion culling, which keeps views preparing their own
			using var system = new RenderSystem { DepthPrepass = false };
			using var other = new RenderSystem { DepthPrepass = false };
			var empty = new RenderWorld();
			Borealis.Shadows.ShadowMapper.ResetCache();
			var feature = system.GetFeature<AggregateRenderFeature>();

			var startedFirst = 0;
			for ( int frame = 0; frame < 4 * AggregateRenderFeature.SizeEveryFrames; frame++ )
			{
				CollectAndPrepare( system, world, LookingDownX() );
				Assert.IsTrue( frame == 0 || system.Transforms.Generation % 2 == 1, "the system's generations are odd" );
				if ( frame == 0 ) startedFirst = feature.Sizes?.Started ?? 0;

				// A sizing done before the next frame, as a fast worker's is, whatever the pool is busy with
				for ( int i = 0; i < 3000 && feature.Sizes is { Running: true }; i++ ) System.Threading.Thread.Sleep( 10 );

				// The other system takes the next generation each frame, and one more the first time if that was odd
				CollectAndPrepare( other, empty, LookingDownX() );
				if ( frame == 0 && other.Transforms.Generation % 2 == 1 ) CollectAndPrepare( other, empty, LookingDownX() );
			}

			Assert.IsTrue( feature.ResidentFor( system.MainPass ) is { Batches.Count: 1 }, "the aggregate is resident" );
			Assert.IsTrue( feature.Sizes is not null && feature.Sizes.Started - startedFirst >= 3, "the worker is started every few frames" );

			var asked = new System.Collections.Generic.List<(GpuMaterial Material, int Size)>();
			feature.Sizes.Take( ( m, s ) => asked.Add( (m, s) ) );
			Assert.AreSame( material, asked.Single().Material, "the resident aggregate's material is asked for" );
			Assert.IsTrue( asked.Single().Size > 0 );
		}
		finally
		{
			device.ResidentAggregates = false;
			device.StreamTextures = false;
		}
	}

	/// <summary>
	/// A map's render systems go when it unloads: the streaming device stops taking their sizes, and an answer once taken
	/// isn't kept, so neither holds the materials of a world that has gone.
	/// </summary>
	[TestMethod]
	public void SizesGoWithTheirSystemAndKeepNoAnswer()
	{
		var device = HeadlessRenderHost.Headless;
		device.ResidentAggregates = true;
		device.StreamTextures = true;
		try
		{
			using var material = new HeadlessMaterial( "ground", "complex" );
			var mesh = new RenderMesh( new HeadlessMesh( "ground" ), UnitBox, [[new RenderMesh.Draw( 0, 0, 0, 36, 24, 0, material )]] );
			var world = new RenderWorld();
			world.Add( new AggregateObject( mesh, [AggregateTests.Fragment( 0, new Vector3( 20, 0, 0 ) ) with { UvDensity = 4 }] ) { IsStatic = true } );

			var system = new RenderSystem { DepthPrepass = false };
			Borealis.Shadows.ShadowMapper.ResetCache();
			var feature = system.GetFeature<AggregateRenderFeature>();
			for ( int frame = 0; frame < 2 * AggregateRenderFeature.SizeEveryFrames && feature.Sizes is not { Count: > 0 }; frame++ )
			{
				CollectAndPrepare( system, world, LookingDownX() );
				for ( int i = 0; i < 3000 && feature.Sizes is { Running: true }; i++ ) System.Threading.Thread.Sleep( 10 );
			}

			var sizes = feature.Sizes;
			Assert.IsTrue( sizes is { Count: > 0 }, "the worker sized the aggregate" );
			lock ( device.TextureSizeSources ) Assert.IsTrue( device.TextureSizeSources.Contains( sizes ), "the device takes its sizes" );
			Assert.AreEqual( 1, sizes.Answered );

			var asked = 0;
			sizes.Take( ( m, s ) => asked++ );
			Assert.AreEqual( 1, asked );
			Assert.AreEqual( 0, sizes.Answered, "the answer taken isn't kept" );

			system.Dispose();
			lock ( device.TextureSizeSources ) Assert.IsFalse( device.TextureSizeSources.Contains( sizes ), "the device stops taking a disposed system's sizes" );
		}
		finally
		{
			lock ( device.TextureSizeSources ) device.TextureSizeSources.Clear();
			device.ResidentAggregates = false;
			device.StreamTextures = false;
		}
	}
}
