using Borealis;
using Borealis.Features;
using Borealis.Gpu;
using BorealisTests.Headless;
using System;
using System.Collections.Generic;
using System.Linq;
using static BorealisTests.TestScenes;

namespace BorealisTests;

/// <summary>
/// Work a frame mustn't repeat, counted rather than timed, as timings drift from machine to machine: a batch key per
/// object, a sort of buckets already in order, a readback every frame.
/// </summary>
// Frames go through Borealis's ShadowMapper, whose cache and frame counter are static
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
		Borealis.Shadows.ShadowMapper.ResetCache();
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
		Borealis.Shadows.ShadowMapper.ResetCache();
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
		Borealis.Shadows.ShadowMapper.ResetCache();
		CollectAndPrepare( system, world, view );

		var feature = system.GetFeature<MeshRenderFeature>();
		var classified = feature.ObjectsClassified;
		var keys = feature.KeysMade;
		Borealis.Shadows.ShadowMapper.ResetCache();
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
		Borealis.Shadows.ShadowMapper.ResetCache();
		CollectAndPrepare( system, world, view );

		var feature = system.GetFeature<MeshRenderFeature>();
		var changed = (MeshObject)world.Objects[500];
		changed.Attributes = new HeadlessParameters();
		var classified = feature.ObjectsClassified;
		var keys = feature.KeysMade;
		Borealis.Shadows.ShadowMapper.ResetCache();
		CollectAndPrepare( system, world, view );

		var kinds = (system.MainPass.Visible( feature ).Contains( changed.Index ) ? 1 : 0)
			+ (system.ShadowMaps.Passes.ToArray().Any( p => p.Visible( feature ).Contains( changed.Index ) ) ? 1 : 0);
		Assert.IsTrue( kinds > 0, "the changed object is drawn" );
		Assert.AreEqual( kinds, feature.ObjectsClassified - classified, "objects classified" );
		Assert.AreEqual( 1, feature.KeysMade - keys, "one new bin, which the main and shadow views share" );
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

			Borealis.Shadows.ShadowMapper.ResetCache();
			CollectAndPrepare( system, world, view );
			var kept = Describe( system, world );

			using var rebuilt = new RenderSystem();
			Borealis.Shadows.ShadowMapper.ResetCache();
			CollectAndPrepare( rebuilt, world, view );
			Assert.AreEqual( Describe( rebuilt, world ), kept, $"frame {frame}" );
		}
	}

	/// <summary>
	/// A rigid mesh's transform entry is kept: written when the object moves or changes tint and not otherwise, and uploaded
	/// into each of the buffers frames in flight read once it's written, so what the buffer a frame draws with holds stays what
	/// the objects are, as they move, change, go and come.
	/// </summary>
	[TestMethod]
	public void KeptEntriesReachEveryBuffer()
	{
		var world = Boxes( TestMesh( false ), 300 );
		var view = LookingDownX();
		using var target = ViewTarget.Create( new Vector2Int( 64, 64 ) );
		using var system = new RenderSystem();
		var transforms = system.Transforms;
		var random = new Random( 3 );

		void Frame()
		{
			RenderHost.Current.NextFrame();
			system.Render( world, view, target );
		}

		// Slots are made between frames, for the objects that wanted one
		for ( int i = 0; i < 4; i++ ) Frame();
		Assert.AreEqual( 0, transforms.Count, "no frame entries once every box has a slot" );

		var writes = transforms.KeptWrites;
		for ( int i = 0; i < 4; i++ ) Frame();
		Assert.AreEqual( writes, transforms.KeptWrites, "nothing changed, so nothing written" );
		Assert.AreEqual( 0, transforms.KeptUploaded, "or uploaded" );

		// One moves: written once, then uploaded into each of the three buffers in turn
		var moved = world.Objects[150];
		moved.Transform = new Transform( moved.Transform.Position + new Vector3( 0, 0, 1 ) );
		for ( int i = 0; i < 3; i++ )
		{
			Frame();
			Assert.AreEqual( writes + 1, transforms.KeptWrites, "written once" );
			Assert.AreEqual( 1, transforms.KeptUploaded, $"uploaded in frame {i}" );
		}

		Frame();
		Assert.AreEqual( 0, transforms.KeptUploaded, "every buffer has it" );

		for ( int frame = 0; frame < 40; frame++ )
		{
			for ( int c = 0; c < 8; c++ )
			{
				var obj = (MeshObject)world.Objects[random.Next( world.Count )];
				switch ( random.Next( 4 ) )
				{
					case 0: obj.Transform = new Transform( obj.Transform.Position + new Vector3( 0, 0.5f, 0 ) ); break;
					case 1: obj.Tint = new Color( (float)random.NextDouble(), 1, 1 ); break;
					case 2: world.Remove( obj ); break;
					default: world.Add( new MeshObject( obj.Mesh, new Transform( new Vector3( 100 + random.Next( 30 ), random.Next( 30 ) - 15, random.Next( 30 ) - 15 ) ) ) ); break;
				}
			}

			Frame();
			var entries = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, TransformBuffer.Entry>( ((HeadlessBuffer)transforms.Current.Resource).Contents );
			var drawn = 0;
			foreach ( var obj in world.Objects )
			{
				var slot = transforms.SlotOf( obj );
				if ( slot < 0 ) continue;

				Assert.AreEqual( TransformBuffer.MakeEntry( world.LocalToWorld[obj.Index], ((MeshObject)obj).Tint ), entries[slot], $"frame {frame}, object {obj.Index}" );
				drawn++;
			}

			Assert.IsTrue( drawn > 250, $"{drawn} drawn" );
		}
	}

	/// <summary>
	/// Bins nothing draws with are dropped, so objects taking new attributes every frame don't pile them up, and an object out
	/// of view for longer than a bin lives finds its bin again and still matches a rebuild.
	/// </summary>
	[TestMethod]
	public void UnusedBinsAreDropped()
	{
		var world = Boxes( TestMesh( false ), 100 );
		var away = new MeshObject( TestMesh( false, true ), new Transform( new Vector3( -100, 0, 0 ) ) );
		world.Add( away );
		var view = LookingDownX();
		using var system = new RenderSystem();
		var feature = system.GetFeature<MeshRenderFeature>();

		var frames = MeshRenderFeature.BinLifetime * 4;
		var most = 0;
		for ( int frame = 0; frame < frames; frame++ )
		{
			for ( int c = 0; c < 10; c++ )
				((MeshObject)world.Objects[(frame * 10 + c) % 100]).Attributes = new HeadlessParameters();

			// Behind the camera for most of the run, then back in view
			away.Transform = new Transform( new Vector3( frame < frames - 4 ? -100 : 120, 0, 0 ) );
			CollectAndPrepare( system, world, view );
			if ( frame > MeshRenderFeature.BinLifetime * 2 ) most = Math.Max( most, feature.BinCount );
		}

		Assert.IsTrue( most < 10 * (MeshRenderFeature.BinLifetime + MeshRenderFeature.BinSweepInterval) + 100, $"{most} bins kept" );
		Assert.IsTrue( system.MainPass.Visible( feature ).Contains( away.Index ), "back in view" );

		var kept = Describe( system, world );
		using var rebuilt = new RenderSystem();
		CollectAndPrepare( rebuilt, world, view );
		Assert.AreEqual( Describe( rebuilt, world ), kept );
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
	/// The occlusion cull's counts are read back every <see cref="Borealis.Culling.OcclusionCuller.StatsInterval"/> frames, not
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
		Borealis.Shadows.ShadowMapper.ResetCache();

		const int frames = Borealis.Culling.OcclusionCuller.StatsInterval * 2;
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
