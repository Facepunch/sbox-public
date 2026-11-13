# Nav

A navigation library for games: navmesh generation from triangles, a tiled mesh that can be rebuilt piece by piece
while other threads query it, path queries, blocking and cost volumes, hand-placed links, bakes, and a crowd that
steers agents. Plain C# on .NET 10 with no package or project references. It was written as the core of the s&box
navigation system and knows nothing of s&box: the engine layer lives in
`engine/Sandbox.Engine/Game/Navigation/`.

## What it does and does not do

| It does | It does not |
|---------|-------------|
| Voxelize triangles per tile, erode by the agent radius, build layer regions, contours and convex polygons of up to 6 vertices | Keep a detail mesh: heights are polygon-plane heights |
| Keep tiles immutable behind one swapped state, so queries on any thread run while the mesh changes | Start threads. Builds run where the caller runs them; the crowd runs through a hook the caller supplies |
| Rebuild tiles under volumes from a cached field, without asking for geometry again | Collect geometry. The engine hands triangles to the tile jobs that ask for them |
| Find nearest points, paths, string-pulled corners, surface moves, walls, random points | Search hierarchically, slice a search, or cancel one mid-way |
| Save and load finished tiles in a compact format: polygons and the cells of their vertices, the rest rebuilt at load | Compress. A bake is compact without a codec and the engine may put its own on top; the rebuild cache takes an optional codec |
| Step a crowd: planning, avoidance, wall steering, separation, links, agents moved by the game | Raise events. Callers compare states between steps |

Z is up, always; a Y-up engine swaps at its boundary. Every distance is in the caller's units and every tolerance
derives from `NavSettings`, the agent or a time horizon. One mesh has one agent radius, height and climb; agents of
very different sizes need a mesh each.

How the engine uses it is in `docs/reference/simulation.md`. The three examples below are compiled and run as
tests in `engine/Tests/Sandbox.Test.Navigation/Docs/ReadmeExamples.cs`; change one, change the other.

## Example: a static level and a path

```csharp
using System.Numerics;
using Nav;

// Metres. A 50 x 30 floor and a wall 3 high across part of it.
Vector3[] vertices =
[
	new( 0, 0, 0 ), new( 50, 0, 0 ), new( 50, 30, 0 ), new( 0, 30, 0 ),
	new( 25, 0, 0 ), new( 25, 20, 0 ), new( 25, 20, 3 ), new( 25, 0, 3 ),
];
int[] indices =
[
	0, 1, 2, 0, 2, 3,   // floor: cross( v1 - v0, v2 - v0 ) points up, so it is ground
	4, 5, 6, 4, 6, 7,   // wall: every triangle is an obstacle, whichever way it faces
];

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
```

`NavMesh.Build` sorts the soup into tiles, builds every tile on the calling thread, commits, and returns the number
of tiles that failed. Triangles with an index out of range or a non-finite coordinate are left out and counted in
`GetStats().TrianglesRejected`, as are triangles that are not ground, such as the wall here. The path goes round
the end of the wall; its corners are on the polygon plane, here a cell height above the floor.

## Example: the engine drives the tiles, with a blocker at runtime

The flow every engine that owns its geometry runs, also for every later rebuild: take jobs on the owner thread,
give geometry to the jobs that ask, build on workers, commit on the owner thread. Jobs come only from `TakeJobs`.

```csharp
using System.Numerics;
using Nav;

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
```

```csharp
var settings = NavSettings.Create( agentHeight: 1.8f, agentRadius: 0.4f, agentClimb: 0.4f, agentMaxSlope: 45, cellSize: 0.1f, cellHeight: 0.1f );
var mesh = new NavMesh( settings );
var streamer = new TileStreamer( mesh, vertices, indices );   // the level of the first example

mesh.MarkDirty( new Vector3( 0, 0, -10 ), new Vector3( 50, 30, 10 ) );
while ( streamer.Pump() > 0 ) { }

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

mesh.RemoveVolume( crate );
while ( streamer.Pump() > 0 ) { }   // rebuilt from the field the mesh kept: no job asks for geometry
```

Queries on other threads go on during all of this: each `NavQuery` call works on the state it started with, and
every `Commit` publishes a new one. A `PolyRef` into a rebuilt tile stops resolving.

## Example: a crowd with one agent the game moves

```csharp
using System.Numerics;
using Nav;

// mesh: built as in the first example.
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
```

All five end `Arrived`. The crowd keeps an External agent exactly where `SetPosition` puts it and derives its
velocity from successive positions (call `SetVelocity` to give one instead); the walkers avoid it and it is never
pushed. A stage hands work to `run` only when it can give at least two bodies 64 agents each (128 for planning), so
a crowd this small runs on the calling thread. An id is reused after `Remove`: a holder that kept `id` and
`generation` checks `crowd.States[id].Generation == generation` and that the status is not `Free`. Every `NavCrowd`
call happens on one thread and never during a `Step`; a mesh `Commit` during a step is harmless.

## Things that are easy to miss

**Geometry**

- A triangle is ground when `cross( v1 - v0, v2 - v0 )` points up (within the slope limit). "Counter-clockwise
  seen from above" is only true in a right-handed frame: a left-handed Z-up engine must swap two indices or gets an
  empty mesh. `GetStats().TrianglesRejected` shows triangles that faced down or were too steep. `GetPolygons` writes
  polygons in the same sense, so for consecutive vertices a, b, c the cross product of b - a and c - b points up.
- In the job flow, a job that `NeedsGeometry` must get `SetGeometry`. An empty set means "nothing here" and the
  commit removes the tile. Building without `SetGeometry` ends `Failed` and keeps the old tile, which is the right
  answer for a game shipped without its source geometry. A non-finite vertex or an index out of range fails the
  whole tile; only `NavMesh.Build` filters such triangles out.
- `BoundsMin` and `BoundsMax` include a border of the agent radius plus three cells around the tile, and are
  unbounded in Z unless the job's `MinZ` and `MaxZ` are set. Triangle binning is the engine's; the library's own is
  internal to `NavMesh.Build`.
- `GetStats().TrianglesUsed` and `TrianglesRejected` are running totals over every build from triangles, not a
  count of what the mesh holds now.

**Settings and struct defaults**

- `new NavSettings { ... }` without `Create` leaves `MaxEdgeCells`, `MinRegionCells` and `MaxEdgeError` at 0. That
  is valid and gives outlines that are not simplified, no splitting of long edges and no removal of small regions.
  `TileSize` 0 is not valid and the `NavMesh` constructor throws. Those three are in cells, not world units.
- `new AgentParams { ... }` without `Create` leaves `Separation` at 0 and `AutoLinks` false: such an agent waits on
  every link until `CompleteLink`.
- `default( NavFilter )` allows every area at cost 1. `NavLink.Area` 0 means ground (`NavFilter.DefaultArea`).
  `NavVolume.Area` 0 (`NavFilter.NoArea`) blocks. Area ids above 63 throw.
- `SubstepTime = 0.05` and `AvoidHorizon = 0.75` are seconds. A caller stepping in milliseconds sets 50 and 750.
- Blockers do not re-erode the mesh: without `Margin` the walkable edge comes right up to the volume.
- `keepFields` (on by default) keeps a tile's eroded field only once a volume overlaps the tile, so the first volume
  over a tile asks for its triangles again. With `keepFields` false every volume change asks for them.
  `GetStats().CacheBytes` is what is kept.

**Queries**

- `halfExtents` is how far off the mesh a point may be and still snap, Z third. For where an agent stands the crowd
  uses 2.1 radii sideways and 1.51 heights vertically, for targets 16 radii and never less than that of the agent
  itself; infinite extents search the whole mesh.
- Heights are on the polygon plane, off by up to a cell height plus the simplification error from the real ground,
  more on rough terrain. Use a ground trace for feet.
- `NavPath.Corners` passes through links: a link gives its entry and its exit as two corners in a row, and `Polys`
  has the link between its two polygons (`PolyRef.IsLink`). `FindCorners` stops at the first link entry instead.
  `GetLink` picks the direction of a two-way link by which end is nearer to `from`.
- A fresh `NavPath` reports `StartNotFound`, the enum's zero. `Partial` means the target cannot be reached and `End`
  is the reachable point nearest to it. `FindPath` has no expansion limit and cannot be cancelled; very long paths
  on very large maps are slow.
- A `PolyRef` means something only to the mesh that made it: every mesh numbers its builds from 1, so a loaded mesh
  can resolve an old ref to one of its own polygons. Keep the mesh with any ref or path that can outlive it.

**Crowd**

- `Blocked` means "at the end of a partial route", not blocked by other agents. `Pending` means a plan is due in
  the next step. `Unreachable` means the agent has a target and no way to it (off the mesh, target too far from the
  mesh, or no route); it is planned again on a new target, a changed filter, a new position or a changed mesh.
  `Target` is stale while `Idle` or `Arrived`.
- `SetPosition` means "teleport" for an agent the crowd moves and "this tick's position" for an External one.
- `SetParams` compares the filter's cost array by content, but only when it is handed in: an array edited in place
  counts once `SetParams` is called again.
- `workers` only sizes scratch; threads come from `Step`'s `run` hook, which must call `body( i )` for every `i`
  below `count` and return when all have returned. Small crowds never call it.

**Bakes**

- A bake is for start-up time on large maps: it saves generating the mesh at load. A level that generates in a
  fraction of a second gains little from one; a map of thousands of tiles loads from a bake in milliseconds where
  generation takes seconds even on many threads.
- A bake holds each tile's polygons (a byte each plus their corners) and the cells of its vertices, as small
  varints: about 12 bytes a polygon, about 7 after LZ4. Neighbours, the tree and the portal edges are rebuilt at
  load. Layout: the class comment of `Mesh/NavMesh.Bake.cs`.
- `Save` belongs on the owner thread. Its writer must hand out the header as one span and each tile as one span
  (`ArrayBufferWriter<byte>` does).
- A bake holds tiles only, not volumes, links or the rebuild cache. After `Load`, add the scene's volumes and links
  before the first `TakeJobs`: tiles baked with volumes that are not there by then are rebuilt, asking for geometry.
- `Load` checks every count, index and coordinate and returns null for anything wrong. A loaded tile is the built
  one bit for bit.
- `TryReadSettings` gives the settings a bake was made with, and fails for another format version; compare them
  with yours before `Load`.

**Limits**

| What | Limit |
|------|-------|
| Tile size | 32 to 1,024 cells, a multiple of 16 |
| Agent | radius up to 127 cells; height and climb up to 32,767 cell heights |
| One tile | 16,383 polygons, 65,535 vertices, geometry up to 65,533 cell heights tall |
| Loaded tiles | 262,143 |
| Links | 16,384 |
| Area ids | 0 to 63 |
| Bake | 2 GB |

Over a tile limit the job ends `Failed` with the reason in `Error`; `AddLink` returns -1 when no id is free.
