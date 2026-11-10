using Basalt;
using Basalt.Features;
using BasaltTests.Headless;
using System;
using System.Collections.Generic;
using System.Linq;
using static BasaltTests.TestScenes;

namespace BasaltTests;

/// <summary>
/// Work a frame mustn't repeat, counted rather than timed, as timings drift from machine to machine: a batch key per
/// object, a sort of buckets already in order, a readback every frame.
/// </summary>
// Frames go through Basalt's ShadowMapper, whose cache and frame counter are static
[TestClass]
[DoNotParallelize]
public class WorkTests
{
	static RenderWorld Boxes( RenderMesh mesh, int count )
	{
		var world = new RenderWorld();
		for ( int i = 0; i < count; i++ )
			world.Add( new MeshObject( mesh, new Transform( new Vector3( 100 + i % 10 * 3, i / 10 % 10 * 3 - 15, i / 100 * 3 - 15 ) ) ) );
		return world;
	}

	/// <summary>
	/// Objects that share everything share a batch key, which each view makes once rather than once an object, and their
	/// buckets are already in order, so none is sorted. Hashing every object's references cost more than the rest of
	/// classifying it.
	/// </summary>
	[TestMethod]
	public void IdenticalObjectsShareABatchKeyAndSkipTheSort()
	{
		var world = Boxes( TestMesh( false ), 1000 );
		var view = LookingDownX();
		using var system = new RenderSystem();
		Basalt.Shadows.ShadowMapper.ResetCache();
		CollectAndPrepare( system, world, view );

		var feature = system.GetFeature<MeshRenderFeature>();
		var keys = feature.KeysMade;
		var sorted = feature.BucketsSorted;
		CollectAndPrepare( system, world, view );

		Assert.AreEqual( 1000, system.MainPass.Visible( feature ).Count );
		var views = 1 + system.ShadowMaps.Passes.Length;
		Assert.IsTrue( feature.KeysMade - keys <= views, $"{feature.KeysMade - keys} keys made for {views} views" );
		Assert.AreEqual( sorted, feature.BucketsSorted, "no bucket sorted" );
	}

	static RenderWorld LitBoxes( int count )
	{
		var world = Boxes( TestMesh( false ), count );
		world.Add( new LightObject( LightObject.LightKind.Spot, new Transform( new Vector3( 60, 5, 0 ) ) ) { Radius = 200, ConeOuter = 45 } );
		world.Add( new LightObject( LightObject.LightKind.Spot, new Transform( new Vector3( 60, -5, 0 ) ) ) { Radius = 200, ConeOuter = 45 } );
		return world;
	}

	/// <summary>
	/// An object in several shadow views is classified once, for the first of them: its LOD comes from the main view's
	/// camera and shadows take no probe, so every shadow view would make the same of it.
	/// </summary>
	[TestMethod]
	public void ShadowViewsClassifyAnObjectOnce()
	{
		var world = LitBoxes( 1000 );
		var view = LookingDownX();
		using var system = new RenderSystem();
		Basalt.Shadows.ShadowMapper.ResetCache();
		CollectAndPrepare( system, world, view );

		var feature = system.GetFeature<MeshRenderFeature>();
		var shadowVisible = system.ShadowMaps.Passes.ToArray().SelectMany( p => p.Visible( feature ) ).ToList();
		var distinct = shadowVisible.Distinct().Count();
		Assert.IsTrue( shadowVisible.Count >= distinct * 2, $"objects in several shadow views: {shadowVisible.Count} in {distinct}" );
		Assert.AreEqual( system.MainPass.Visible( feature ).Count + distinct, feature.ObjectsClassified );
	}

	/// <summary>
	/// What a frame makes of each object is kept: a frame where nothing changed classifies nothing and makes no batch key,
	/// in the main view or any shadow view.
	/// </summary>
	[TestMethod]
	public void AnUnchangedFrameClassifiesNothing()
	{
		var world = LitBoxes( 1000 );
		var view = LookingDownX();
		using var system = new RenderSystem();
		Basalt.Shadows.ShadowMapper.ResetCache();
		CollectAndPrepare( system, world, view );

		var feature = system.GetFeature<MeshRenderFeature>();
		var classified = feature.ObjectsClassified;
		var keys = feature.KeysMade;
		Basalt.Shadows.ShadowMapper.ResetCache();
		CollectAndPrepare( system, world, view );

		Assert.IsTrue( system.ShadowMaps.Passes.Length > 0, "shadow views" );
		Assert.AreEqual( 1000, system.MainPass.Visible( feature ).Count );
		Assert.AreEqual( classified, feature.ObjectsClassified, "objects classified" );
		Assert.AreEqual( keys, feature.KeysMade, "batch keys made" );
	}

	/// <summary>
	/// A change to one object is made again for that object alone: once for the main view and once for the shadow views.
	/// </summary>
	[TestMethod]
	public void ChangingOneObjectClassifiesItAlone()
	{
		var world = LitBoxes( 1000 );
		var view = LookingDownX();
		using var system = new RenderSystem();
		Basalt.Shadows.ShadowMapper.ResetCache();
		CollectAndPrepare( system, world, view );

		var feature = system.GetFeature<MeshRenderFeature>();
		var changed = (MeshObject)world.Objects[500];
		changed.Attributes = new HeadlessParameters();
		var classified = feature.ObjectsClassified;
		var keys = feature.KeysMade;
		Basalt.Shadows.ShadowMapper.ResetCache();
		CollectAndPrepare( system, world, view );

		var kinds = (system.MainPass.Visible( feature ).Contains( changed.Index ) ? 1 : 0)
			+ (system.ShadowMaps.Passes.ToArray().Any( p => p.Visible( feature ).Contains( changed.Index ) ) ? 1 : 0);
		Assert.IsTrue( kinds > 0, "the changed object is drawn" );
		Assert.AreEqual( kinds, feature.ObjectsClassified - classified, "objects classified" );
		Assert.AreEqual( kinds, feature.KeysMade - keys, "batch keys made" );
	}

	/// <summary>
	/// What's kept between frames stays what a render system that kept nothing would make of the same world, as objects
	/// come, go (moving others to new indices) and change everything that decides their lists and keys.
	/// </summary>
	[TestMethod]
	public void KeptListsMatchARebuild()
	{
		using var overrideMaterial = new HeadlessMaterial( "override", "complex" );
		using var glass = new HeadlessMaterial( "glass", "glass" ) { Translucent = true };
		var opaque = TestMesh( false );
		var mixed = TestMesh( false, true );
		var other = TestMesh( false, false );
		var shared = new HeadlessParameters();

		var world = LitBoxes( 0 );
		var random = new Random( 7 );
		void AddBox()
		{
			var i = world.Count;
			var mesh = (i % 3) switch { 0 => opaque, 1 => mixed, _ => other };
			world.Add( new MeshObject( mesh, new Transform( new Vector3( 100 + random.Next( 30 ), random.Next( 30 ) - 15, random.Next( 30 ) - 15 ) ) ) );
		}

		for ( int i = 0; i < 300; i++ ) AddBox();

		var view = LookingDownX();
		using var system = new RenderSystem();
		for ( int frame = 0; frame < 24; frame++ )
		{
			// Change a few objects in every way that decides their lists and keys
			for ( int c = 0; c < 6; c++ )
			{
				if ( world.Objects[random.Next( world.Count )] is not MeshObject obj ) continue;
				switch ( random.Next( 14 ) )
				{
					case 0: obj.Tint = obj.Tint.a < 1 ? Color.White : Color.White.WithAlpha( 0.5f ); break;
					case 1: obj.MaterialOverride = obj.MaterialOverride is null ? overrideMaterial : null; break;
					case 2: obj.MaterialOverride = glass; break;
					case 3: obj.Attributes = obj.Attributes is null ? shared : null; break;
					case 4: obj.Attributes = new HeadlessParameters(); break;
					case 5: obj.Mesh = obj.Mesh == opaque ? mixed : opaque; break;
					case 6: obj.GameLayers = !obj.GameLayers; break;
					case 7: obj.Overlay = !obj.Overlay; break;
					case 8: obj.Bloom = !obj.Bloom; break;
					case 9: obj.DrawsTranslucent = !obj.DrawsTranslucent; break;
					case 10: obj.DepthPrepass = !obj.DepthPrepass; break;
					case 11: obj.LayerMatch = obj.LayerMatch == LayerMatch.None ? LayerMatch.OverlayWithDepth : LayerMatch.None; break;
					case 12: world.Remove( obj ); break;
					default: AddBox(); break;
				}
			}

			Basalt.Shadows.ShadowMapper.ResetCache();
			CollectAndPrepare( system, world, view );
			var kept = Describe( system, world );

			using var rebuilt = new RenderSystem();
			Basalt.Shadows.ShadowMapper.ResetCache();
			CollectAndPrepare( rebuilt, world, view );
			Assert.AreEqual( Describe( rebuilt, world ), kept, $"frame {frame}" );
		}
	}

	/// <summary>
	/// Every view's lists: each run's state, its objects by world index in instance order, and its list's draws.
	/// </summary>
	static string Describe( RenderSystem system, RenderWorld world )
	{
		var feature = system.GetFeature<MeshRenderFeature>();
		var owners = new Dictionary<int, int>();
		foreach ( var obj in world.Objects )
		{
			var slot = system.Transforms.SlotOf( obj );
			if ( slot >= 0 ) owners[slot] = obj.Index;
		}

		var text = new System.Text.StringBuilder();
		foreach ( var pass in system.ShadowMaps.Passes.ToArray().Prepend( system.MainPass ) )
		{
			text.AppendLine( pass.IsShadow ? "shadow" : "main" );
			var lists = feature.RunsFor( pass );
			for ( int bit = 0; bit < lists.ByBit.Length; bit++ )
			{
				foreach ( var run in lists.ByBit[bit] )
				{
					text.Append( $" {bit}: {Id( run.Mesh )} lod {run.Lod} {Id( run.Override )} {Id( run.Attributes )} {run.Filter} x{run.Count} [" );
					for ( int i = 0; i < run.Count; i++ )
						text.Append( $"{owners[system.Transforms.InstanceSlot( run.FirstInstance + i )]} " );
					text.AppendLine( "]" );
				}

				text.AppendLine( $" {bit} draws: {string.Join( ",", lists.ItemsByBit[bit].Select( d => $"{d.Run}.{d.Draw}" ) )}" );
			}
		}

		return text.ToString();
	}

	static string Id( object obj ) => obj is null ? "-" : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode( obj ).ToString();

	/// <summary>
	/// The occlusion cull's counts are read back every <see cref="Basalt.Culling.OcclusionCuller.StatsInterval"/> frames, not
	/// every frame: each read makes a staging buffer in native, a fraction of a millisecond.
	/// </summary>
	[TestMethod]
	public void OcclusionStatsAreReadNowAndThen()
	{
		var mesh = new RenderMesh( new HeadlessMesh( "box" ), UnitBox, [[new RenderMesh.Draw( 0, 0, 0, 36, 24, 0, null )]] );
		var world = Boxes( mesh, 100 );
		using var target = ViewTarget.Create( new Vector2Int( 64, 64 ) );
		using var system = new RenderSystem();
		var view = LookingDownX();
		Basalt.Shadows.ShadowMapper.ResetCache();

		const int frames = Basalt.Culling.OcclusionCuller.StatsInterval * 2;
		var reads = 0;
		for ( int i = 0; i < frames; i++ )
		{
			using var frame = HeadlessRenderHost.Headless.Capture();
			RenderHost.Current.NextFrame();
			system.Render( world, view, target );
			Assert.IsTrue( system.OcclusionActive, "culling" );
			reads += frame.Commands.Count( c => c.Op == GpuOp.ReadBuffer );
		}

		Assert.AreEqual( 2, reads );
	}
}
