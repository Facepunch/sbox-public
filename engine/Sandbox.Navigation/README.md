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
derives from `NavSettings`, the agent or a time horizon. Agents of very different sizes are agent types of one mesh, each
with settings and tiles of its own (the last example).

How the engine uses it is in `docs/reference/simulation.md`. The examples below are compiled and run as
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

var settings = NavSettings.Create( new NavSettingsDesc { AgentHeight = 1.8f, AgentRadius = 0.4f, AgentClimb = 0.4f, AgentMaxSlope = 45, CellSize = 0.1f, CellHeight = 0.1f, TileSize = 256 } );
var mesh = new NavMesh( settings, NavMeshOptions.Create( settings ) );

var failures = new List<(int X, int Y, string Error)>();
mesh.Build( new NavBuilder(), vertices, indices, failures );
foreach ( var (x, y, error) in failures ) Console.WriteLine( $"tile {x},{y} failed: {error}" );

// One query per thread, kept; one path, reused.
var query = new NavQuery( mesh );
var path = new NavPath();
var halfExtents = new Vector3( 2.1f * settings.AgentRadius, 2.1f * settings.AgentRadius, 1.51f * settings.AgentHeight );
var anyone = NavFilter.Create();   // every area at cost 1, nothing forbidden; change it with its With methods
var status = query.FindPath( new Vector3( 5, 5, 0 ), new Vector3( 45, 5, 0 ), halfExtents, anyone, path );
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
var settings = NavSettings.Create( new NavSettingsDesc { AgentHeight = 1.8f, AgentRadius = 0.4f, AgentClimb = 0.4f, AgentMaxSlope = 45, CellSize = 0.1f, CellHeight = 0.1f, TileSize = 256 } );
var mesh = new NavMesh( settings, NavMeshOptions.Create( settings ) );
var streamer = new TileStreamer( mesh, vertices, indices );   // the level of the first example

mesh.MarkDirty( new Vector3( 0, 0, -10 ), new Vector3( 50, 30, 10 ) );
while ( streamer.Pump() > 0 ) { }

// A crate lands in the way. Area NoArea blocks exactly the box: the walkable edge comes right up to it.
int crate = mesh.AddVolume( new NavVolume
{
	Shape = VolumeShape.Box,
	Area = NavFilter.NoArea,
	LocalToWorld = Matrix4x4.CreateTranslation( 10, 10, 0 ),
	A = new Vector3( -1, -1, -0.5f ),
	B = new Vector3( 1, 1, 2 )
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
var player = AgentParams.Create( new AgentDesc { Radius = 0.4f, Height = 1.8f, MaxSpeed = 5, Acceleration = 20 } );
player.External = true;
var position = new Vector3( 5, 5, 0 );
int id = crowd.Add( position, player );
uint generation = crowd.States[id].Generation;
crowd.MoveTo( id, new Vector3( 45, 5, 0 ) );

// Four agents the crowd moves, coming the other way.
var walker = AgentParams.Create( new AgentDesc { Radius = 0.4f, Height = 1.8f, MaxSpeed = 3, Acceleration = 10 } );
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

## Example: generated links

Drops off ledges and jumps across gaps, found by the tile builds themselves. Off unless the `NavLinkSettings`
of the mesh's options set a distance. With `Insert` the mesh adds them as links of its own, as here; without, it reports them and the
caller adds them (next example).

```csharp
using System.Numerics;
using Nav;

// Metres. A loading dock 2 high along one side of a yard: its top, the yard, the face between them.
Vector3[] dockVertices =
[
	new( 0, 0, 2 ), new( 8, 0, 2 ), new( 8, 20, 2 ), new( 0, 20, 2 ),
	new( 8, 0, 0 ), new( 20, 0, 0 ), new( 20, 20, 0 ), new( 8, 20, 0 ),
	new( 8, 0, 0 ), new( 8, 20, 0 ), new( 8, 20, 2 ), new( 8, 0, 2 ),
];
int[] dockIndices = [0, 1, 2, 0, 2, 3, 4, 5, 6, 4, 6, 7, 8, 9, 10, 8, 10, 11];

// The link types are the caller's numbers: what a filter forbids or prices generated links by.
const byte drop = 1, jump = 2;
var settings = NavSettings.Create( new NavSettingsDesc { AgentHeight = 1.8f, AgentRadius = 0.4f, AgentClimb = 0.4f, AgentMaxSlope = 45, CellSize = 0.1f, CellHeight = 0.1f, TileSize = 256 } );
var links = NavLinkSettings.Create( settings ) with { MaxDrop = 3, MaxJumpDistance = 2, MaxJumpUp = 0.5f, DropType = drop, JumpType = jump, Insert = true };
var mesh = new NavMesh( settings, NavMeshOptions.Create( settings ) with { Links = links } );
mesh.Build( new NavBuilder(), dockVertices, dockIndices );

// For drawing: the generated links, apart from those placed by hand.
var ids = new List<int>();
mesh.GetGeneratedLinks( ids );
foreach ( int id in ids )
{
	mesh.GetLink( id, out var link, out _ );
	Console.WriteLine( $"{(link.Type == drop ? "drop" : "jump")} from {link.Start} to {link.End}" );
}

// Off the dock into the yard. A traveller that may not drop gets as far as the edge.
var query = new NavQuery( mesh );
var path = new NavPath();
var halfExtents = new Vector3( 2.1f * settings.AgentRadius, 2.1f * settings.AgentRadius, 1.51f * settings.AgentHeight );
Vector3 onTheDock = new( 4, 10, 2 ), inTheYard = new( 15, 10, 0 );
var anyone = NavFilter.Create();
var down = query.FindPath( onTheDock, inTheYard, halfExtents, anyone, path );
var careful = query.FindPath( onTheDock, inTheYard, halfExtents, anyone.WithForbiddenLinks( 1ul << drop ), path );
var up = query.FindPath( inTheYard, onTheDock, halfExtents, anyone, path );
```

`down` is `Complete` and crosses one of the drops, spaced two agent diameters apart along the dock; `careful` and
`up` are `Partial`: a drop is one way, and a jump back up comes beside it only where the ledge is no higher than
`MaxJumpUp`, which 2 is not. Generated links are ordinary links to
queries and the crowd, with `NavLink.Type` set to `DropType` or `JumpType`. They follow the mesh: a tile built again
from triangles finds its links again, a blocking volume takes out the links it stands on or in the way of and gives
them back when it leaves, without triangles.

## Example: generated links an engine keeps as its own objects

An engine whose links are objects of its own (a component, an entity) leaves `Insert` off. The mesh then adds
nothing: after each `Commit` it says which tiles' generated links changed, and the engine makes its objects match
and adds them with `AddLink` like any link placed by hand.

```csharp
var reported = new NavMesh( settings, NavMeshOptions.Create( settings ) with { Links = links with { Insert = false } } );
reported.Build( new NavBuilder(), dockVertices, dockIndices );

// Per tile, the link objects the engine made and the id each got from AddLink.
var objects = new Dictionary<(int X, int Y), List<(NavLink Link, int Id)>>();
var changed = new List<(int X, int Y)>();
var now = new List<NavLink>();

// After every Commit of jobs:
reported.TakeLinkChanges( changed );
foreach ( var tile in changed )
{
	now.Clear();
	reported.GetGeneratedLinks( tile.X, tile.Y, now );
	if ( !objects.TryGetValue( tile, out var had ) ) objects[tile] = had = [];

	// Gone from the tile's set: destroy the object. New in it: create one. The rest is left alone.
	for ( int i = had.Count - 1; i >= 0; i-- )
	{
		if ( now.Contains( had[i].Link ) ) continue;
		reported.RemoveLink( had[i].Id );
		had.RemoveAt( i );
	}
	foreach ( var link in now )
	{
		if ( !had.Exists( o => o.Link == link ) ) had.Add( (link, reported.AddLink( link )) );
	}
}
changed.Clear();
reported.Commit();
```

A reported link is plain `NavLink` data with the type set, its ends on the polygons it joins and a radius of a
cell, so `AddLink` connects it to those polygons. A tile's set comes in a fixed order and with the same values for
the same mesh: compare old and new by value, as above. A tile that is built again and finds the same links is not
reported as changed. Links the caller adds are never counted as a way on foot, so adding the reported links back
does not make the generator drop them.

## Example: people and cars in one mesh

Each agent type has settings, tiles, jobs and generated links of its own; the types share the triangles, the volumes,
the links placed by the caller, the commit and the bake.

```csharp
using System.Numerics;
using Nav;

// The level of the first example, for people and for cars.
var people = NavSettings.Create( new NavSettingsDesc { AgentHeight = 1.8f, AgentRadius = 0.4f, AgentClimb = 0.4f, AgentMaxSlope = 45, CellSize = 0.1f, CellHeight = 0.1f, TileSize = 256 } );
var cars = NavSettings.Create( new NavSettingsDesc { AgentHeight = 2, AgentRadius = 1.5f, AgentClimb = 0.2f, AgentMaxSlope = 15, CellSize = 0.25f, CellHeight = 0.1f, TileSize = 128 } );
var mesh = new NavMesh( [
	new NavAgentTypeDesc { Settings = people, Options = NavMeshOptions.Create( people ) },
	new NavAgentTypeDesc { Settings = cars, Options = NavMeshOptions.Create( cars ) },
] );
var failures = new List<(int AgentType, int X, int Y, string Error)>();
mesh.Build( new NavBuilder(), vertices, indices, failures );

// A query, and an agent, name their type: 0 for people, 1 for cars.
var carQuery = new NavQuery( mesh, 1 );
var path = new NavPath();
var carBox = new Vector3( 2.1f * cars.AgentRadius, 2.1f * cars.AgentRadius, 1.51f * cars.AgentHeight );
var status = carQuery.FindPath( new Vector3( 5, 5, 0 ), new Vector3( 45, 5, 0 ), carBox, NavFilter.Create(), path );

var crowd = new NavCrowd( mesh, workers: Environment.ProcessorCount );
var car = AgentParams.Create( new AgentDesc { Radius = 1.5f, Height = 2, MaxSpeed = 8, Acceleration = 6 } ) with { AgentType = 1 };
int id = crowd.Add( new Vector3( 5, 5, 0 ), car );
crowd.MoveTo( id, new Vector3( 45, 5, 0 ) );
```

The car's path keeps its own radius from the wall. People and cars in one crowd avoid each other. A call about one
type's tiles or links (`GetPolygons`, `MarkDirty( x, y )`, `GetLinkEnds`, `new NavQuery( mesh )`, ...) has a form that
names the type; the form without one throws on a mesh of several types, so no type is ever picked for the caller.
`AddLink( link )` adds a link to every type, each resolving it on its own tiles; `AddLink( agentType, link )` to one.
A mesh of several types saves a bake of format version 4, which `Load( data, options )` with one options entry per type
reads; a mesh of one type writes version 3, as before.

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
- `BoundsMin` and `BoundsMax` include a border of the agent radius plus three cells around the tile (more for
  some jobs when links are generated, see below), and are unbounded in Z unless the job's `MinZ` and `MaxZ` are set. Triangle binning is the engine's; the library's own is
  internal to `NavMesh.Build`.
- `GetStats().TrianglesUsed` and `TrianglesRejected` are running totals over every build from triangles, not a
  count of what the mesh holds now.

**Settings and defaults**

- No value stands for another: 0 means 0, and a value is used as set or refused with an `ArgumentException`, never
  raised, lowered or replaced. Defaults come only from the `Create` methods (`NavSettings`, `NavMeshOptions`,
  `NavLinkSettings`, `NavFilter`, `NavLink`, `AgentParams`), which set every field. Start from one and change
  fields with `with`.
- A call that would take several numbers of one kind in a row takes a struct with named fields instead:
  `NavSettingsDesc` for `NavSettings.Create`, `AgentDesc` for `AgentParams.Create`, `WallRequest` for `FindWalls`,
  `RouteDesc` for `NavCrowd.SetPath`. Every field of these is `required`, so the compiler refuses one left out;
  the tile size too, since it sets `MaxTilePolygons` and how many tiles a mesh holds.
- `new NavSettings { ... }` without `Create` leaves `MaxEdgeCells` at 0, which is not valid: it is at least 1, and
  `int.MaxValue` for no limit. `MinRegionCells` and `MaxEdgeError` at 0 are valid and give outlines that are not
  simplified and no removal of small regions. All three are in cells, not world units.
- `default( NavFilter )` is not a filter: every query and the crowd throw for it. `NavFilter.Create()` allows every
  area and link at cost 1; the `With` methods change one part and check it. A filter is a value of about 780 bytes,
  passed with `in`.
- `NavLink.Area` 0 (`NavFilter.NoArea`) is refused by `AddLink` and `SetLink`; `NavLink.Create` gives
  `NavFilter.DefaultArea`, crossed like ground. `NavVolume.Area` 0 blocks. Area ids above 63 throw.
- `new AgentParams { ... }` without `Create` has no filter, and `Add` throws. `Add` and `SetParams` also throw for
  a size of 0, a negative speed or a separation outside 0 to 1.
- `SubstepTime = 0.05` and `AvoidHorizon = 0.75` are seconds. A caller stepping in milliseconds sets 50 and 750.
- Blockers do not re-erode the mesh: the walkable edge comes right up to the volume. A volume that should keep
  agents further away is made that much bigger.
- `NavMeshOptions.KeepFields` (true from `Create`) keeps a tile's eroded field only once a volume overlaps the tile,
  so the first volume over a tile asks for its triangles again. With it false every volume change asks for them.
  `GetStats().CacheBytes` is what is kept.

**Queries**

- `halfExtents` is how far off the mesh a point may be and still snap, Z third. For where an agent stands the crowd
  uses 2.1 radii sideways and 1.51 heights vertically, for targets 16 radii, both of the mesh's agent as the
  crowd is made, and never less than the agent's own 2.1 radii and 1.51 heights; infinite extents search the whole
  mesh. `SetMesh` keeps `PlacementExtents` and `TargetExtents` as they are: set them again for a mesh of another
  agent or unit.
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
- The crowd keeps its own copy of the params, filter included, and compares a new filter by value in `SetParams`.
- `workers` only sizes scratch; threads come from `Step`'s `run` hook, which must call `body( i )` for every `i`
  below `count` and return when all have returned. Small crowds never call it.

**Generated links**

- With `NavLinkSettings.Create` nothing is generated and a build does what it does without. Any of `MaxDrop` and
  `MaxJumpDistance` above 0 turns generation on; `MaxJumpDistance` plus two agent diameters must fit in a tile, and
  `Spacing` (two agent diameters from `Create`) must be at least a cell, or the constructor throws. The spacing is
  used as set.
- The type says what the agent does. A drop is one way. A ledge no higher than `MaxJumpUp` also gets a one-way
  jump from below up onto it, so a filter can allow down and forbid up. A jump is two-way when its ends are within
  `MaxJumpUp` of each other in height, else one way towards the lower end. Links go straight out from
  an edge: a landing that lies to the side of every edge facing it gets none.
- A link is found on the height field of a tile build: the agent's body must fit along the way, over its width less
  a cell on each side, level out and straight down for a drop, along `NavLinkSettings.JumpHeight` for a jump. A way
  that is no more than four times as long on foot gets no link. Hand-placed links do not count as a way on foot and
  hide nothing.
- Links across a tile border need both tiles. While tiles around a built tile are still dirty the mesh holds the
  tile's solid spans (`TakeJobs` hands dirty tiles out row by row for that reason, and beyond 1,024 held fields the
  oldest are let go early), so commit often during a generation: what is held grows with the jobs per commit. A tile built
  beside tiles that are not being rebuilt asks for triangles from further out instead: `BoundsMin` and `BoundsMax`
  of such a job are wider by the reach of a link, and the engine must honour them.
- Judging the links across borders is work for a worker too. It rides on the first job of a `TakeJobs`; when no
  tile is dirty, `TakeJobs` hands out a job that builds no tile and needs no geometry (`Unchanged` once built).
  Build and commit it like any other. Until it is back its tiles count in `PendingTiles` and `IsPending`, so a
  loop that runs until nothing is pending does the right thing, and one that stops when no tile is dirty stops a
  job too early: the last links across borders are then missing and their spans stay held.
- `NavLinkSettings.Insert` decides who owns them. On: the mesh adds them as links of its own;
  `GetGeneratedLinks( ids )` and `GetLink` tell them from the caller's, `SetLink` throws for a generated id and
  `RemoveLink` ignores it, their ids change when a tile's links change and a rebuild that finds the same links keeps
  them. They share the 65,535 link ids with the caller's: one that finds no free id is left out and counted in
  `LinksRefused`, and the next build of its tile tries again. Off: the mesh adds nothing, uses no ids, and
  `LinksRefused` stays 0.
- Either way `GetGeneratedLinks( tileX, tileY, list )` gives a tile's links that are good as of the last `Commit`.
  With `Insert` off, `TakeLinkChanges` gives the tiles whose set changed since it was last called (with it on the
  mesh keeps no such list). Both belong to the owner thread. A
  tile whose links across a border are still to be judged reports them once the tiles around it are in; a tile that
  is being built again keeps reporting what it had until the new links are made.
- A bake does not hold generated links. `Load` takes the link settings in its options for later builds; a loaded tile has no
  generated links, and reports none, until it is built from triangles again. An engine that saves its link objects
  with the scene has them after a load without generating.

**Bakes**

- A bake is for start-up time on large maps: it saves generating the mesh at load. A level that generates in a
  fraction of a second gains little from one; a map of thousands of tiles loads from a bake in milliseconds where
  generation takes seconds even on many threads.
- A bake holds each tile's polygons (a byte each plus their corners) and the cells of its vertices, as small
  varints: about 12 bytes a polygon, about 7 after LZ4. Neighbours, the tree and the portal edges are rebuilt at
  load. Layout: the class comment of `NavMesh.Bake.cs`.
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
| Agent types | 16 per mesh |
| Tile size | 32 to 1,024 cells, a multiple of 16 |
| Agent | radius up to 127 cells; height and climb up to 32,767 cell heights |
| One tile | 16,383 polygons, 65,535 vertices, geometry up to 65,533 cell heights tall |
| Loaded tiles | 262,140 |
| Links | 65,535, generated ones included |
| Area ids | 0 to 63 |
| Bake | 2 GB |

Over a tile limit the job ends `Failed` with the reason in `Error`; `AddLink` returns -1 when no id is free.
