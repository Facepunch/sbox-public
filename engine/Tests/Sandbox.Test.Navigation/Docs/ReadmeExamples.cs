using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Nav.Lib.Tests.Docs;

/// <summary>
/// The three examples of the library's README.md, compiled and run. The example code is kept as the README has
/// it; what is added around it are the checks. Change one, change the other.
/// </summary>
[TestClass]
public class ReadmeExamples
{
	// The level of the first example. Metres. A 50 x 30 floor and a wall 3 high across part of it.
	static readonly Vector3[] vertices =
	[
		new( 0, 0, 0 ), new( 50, 0, 0 ), new( 50, 30, 0 ), new( 0, 30, 0 ),
		new( 25, 0, 0 ), new( 25, 20, 0 ), new( 25, 20, 3 ), new( 25, 0, 3 ),
	];
	static readonly int[] indices =
	[
		0, 1, 2, 0, 2, 3,   // floor: cross( v1 - v0, v2 - v0 ) points up, so it is ground
		4, 5, 6, 4, 6, 7,   // wall: every triangle is an obstacle, whichever way it faces
	];

	// The mesh "built as in the first example".
	static NavMesh BuildLevel()
	{
		var settings = NavSettings.Create( agentHeight: 1.8f, agentRadius: 0.4f, agentClimb: 0.4f, agentMaxSlope: 45, cellSize: 0.1f, cellHeight: 0.1f );
		var mesh = new NavMesh( settings );
		Assert.AreEqual( 0, mesh.Build( new NavBuilder(), vertices, indices ) );
		return mesh;
	}

	static PathStatus FindPath( NavMesh mesh, NavPath path )
	{
		var settings = mesh.Settings;
		var halfExtents = new Vector3( 2.1f * settings.AgentRadius, 2.1f * settings.AgentRadius, 1.51f * settings.AgentHeight );
		return new NavQuery( mesh ).FindPath( new Vector3( 5, 5, 0 ), new Vector3( 45, 5, 0 ), halfExtents, default, path );
	}

	[TestMethod]
	public void StaticLevelAndPath()
	{
		var settings = NavSettings.Create( agentHeight: 1.8f, agentRadius: 0.4f, agentClimb: 0.4f, agentMaxSlope: 45, cellSize: 0.1f, cellHeight: 0.1f );
		var mesh = new NavMesh( settings );

		var failures = new List<(int X, int Y, string Error)>();
		mesh.Build( new NavBuilder(), vertices, indices, failures );
		foreach ( var (x, y, error) in failures ) Console.WriteLine( $"tile {x},{y} failed: {error}" );

		// One query per thread, kept; one path, reused.
		var query = new NavQuery( mesh );
		var path = new NavPath();
		var halfExtents = new Vector3( 2.1f * settings.AgentRadius, 2.1f * settings.AgentRadius, 1.51f * settings.AgentHeight );
		var status = query.FindPath( new Vector3( 5, 5, 0 ), new Vector3( 45, 5, 0 ), halfExtents, default, path );
		Console.WriteLine( $"{status}, {path.Length:0.0} long" );
		foreach ( var corner in path.Corners ) Console.WriteLine( corner );

		Assert.AreEqual( 0, failures.Count );
		var stats = mesh.GetStats();
		Assert.IsTrue( stats.Tiles > 0 && stats.Polygons > 0 );
		Assert.IsTrue( stats.TrianglesUsed > 0 && stats.TrianglesRejected > 0, "The floor is ground, the wall is not" );
		Assert.AreEqual( PathStatus.Complete, status );

		// Around the end of the wall at y = 20, not through it. Heights are on the polygon plane.
		Assert.IsTrue( path.Corners.Length >= 3 );
		Assert.IsTrue( path.Length > 45, $"{path.Length}" );
		float furthest = 0;
		foreach ( var corner in path.Corners ) furthest = MathF.Max( furthest, corner.Y );
		Assert.IsTrue( furthest >= 20, $"{furthest}" );
		Assert.IsTrue( Vector2.Distance( new( path.Corners[0].X, path.Corners[0].Y ), new( 5, 5 ) ) < 0.01f );
		Assert.IsTrue( Vector2.Distance( new( path.Corners[^1].X, path.Corners[^1].Y ), new( 45, 5 ) ) < 0.01f );
		Assert.IsTrue( MathF.Abs( path.Corners[^1].Z ) <= settings.CellHeight + 0.01f );
	}

	sealed class TileStreamer
	{
		readonly NavMesh mesh;
		readonly Vector3[] levelVertices;
		readonly int[] levelIndices;
		readonly NavBuilder[] builders = new NavBuilder[Environment.ProcessorCount];
		readonly TileJob[] jobs = new TileJob[32];

		public TileStreamer( NavMesh mesh, Vector3[] levelVertices, int[] levelIndices )
		{
			this.mesh = mesh;
			this.levelVertices = levelVertices;
			this.levelIndices = levelIndices;
			for ( int i = 0; i < builders.Length; i++ ) builders[i] = new NavBuilder();
		}

		// One round on the owner thread. A game calls it once a frame; returns how many jobs ran.
		public int Pump()
		{
			int count = mesh.TakeJobs( jobs );
			for ( int i = 0; i < count; i++ )
			{
				if ( jobs[i].NeedsGeometry ) Collect( jobs[i] );
			}

			// One builder per worker, each worker takes the next job nobody has taken.
			int next = -1;
			Parallel.For( 0, Math.Min( builders.Length, count ), worker =>
			{
				for ( int i = Interlocked.Increment( ref next ); i < count; i = Interlocked.Increment( ref next ) ) builders[worker].Build( jobs[i] );
			} );

			mesh.Commit( jobs.AsSpan( 0, count ) );
			for ( int i = 0; i < count; i++ )
			{
				if ( jobs[i].Status == TileJobStatus.Failed ) Console.WriteLine( $"tile {jobs[i].X},{jobs[i].Y} failed: {jobs[i].Error}" );
			}
			return count;
		}

		// The engine's part: the triangles that reach into the job's box, here from one list. An engine asks its
		// physics or scene. No triangles at all is a valid answer and gives an empty tile.
		void Collect( TileJob job )
		{
			var vertices = new List<Vector3>();
			var indices = new List<int>();
			for ( int t = 0; t < levelIndices.Length; t += 3 )
			{
				Vector3 a = levelVertices[levelIndices[t]], b = levelVertices[levelIndices[t + 1]], c = levelVertices[levelIndices[t + 2]];
				var min = Vector3.Min( Vector3.Min( a, b ), c );
				var max = Vector3.Max( Vector3.Max( a, b ), c );
				if ( max.X < job.BoundsMin.X || min.X > job.BoundsMax.X || max.Y < job.BoundsMin.Y || min.Y > job.BoundsMax.Y ) continue;
				int first = vertices.Count;
				vertices.AddRange( [a, b, c] );
				indices.AddRange( [first, first + 1, first + 2] );
			}
			job.SetGeometry( vertices.ToArray(), indices.ToArray() );
		}
	}

	[TestMethod]
	public void JobLoopWithBlocker()
	{
		var settings = NavSettings.Create( agentHeight: 1.8f, agentRadius: 0.4f, agentClimb: 0.4f, agentMaxSlope: 45, cellSize: 0.1f, cellHeight: 0.1f );
		var mesh = new NavMesh( settings );
		var streamer = new TileStreamer( mesh, vertices, indices );   // the level of the first example

		mesh.MarkDirty( new Vector3( 0, 0, -10 ), new Vector3( 50, 30, 10 ) );
		while ( streamer.Pump() > 0 ) { }

		var level = BuildLevel().GetStats();
		Assert.AreEqual( 0, mesh.PendingTiles );
		Assert.AreEqual( (level.Tiles, level.Polygons, level.Vertices), (mesh.GetStats().Tiles, mesh.GetStats().Polygons, mesh.GetStats().Vertices), "The job loop builds what NavMesh.Build builds" );
		var before = new NavPath();
		Assert.AreEqual( PathStatus.Complete, FindPath( mesh, before ) );

		// Only builds from triangles add to the triangle counter
		long triangles = mesh.GetStats().TrianglesUsed;

		// A crate lands in the way. Area NoArea blocks; Margin keeps agents an agent radius away from it.
		int crate = mesh.AddVolume( new NavVolume
		{
			Shape = VolumeShape.Box,
			Area = NavFilter.NoArea,
			LocalToWorld = Matrix4x4.CreateTranslation( 10, 10, 0 ),
			A = new Vector3( -1, -1, -0.5f ),
			B = new Vector3( 1, 1, 2 ),
			Margin = settings.AgentRadius
		} );
		while ( streamer.Pump() > 0 ) { }   // the first volume over a tile asks for its triangles once

		Assert.AreEqual( 0, mesh.PendingTiles );
		Assert.IsTrue( mesh.GetStats().TrianglesUsed > triangles, "The first volume over a tile asks for its triangles" );
		Assert.IsTrue( mesh.GetStats().CacheBytes > 0, "and the mesh keeps the tile's field" );
		var blocked = new NavPath();
		Assert.AreEqual( PathStatus.Complete, FindPath( mesh, blocked ) );
		Assert.IsTrue( blocked.Length > before.Length + 0.01f, $"{blocked.Length} against {before.Length}" );
		triangles = mesh.GetStats().TrianglesUsed;

		mesh.RemoveVolume( crate );
		while ( streamer.Pump() > 0 ) { }   // rebuilt from the field the mesh kept: no job asks for geometry

		Assert.AreEqual( 0, mesh.PendingTiles );
		Assert.AreEqual( triangles, mesh.GetStats().TrianglesUsed, "No job was built from triangles" );
		var after = new NavPath();
		Assert.AreEqual( PathStatus.Complete, FindPath( mesh, after ) );
		Assert.AreEqual( before.Length, after.Length );
		Assert.IsTrue( before.Corners.SequenceEqual( after.Corners ) );
	}

	[TestMethod]
	public void CrowdWithAnExternalAgent()
	{
		var mesh = BuildLevel();

		var crowd = new NavCrowd( mesh, workers: Environment.ProcessorCount );

		// The player: the game moves it, the crowd plans its route and makes others avoid it.
		var player = AgentParams.Create( radius: 0.4f, height: 1.8f, maxSpeed: 5, acceleration: 20 );
		player.External = true;
		var position = new Vector3( 5, 5, 0 );
		int id = crowd.Add( position, player );
		uint generation = crowd.States[id].Generation;
		crowd.MoveTo( id, new Vector3( 45, 5, 0 ) );

		// Four agents the crowd moves, coming the other way.
		var walker = AgentParams.Create( radius: 0.4f, height: 1.8f, maxSpeed: 3, acceleration: 10 );
		for ( int i = 0; i < 4; i++ )
		{
			int other = crowd.Add( new Vector3( 45, 3 + i * 1.5f, 0 ), walker );
			crowd.MoveTo( other, new Vector3( 5, 3 + i * 1.5f, 0 ) );
		}

		// The crowd's parallel stages run through this hook: run body( i ) for every i below count, then return.
		// Parallel.For stands in for the engine's job system.
		Action<int, Action<int>> run = ( count, body ) => Parallel.For( 0, count, body );

		const float dt = 1 / 60f;
		for ( int frame = 0; frame < 60 * 60; frame++ )
		{
			crowd.Step( dt, run );

			// Done when no agent waits for a plan, walks a route or is on a link.
			bool busy = false;
			foreach ( var state in crowd.States ) busy |= state.Status is AgentStatus.Pending or AgentStatus.Moving or AgentStatus.OnLink;
			if ( !busy ) break;

			// The game's character controller goes here. This one follows what the crowd wants exactly.
			position += crowd.States[id].DesiredVelocity * dt;
			crowd.SetPosition( id, position );
		}

		// Every agent arrived, so the loop ended before the frames ran out
		Assert.AreEqual( 5, crowd.States.Length );
		Assert.AreEqual( generation, crowd.States[id].Generation );
		Assert.IsTrue( Vector2.Distance( new( crowd.States[id].Position.X, crowd.States[id].Position.Y ), new( 45, 5 ) ) < player.Radius );
		for ( int i = 0; i < crowd.States.Length; i++ )
		{
			var state = crowd.States[i];
			Assert.AreEqual( AgentStatus.Arrived, state.Status, $"agent {i}" );
			var goal = i == id ? new Vector2( 45, 5 ) : new Vector2( 5, 3 + (i - 1) * 1.5f );
			Assert.IsTrue( Vector2.Distance( new( state.Position.X, state.Position.Y ), goal ) < player.Radius, $"agent {i} at {state.Position}" );
		}
		Assert.AreEqual( position, crowd.States[id].Position, "An External agent stays where the game put it" );
	}
}
