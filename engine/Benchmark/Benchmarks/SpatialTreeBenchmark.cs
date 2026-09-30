using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using Sandbox;
using Sandbox.SceneRenderer;
using Sandbox.SceneRenderer.Culling;
using V3 = System.Numerics.Vector3;

/// <summary>
/// Paired, deterministic CPU benchmark of tree maintenance and renderer culling. No native engine required.
/// </summary>
internal static class SpatialTreeBenchmark
{
	interface ITree
	{
		int Add( in V3 min, in V3 max, int item );
		bool Move( int leaf, in V3 min, in V3 max );
		void Remove( int leaf );
		void Query<T>( ref T query, List<int> results, out int rejected ) where T : struct, ISpatialQuery;
		void Validate();
		int Height { get; }
	}

	readonly struct Baseline : ITree
	{
		readonly SpatialTree tree = new();
		public Baseline() { }
		public int Add( in V3 min, in V3 max, int item ) => tree.Add( min, max, item );
		public bool Move( int leaf, in V3 min, in V3 max ) => tree.Move( leaf, min, max );
		public void Remove( int leaf ) => tree.Remove( leaf );
		public void Query<T>( ref T query, List<int> results, out int rejected ) where T : struct, ISpatialQuery => tree.QueryClassified( ref query, results, out rejected );
		public void Validate() => tree.Validate();
		public int Height => tree.Height;
	}

	readonly struct Candidate : ITree
	{
		readonly Box3DTree tree = new();
		public Candidate() { }
		public int Add( in V3 min, in V3 max, int item ) => tree.Add( min, max, item );
		public bool Move( int leaf, in V3 min, in V3 max ) => tree.Move( leaf, min, max );
		public void Remove( int leaf ) => tree.Remove( leaf );
		public void Query<T>( ref T query, List<int> results, out int rejected ) where T : struct, ISpatialQuery => tree.QueryClassified( ref query, results, out rejected );
		public void Validate() => tree.Validate();
		public int Height => tree.Height;
	}

	readonly struct InsertRotations : ITree
	{
		readonly Box3DTree tree = new( false );
		public InsertRotations() { }
		public int Add( in V3 min, in V3 max, int item ) => tree.Add( min, max, item );
		public bool Move( int leaf, in V3 min, in V3 max ) => tree.Move( leaf, min, max );
		public void Remove( int leaf ) => tree.Remove( leaf );
		public void Query<T>( ref T query, List<int> results, out int rejected ) where T : struct, ISpatialQuery => tree.QueryClassified( ref query, results, out rejected );
		public void Validate() => tree.Validate();
		public int Height => tree.Height;
	}

	struct CountingQuery<T> : ISpatialQuery where T : struct, ISpatialQuery
	{
		public T Query;
		public int Tests;
		public Containment Test( in Vector3 min, in Vector3 max ) { Tests++; return Query.Test( min, max ); }
		public bool Reject( in Vector3 min, in Vector3 max ) => Query.Reject( min, max );
	}

	readonly struct Heuristic<TPolicy> : ITree where TPolicy : struct
	{
		readonly HeuristicTree<TPolicy> tree = new();
		public Heuristic() { }
		public int Add( in V3 min, in V3 max, int item ) => tree.Add( min, max, item );
		public bool Move( int leaf, in V3 min, in V3 max ) => tree.Move( leaf, min, max );
		public void Remove( int leaf ) => tree.Remove( leaf );
		public void Query<T>( ref T query, List<int> results, out int rejected ) where T : struct, ISpatialQuery => tree.Query( ref query, results, out rejected );
		public void Validate() => tree.Validate();
		public int Height => tree.Height;
	}

	static string TreeName<T>() => typeof( T ).IsGenericType ? typeof( T ).GenericTypeArguments[0].Name : typeof( T ).Name;

	sealed record Implementation( Action<Workload> Verify, Action<Workload, int, bool> Measure );

	sealed class Workload
	{
		public string Name;
		public V3[] Centers, Extents;
		public SpatialTree.FrustumQuery[] Frustums;
		public SpatialTree.SphereQuery[] Spheres;
	}

	sealed record Measurement( string Scene, int Seed, string Tree, string Operation, double Milliseconds, long AllocatedBytes,
		int Height, long PlaneOrSphereTests, long Candidates );

	static readonly List<Measurement> measurements = new();
	static long sink;
	static int moveFrames;

	public static void Run( string[] args )
	{
		if ( args.Length > 2 && args[1] == "--summarize" )
		{
			measurements.Clear();
			measurements.AddRange( JsonSerializer.Deserialize<List<Measurement>>( File.ReadAllText( args[2] ) ) );
			Summarize();
			return;
		}

		var output = args.Length > 1 ? args[1] : "spatial-tree-results.json";
		var seeds = args.Length > 2 ? int.Parse( args[2] ) : 3;
		moveFrames = args.Length > 3 ? int.Parse( args[3] ) : 200;
		var alternatives = args.Length > 4 && args[4] == "alternatives";
		Implementation[] implementations = alternatives
			? [new( Verify<Baseline>, Measure<Baseline> ),
				new( Verify<Heuristic<SurfaceAreaPolicy>>, Measure<Heuristic<SurfaceAreaPolicy>> ),
				new( Verify<Heuristic<VolumePolicy>>, Measure<Heuristic<VolumePolicy>> ),
				new( Verify<Heuristic<ProximityPolicy>>, Measure<Heuristic<ProximityPolicy>> ),
				new( Verify<Heuristic<LeafWeightedPolicy>>, Measure<Heuristic<LeafWeightedPolicy>> )]
			: [new( Verify<Baseline>, Measure<Baseline> ), new( Verify<Candidate>, Measure<Candidate> ), new( Verify<InsertRotations>, Measure<InsertRotations> )];
		measurements.Clear();
		Console.WriteLine( $"{System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}; {System.Runtime.InteropServices.RuntimeInformation.OSDescription}; {Environment.ProcessorCount} logical CPUs" );
		Console.WriteLine( $"TieredCompilation={Environment.GetEnvironmentVariable( "DOTNET_TieredCompilation" ) ?? "default"}; seeds={seeds}; move frames={moveFrames}; five repetitions with rotating implementation order." );
		Console.WriteLine( "Times are milliseconds per build, update batch, or query batch. Query batches: 5 frustums or 32 light spheres, including exact leaf tests." );

		var warmup = Create( "Mixed", 2000, 123 );
		for ( int i = 0; i < 3; i++ )
		{
			foreach ( var implementation in implementations ) implementation.Measure( warmup, 0, false );
		}

		foreach ( var (name, count) in new[] { ("Grid", 10000), ("Grid", 50000), ("Mixed", 50000), ("Clusters", 50000), ("Sorted", 50000), ("Overlap", 10000) } )
		{
			for ( int seed = 1; seed <= seeds; seed++ )
			{
				var scene = Create( name, count, seed );
				foreach ( var implementation in implementations ) implementation.Verify( scene );
				Console.WriteLine( $"Verified {scene.Name}, seed {seed}: all candidates against brute force before/after movement and churn." );
				for ( int repeat = 0; repeat < 5; repeat++ )
				{
					for ( int i = 0; i < implementations.Length; i++ )
						implementations[(seed + repeat + i) % implementations.Length].Measure( scene, seed, true );
				}
			}
		}
		File.WriteAllText( output, JsonSerializer.Serialize( measurements, new JsonSerializerOptions { WriteIndented = true } ) );
		Console.WriteLine( $"Saved {measurements.Count} measurements to {output}; checksum {sink}" );
		Summarize();
	}

	static void Summarize()
	{
		Console.WriteLine( "MEDIANS: scene / operation / baseline ms / candidate ms (change)" );
		foreach ( var group in measurements.GroupBy( x => (x.Scene, x.Operation) ) )
		{
			var baseline = Median( group.Where( x => x.Tree == nameof( Baseline ) ).Select( x => x.Milliseconds ) );
			Console.Write( $"{group.Key.Scene,-15} {group.Key.Operation,-18} {baseline,9:F3}" );
			foreach ( var candidate in group.Where( x => x.Tree != nameof( Baseline ) ).GroupBy( x => x.Tree ) )
			{
				var median = Median( candidate.Select( x => x.Milliseconds ) );
				Console.Write( $" {candidate.Key}: {median:F3} ({100 * (median / baseline - 1):F1}%)" );
			}
			Console.WriteLine();
		}
		Console.WriteLine( "CHECKS: scene / tree / max height / max query bytes / median moved sphere tests" );
		foreach ( var group in measurements.GroupBy( x => (x.Scene, x.Tree) ) )
		{
			var bytes = group.Where( x => x.Operation.Contains( "frustum", StringComparison.OrdinalIgnoreCase ) || x.Operation.Contains( "sphere", StringComparison.OrdinalIgnoreCase ) ).Max( x => x.AllocatedBytes );
			var tests = Median( group.Where( x => x.Operation == "Moved-spheres" ).Select( x => (double)x.PlaneOrSphereTests ) );
			Console.WriteLine( $"{group.Key.Scene,-15} {group.Key.Tree,-16} {group.Max( x => x.Height ),4} {bytes,6} {tests,10}" );
		}
		Console.WriteLine( "PER-SEED: candidate / baseline ratio for (move batch + moved frustums + moved spheres); then static query ratio" );
		foreach ( var group in measurements.GroupBy( x => (x.Scene, x.Seed) ) )
		{
			double Cost( string tree, params string[] operations ) => operations.Sum( op => Median( group.Where( x => x.Tree == tree && x.Operation == op ).Select( x => x.Milliseconds ) ) );
			var baseline = Cost( nameof( Baseline ), "Move-10%-frame", "Moved-frustums", "Moved-spheres" );
			foreach ( var tree in group.Select( x => x.Tree ).Distinct().Where( x => x != nameof( Baseline ) ) )
			{
				var candidate = Cost( tree, "Move-10%-frame", "Moved-frustums", "Moved-spheres" );
				var staticRatio = Cost( tree, "Frustums", "Spheres" ) / Cost( nameof( Baseline ), "Frustums", "Spheres" );
				Console.WriteLine( $"{group.Key.Scene,-15} {group.Key.Seed} {tree}: {candidate / baseline:F3} static {staticRatio:F3}" );
			}
		}
	}

	static double Median( IEnumerable<double> values )
	{
		var sorted = values.Order().ToArray();
		return sorted[sorted.Length / 2];
	}

	static Workload Create( string name, int count, int seed )
	{
		var random = new Random( seed );
		var centers = new V3[count];
		var extents = new V3[count];
		for ( int i = 0; i < count; i++ )
		{
			centers[i] = new V3( random.NextSingle() * 16000 - 8000, random.NextSingle() * 16000 - 8000, random.NextSingle() * 2000 );
			extents[i] = new V3( 1 + random.NextSingle() * 60, 1 + random.NextSingle() * 60, 1 + random.NextSingle() * 100 );
			if ( name == "Grid" )
			{
				centers[i] = new V3( (i % 100) * 150 - 7500, (i / 100 % 100) * 150 - 7500, i / 10000 * 150 );
				extents[i] = new V3( 20 );
			}
			if ( name == "Clusters" )
			{
				var cluster = i % 32;
				centers[i] = new V3( (cluster % 8) * 2000 - 7000, (cluster / 8) * 3500 - 5250, 0 )
					+ new V3( random.NextSingle() * 500, random.NextSingle() * 500, random.NextSingle() * 500 );
			}
			if ( name == "Overlap" )
			{
				centers[i] *= 0.05f;
				extents[i] *= 10;
			}
			if ( name == "Mixed" && i % 100 == 0 ) extents[i] *= 20;
		}
		if ( name == "Sorted" ) Array.Sort( centers, extents, Comparer<V3>.Create( ( a, b ) => a.X.CompareTo( b.X ) ) );

		var view = new RenderView
		{
			Position = new Vector3( -9000, -2000, 1000 ),
			Rotation = Rotation.Identity,
			FieldOfView = 90,
			ZNear = 1,
			ZFar = 20000,
			Viewport = new Rect( 0, 0, 1920, 1080 ),
			SizeCullThreshold = 0.0025f,
		};
		view.Update();
		var size = new SizeCull( view );
		var frustums = new SpatialTree.FrustumQuery[5];
		frustums[0] = new() { Frustum = view.Frustum, Size = size };
		for ( int i = 1; i < frustums.Length; i++ )
		{
			view.Orthographic = true;
			view.OrthoSize = new Vector2( 1000 * (1 << i), 1000 * (1 << i) );
			view.Position = new Vector3( -9000, i * 500 - 2000, 500 );
			view.Update();
			frustums[i] = new() { Frustum = view.Frustum, Size = size };
		}
		var spheres = new SpatialTree.SphereQuery[32];
		for ( int i = 0; i < spheres.Length; i++ )
			spheres[i] = new() { Center = centers[random.Next( count )], Radius = 300 + random.NextSingle() * 1700, Size = size };
		return new() { Name = $"{name}-{count / 1000}k", Centers = centers, Extents = extents, Frustums = frustums, Spheres = spheres };
	}

	static T Build<T>( Workload scene, int[] leaves ) where T : struct, ITree
	{
		var tree = new T();
		for ( int i = 0; i < leaves.Length; i++ ) leaves[i] = tree.Add( scene.Centers[i] - scene.Extents[i], scene.Centers[i] + scene.Extents[i], i );
		return tree;
	}

	static void Measure<T>( Workload scene, int seed, bool record ) where T : struct, ITree
	{
		var leaves = new int[scene.Centers.Length];
		var results = new List<int>( leaves.Length );
		var exactCenters = scene.Centers;
		GC.Collect();
		GC.WaitForPendingFinalizers();
		var allocated = GC.GetAllocatedBytesForCurrentThread();
		var start = Stopwatch.GetTimestamp();
		var tree = Build<T>( scene, leaves );
		Save( "Build", Stopwatch.GetElapsedTime( start ).TotalMilliseconds, GC.GetAllocatedBytesForCurrentThread() - allocated );
		tree.Validate();
		QueryTime( "Frustums", false );
		QueryTime( "Spheres", true );

		allocated = GC.GetAllocatedBytesForCurrentThread();
		start = Stopwatch.GetTimestamp();
		for ( int i = 0; i < leaves.Length; i++ )
		{
			var center = scene.Centers[i] + new V3( 0.1f );
			if ( tree.Move( leaves[i], center - scene.Extents[i], center + scene.Extents[i] ) ) throw new Exception( "Nudge should retain padding" );
		}
		Save( "Nudge-all", Stopwatch.GetElapsedTime( start ).TotalMilliseconds, GC.GetAllocatedBytesForCurrentThread() - allocated );

		allocated = GC.GetAllocatedBytesForCurrentThread();
		start = Stopwatch.GetTimestamp();
		for ( int frame = 0; frame < moveFrames; frame++ ) MoveBatch( ref tree, scene, leaves, frame );
		Save( "Move-10%-frame", Stopwatch.GetElapsedTime( start ).TotalMilliseconds / moveFrames, (GC.GetAllocatedBytesForCurrentThread() - allocated) / moveFrames );
		tree.Validate();
		exactCenters = (V3[])scene.Centers.Clone();
		for ( int i = 0; i < leaves.Length; i += 10 ) exactCenters[i] = MovedCenter( scene, i, moveFrames - 1 );
		QueryTime( "Moved-frustums", false );
		QueryTime( "Moved-spheres", true );

		allocated = GC.GetAllocatedBytesForCurrentThread();
		start = Stopwatch.GetTimestamp();
		for ( int i = 0; i < leaves.Length; i += 10 )
		{
			tree.Remove( leaves[i] );
			leaves[i] = tree.Add( scene.Centers[i] - scene.Extents[i], scene.Centers[i] + scene.Extents[i], i );
		}
		Save( "Churn-10%", Stopwatch.GetElapsedTime( start ).TotalMilliseconds, GC.GetAllocatedBytesForCurrentThread() - allocated );
		tree.Validate();

		void QueryTime( string operation, bool spheres )
		{
			for ( int i = 0; i < 3; i++ ) sink += QueryBatch( ref tree, scene, exactCenters, results, spheres );
			var bytes = GC.GetAllocatedBytesForCurrentThread();
			var before = Stopwatch.GetTimestamp();
			int iterations = 0;
			do
			{
				sink += QueryBatch( ref tree, scene, exactCenters, results, spheres );
				iterations++;
			} while ( Stopwatch.GetElapsedTime( before ).TotalMilliseconds < 150 );
			var elapsed = Stopwatch.GetElapsedTime( before ).TotalMilliseconds / iterations;
			bytes = (GC.GetAllocatedBytesForCurrentThread() - bytes) / iterations;
			long tests = 0, candidates = 0;
			if ( spheres )
			{
				foreach ( var query in scene.Spheres )
				{
					var counted = new CountingQuery<SpatialTree.SphereQuery> { Query = query };
					results.Clear(); tree.Query( ref counted, results, out _ );
					tests += counted.Tests; candidates += results.Count;
				}
			}
			else
			{
				foreach ( var query in scene.Frustums )
				{
					var counted = new CountingQuery<SpatialTree.FrustumQuery> { Query = query };
					results.Clear(); tree.Query( ref counted, results, out _ );
					tests += counted.Tests; candidates += results.Count;
				}
			}
			Save( operation, elapsed, bytes, tests, candidates );
		}

		void Save( string operation, double elapsed, long bytes, long tests = 0, long candidates = 0 )
		{
			if ( !record ) return;
			var row = new Measurement( scene.Name, seed, TreeName<T>(), operation, elapsed, bytes, tree.Height, tests, candidates );
			measurements.Add( row );
		}
	}

	static V3 MovedCenter( Workload scene, int i, int frame ) => scene.Centers[i]
		+ new V3( MathF.Sin( frame * 0.3f + i ) * 400, MathF.Cos( frame * 0.3f + i ) * 400, MathF.Sin( frame * 0.1f ) * 400 );

	static void MoveBatch<T>( ref T tree, Workload scene, int[] leaves, int frame ) where T : struct, ITree
	{
		for ( int i = 0; i < leaves.Length; i += 10 )
		{
			var center = MovedCenter( scene, i, frame );
			tree.Move( leaves[i], center - scene.Extents[i], center + scene.Extents[i] );
		}
	}

	static long QueryBatch<T>( ref T tree, Workload scene, V3[] centers, List<int> results, bool spheres ) where T : struct, ITree
	{
		long count = 0;
		if ( spheres )
		{
			foreach ( var original in scene.Spheres )
			{
				var query = original;
				results.Clear(); tree.Query( ref query, results, out _ );
				foreach ( var entry in results )
				{
					var i = entry >> 1;
					var center = centers[i];
					var extents = scene.Extents[i];
					if ( (entry & 1) == 0 && query.Test( center - extents, center + extents ) == Containment.Outside ) continue;
					if ( !query.Size.Culls( center, extents ) ) count++;
				}
			}
		}
		else
		{
			foreach ( var original in scene.Frustums )
			{
				var query = original;
				results.Clear(); tree.Query( ref query, results, out _ );
				foreach ( var entry in results )
				{
					var i = entry >> 1;
					if ( (entry & 1) == 0 && !query.Frustum.Intersects( centers[i], scene.Extents[i] ) ) continue;
					if ( !query.Size.Culls( centers[i], scene.Extents[i] ) ) count++;
				}
			}
		}
		return count;
	}

	static void Verify<TTree>( Workload scene ) where TTree : struct, ITree
	{
		var leavesA = new int[scene.Centers.Length];
		var a = Build<TTree>( scene, leavesA );
		var results = new List<int>( leavesA.Length );
		for ( int state = 0; state < 3; state++ )
		{
			a.Validate();
			var centers = (V3[])scene.Centers.Clone();
			if ( state == 1 ) for ( int i = 0; i < centers.Length; i += 10 ) centers[i] = MovedCenter( scene, i, moveFrames - 1 );
			foreach ( var original in scene.Frustums )
			{
				var query = original;
				var expected = Enumerable.Range( 0, centers.Length ).Where( i => query.Frustum.Intersects( centers[i], scene.Extents[i] ) && !query.Size.Culls( centers[i], scene.Extents[i] ) ).ToHashSet();
				Check( ref a, query, expected, centers );
			}
			foreach ( var original in scene.Spheres )
			{
				var query = original;
				var expected = Enumerable.Range( 0, centers.Length ).Where( i => query.Test( centers[i] - scene.Extents[i], centers[i] + scene.Extents[i] ) != Containment.Outside && !query.Size.Culls( centers[i], scene.Extents[i] ) ).ToHashSet();
				CheckSphere( ref a, query, expected, centers );
			}
			if ( state == 0 ) for ( int frame = 0; frame < moveFrames; frame++ ) MoveBatch( ref a, scene, leavesA, frame );
			if ( state == 1 ) for ( int i = 0; i < centers.Length; i += 10 )
			{
				a.Remove( leavesA[i] );
				leavesA[i] = a.Add( scene.Centers[i] - scene.Extents[i], scene.Centers[i] + scene.Extents[i], i );
			}
		}

		void Check<T>( ref T tree, SpatialTree.FrustumQuery query, HashSet<int> expected, V3[] centers ) where T : struct, ITree
		{
			results.Clear(); tree.Query( ref query, results, out _ );
			var found = results.Where( e => ((e & 1) != 0 || query.Frustum.Intersects( centers[e >> 1], scene.Extents[e >> 1] )) && !query.Size.Culls( centers[e >> 1], scene.Extents[e >> 1] ) ).Select( e => e >> 1 ).ToArray();
			if ( found.Length != expected.Count || !expected.SetEquals( found ) ) throw new Exception( $"{scene.Name}: {typeof( T ).Name} frustum mismatch" );
		}
		void CheckSphere<T>( ref T tree, SpatialTree.SphereQuery query, HashSet<int> expected, V3[] centers ) where T : struct, ITree
		{
			results.Clear(); tree.Query( ref query, results, out _ );
			var found = results.Where( e => ((e & 1) != 0 || query.Test( centers[e >> 1] - scene.Extents[e >> 1], centers[e >> 1] + scene.Extents[e >> 1] ) != Containment.Outside) && !query.Size.Culls( centers[e >> 1], scene.Extents[e >> 1] ) ).Select( e => e >> 1 ).ToArray();
			if ( found.Length != expected.Count || !expected.SetEquals( found ) ) throw new Exception( $"{scene.Name}: {typeof( T ).Name} sphere mismatch" );
		}
	}
}
