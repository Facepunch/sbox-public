using BorealisTests.Headless;
using System;
using System.Collections.Generic;
using Borealis;
using Borealis.Features;
using System.Diagnostics;
using static BorealisTests.TestScenes;

namespace BorealisTests;

/// <summary>
/// <see cref="InstancedMeshObject"/>: one model at many placements. A device with resident meshes draws it as one group the
/// GPU culls; anywhere else (the first frame, a view with occlusion culling, a device without them) the mesh feature culls each
/// placement against the view and draws every LOD's as one run.
/// </summary>
[TestClass]
[DoNotParallelize]
public class InstancedMeshTests
{
	/// <summary>Two LODs, switching to the second past 10 units from the camera, each one draw with a material the headless device takes resident.</summary>
	internal static RenderMesh ModelWithLods()
	{
		var material = RenderHost.Current.Device.LoadMaterial( "instanced-test.vmat" );
		var draw = new RenderMesh.Draw( 0, 0, 0, 36, 24, 0, material );
		return new RenderMesh( UnitBox, [[draw], [draw]], [0, 1] );
	}

	/// <summary>Placements in front of <see cref="LookingDownX"/>: three near (LOD 0), three far (LOD 1), two behind it and one too small to see.</summary>
	static Transform[] Scatter() =>
	[
		new( new Vector3( 5, -2, 0 ) ), new( new Vector3( 5, 0, 0 ) ), new( new Vector3( 5, 2, 0 ) ),
		new( new Vector3( 40, -5, 0 ) ), new( new Vector3( 40, 0, 0 ) ), new( new Vector3( 40, 5, 0 ) ),
		new( new Vector3( -20, 0, 0 ) ), new( new Vector3( -30, 3, 0 ) ),
		new( new Vector3( 900, 0, 0 ), Rotation.Identity, 0.01f ),
	];

	static InstancedMeshObject Instances( Transform[] transforms, RenderMesh mesh = null )
	{
		var obj = new InstancedMeshObject { Mesh = mesh ?? ModelWithLods() };
		obj.SetInstances( transforms, default );
		return obj;
	}

	static IDisposable ResidentMeshes( bool on )
	{
		var device = HeadlessRenderHost.Headless;
		(device.ResidentAggregates, device.ResidentMeshes) = (on, on);
		return new Off( device );
	}

	sealed class Off( HeadlessDevice device ) : IDisposable
	{
		public void Dispose() => (device.ResidentAggregates, device.ResidentMeshes) = (false, false);
	}

	/// <summary>
	/// What the mesh feature drew a view's instances from, by LOD: each run's placements' x, as their transform entries say.
	/// </summary>
	static List<float>[] DrawnX( RenderSystem system, ViewPass pass, int lods )
	{
		var drawn = new List<float>[lods];
		for ( int i = 0; i < lods; i++ ) drawn[i] = new();
		foreach ( var run in system.GetFeature<MeshRenderFeature>().RunsFor( pass ).Opaque )
		{
			for ( int i = 0; i < run.Count; i++ ) drawn[run.Lod].Add( system.Transforms[system.Transforms.InstanceSlot( run.FirstInstance + i )].Row0.w );
		}

		return drawn;
	}

	[TestMethod]
	public void ResidentInstancesAreOneGroupWithAKeptSlotEach()
	{
		using var _ = ResidentMeshes( true );
		var world = new RenderWorld();
		const int Count = 64;
		var transforms = new Transform[Count];
		for ( int i = 0; i < Count; i++ ) transforms[i] = new Transform( new Vector3( 10 + i * 0.5f, 0, 0 ) );
		var obj = Instances( transforms );
		world.Add( obj );

		// No depth prepass, so no occlusion culling, which keeps views from preparing their own
		using var system = new RenderSystem { DepthPrepass = false };
		Borealis.Shadows.ShadowMapper.ResetCache();
		var aggregates = system.GetFeature<AggregateRenderFeature>();
		var meshes = system.GetFeature<MeshRenderFeature>();
		var view = LookingDownX();

		// The first frame it has no kept run: the view draws every placement itself, from frame entries
		CollectAndPrepare( system, world, view );
		Assert.AreEqual( 0, aggregates.ResidentFor( system.MainPass ).MeshGroups.Count, "nothing resident before its run is made" );
		Assert.AreEqual( Count, meshes.RunsFor( system.MainPass ).Opaque.Sum( r => r.Count ), "nothing vanishes the first frame" );

		CollectAndPrepare( system, world, view );
		var list = aggregates.ResidentFor( system.MainPass );
		var group = list.MeshGroups.Single();
		var owner = group.Owner;
		Assert.AreEqual( Count, owner.Count, "a member per placement" );
		Assert.AreEqual( Count, owner.Slots.Distinct().Count(), "each with a slot of its own" );
		Assert.IsTrue( owner.Slots.All( s => s < system.Transforms.FrameBase ), "kept ones" );
		Assert.AreEqual( 10.0f, system.Transforms[owner.Slots[0]].Row0.w, "written from its placement" );
		Assert.AreEqual( 10 + 63 * 0.5f, system.Transforms[owner.Slots[63]].Row0.w );
		Assert.AreEqual( 2, list.DeviceCount, "a batch per LOD and draw" );
		Assert.AreEqual( 0, meshes.RunsFor( system.MainPass ).Opaque.Count, "the CPU path draws nothing" );
		Assert.AreEqual( 0, system.MainPass.Visible( meshes ).Count, "its collect leaves it out" );
		Assert.IsTrue( system.ShadowMaps.Passes.ToArray().Any( p => aggregates.ResidentFor( p ) is { MeshGroups.Count: 1 } ), "a cascade draws it from its list too" );
		Assert.AreEqual( 1, aggregates.ResidentCandidatesChecked, "its materials are looked at once" );

		// Unchanged frames keep the list, its owner, and write nothing
		var made = aggregates.ResidentListsMade;
		var released = aggregates.ResidentOwnersReleased;
		var written = system.Transforms.KeptWrites;
		for ( int i = 0; i < 5; i++ ) CollectAndPrepare( system, world, view );
		Assert.AreSame( list, aggregates.ResidentFor( system.MainPass ) );
		Assert.AreSame( owner, aggregates.ResidentFor( system.MainPass ).MeshGroups.Single().Owner );
		Assert.AreEqual( made, aggregates.ResidentListsMade, "no list made again" );
		Assert.AreEqual( released, aggregates.ResidentOwnersReleased, "no owner let go" );
		Assert.AreEqual( written, system.Transforms.KeptWrites, "no record rewritten" );

		// Another object's change makes the lists again; its owner stays, its arrays aren't compared
		world.Add( new TestObject( UnitBox, new Transform( new Vector3( 500, 0, 0 ) ) ) );
		CollectAndPrepare( system, world, view );
		Assert.AreSame( owner, aggregates.ResidentFor( system.MainPass ).MeshGroups.Single().Owner, "the same owner in a new list" );
		Assert.AreEqual( released, aggregates.ResidentOwnersReleased );

		// New instances replace the owner, and rewrite the run once
		var moved = new Transform[Count];
		for ( int i = 0; i < Count; i++ ) moved[i] = new Transform( new Vector3( 12 + i * 0.5f, 1, 0 ) );
		obj.SetInstances( moved, default );
		CollectAndPrepare( system, world, view );
		var replaced = aggregates.ResidentFor( system.MainPass ).MeshGroups.Single().Owner;
		Assert.AreNotSame( owner, replaced, "a new owner for new instances" );
		Assert.IsTrue( aggregates.ResidentOwnersReleased > released, "the old one let go" );
		Assert.AreEqual( written + Count, system.Transforms.KeptWrites, "every entry rewritten once" );
		Assert.AreEqual( 12.0f, system.Transforms[replaced.Slots[0]].Row0.w );
		Assert.AreEqual( 1, aggregates.ResidentCandidatesChecked, "and not looked at again" );

		// More of them: a new run, drawn from frame entries until it's made
		var more = new Transform[Count + 8];
		moved.CopyTo( more, 0 );
		for ( int i = Count; i < more.Length; i++ ) more[i] = new Transform( new Vector3( 20, 0, 0 ) );
		obj.SetInstances( more, default );
		CollectAndPrepare( system, world, view );
		Assert.AreEqual( more.Length, meshes.RunsFor( system.MainPass ).Opaque.Sum( r => r.Count ), "nothing vanishes while the run is made" );
		CollectAndPrepare( system, world, view );
		Assert.AreEqual( more.Length, aggregates.ResidentFor( system.MainPass ).MeshGroups.Single().Owner.Count );
	}

	[TestMethod]
	public void InstancesAreDrawnOnTheCpuWithoutResidentMeshes()
	{
		var world = new RenderWorld();
		world.Add( Instances( Scatter() ) );

		using var system = new RenderSystem { DepthPrepass = false };
		Borealis.Shadows.ShadowMapper.ResetCache();
		var view = LookingDownX();

		// The first frame writes frame entries, the next kept ones: the same placements are drawn
		for ( int frame = 0; frame < 3; frame++ )
		{
			CollectAndPrepare( system, world, view );
			var drawn = DrawnX( system, system.MainPass, 2 );
			CollectionAssert.AreEquivalent( new[] { 5f, 5f, 5f }, drawn[0], $"frame {frame}: the near ones draw at LOD 0" );
			CollectionAssert.AreEquivalent( new[] { 40f, 40f, 40f }, drawn[1], $"frame {frame}: the far ones at LOD 1" );
			Assert.AreEqual( frame == 0, system.Transforms.InstanceSlot( system.GetFeature<MeshRenderFeature>().RunsFor( system.MainPass ).Opaque[0].FirstInstance ) >= system.Transforms.FrameBase,
				$"frame {frame}: frame entries only until its run is made" );
		}

		// A shadow view culls them too: what it draws is a subset of the placements, by its own frustum
		Assert.IsTrue( system.ShadowMaps.Passes.ToArray().Any( p => DrawnX( system, p, 2 ).Sum( l => l.Count ) > 0 ), "a cascade draws some" );
		Assert.IsTrue( system.GetFeature<MeshRenderFeature>().InstancesCulled >= 3 * 3, "the ones behind and too small were culled in every view" );
	}

	[TestMethod]
	public void ViewsWithOcclusionCullingDrawInstancesThemselves()
	{
		using var _ = ResidentMeshes( true );
		var world = new RenderWorld();
		world.Add( Instances( Scatter() ) );

		// A depth prepass means occlusion culling, which has no resident list
		using var system = new RenderSystem();
		Borealis.Shadows.ShadowMapper.ResetCache();
		var view = LookingDownX();
		for ( int frame = 0; frame < 4; frame++ )
		{
			CollectAndPrepare( system, world, view );
			var drawn = DrawnX( system, system.MainPass, 2 );
			Assert.AreEqual( 3, drawn[0].Count, $"frame {frame}: LOD 0" );
			Assert.AreEqual( 3, drawn[1].Count, $"frame {frame}: LOD 1" );
		}

		Assert.AreEqual( 0, system.GetFeature<AggregateRenderFeature>().ResidentFor( system.MainPass )?.MeshGroups.Count ?? 0 );
	}

	[TestMethod]
	public void InstancesHonourTheirObjectsFlags()
	{
		var world = new RenderWorld();
		var obj = Instances( Scatter() );
		world.Add( obj );

		using var system = new RenderSystem { DepthPrepass = false };
		Borealis.Shadows.ShadowMapper.ResetCache();
		var view = LookingDownX();

		// Out of the game's layers it casts shadows only; no shadows, it draws in the main view only
		obj.GameLayers = false;
		CollectAndPrepare( system, world, view );
		Assert.AreEqual( 0, DrawnX( system, system.MainPass, 2 ).Sum( l => l.Count ) );
		Assert.IsTrue( system.ShadowMaps.Passes.ToArray().Any( p => DrawnX( system, p, 2 ).Sum( l => l.Count ) > 0 ) );

		obj.GameLayers = true;
		obj.CastShadows = false;
		CollectAndPrepare( system, world, view );
		Assert.AreEqual( 6, DrawnX( system, system.MainPass, 2 ).Sum( l => l.Count ) );
		Assert.IsTrue( system.ShadowMaps.Passes.ToArray().All( p => DrawnX( system, p, 2 ).Sum( l => l.Count ) == 0 ) );
	}

	[TestMethod]
	public void InstancesMakeTheObjectsBoundsTheirUnion()
	{
		var obj = Instances( Scatter() );
		var world = new RenderWorld();
		world.Add( obj );
		var min = world.BoundsCenter[0] - world.BoundsExtents[0];
		var max = world.BoundsCenter[0] + world.BoundsExtents[0];
		Assert.IsTrue( (min - new Vector3( -31, -6, -1 )).Length < 0.01f, $"min {min}" );
		Assert.IsTrue( (max - new Vector3( 900.01f, 6, 1 )).Length < 0.01f, $"max {max}" );

		obj.Transform = new Transform( new Vector3( 0, 100, 0 ) );
		Assert.AreEqual( 94.0f, world.BoundsCenter[0].y - world.BoundsExtents[0].y, 0.01f, "the object's transform applies on top" );
	}

	/// <summary>
	/// Before and after, on the headless device, of the same model at N placements as N mesh objects and as one instanced object:
	/// what the CPU does to get them on screen (cold frame with its rescan, the frame that makes the lists, steady frames) and
	/// what the GPU is handed. Timings are printed; run it in Release.
	/// </summary>
	[TestMethod]
	[DataRow( 2_000 )]
	[DataRow( 10_000 )]
	[DataRow( 50_000 )]
	public void CompareWithOneMeshObjectPerPlacement( int count )
	{
		var transforms = new Transform[count];
		var random = new Random( 7 );
		for ( int i = 0; i < count; i++ ) transforms[i] = new Transform( new Vector3( 8 + random.NextSingle() * 150, (random.NextSingle() - 0.5f) * 120, 0 ), Rotation.Identity, 0.5f + random.NextSingle() );

		foreach ( var resident in new[] { true, false } )
		{
			using var _ = ResidentMeshes( resident );

			// Both paths once first, so neither pays for compiling
			Measure( 200, resident, false, transforms[..200] );
			Measure( 200, resident, true, transforms[..200] );
			var separate = Measure( count, resident, false, transforms );
			var instanced = Measure( count, resident, true, transforms );
			Console.WriteLine( $"N={count,6} {(resident ? "resident" : "CPU path")}\n  separate MeshObjects : {separate}\n  one InstancedMeshObject: {instanced}" );
			Assert.AreEqual( 1, instanced.WorldObjects );
			Assert.AreEqual( count, separate.WorldObjects );
			if ( !resident ) continue;

			Assert.AreEqual( count, separate.Members, "every mesh object is a member" );
			Assert.AreEqual( count, instanced.Members, "so is every placement" );
			Assert.AreEqual( 1, instanced.Candidates, "a candidate test per object" );
			Assert.AreEqual( 1, separate.Groups );
			Assert.AreEqual( 1, instanced.Groups );
		}
	}

	record Measured( int WorldObjects, int Members, int Groups, int Candidates, int Batches, int CpuRuns, int CpuInstances, double ColdMs, double ListsMs, double SteadyMs )
	{
		public override string ToString() => $"objects {WorldObjects,6}, resident members {Members,6}, groups {Groups}, device batches {Batches}, candidates checked {Candidates,6}, CPU runs {CpuRuns,5} ({CpuInstances,6} instances), "
			+ $"cold frame {ColdMs,8:0.00} ms, frame making lists {ListsMs,8:0.00} ms, steady frame {SteadyMs,8:0.000} ms";
	}

	static Measured Measure( int count, bool resident, bool instanced, Transform[] transforms )
	{
		var world = new RenderWorld();
		var mesh = ModelWithLods();
		if ( instanced )
		{
			var obj = Instances( transforms, mesh );
			obj.IsStatic = true;
			world.Add( obj );
		}
		else
		{
			for ( int i = 0; i < count; i++ ) world.Add( new MeshObject( mesh, transforms[i] ) { IsStatic = true } );
		}

		using var system = new RenderSystem { DepthPrepass = false };
		Borealis.Shadows.ShadowMapper.ResetCache();
		var aggregates = system.GetFeature<AggregateRenderFeature>();
		var meshes = system.GetFeature<MeshRenderFeature>();
		var view = LookingDownX();
		view.ZFar = 5000;
		view.Update();

		double Frame()
		{
			var start = Stopwatch.GetTimestamp();
			CollectAndPrepare( system, world, view );
			return Stopwatch.GetElapsedTime( start ).TotalMilliseconds;
		}

		var cold = Frame();
		var lists = Frame();
		for ( int i = 0; i < 10; i++ ) Frame();
		var steady = 0.0;
		const int Steady = 30;
		for ( int i = 0; i < Steady; i++ ) steady += Frame();

		var list = aggregates.ResidentFor( system.MainPass );
		var runs = meshes.RunsFor( system.MainPass ).Opaque;
		return new Measured( world.Count, list?.MeshGroups.Sum( g => g.Owner.Count ) ?? 0, list?.MeshGroups.Count ?? 0, aggregates.ResidentCandidatesChecked, list?.DeviceCount ?? 0,
			runs.Count, runs.Sum( r => r.Count ), cold, lists, steady / Steady );
	}
}
