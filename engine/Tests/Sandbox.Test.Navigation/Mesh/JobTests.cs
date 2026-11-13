using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Nav.Lib.Tests;

/// <summary>
/// The job flow against the tile seam: a test plays the builder by completing jobs by hand.
/// The cases are the ones of review item B2.
/// </summary>
[TestClass]
public class JobTests
{
	static TileJob[] Take( NavMesh mesh, int capacity = 16 )
	{
		var jobs = new TileJob[capacity];
		int count = mesh.TakeJobs( jobs );
		return jobs[..count];
	}

	static void Build( TileJob job ) => job.Complete( TileJobStatus.Built, GridMesh.Quad( job.X, job.Y ) );

	[TestMethod]
	public void BuiltJobsArePublishedInOneSwap()
	{
		var mesh = GridMesh.Create();
		mesh.MarkDirty( 0, 0 );
		mesh.MarkDirty( 1, 0 );
		Assert.AreEqual( 2, mesh.PendingTiles );

		var jobs = Take( mesh );
		Assert.AreEqual( 2, jobs.Length );
		Assert.AreEqual( 2, mesh.PendingTiles, "Tiles in flight still count as pending" );
		foreach ( var job in jobs )
		{
			Assert.AreEqual( TileJobStatus.Pending, job.Status );
			Assert.IsTrue( job.NeedsGeometry );
			Build( job );
		}

		uint version = mesh.Version;
		Assert.AreEqual( 0, mesh.Commit( jobs ) );
		Assert.AreEqual( version + 1, mesh.Version, "One commit is one state swap" );
		Assert.AreEqual( 0, mesh.PendingTiles );
		Assert.AreEqual( 2, mesh.GetStats().Tiles );
		Assert.AreEqual( 1, mesh.State.Find( 0, 0 )!.Ext.Length );
	}

	[TestMethod]
	public void JobBoundsCoverTheTileAndItsBorder()
	{
		var mesh = GridMesh.Create();
		mesh.MarkDirty( -2, 3 );
		var job = Take( mesh )[0];
		Assert.AreEqual( (-2, 3), (job.X, job.Y) );
		// Radius 8 at cell 10 is one cell, plus three.
		float border = 4 * GridMesh.Cell;
		Assert.AreEqual( -2 * GridMesh.TileWorld - border, job.BoundsMin.X );
		Assert.AreEqual( 3 * GridMesh.TileWorld - border, job.BoundsMin.Y );
		Assert.AreEqual( -1 * GridMesh.TileWorld + border, job.BoundsMax.X );
		Assert.AreEqual( 4 * GridMesh.TileWorld + border, job.BoundsMax.Y );
		Assert.IsTrue( float.IsNegativeInfinity( job.BoundsMin.Z ) && float.IsPositiveInfinity( job.BoundsMax.Z ) );
	}

	[TestMethod]
	public void MarkDirtyByBoxReachesTilesWhoseBorderSeesTheBox()
	{
		var mesh = GridMesh.Create();
		mesh.MarkDirty( new Vector3( 10, 300, 0 ), new Vector3( 20, 310, 50 ) );
		var jobs = Take( mesh );
		CollectionAssert.AreEquivalent( new[] { (-1, 0), (0, 0) }, jobs.Select( j => (j.X, j.Y) ).ToArray() );
		Assert.ThrowsException<ArgumentException>( () => mesh.MarkDirty( new Vector3( float.NegativeInfinity ), Vector3.Zero ) );
	}

	[TestMethod]
	public void OldestDirtyTileIsHandedOutFirstAndOnlyOnce()
	{
		var mesh = GridMesh.Create();
		mesh.MarkDirty( 5, 5 );
		mesh.MarkDirty( 1, 1 );
		mesh.MarkDirty( 3, 3 );
		mesh.MarkDirty( 5, 5 );

		var first = Take( mesh, 2 );
		CollectionAssert.AreEqual( new[] { (5, 5), (1, 1) }, first.Select( j => (j.X, j.Y) ).ToArray() );
		var second = Take( mesh );
		CollectionAssert.AreEqual( new[] { (3, 3) }, second.Select( j => (j.X, j.Y) ).ToArray() );
		Assert.AreEqual( 0, Take( mesh ).Length, "At most one job in flight per tile" );
	}

	[TestMethod]
	public void FailedJobClearsTheDirtyMarkSoNoLoopSpins()
	{
		// B2 (1): one bad tile must not keep the take, build, commit loop alive for ever.
		var mesh = GridMesh.Create();
		mesh.MarkDirty( 0, 0 );
		mesh.MarkDirty( 1, 0 );

		int failed = 0, rounds = 0;
		while ( mesh.PendingTiles > 0 )
		{
			Assert.IsTrue( ++rounds < 10, "The loop does not end" );
			var jobs = Take( mesh );
			foreach ( var job in jobs )
			{
				if ( job.X == 0 ) job.Complete( TileJobStatus.Failed, null, "vertex 3 is not finite" );
				else Build( job );
			}
			failed += mesh.Commit( jobs );
		}
		Assert.AreEqual( 1, failed );
		Assert.AreEqual( 1, rounds );
		Assert.AreEqual( 1, mesh.GetStats().Tiles );

		// Until it is marked dirty again.
		mesh.MarkDirty( 0, 0 );
		var retry = Take( mesh );
		Assert.AreEqual( 1, retry.Length );
		Build( retry[0] );
		Assert.AreEqual( 0, mesh.Commit( retry ) );
		Assert.AreEqual( 2, mesh.GetStats().Tiles );
	}

	[TestMethod]
	public void FailedJobKeepsTheOldTile()
	{
		var mesh = GridMesh.Mesh( (0, 0) );
		uint serial = mesh.State.Find( 0, 0 )!.Serial;
		mesh.MarkDirty( 0, 0 );
		var jobs = Take( mesh );
		jobs[0].Complete( TileJobStatus.Failed, null, "geometry needed but not supplied" );
		Assert.AreEqual( 1, mesh.Commit( jobs ) );
		Assert.AreEqual( "geometry needed but not supplied", jobs[0].Error );
		Assert.AreEqual( serial, mesh.State.Find( 0, 0 )!.Serial );
	}

	[TestMethod]
	public void PendingJobIsAbandonedAndItsLateResultIgnored()
	{
		// B2 (3): committing while a worker is still building.
		var mesh = GridMesh.Create();
		mesh.MarkDirty( 0, 0 );
		var early = Take( mesh );
		Assert.AreEqual( 0, mesh.Commit( early ) );
		Assert.AreEqual( 1, mesh.PendingTiles, "The tile stays dirty" );
		Assert.AreEqual( 0, mesh.GetStats().Tiles );

		// The tile is handed out again as a new job. The first job finishing late changes nothing.
		var again = Take( mesh );
		Assert.AreEqual( 1, again.Length );
		Assert.AreNotSame( early[0], again[0] );
		Build( early[0] );
		Assert.AreEqual( 0, mesh.Commit( early ) );
		Assert.AreEqual( 0, mesh.GetStats().Tiles, "A late result was published" );
		Assert.AreEqual( 1, mesh.PendingTiles );

		Build( again[0] );
		Assert.AreEqual( 0, mesh.Commit( again ) );
		Assert.AreEqual( 1, mesh.GetStats().Tiles );
		Assert.AreEqual( 0, mesh.PendingTiles );

		// Committing a job twice is harmless too.
		uint version = mesh.Version;
		Assert.AreEqual( 0, mesh.Commit( again ) );
		Assert.AreEqual( version, mesh.Version );
	}

	[TestMethod]
	public void RemoveTileDiscardsTheJobInFlight()
	{
		// B2 (4): TakeJobs T, RemoveTile( T ), Commit: T must not reappear.
		var mesh = GridMesh.Mesh( (0, 0) );
		mesh.MarkDirty( 0, 0 );
		var jobs = Take( mesh );
		mesh.RemoveTile( 0, 0 );
		Build( jobs[0] );
		Assert.AreEqual( 0, mesh.Commit( jobs ) );
		Assert.AreEqual( 0, mesh.GetStats().Tiles );
		Assert.AreEqual( 0, mesh.PendingTiles );
		Assert.AreEqual( 0, Take( mesh ).Length );
	}

	[TestMethod]
	public void RemoveTileThenMarkDirtyBuildsTheTileAgain()
	{
		var mesh = GridMesh.Mesh( (0, 0) );
		uint serial = mesh.State.Find( 0, 0 )!.Serial;
		mesh.MarkDirty( 0, 0 );
		var stale = Take( mesh );
		mesh.RemoveTile( 0, 0 );
		mesh.MarkDirty( 0, 0 );
		Build( stale[0] );
		Assert.AreEqual( 0, mesh.Commit( stale ) );
		Assert.AreEqual( 0, mesh.GetStats().Tiles, "The removal is published, the stale build is not" );
		Assert.AreEqual( 1, mesh.PendingTiles );

		var fresh = Take( mesh );
		Build( fresh[0] );
		mesh.Commit( fresh );
		Assert.AreEqual( 1, mesh.GetStats().Tiles );
		Assert.AreNotEqual( serial, mesh.State.Find( 0, 0 )!.Serial );
	}

	[TestMethod]
	public void NewerDirtyMarkPublishesTheResultAndKeepsTheTileDirty()
	{
		var mesh = GridMesh.Create();
		mesh.MarkDirty( 0, 0 );
		var jobs = Take( mesh );
		mesh.MarkDirty( 0, 0 );
		Assert.AreEqual( 0, Take( mesh ).Length, "No second job while the first is in flight" );
		Build( jobs[0] );
		Assert.AreEqual( 0, mesh.Commit( jobs ) );
		Assert.AreEqual( 1, mesh.GetStats().Tiles, "A build that a dirty mark overtook is published all the same" );
		Assert.IsTrue( mesh.IsPending( 0, 0 ), "And the tile is still dirty" );
		uint serial = mesh.State.Find( 0, 0 )!.Serial;

		var next = Take( mesh );
		Assert.AreEqual( 1, next.Length );
		Assert.IsTrue( next[0].NeedsGeometry );
		Build( next[0] );
		mesh.Commit( next );
		Assert.AreNotEqual( serial, mesh.State.Find( 0, 0 )!.Serial );
		Assert.AreEqual( 0, mesh.PendingTiles );
		Assert.AreEqual( 0, Take( mesh ).Length );
	}

	[TestMethod]
	public void TileMarkedBeforeEveryCommitGetsEachFinishedBuild()
	{
		// What starved before: the tile is asked for in every update, and a build takes three of them.
		var mesh = GridMesh.Create();
		var others = new[] { (4, 4), (5, 4) };
		TileJob? flying = null;
		int taken = 0, published = 0, started = 0;
		uint serial = 0;
		for ( int update = 0; update < 300; update++ )
		{
			mesh.MarkDirty( 0, 0 );
			if ( update == 100 ) foreach ( var (x, y) in others ) mesh.MarkDirty( x, y );
			Assert.IsTrue( mesh.IsPending( 0, 0 ) );

			if ( flying != null && update - started == 3 )
			{
				Build( flying );
				Assert.AreEqual( 0, mesh.Commit( [flying] ) );
				var tile = mesh.State.Find( 0, 0 );
				Assert.IsNotNull( tile, $"The build that finished in update {update} was not published" );
				Assert.AreNotEqual( serial, tile.Serial );
				serial = tile.Serial;
				published++;
				flying = null;
			}
			else Assert.AreEqual( 0, mesh.Commit() );

			foreach ( var job in Take( mesh ) )
			{
				if ( (job.X, job.Y) != (0, 0) )
				{
					// The tile that is always dirty does not keep others from their turn
					Build( job );
					mesh.Commit( [job] );
					continue;
				}
				Assert.IsNull( flying, "A second job for a tile in flight" );
				flying = job;
				started = update;
				taken++;
			}
		}
		Assert.AreEqual( 100, taken );
		Assert.AreEqual( 99, published );
		foreach ( var (x, y) in others )
		{
			Assert.IsNotNull( mesh.State.Find( x, y ) );
			Assert.IsFalse( mesh.IsPending( x, y ) );
		}

		// The marks stop: one more build and the tile is clean
		Build( flying! );
		mesh.Commit( [flying!] );
		Assert.IsTrue( mesh.IsPending( 0, 0 ), "The last mark came after the job was taken" );
		var last = Take( mesh );
		Assert.AreEqual( 1, last.Length );
		Build( last[0] );
		mesh.Commit( last );
		Assert.IsFalse( mesh.IsPending( 0, 0 ) );
		Assert.AreEqual( 0, mesh.PendingTiles );
		Assert.AreEqual( 0, Take( mesh ).Length );
	}

	[TestMethod]
	public void IsPendingFollowsATileFromItsMarkToItsCommit()
	{
		var mesh = GridMesh.Create();
		Assert.IsFalse( mesh.IsPending( 0, 0 ) );
		mesh.MarkDirty( 0, 0 );
		mesh.MarkDirty( 3, -7 );
		Assert.IsTrue( mesh.IsPending( 0, 0 ) && mesh.IsPending( 3, -7 ) );
		Assert.IsFalse( mesh.IsPending( 1, 0 ) );

		var jobs = Take( mesh, 1 );
		Assert.IsTrue( mesh.IsPending( 0, 0 ), "In flight" );
		Assert.AreEqual( 0, mesh.Commit( jobs ) );
		Assert.IsTrue( mesh.IsPending( 0, 0 ), "Abandoned, so dirty again" );

		jobs = Take( mesh, 1 );
		Assert.AreEqual( (3, -7), (jobs[0].X, jobs[0].Y) );
		jobs[0].Complete( TileJobStatus.Failed, null, "no" );
		Assert.AreEqual( 1, mesh.Commit( jobs ) );
		Assert.IsFalse( mesh.IsPending( 3, -7 ), "A failed tile is not pending" );

		jobs = Take( mesh );
		Build( jobs[0] );
		mesh.Commit( jobs );
		Assert.IsFalse( mesh.IsPending( 0, 0 ) );

		mesh.MarkDirty( 0, 0 );
		jobs = Take( mesh );
		mesh.RemoveTile( 0, 0 );
		Assert.IsTrue( mesh.IsPending( 0, 0 ), "Its job is still in flight" );
		mesh.Commit( jobs );
		Assert.IsFalse( mesh.IsPending( 0, 0 ) );
	}

	[TestMethod]
	public void FailedResultOlderThanADirtyMarkIsCountedAndTheTileTriedAgain()
	{
		var mesh = GridMesh.Create();
		mesh.MarkDirty( 0, 0 );
		var jobs = Take( mesh );
		mesh.MarkDirty( 0, 0 );
		jobs[0].Complete( TileJobStatus.Failed, null, "stale" );
		Assert.AreEqual( 1, mesh.Commit( jobs ), "A tile that is marked all the time must not fail in silence" );
		Assert.AreEqual( 1, Take( mesh ).Length );
	}

	[TestMethod]
	public void NeedsGeometryIssuesTheTileAgain()
	{
		// B2 (2) and (5): a cached field that cannot be used is not a failure and not an empty tile.
		var mesh = GridMesh.Mesh( (0, 0) );
		mesh.MarkDirty( 0, 0 );
		var jobs = Take( mesh );
		jobs[0].Complete( TileJobStatus.NeedsGeometry );
		Assert.AreEqual( 0, mesh.Commit( jobs ) );
		Assert.AreEqual( 1, mesh.GetStats().Tiles, "The old tile stays" );
		Assert.AreEqual( 1, mesh.PendingTiles );

		var again = Take( mesh );
		Assert.AreEqual( 1, again.Length );
		Assert.IsTrue( again[0].NeedsGeometry );
	}

	[TestMethod]
	public void EmptyBuildRemovesTheTileAndUnchangedKeepsIt()
	{
		var mesh = GridMesh.Mesh( (0, 0), (1, 0) );
		var kept = mesh.State.Find( 1, 0 )!;
		mesh.MarkDirty( 0, 0 );
		mesh.MarkDirty( 1, 0 );
		var jobs = Take( mesh );
		foreach ( var job in jobs ) job.Complete( job.X == 0 ? TileJobStatus.Built : TileJobStatus.Unchanged );
		Assert.AreEqual( 0, mesh.Commit( jobs ) );
		Assert.IsNull( mesh.State.Find( 0, 0 ) );
		Assert.AreEqual( kept.Serial, mesh.State.Find( 1, 0 )!.Serial );
		Assert.AreEqual( 0, mesh.PendingTiles );
	}

	[TestMethod]
	public void JobsOfAnotherMeshAreIgnored()
	{
		var a = GridMesh.Create();
		var b = GridMesh.Create();
		a.MarkDirty( 0, 0 );
		b.MarkDirty( 0, 0 );
		var jobs = Take( a );
		Build( jobs[0] );
		Assert.AreEqual( 0, b.Commit( jobs ) );
		Assert.AreEqual( 0, b.GetStats().Tiles );
		Assert.AreEqual( 1, b.PendingTiles );
		Assert.AreEqual( 0, a.Commit( jobs ) );
		Assert.AreEqual( 1, a.GetStats().Tiles );
	}
}
