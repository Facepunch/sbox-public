using Sandbox.SceneRenderer;
using Sandbox.SceneRenderer.Features;
using System;
using System.Collections.Generic;
using System.Linq;
using static SceneRendererTests.TestScenes;

namespace SceneRendererTests;

/// <summary>
/// The frame on the CPU: culling and preparing allocate nothing in steady state, size culling, and LOD selection.
/// </summary>
// Frames go through the engine's ShadowMapper, whose cache and frame counter are static: one test's frame
// can't run in the middle of another's
[TestClass]
[DoNotParallelize]
public class PipelineTests
{
	/// <summary>
	/// Steady state culling and preparing allocate nothing - anything they did allocate, every
	/// frame, per object, would come back as GC pauses. The meshes have no model behind them (no GPU), but
	/// they're sorted into every layer: opaque, faded, translucent and partially blended.
	/// </summary>
	[TestMethod]
	public void CollectAndPrepareDoNotAllocate()
	{
		RenderMesh[] meshes = [TestMesh( false ), TestMesh( true ), TestMesh( false, true ), TestSkinnedMesh( 4 )];

		// Some objects tagged, and some with attributes of their own, which draw alone
		uint[] tags = [new StringToken( "prop" ).Value];
		var attributes = new RenderAttributes( default( NativeEngine.CRenderAttributes ) );

		var world = new RenderWorld();
		for ( int i = 0; i < 5000; i++ )
		{
			var obj = new MeshObject( meshes[i % 4], new Transform( new Vector3( 100 + i % 100, i / 100, 0 ) ) )
			{
				Tint = i % 7 == 0 ? Color.White.WithAlpha( 0.5f ) : Color.White,
				Tags = i % 5 == 0 ? tags : [],
				Attributes = i % 11 == 0 ? attributes : null,
			};

			// Every other skinned one posed, the rest in bind pose
			if ( i % 8 == 3 ) obj.SetBones( [new Transform( obj.Transform.Position ), new Transform( obj.Transform.Position + Vector3.Up )] );
			world.Add( obj );
		}

		for ( int i = 0; i < 64; i++ )
			world.Add( new LightObject( i % 2 == 0 ? LightObject.LightKind.Point : LightObject.LightKind.Spot, new Transform( new Vector3( 100 + i, 0, 10 ) ) ) { Radius = 50 } );

		// Decals, sorted into the binner, and custom objects, sorted with the meshes and some casting shadows
		for ( int i = 0; i < 16; i++ )
		{
			world.Add( new DecalObject( new Transform( new Vector3( 100 + i * 5, 10, 0 ), Rotation.Identity, 10 ) ) { SortOrder = (uint)(16 - i) } );
			world.Add( new EffectsTests.TestCustom( new Vector3( 100 + i * 5, -10, 0 ) ) { IsOpaque = i % 2 == 0, CastShadows = i % 4 == 0 } );
		}

		// A 3D skybox, collected and prepared through its own render system
		var sky = new RenderWorld();
		for ( int i = 0; i < 200; i++ )
			sky.Add( new MeshObject( meshes[i % 3], new Transform( new Vector3( 10 + i, i % 10, 0 ) ) ) );
		world.Skybox3D = new Skybox3D( sky );

		var view = LookingDownX();
		view.ExcludeTags = [new StringToken( "hidden" ).Value];
		using var system = new RenderSystem();

		// The shadow cache is shared: start from an empty one, so no other test's lights go stale and get
		// evicted mid-measurement
		Sandbox.Rendering.ShadowMapper.ResetCache();

		// Warm up - the first frames grow the lists and arrays to fit
		for ( int i = 0; i < 3; i++ )
		{
			var warm = new RenderStats();
			Application.FrameCount++;
			system.Collect( world, view, ref warm );
			system.Prepare( world, view );
		}

		Application.FrameCount++;
		// Every thread's: shadow views are culled on the thread pool too
		var before = GC.GetAllocatedBytesForCurrentThread();
		var totalBefore = GC.GetTotalAllocatedBytes( true );

		var stats = new RenderStats();
		system.Collect( world, view, ref stats );
		system.Prepare( world, view );

		var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
		var allocatedAnywhere = GC.GetTotalAllocatedBytes( true ) - totalBefore;

		Assert.AreEqual( 5096, stats.ObjectsVisible );
		Assert.AreEqual( 64, system.GetFeature<LightBinnerFeature>().Count );
		Assert.AreEqual( 16, system.GetFeature<LightBinnerFeature>().DecalCount );
		Assert.IsTrue( system.SkyboxStats?.ObjectsVisible > 0, "the skybox was prepared" );
		Assert.AreEqual( 0, allocated, "Collect and Prepare allocated" );
		Assert.AreEqual( 0, allocatedAnywhere, "Culling on the thread pool allocated" );
	}

	/// <summary>
	/// An object the main view and the sun's cascades all draw has one transform entry a frame, which every view's instance
	/// reads.
	/// </summary>
	[TestMethod]
	public void ObjectsAreWrittenOnceAFrame()
	{
		var world = new RenderWorld();
		var obj = new MeshObject( TestMesh( false ), new Transform( new Vector3( 20, 0, 0 ) ) );
		world.Add( obj );

		using var system = new RenderSystem();
		Sandbox.Rendering.ShadowMapper.ResetCache();
		CollectAndPrepare( system, world, LookingDownX() );

		var feature = system.GetFeature<MeshRenderFeature>();
		var shadowRuns = system.ShadowMaps.Passes.ToArray().SelectMany( p => feature.RunsFor( p ).Opaque ).ToArray();
		Assert.IsTrue( shadowRuns.Length > 0, "a cascade drew it" );
		Assert.AreEqual( 1, system.Transforms.Count, "one entry" );
		Assert.AreEqual( 1 + shadowRuns.Length, system.Transforms.InstanceCount, "an instance per view" );

		var main = feature.RunsFor( system.MainPass ).Opaque.Single();
		Assert.AreEqual( obj.Slot, system.Transforms.InstanceSlot( main.FirstInstance ) );
		foreach ( var run in shadowRuns )
			Assert.AreEqual( obj.Slot, system.Transforms.InstanceSlot( run.FirstInstance ) );
	}

	/// <summary>
	/// A feature records through the same contract whichever layer it is: a layer's runs are every feature's in order, and a
	/// share of them goes to each feature as its part of the range.
	/// </summary>
	[TestMethod]
	public void RecordingSharesRunsOutAcrossFeatures()
	{
		using var system = new RenderSystem();
		var a = new CountingFeature { Runs = 3 };
		var b = new CountingFeature { Runs = 2 };
		system.AddFeature( a );
		system.AddFeature( b );

		var stats = new RenderStats();
		system.Collect( new RenderWorld(), LookingDownX(), ref stats );
		var view = system.MainPass;

		var layers = system.Layers.OfType<MeshLayer>().ToArray();
		Assert.AreEqual( 16, layers.Length, "shadow, overlay prepass, prepass, bloom, refraction stencil, opaque, tools opaque, static overlays, decals, translucent, tools translucent, the two game overlay layers, the two screen overlay layers and after UI" );

		foreach ( var layer in layers )
		{
			Assert.AreEqual( 5, system.RunCount( view, layer ), $"{layer}: every feature's runs" );

			a.Drawn.Clear();
			b.Drawn.Clear();
			system.DrawRange( null, view, layer, 1, 3, ref stats );
			CollectionAssert.AreEqual( new[] { (layer, 1, 2) }, a.Drawn, $"{layer}: the first feature's part" );
			CollectionAssert.AreEqual( new[] { (layer, 0, 1) }, b.Drawn, $"{layer}: then the next's" );
		}
	}

	sealed class CountingFeature : RenderFeature
	{
		public int Runs;
		public readonly List<(MeshLayer Layer, int First, int Count)> Drawn = new();

		public override Type ObjectType => typeof( CountingFeature );
		internal override int RunCount( ViewPass view, MeshLayer layer ) => Runs;
		internal override void Draw( Sandbox.SceneRenderer.Gpu.RenderContext context, ViewPass view, MeshLayer layer, int first, int count, ref RenderStats stats ) => Drawn.Add( (layer, first, count) );
	}

	/// <summary>
	/// Two render systems drawing one world - two cameras - each write the world's entries into their own buffer: an
	/// object's slot from one isn't taken as written in the other.
	/// </summary>
	[TestMethod]
	public void EachSystemWritesItsOwnEntries()
	{
		var world = new RenderWorld();
		world.Add( new MeshObject( TestMesh( false ), new Transform( new Vector3( 20, 0, 0 ) ) ) );
		world.Lighting.SunColor = Color.Black;

		using var first = new RenderSystem();
		using var second = new RenderSystem();
		CollectAndPrepare( first, world, LookingDownX() );
		CollectAndPrepare( second, world, LookingDownX() );

		Assert.AreEqual( 1, first.Transforms.Count );
		Assert.AreEqual( 1, second.Transforms.Count, "the second wrote its own" );
	}

	/// <summary>
	/// Objects too small on screen are dropped the way native's <c>r_size_cull_threshold</c> drops them;
	/// lights never are.
	/// </summary>
	[TestMethod]
	public void SizeCulling()
	{
		var world = new RenderWorld();
		var view = LookingDownX(); // 90 degree fov, so tan( fov / 2 ) is 1

		// A unit box has a bounding radius of sqrt(3): at 100 units it's about 1.7% of the screen,
		// at 10000 about 0.017% - under the 0.25% threshold
		var near = new MeshObject( null, new Transform( new Vector3( 100, 0, 0 ) ) ) { LocalBounds = UnitBox };
		var far = new MeshObject( null, new Transform( new Vector3( 10000, 0, 0 ) ) ) { LocalBounds = UnitBox };
		var farLight = new LightObject( LightObject.LightKind.Point, new Transform( new Vector3( 10000, 0, 0 ) ) ) { Radius = 1 };
		world.Add( near );
		world.Add( far );
		world.Add( farLight );

		view.ZFar = 100000;
		view.Update();

		using var system = new RenderSystem();
		var stats = new RenderStats();
		system.Collect( world, view, ref stats );

		Assert.AreEqual( 2, stats.ObjectsVisible, "the near box and the light" );
		Assert.AreEqual( 1, stats.ObjectsSizeCulled );

		view.SizeCullThreshold = 0;
		stats = new RenderStats();
		system.Collect( world, view, ref stats );
		Assert.AreEqual( 3, stats.ObjectsVisible, "nothing culled with the threshold off" );
	}

	/// <summary>
	/// Native's LOD metric: 50 over the screen width of a half unit sphere, against switch distances
	/// scaled by the object's scale - every distance walked, the result clamped to the model's LODs.
	/// </summary>
	[TestMethod]
	public void LodSelection()
	{
		// ModelBuilder's defaults are lod * 50; this model sets 1 and 2 and has three LODs
		float[] distances = [0, 60, 150, 150, 200, 250, 300, 350];

		Assert.AreEqual( 0, RenderMesh.LodForScreenSize( distances, 3, 2.0f, 1 ), "metric 25, under every switch" );
		Assert.AreEqual( 1, RenderMesh.LodForScreenSize( distances, 3, 0.5f, 1 ), "metric 100, past LOD 1" );
		Assert.AreEqual( 2, RenderMesh.LodForScreenSize( distances, 3, 0.25f, 1 ), "metric 200, past LOD 3 - clamped to the last" );
		Assert.AreEqual( 0, RenderMesh.LodForScreenSize( distances, 3, 0.5f, 2 ), "doubling the scale doubles the distances" );
		Assert.AreEqual( 0, RenderMesh.LodForScreenSize( distances, 3, 0, 1 ), "native reads no width as a zero metric, which is LOD 0" );
	}

	/// <summary>
	/// An object's fixed LOD wins over its size on screen, clamped to its mesh's LODs - native's <c>SetLOD</c>, which a map's
	/// props and <c>ModelRenderer.LodOverride</c> set.
	/// </summary>
	[TestMethod]
	public void FixedLodWinsOverScreenSize()
	{
		var draw = new RenderMesh.Draw( 0, 0, 0, 36, 24, 0, null );
		var mesh = new RenderMesh( UnitBox, [[draw], [draw], [draw]], [0, 60, 150] );
		var world = new RenderWorld();
		// Small, so its switch distances are too (metric 90 against 18 and 45), with bounds big enough not to be size culled
		var far = new MeshObject( mesh, new Transform( new Vector3( 900, 0, 0 ), Rotation.Identity, 0.3f ) ) { LocalBounds = new BBox( new Vector3( -200 ), new Vector3( 200 ) ) };
		world.Add( far );

		using var system = new RenderSystem();
		var feature = system.GetFeature<MeshRenderFeature>();
		CollectAndPrepare( system, world, LookingDownX() );
		Assert.AreEqual( 2, feature.LodFor( far ), "far enough for the last" );

		far.LodOverride = 0;
		CollectAndPrepare( system, world, LookingDownX() );
		Assert.AreEqual( 0, feature.LodFor( far ) );

		far.LodOverride = 5;
		CollectAndPrepare( system, world, LookingDownX() );
		Assert.AreEqual( 2, feature.LodFor( far ), "clamped to the mesh's LODs" );
	}
}
