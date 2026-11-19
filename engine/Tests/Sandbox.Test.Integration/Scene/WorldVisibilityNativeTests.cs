using Borealis.Source2.Resources;
using Borealis.Source2.World;
using NativeEngine;
using Sandbox.Network;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using N3 = System.Numerics.Vector3;

namespace SceneTests;

/// <summary>
/// Network visibility reads the map's visibility itself (<see cref="WorldVisibility"/>, <see cref="SceneMap.Visibility"/>)
/// since maps stopped loading native's PVS. Its answers have to be native's: the reference here is native's PVS, built
/// once with <c>loadVis</c>. Needs a map with real visibility mounted (<c>SBOX_TEST_VIS_MAP</c>, a game path to its VPK);
/// inconclusive without one.
/// </summary>
[TestClass]
public class WorldVisibilityNativeTests
{
	const string DefaultMap = "maps/rl_white_bloodfield_benchmark.vpk";

	static string MapOrSkip()
	{
		var map = Environment.GetEnvironmentVariable( "SBOX_TEST_VIS_MAP" ) ?? DefaultMap;
		if ( FileSystem.Mounted.GetFullPath( map ) is not { } full || !File.Exists( full ) ) Assert.Inconclusive( $"{map} isn't mounted" );
		return map;
	}

	[TestMethod]
	public unsafe void AnswersMatchNativesPvs()
	{
		var map = MapOrSkip();
		var origin = new Vector3( 96, -40, 24 );
		var world = new SceneWorld();
		var worldRef = g_pWorldRendererMgr.CreateWorld( map, world, false, true, true, false, world.native.GetWorldDebugName(), new Transform( origin ) );
		Assert.IsTrue( worldRef.IsValid, "native loads the map" );
		var pvs = g_pEnginePVSManager.BuildPvs( worldRef );

		try
		{
			using var package = VpkFile.Open( FileSystem.Mounted.GetFullPath( map ) );
			var visibility = WorldVisibility.Read( package.Read( worldRef.GetFolder() + "/world_visibility.vvis_c" ) );
			Assert.IsNotNull( visibility );
			Assert.IsTrue( pvs.IsValid );
			Assert.AreEqual( pvs.IsEmptyPVS(), visibility.ClusterCount == 2 );
			if ( visibility.IsEmpty ) Assert.Inconclusive( $"{map} has no clusters" );

			var random = new Random( 18 );
			var grow = (visibility.Maxs - visibility.Mins) * 0.05f;
			var lo = (Vector3)visibility.Mins - (Vector3)grow + origin;
			var hi = (Vector3)visibility.Maxs + (Vector3)grow + origin;

			// Anywhere in (and a little around) the map, half of them on the vis grid so they land on voxel and node faces
			Vector3 Point()
			{
				var p = new Vector3( lo.x + random.NextSingle() * (hi.x - lo.x), lo.y + random.NextSingle() * (hi.y - lo.y), lo.z + random.NextSingle() * (hi.z - lo.z) );
				if ( random.Next( 2 ) == 0 )
				{
					var g = visibility.GridSize;
					p = new Vector3( MathF.Round( (p.x - origin.x) / g ) * g, MathF.Round( (p.y - origin.y) / g ) * g, MathF.Round( (p.z - origin.z) / g ) * g ) + origin;
				}

				return p;
			}

			// Small, medium and big boxes: those 512 or more on every side take native's enclosed cluster lists
			Vector3 Extent() => random.Next( 3 ) switch
			{
				0 => new Vector3( random.NextSingle() * 32, random.NextSingle() * 32, random.NextSingle() * 32 ),
				1 => new Vector3( random.NextSingle() * 300, random.NextSingle() * 300, random.NextSingle() * 300 ),
				_ => new Vector3( 256 + random.NextSingle() * 1500, 256 + random.NextSingle() * 1500, 256 + random.NextSingle() * 1500 ),
			};

			// Most random spots are in solid or outside, which see everything: also look from spots that don't
			var open = new List<Vector3>();
			for ( var tries = 0; tries < 20000 && open.Count < 64; tries++ )
			{
				var spot = Point();
				for ( var k = 0; k < 8; k++ )
				{
					if ( visibility.IsPointVisible( [(N3)spot], Point(), origin ) ) continue;
					open.Add( spot );
					break;
				}
			}

			Vector3 Source() => open.Count > 0 && random.Next( 10 ) < 7
				? open[random.Next( open.Count )] + new Vector3( random.NextSingle() - 0.5f, random.NextSingle() - 0.5f, random.NextSingle() - 0.5f ) * visibility.GridSize * 2
				: Point();

			var sources = new Vector3[3];
			int points = 0, pointsSeen = 0, boxes = 0, boxesSeen = 0, wrong = 0;
			var firstWrong = "";
			for ( var i = 0; i < 40000; i++ )
			{
				var count = i % 50 == 0 ? 0 : i % 5 == 0 ? 3 : 1;
				for ( var s = 0; s < count; s++ ) sources[s] = Source();
				var managedSources = MemoryMarshal.Cast<Vector3, N3>( sources.AsSpan( 0, count ) );

				var point = random.Next( 2 ) == 0 ? Source() : Point();
				var extent = Extent();
				bool nativePoint, nativeBox;
				fixed ( Vector3* s = sources )
				{
					nativePoint = pvs.IsInPVS( count, (IntPtr)s, point );
					nativeBox = pvs.IsAbsBoxInPVS( count, (IntPtr)s, point - extent, point + extent );
				}

				var managedPoint = visibility.IsPointVisible( managedSources, point, origin );
				var managedBox = visibility.IsBoxVisible( managedSources, point - extent, point + extent, origin );

				points++;
				boxes++;
				if ( nativePoint ) pointsSeen++;
				if ( nativeBox ) boxesSeen++;
				if ( nativePoint != managedPoint || nativeBox != managedBox )
				{
					if ( wrong == 0 ) firstWrong = $"sources {string.Join( ", ", sources.Take( count ) )}, point {point} (native {nativePoint}), box +-{extent} (native {nativeBox})";
					wrong++;
				}
			}

			Console.WriteLine( $"{map}: {visibility.ClusterCount} clusters, {open.Count} open spots; points {pointsSeen}/{points} seen, boxes {boxesSeen}/{boxes} seen, {wrong} differ" );
			Assert.AreEqual( 0, wrong, firstWrong );

			// Both answers came up plenty, so the comparison means something
			Assert.IsTrue( pointsSeen >= 1000 && points - pointsSeen >= 1000, $"points seen {pointsSeen}/{points}" );
			Assert.IsTrue( boxesSeen >= 1000 && boxes - boxesSeen >= 1000, $"boxes seen {boxesSeen}/{boxes}" );
		}
		finally
		{
			g_pEnginePVSManager.DestroyPvs( pvs );
			worldRef.Release();
			g_pWorldRendererMgr.ServiceWorldRequests();
			world.Delete();
		}
	}

	/// <summary>
	/// A loaded map reads its visibility on first use and drops it when deleted; the scene's connection checks use it,
	/// and a connection sees what the visibility says from where it looks.
	/// </summary>
	[TestMethod]
	public void SceneChecksUseTheMapsVisibility()
	{
		var map = MapOrSkip();

		var scene = new Scene();
		using var sceneScope = scene.Push();
		var instance = scene.CreateObject().Components.Create<MapInstance>();
		Assert.IsTrue( Load( instance, map ), "the map loads" );
		var sceneMap = instance.GetNetworkMap();
		Assert.IsNotNull( sceneMap, "the map loaded" );

		// Read on a worker, as a host does, then taken when first asked for
		sceneMap.PrefetchVisibility();
		var visibility = sceneMap.Visibility;
		Assert.IsNotNull( visibility );
		Assert.IsFalse( visibility.IsEmpty );
		Assert.AreSame( visibility, sceneMap.Visibility, "read once" );

		scene.SceneNetworkUpdate();

		// Find a spot that can't see another, and one that can
		var random = new Random( 7 );
		var connection = new MockConnection( Guid.NewGuid() ) { VisibilityOrigins = new Vector3[1] };
		int hidden = 0, seen = 0;
		for ( var i = 0; i < 2000 && (hidden == 0 || seen == 0); i++ )
		{
			var from = Lerp( visibility, random );
			var to = Lerp( visibility, random );
			connection.VisibilityOrigins[0] = from;
			var box = BBox.FromPositionAndSize( to, 16 );
			var expected = visibility.IsBoxVisible( [(N3)from], box.Mins, box.Maxs, sceneMap.Origin );
			Assert.AreEqual( expected, scene.IsBBoxVisibleToConnection( connection, box ) );
			Assert.AreEqual( visibility.IsPointVisible( [(N3)from], to, sceneMap.Origin ), scene.IsPointVisibleToConnection( connection, to ) );
			if ( expected ) seen++; else hidden++;
		}

		Assert.IsTrue( hidden > 0 && seen > 0, $"{hidden} hidden, {seen} seen" );

		instance.UnloadMap();
		Assert.IsNull( sceneMap.Visibility, "freed with the map" );
		scene.SceneNetworkUpdate();
		Assert.IsTrue( scene.IsBBoxVisibleToConnection( connection, BBox.FromPositionAndSize( 0, 16 ) ), "no map, everything is visible" );
		scene.Destroy();
	}

	static bool Load( MapInstance instance, string mapName )
	{
		var method = typeof( MapInstance ).GetMethod( "LoadMapAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance, [typeof( string ), typeof( LoadingContext )] );
		var task = (Task<bool>)method.Invoke( instance, [mapName, null] );

		var deadline = System.Diagnostics.Stopwatch.StartNew();
		while ( !task.IsCompleted && deadline.ElapsedMilliseconds < 60000 )
		{
			Sandbox.Tasks.SyncContext.MainThread.ProcessQueue();
			System.Threading.Thread.Sleep( 1 );
		}

		Assert.IsTrue( task.IsCompleted, "map load should finish" );
		return task.Result;
	}

	static Vector3 Lerp( WorldVisibility visibility, Random random )
	{
		var t = new N3( random.NextSingle(), random.NextSingle(), random.NextSingle() );
		return visibility.Mins + (visibility.Maxs - visibility.Mins) * t;
	}
}
