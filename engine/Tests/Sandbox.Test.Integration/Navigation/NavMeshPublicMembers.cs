using Sandbox.Navigation;
using Sandbox.Volumes;
using System;
using System.Threading;

namespace NavigationTests;

/// <summary>
/// Public navigation members the other tests do not reach: random points, the simple path, tile
/// requests, dirty rebuilds, changed agent settings, look ahead, agents moved by their owner, disposal,
/// custom bounds, body filters and the shapes of areas.
/// </summary>
[TestClass]
public class NavMeshPublicMembers
{
	[TestMethod]
	public async Task RandomPointsStayInsideTheirBoxOrSphere()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		scene.Floor( Vector3.Zero, 700 );
		await scene.NavMesh.Generate( scene.PhysicsWorld );

		var box = BBox.FromPositionAndSize( new Vector3( 200, 200, 0 ), 100 );
		var sphere = new Sphere( new Vector3( -200, -200, 0 ), 80 );
		for ( int i = 0; i < 50; i++ )
		{
			var inBox = scene.NavMesh.GetRandomPoint( box );
			Assert.IsTrue( inBox.HasValue && box.Contains( inBox.Value ), $"{inBox} is not in the box" );
			var inSphere = scene.NavMesh.GetRandomPoint( sphere.Center, sphere.Radius );
			Assert.IsTrue( inSphere.HasValue && sphere.Contains( inSphere.Value ), $"{inSphere} is not in the sphere" );
			Assert.IsTrue( scene.NavMesh.OnMesh( scene.NavMesh.GetRandomPoint().Value, 1 ) );
		}

		Assert.IsNull( scene.NavMesh.GetRandomPoint( BBox.FromPositionAndSize( new Vector3( 2000, 0, 0 ), 100 ) ) );
		Assert.IsNull( scene.NavMesh.GetRandomPoint( new Vector3( 2000, 0, 0 ), 80 ) );
	}

	[TestMethod]
	public async Task SimplePathReturnsThePointsOfThePath()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		scene.Floor( Vector3.Zero );
		await scene.NavMesh.Generate( scene.PhysicsWorld );

#pragma warning disable CS0618
		var points = scene.NavMesh.GetSimplePath( new Vector3( -100, 0, 0 ), new Vector3( 100, 0, 0 ) );
		Assert.IsTrue( points.Count >= 2 );
		Assert.IsTrue( points[0].Distance( new Vector3( -100, 0, 4 ) ) < 1 );
		Assert.IsTrue( points[^1].Distance( new Vector3( 100, 0, 4 ) ) < 1 );
		Assert.AreEqual( 0, scene.NavMesh.GetSimplePath( new Vector3( 5000, 0, 0 ), Vector3.Zero ).Count );
#pragma warning restore CS0618
	}

	[TestMethod]
	public async Task TilesAreRequestedUnloadedAndDeferred()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		scene.Floor( Vector3.Zero );
		var upperRight = new Vector3( 100, 100, 0 );
		var lowerLeft = new Vector3( -100, -100, 0 );

		// A deferred load computes the bounds and builds nothing
		scene.NavMesh.DeferGeneration = true;
		Assert.IsTrue( await scene.NavMesh.Load( scene.PhysicsWorld ) );
		Assert.IsTrue( scene.NavMesh.IsEnabled );
		Assert.IsTrue( scene.NavMesh.IsLoaded );
		Assert.IsNull( scene.NavMesh.GetRandomPoint() );
		Assert.IsTrue( scene.NavMesh.Bounds.Size.x > 400 );

		await scene.NavMesh.GenerateTile( scene.PhysicsWorld, upperRight );
		Assert.IsTrue( scene.NavMesh.OnMesh( upperRight ) );
		Assert.IsFalse( scene.NavMesh.OnMesh( lowerLeft ) );

		scene.NavMesh.RequestTileGeneration( lowerLeft );
		Assert.IsFalse( scene.NavMesh.OnMesh( lowerLeft ), "A request is carried out by the following updates" );
		await scene.Settle();
		Assert.IsTrue( scene.NavMesh.OnMesh( lowerLeft ) );

		scene.NavMesh.UnloadTile( upperRight );
		Assert.IsFalse( scene.NavMesh.OnMesh( upperRight ) );
		Assert.IsTrue( scene.NavMesh.OnMesh( lowerLeft ) );

		// Disabled: tile calls do nothing, queries keep answering
		scene.NavMesh.IsEnabled = false;
		scene.NavMesh.UnloadTile( lowerLeft );
		await scene.NavMesh.GenerateTile( scene.PhysicsWorld, upperRight );
		scene.NavMesh.RequestTilesGeneration( scene.NavMesh.Bounds );
		Assert.IsTrue( scene.NavMesh.OnMesh( lowerLeft ) );
		Assert.IsFalse( scene.NavMesh.OnMesh( upperRight ) );
		Assert.AreEqual( 0, scene.NavMesh.mesh.PendingTiles );
	}

	[TestMethod]
	public async Task ADirtyNavmeshRebuildsOverTheFollowingUpdates()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		scene.Floor( Vector3.Zero );
		Assert.IsFalse( scene.NavMesh.IsGenerating );
		Assert.IsTrue( await scene.NavMesh.Generate( scene.PhysicsWorld ) );
		Assert.IsFalse( scene.NavMesh.IsGenerating );
		Assert.IsFalse( scene.NavMesh.IsDirty );

		// New geometry beside the old, and a crate on the old floor, too low to stand in
		var added = new Vector3( 500, 0, 0 );
		scene.Floor( added );
		scene.Box( new Vector3( 0, 0, 20 ), new Vector3( 60, 60, 40 ) );
		Assert.IsFalse( scene.NavMesh.OnMesh( added ) );

		scene.NavMesh.SetDirty();
		Assert.IsTrue( scene.NavMesh.IsDirty );
		Assert.IsFalse( scene.NavMesh.OnMesh( added ), "Nothing happens before the scene updates" );

		scene.NavUpdate();
		Assert.IsFalse( scene.NavMesh.IsDirty );
		await scene.Settle();
		Assert.IsTrue( scene.NavMesh.OnMesh( added ) );
		Assert.IsFalse( scene.NavMesh.OnMesh( Vector3.Zero ), "The crate should have cut the old floor" );
		Assert.IsTrue( scene.NavMesh.Bounds.Maxs.x > 700, "The bounds should have grown to the new geometry" );
	}

	// An editor scene that auto updates invalidates every frame, and game code may request the same
	// tiles every frame. A build under way must still be published.
	[TestMethod]
	public async Task TilesAskedForEveryUpdateStillArrive()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		scene.Floor( Vector3.Zero );
		scene.NavMesh.IsEnabled = true;

		bool arrived = false;
		for ( int i = 0; i < 2000 && !arrived; i++ )
		{
			scene.NavMesh.InvalidateAllTiles( scene.PhysicsWorld );
			scene.NavMesh.RequestTilesGeneration( scene.NavMesh.Bounds );
			scene.NavUpdate();
			arrived = scene.NavMesh.OnMesh( new Vector3( 100, 100, 0 ) ) && scene.NavMesh.OnMesh( new Vector3( -100, -100, 0 ) );
			await Task.Delay( 1 );
		}
		Assert.IsTrue( arrived, "Asking again while a tile is being built must not throw its build away for good" );

		await scene.Settle();
		Assert.IsTrue( scene.NavMesh.OnMesh( new Vector3( 100, 100, 0 ) ) );
	}

	[TestMethod]
	public async Task AnAwaitedTileDoesNotWaitForTheRest()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		scene.Floor( Vector3.Zero, 20000 );
		scene.NavMesh.IsEnabled = true;

		// One tile is awaited, a few hundred others are only asked for
		var tile = scene.NavMesh.GenerateTile( scene.PhysicsWorld, new Vector3( 100, 100, 0 ) );
		scene.NavMesh.RequestTilesGeneration( new BBox( new Vector3( -9900, -9900, -100 ), new Vector3( 9900, -100, 100 ) ) );
		await tile;

		Assert.IsTrue( scene.NavMesh.OnMesh( new Vector3( 100, 100, 0 ) ) );
		Assert.IsTrue( scene.NavMesh.mesh.PendingTiles > 0, "The awaited tile should be back before the others are built" );

		await scene.Settle();
		Assert.IsTrue( scene.NavMesh.OnMesh( new Vector3( 9000, -9000, 0 ) ) );
	}

	[TestMethod]
	public async Task AnAwaitedCallBuildsEveryTileFromTheWorldItWasGiven()
	{
		// The scene has no geometry. The floor is in another world, across more tiles than are built at once
		var scene = new Scene();
		using var scope = scene.Push();
		var world = new PhysicsWorld();
		new PhysicsBody( world ).AddBoxShape( BBox.FromPositionAndSize( new Vector3( 0, 0, -16 ), new Vector3( 8000, 8000, 32 ) ), Rotation.Identity );
		scene.NavMesh.IsEnabled = true;

		// The scene goes on updating its navmesh with its own world while the call is awaited
		var tiles = scene.NavMesh.GenerateTiles( world, BBox.FromPositionAndSize( 0, 7900 ) );
		for ( int i = 0; i < 5000 && !tiles.IsCompleted; i++ )
		{
			scene.NavUpdate();
			await Task.Delay( 1 );
		}
		await tiles;

		for ( int x = -3500; x <= 3500; x += 1000 )
		{
			for ( int y = -3500; y <= 3500; y += 1000 )
			{
				Assert.IsTrue( scene.NavMesh.OnMesh( new Vector3( x, y, 0 ) ), $"The tile at {x},{y} was not built from the world of the call" );
			}
		}

		world.Delete();
	}

	[TestMethod]
	public async Task GenerateSaysWhenATileFailed()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		scene.Floor( Vector3.Zero );
		scene.Floor( new Vector3( 3000, 0, 0 ) );
		Assert.IsTrue( await scene.NavMesh.Generate( scene.PhysicsWorld ) );

		// Geometry taller than a tile can hold, above the first floor
		var far = scene.Floor( new Vector3( 0, 0, 300000 ) );
		Assert.IsFalse( await scene.NavMesh.Generate( scene.PhysicsWorld ) );
		Assert.IsFalse( scene.NavMesh.IsGenerating );
		Assert.IsTrue( scene.NavMesh.OnMesh( new Vector3( 3000, 0, 0 ) ), "The tiles that can be built are" );
		Assert.IsTrue( scene.NavMesh.OnMesh( Vector3.Zero ), "A tile that failed keeps what it had" );

		scene.Remove( far );
		Assert.IsTrue( await scene.NavMesh.Generate( scene.PhysicsWorld ) );
	}

	[TestMethod]
	public async Task ChangedAgentSettingsApplyAtTheNextFullGeneration()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		scene.Floor( Vector3.Zero );
		await scene.NavMesh.Generate( scene.PhysicsWorld );
		var agent = scene.Agent();
		agent.MoveTo( new Vector3( 150, 0, 0 ) );
		scene.GameTick();
		Assert.IsTrue( scene.NavMesh.OnMesh( new Vector3( 180, 0, 0 ), 4 ) );

		scene.NavMesh.AgentRadius = 48;
		await scene.NavMesh.GenerateTile( scene.PhysicsWorld, new Vector3( 100, 100, 0 ) );
		Assert.IsTrue( scene.NavMesh.OnMesh( new Vector3( 180, 0, 0 ), 4 ), "A single tile keeps the settings the navmesh was made with" );

		await scene.NavMesh.Generate( scene.PhysicsWorld );
		Assert.IsFalse( scene.NavMesh.OnMesh( new Vector3( 180, 0, 0 ), 4 ) );
		Assert.IsTrue( scene.NavMesh.OnMesh( new Vector3( 148, 0, 0 ), 6 ) );

		// The agent came along to the new mesh with its target
		for ( int i = 0; i < 100; i++ ) scene.GameTick();
		Assert.IsTrue( agent.AgentPosition.Distance( new Vector3( 150, 0, 4 ) ) < 6, $"The agent was at {agent.AgentPosition}" );
	}

	[TestMethod]
	public async Task LookAheadWalksAlongTheRoute()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		scene.Floor( Vector3.Zero );
		await scene.NavMesh.Generate( scene.PhysicsWorld );
		var agent = scene.Agent( new Vector3( -150, 0, 0 ) );
		agent.MoveTo( new Vector3( 150, 0, 0 ) );
		scene.GameTick();

		var position = agent.AgentPosition;
		Assert.IsTrue( agent.GetLookAhead( 50 ).Distance( position + new Vector3( 50, 0, 0 ) ) < 1 );
		Assert.IsTrue( agent.GetLookAhead( 0 ).Distance( position ) < 1 );
		Assert.IsTrue( agent.GetLookAhead( 10000 ).Distance( new Vector3( 150, 0, 4 ) ) < 1 );
	}

	[TestMethod]
	public async Task AnAgentWithoutSpeedIsMovedByItsOwnerAndKeepsTheVelocityItIsGiven()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		scene.Floor( Vector3.Zero );
		await scene.NavMesh.Generate( scene.PhysicsWorld );
		var driven = scene.Agent( new Vector3( -100, 0, 0 ) );
		driven.MaxSpeed = 0;
		driven.MoveTo( new Vector3( 100, 0, 0 ) );
		driven.Velocity = new Vector3( 30, 0, 0 );
		for ( int i = 0; i < 10; i++ ) scene.GameTick();

		Assert.AreEqual( new Vector3( 30, 0, 0 ), driven.Velocity );
		Assert.IsTrue( driven.AgentPosition.Distance( new Vector3( -100, 0, 4 ) ) < 1, "The crowd must not move it" );

		driven.SetAgentPosition( new Vector3( -50, 0, 0 ) );
		scene.GameTick();
		Assert.IsTrue( driven.AgentPosition.Distance( new Vector3( -50, 0, 4 ) ) < 1 );
		Assert.IsTrue( driven.IsNavigating, "It still has its route" );
	}

	[TestMethod]
	public async Task ADisposedNavmeshAnswersNothingAndCannotBeEnabled()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		scene.Floor( Vector3.Zero );
		await scene.NavMesh.Generate( scene.PhysicsWorld );
		var agent = scene.Agent();
		agent.MoveTo( new Vector3( 100, 0, 0 ) );
		scene.GameTick();
		var path = scene.NavMesh.CalculatePath( new() { Start = Vector3.Zero, Target = new Vector3( 100, 0, 0 ) } );
		long generation = scene.NavMesh.Generation;

		scene.NavMesh.Dispose();
		scene.NavMesh.Dispose();

		Assert.AreEqual( generation + 1, scene.NavMesh.Generation );
		Assert.IsFalse( scene.NavMesh.IsEnabled );
		Assert.IsNull( scene.NavMesh.Simulation );
		Assert.IsNull( scene.NavMesh.GetClosestPoint( Vector3.Zero ) );
		Assert.IsNull( scene.NavMesh.GetRandomPoint() );
		Assert.AreEqual( NavMeshPathStatus.StartNotFound, scene.NavMesh.CalculatePath( new() { Start = Vector3.Zero, Target = Vector3.One } ).Status );
		Assert.IsFalse( agent.IsNavigating );
		agent.MoveTo( new Vector3( -100, 0, 0 ) );
		agent.SetPath( path );
		Assert.IsFalse( agent.IsNavigating );
		Assert.ThrowsException<ObjectDisposedException>( () => scene.NavMesh.IsEnabled = true );
	}

	[TestMethod]
	public async Task APathOfAnotherMeshIsNotTaken()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		scene.Floor( Vector3.Zero );
		await scene.NavMesh.Generate( scene.PhysicsWorld );
		var path = scene.NavMesh.CalculatePath( new() { Start = Vector3.Zero, Target = new Vector3( 100, 0, 0 ) } );

		// The same floor again after a reset: equal polygons, another mesh
		scene.NavMesh.Reset();
		await scene.NavMesh.Generate( scene.PhysicsWorld );
		var agent = scene.Agent();
		agent.SetPath( path );
		Assert.IsFalse( agent.IsNavigating );

		agent.SetPath( scene.NavMesh.CalculatePath( new() { Start = Vector3.Zero, Target = new Vector3( 100, 0, 0 ) } ) );
		Assert.IsTrue( agent.IsNavigating );
	}

	[TestMethod]
	public async Task AgentsSurviveAResetAndTakeTheirTargetAlong()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		scene.Floor( Vector3.Zero );
		await scene.NavMesh.Generate( scene.PhysicsWorld );
		var agent = scene.Agent();
		var target = new Vector3( 150, 0, 0 );
		agent.MoveTo( target );
		scene.GameTick();

		scene.NavMesh.Reset();
		Assert.AreEqual( (Vector3?)target, agent.TargetPosition, "The target stays readable for when a navmesh is back" );

		await scene.NavMesh.Generate( scene.PhysicsWorld );
		Assert.AreEqual( 1, scene.NavMesh.Simulation.Count );
		for ( int i = 0; i < 100; i++ ) scene.GameTick();
		Assert.IsTrue( agent.AgentPosition.Distance( new Vector3( 150, 0, 4 ) ) < 6 );
	}

	[TestMethod]
	public async Task CustomBoundsCutTheWorldAlsoInHeight()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		scene.Floor( Vector3.Zero );
		scene.Floor( new Vector3( 0, 0, 200 ) );
		scene.Floor( new Vector3( 3000, 0, 0 ) );
		scene.NavMesh.CustomBounds = true;
		scene.NavMesh.Bounds = new BBox( new Vector3( -500, -500, -100 ), new Vector3( 500, 500, 100 ) );
		await scene.NavMesh.Generate( scene.PhysicsWorld );

		Assert.IsNotNull( scene.NavMesh.GetClosestPoint( Vector3.Zero, 40 ) );
		Assert.IsNull( scene.NavMesh.GetClosestPoint( new Vector3( 0, 0, 200 ), 40 ), "The upper floor is above the bounds" );
		Assert.IsNull( scene.NavMesh.GetClosestPoint( new Vector3( 3000, 0, 0 ), 40 ), "The far floor is outside the bounds" );
		Assert.AreEqual( new Vector3( 500, 500, 100 ), scene.NavMesh.Bounds.Maxs );
	}

	[TestMethod]
	public async Task BodiesAreChosenByTypeAndTag()
	{
		// Fixed bounds: a generation leaves tiles outside its bounds alone, as it always has
		var scene = new Scene();
		using var scope = scene.Push();
		scene.NavMesh.CustomBounds = true;
		scene.NavMesh.Bounds = new BBox( new Vector3( -500, -500, -100 ), new Vector3( 3500, 3500, 100 ) );
		var plain = new Vector3( 0, 0, 0 );
		var tagged = new Vector3( 3000, 0, 0 );
		var keyframed = new Vector3( 0, 3000, 0 );
		var dynamic = new Vector3( 3000, 3000, 0 );

		// A collider is a keyframed body unless it is static, and a dynamic one with a rigidbody that moves
		scene.Floor( plain ).GetComponent<BoxCollider>().Static = true;
		var water = scene.Floor( tagged );
		water.GetComponent<BoxCollider>().Static = true;
		water.Tags.Add( "water" );
		scene.Floor( keyframed );
		scene.Floor( dynamic ).Components.Create<Rigidbody>().Gravity = false;

		async Task<string> Generate()
		{
			await scene.NavMesh.Generate( scene.PhysicsWorld );
			return string.Concat( new[] { plain, tagged, keyframed, dynamic }.Select( at => scene.NavMesh.OnMesh( at ) ? "x" : "-" ) );
		}

		Assert.AreEqual( "xxx-", await Generate(), "Dynamic bodies never count" );

		scene.NavMesh.IncludeKeyframedBodies = false;
		Assert.AreEqual( "xx--", await Generate() );

		scene.NavMesh.IncludeKeyframedBodies = true;
		scene.NavMesh.IncludeStaticBodies = false;
		Assert.AreEqual( "--x-", await Generate() );

		scene.NavMesh.IncludeStaticBodies = true;
		scene.NavMesh.ExcludedBodies.Add( "water" );
		Assert.AreEqual( "x-x-", await Generate() );

		scene.NavMesh.ExcludedBodies = new TagSet();
		scene.NavMesh.IncludedBodies.Add( "water" );
		Assert.AreEqual( "-x--", await Generate() );
	}

	[TestMethod]
	public async Task AreasBlockInTheShapeOfTheirVolume()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		scene.Floor( Vector3.Zero, 700 );
		var area = scene.Area( Vector3.Zero, new SceneVolume { Type = SceneVolume.VolumeTypes.Sphere, Sphere = new Sphere( 0, 100 ) } );
		await scene.NavMesh.Generate( scene.PhysicsWorld );

		// A corner of the box around the sphere is outside the sphere, also with the agent radius added
		Assert.IsFalse( scene.NavMesh.OnMesh( Vector3.Zero ) );
		Assert.IsFalse( scene.NavMesh.OnMesh( new Vector3( 100, 0, 0 ) ) );
		Assert.IsTrue( scene.NavMesh.OnMesh( new Vector3( 95, 95, 0 ) ) );

		area.SceneVolume = new SceneVolume { Type = SceneVolume.VolumeTypes.Capsule, Capsule = new Capsule( new Vector3( -150, 0, 0 ), new Vector3( 150, 0, 0 ), 30 ) };
		await scene.Settle();
		Assert.IsFalse( scene.NavMesh.OnMesh( new Vector3( 150, 0, 0 ) ) );
		Assert.IsFalse( scene.NavMesh.OnMesh( new Vector3( -150, 30, 0 ) ) );
		Assert.IsTrue( scene.NavMesh.OnMesh( new Vector3( 0, 70, 0 ) ) );

		// The transform of the game object moves, turns and scales the shape
		area.WorldTransform = new Transform( new Vector3( 0, 150, 0 ), Rotation.FromYaw( 90 ), 0.5f );
		await scene.Settle();
		Assert.IsTrue( scene.NavMesh.OnMesh( new Vector3( 130, 0, 0 ) ) );
		Assert.IsFalse( scene.NavMesh.OnMesh( new Vector3( 0, 210, 0 ) ) );
		Assert.IsTrue( scene.NavMesh.OnMesh( new Vector3( 50, 150, 0 ) ) );

		area.SceneVolume = new SceneVolume { Type = SceneVolume.VolumeTypes.Infinite };
		await scene.Settle();
		Assert.IsNull( scene.NavMesh.GetRandomPoint() );

		area.Enabled = false;
		await scene.Settle();
		Assert.IsTrue( scene.NavMesh.OnMesh( Vector3.Zero ) );
	}

	// Every member of an agent takes the gate of the simulation, and so does its update.
	[TestMethod]
	public async Task CommandsAndReadsFromOtherThreadsAreSafeDuringUpdates()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		scene.Floor( Vector3.Zero, 700 );
		await scene.NavMesh.Generate( scene.PhysicsWorld );

		var agents = Enumerable.Range( 0, 96 ).Select( i => scene.Agent( new Vector3( -300 + i % 12 * 50, -200 + i / 12 * 50, 0 ) ) ).ToArray();
		foreach ( var agent in agents ) agent.MoveTo( new Vector3( 250, 250, 0 ) );

		bool stop = false;
		var commands = Enumerable.Range( 0, 4 ).Select( thread => Task.Run( () =>
		{
			var random = new Random( thread );
			int valid = 0;
			while ( !Volatile.Read( ref stop ) )
			{
				var agent = agents[random.Next( agents.Length )];
				switch ( random.Next( 6 ) )
				{
					case 0: agent.MoveTo( new Vector3( random.Next( -300, 300 ), random.Next( -300, 300 ), 0 ) ); break;
					case 1: agent.Stop(); break;
					case 2: agent.SetAgentPosition( new Vector3( random.Next( -300, 300 ), random.Next( -300, 300 ), 0 ) ); break;
					case 3: agent.SetPath( agent.GetPath() ); break;
					case 4: if ( scene.NavMesh.CalculatePath( new() { Start = agent.AgentPosition, Target = Vector3.Zero, Agent = agent } ).IsValid ) valid++; break;
					default: _ = (agent.AgentPosition, agent.Velocity, agent.WishVelocity, agent.TargetPosition, agent.IsNavigating, agent.GetLookAhead( 40 )); break;
				}
			}
			return valid;
		} ) ).ToArray();

		for ( int i = 0; i < 200; i++ ) scene.GameTick();
		Volatile.Write( ref stop, true );
		Assert.IsTrue( (await Task.WhenAll( commands )).Sum() > 0, "Paths asked for from workers should have been found" );

		foreach ( var agent in agents )
		{
			var position = agent.AgentPosition;
			Assert.IsTrue( position.IsFinite && MathF.Abs( position.x ) < 340 && MathF.Abs( position.y ) < 340, $"An agent ended up at {position}" );
		}
	}
}
