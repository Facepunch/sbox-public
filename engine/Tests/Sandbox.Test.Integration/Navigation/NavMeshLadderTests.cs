using Sandbox.Engine.Resources;
using Sandbox.Navigation;
using Sandbox.Npcs.Layers;

namespace NavigationTests;

[TestClass]
public class NavMeshLadderTests
{
	// A 256-unit platform beside a thin ladder trigger whose top reaches the edge of the platform.
	static (Scene Scene, BoxCollider Ladder, GameObject Platform) Create( float yaw = 0, bool thinY = false, float scale = 1, float pitch = 0 )
	{
		NavigationTestScene.ShippedLinkType( "drop" );
		NavigationTestScene.ShippedLinkType( "jump" );
		NavigationTestScene.ShippedLinkType( "ladder" );
		var scene = new Scene();
		var rotation = Rotation.FromYaw( yaw );
		var origin = new Vector3( 512, 512, 0 );
		scene.Floor( origin, 1000 * scale );
		var platform = scene.Box( origin + rotation * new Vector3( 100, 0, 128 ) * scale, new Vector3( 200, 300, 256 ) * scale );
		platform.GetComponent<BoxCollider>().Static = true;
		platform.WorldRotation = rotation;
		var go = scene.CreateObject();
		go.Tags.Add( "ladder" );
		go.WorldRotation = rotation * Rotation.FromPitch( pitch ) * Rotation.FromYaw( thinY ? 90 : 0 );
		go.WorldScale = scale;
		var ladder = go.Components.Create<BoxCollider>();
		ladder.IsTrigger = true;
		ladder.Center = new Vector3( 0, 1, 120 );
		float length = 264 / go.WorldRotation.Up.z;
		ladder.Scale = thinY ? new Vector3( 32, 8, length ) : new Vector3( 8, 32, length );
		var center = origin + (rotation * new Vector3( -4, 0, 128 ) - go.WorldRotation.Up.WithZ( 0 ) * (length * 0.5f)) * scale;
		go.WorldPosition = center - go.WorldRotation * ladder.Center * scale;
		scene.NavMesh.GenerateLinks = true;
		return (scene, ladder, platform);
	}

	static NavMeshLink[] Links( Scene scene )
	{
		scene.ProcessDeletes();
		return scene.GetAllComponents<NavMeshLink>().Where( x => x.IsGenerated && x.Type == NavMeshLinkType.Ladder ).ToArray();
	}

	[TestMethod]
	[DataRow( 0f, false, 1f, 0f )]
	[DataRow( 90f, true, 1f, 0f )]
	[DataRow( 45f, false, 1f, 0f )]
	[DataRow( 0f, true, 2f, 0f )]
	[DataRow( 0f, false, 1f, 30f )]
	[DataRow( 90f, true, 1f, 30f )]
	[DataRow( 45f, false, 1f, 45f )]
	public async Task TaggedTriggersConnectBothLandings( float yaw, bool thinY, float scale, float pitch )
	{
		var (scene, ladder, _) = Create( yaw, thinY, scale, pitch );
		using var scope = scene.Push();
		Assert.IsTrue( await scene.NavMesh.Generate( scene.PhysicsWorld ) );
		var links = Links( scene );
		Assert.AreEqual( 1, links.Length, $"Ladder: {ladder.WorldPosition}, {ladder.WorldRotation}, {ladder.Center}, {ladder.Scale}; bounds {ladder.LocalBounds}" );
		var link = links.Single();
		Assert.AreEqual( ladder.WorldRotation.Up, link.ClimbDirection );
		Assert.IsTrue( link.IsBiDirectional );
		Assert.IsTrue( link.WorldEndPosition.z - link.WorldStartPosition.z > 240 * scale );
		Assert.IsTrue( link.GameObject.Parent.Flags.HasFlag( GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked ) );
		Assert.IsFalse( scene.Serialize().ToJsonString().Contains( link.GameObject.Id.ToString() ) );
		Assert.AreEqual( NavMeshPathStatus.Complete, scene.NavMesh.CalculatePath( new() { Start = link.WorldStartPosition, Target = link.WorldEndPosition } ).Status );
		Assert.AreEqual( NavMeshPathStatus.Complete, scene.NavMesh.CalculatePath( new() { Start = link.WorldEndPosition, Target = link.WorldStartPosition } ).Status );

		var agent = scene.Agent( link.WorldStartPosition );
		agent.AllowAllLinkTypes = false;
		Assert.AreEqual( NavMeshPathStatus.Partial, scene.NavMesh.CalculatePath( new() { Start = link.WorldStartPosition, Target = link.WorldEndPosition, Agent = agent } ).Status );
		agent.AllowedLinkTypes.Add( NavMeshLinkType.Ladder );
		Assert.AreEqual( NavMeshPathStatus.Complete, scene.NavMesh.CalculatePath( new() { Start = link.WorldStartPosition, Target = link.WorldEndPosition, Agent = agent } ).Status );
	}

	[TestMethod]
	[DataRow( "tag" )]
	[DataRow( "collider" )]
	[DataRow( "object" )]
	[DataRow( "trigger" )]
	[DataRow( "destroy" )]
	[DataRow( "setting" )]
	public async Task RemovingTheConventionRemovesItsLinks( string change )
	{
		var (scene, ladder, _) = Create();
		using var scope = scene.Push();
		Assert.IsTrue( await scene.NavMesh.Generate( scene.PhysicsWorld ) );
		var link = Links( scene ).Single();
		var start = link.WorldStartPosition;
		var end = link.WorldEndPosition;
		switch ( change )
		{
			case "tag": ladder.Tags.Remove( "ladder" ); break;
			case "collider": ladder.Enabled = false; break;
			case "object": ladder.GameObject.Enabled = false; break;
			case "trigger": ladder.IsTrigger = false; break;
			case "destroy": scene.Remove( ladder.GameObject ); break;
			case "setting": scene.NavMesh.GenerateLinks = false; break;
		}
		scene.NavUpdate();
		Assert.AreEqual( 0, Links( scene ).Length );
		Assert.AreEqual( NavMeshPathStatus.Partial, scene.NavMesh.CalculatePath( new() { Start = start, Target = end } ).Status );
	}

	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public async Task UnchangedLaddersKeepTheirComponentsAndMovedLaddersAreRechecked( bool moveParent )
	{
		var (scene, ladder, _) = Create();
		using var scope = scene.Push();
		var moved = ladder.GameObject;
		if ( moveParent )
		{
			moved = scene.CreateObject();
			ladder.GameObject.Parent = moved;
		}
		Assert.IsTrue( await scene.NavMesh.Generate( scene.PhysicsWorld ) );
		var link = Links( scene ).Single();
		var version = scene.NavMesh.mesh.Version;
		for ( int i = 0; i < 10; i++ ) scene.NavUpdate();
		Assert.AreSame( link, Links( scene ).Single() );
		Assert.AreEqual( version, scene.NavMesh.mesh.Version );
		Assert.IsTrue( await scene.NavMesh.Generate( scene.PhysicsWorld ) );
		Assert.AreSame( link, Links( scene ).Single() );

		var position = moved.WorldPosition;
		moved.WorldPosition += Vector3.Left * 500;
		scene.NavUpdate();
		Assert.AreEqual( 0, Links( scene ).Length );
		moved.WorldPosition = position;
		scene.NavUpdate();
		Assert.AreEqual( 1, Links( scene ).Length );

		Links( scene ).Single().GameObject.Parent.Destroy();
		scene.ProcessDeletes();
		scene.NavUpdate();
		Assert.AreEqual( 1, Links( scene ).Length );
	}

	[TestMethod]
	[DataRow( "tag" )]
	[DataRow( "ancestor tag" )]
	[DataRow( "reparent" )]
	[DataRow( "collider" )]
	[DataRow( "object" )]
	[DataRow( "trigger" )]
	[DataRow( "new collider" )]
	public async Task CachedLadderCandidatesFollowSceneChanges( string change )
	{
		var (scene, ladder, _) = Create();
		using var scope = scene.Push();
		var go = ladder.GameObject;
		var parent = scene.CreateObject();
		go.Parent = scene.CreateObject();
		go.Parent.Parent = parent;
		if ( change is "ancestor tag" or "reparent" )
		{
			parent.Tags.Add( "ladder" );
			go.Tags.Remove( "ladder" );
		}
		Assert.IsTrue( await scene.NavMesh.Generate( scene.PhysicsWorld ) );
		Assert.AreEqual( 1, Links( scene ).Length );
		var center = ladder.Center;
		var size = ladder.Scale;
		switch ( change )
		{
			case "tag": go.Tags.Remove( "ladder" ); break;
			case "ancestor tag": parent.Tags.Remove( "ladder" ); break;
			case "reparent": go.Parent.Parent = scene; break;
			case "collider": ladder.Enabled = false; break;
			case "object": parent.Enabled = false; break;
			case "trigger": ladder.IsTrigger = false; break;
			case "new collider": ladder.Destroy(); scene.ProcessDeletes(); break;
		}
		scene.NavUpdate();
		Assert.AreEqual( 0, Links( scene ).Length );
		switch ( change )
		{
			case "tag": go.Tags.Add( "ladder" ); break;
			case "ancestor tag": parent.Tags.Add( "ladder" ); break;
			case "reparent": go.Parent.Parent = parent; break;
			case "collider": ladder.Enabled = true; break;
			case "object": parent.Enabled = true; break;
			case "trigger": ladder.IsTrigger = true; break;
			case "new collider":
				ladder = go.Components.Create<BoxCollider>();
				ladder.Center = center;
				ladder.Scale = size;
				ladder.IsTrigger = true;
				break;
		}
		scene.NavUpdate();
		Assert.AreEqual( 1, Links( scene ).Length );
	}

	[TestMethod]
	[DataRow( "missing landing" )]
	[DataRow( "ceiling" )]
	[DataRow( "horizontal" )]
	[DataRow( "nearly horizontal" )]
	[DataRow( "untagged" )]
	[DataRow( "solid" )]
	[DataRow( "disabled generation" )]
	public async Task InvalidLaddersDoNotProduceLinks( string reason )
	{
		var (scene, ladder, platform) = Create();
		using var scope = scene.Push();
		switch ( reason )
		{
			case "missing landing": scene.Remove( platform ); break;
			case "ceiling": scene.Box( new Vector3( 470, 512, 150 ), new Vector3( 64, 128, 8 ) ); break;
			case "horizontal": ladder.WorldRotation = Rotation.FromPitch( 90 ); break;
			case "nearly horizontal": ladder.WorldRotation = Rotation.FromPitch( 89 ); break;
			case "untagged": ladder.Tags.Remove( "ladder" ); break;
			case "solid": ladder.IsTrigger = false; break;
			case "disabled generation": scene.NavMesh.GenerateLinks = false; break;
		}
		Assert.IsTrue( await scene.NavMesh.Generate( scene.PhysicsWorld ) );
		Assert.AreEqual( 0, Links( scene ).Length );
	}

	[TestMethod]
	[DataRow( 0f )]
	[DataRow( 30f )]
	public async Task RebuiltAndBakedMeshesUseTheCurrentTriggers( float pitch )
	{
		var (scene, ladder, platform) = Create( pitch: pitch );
		using var scope = scene.Push();
		Assert.IsTrue( await scene.NavMesh.Generate( scene.PhysicsWorld ) );
		var link = Links( scene ).Single();
		scene.NavMesh.AgentHeight = 72;
		Assert.IsTrue( await scene.NavMesh.Generate( scene.PhysicsWorld ) );
		Assert.AreSame( link, Links( scene ).Single() );
		Assert.AreEqual( NavMeshPathStatus.Complete, scene.NavMesh.CalculatePath( new() { Start = link.WorldStartPosition, Target = link.WorldEndPosition } ).Status );
		var baked = await scene.NavMesh.BakeDataToBytes();
		await scene.NavMesh.LoadFromBakedData( baked );
		Assert.AreEqual( 1, Links( scene ).Length );
		Assert.AreEqual( ladder.WorldRotation.Up, Links( scene ).Single().ClimbDirection );

		ladder.Tags.Remove( "ladder" );
		await scene.NavMesh.LoadFromBakedData( baked );
		Assert.AreEqual( 0, Links( scene ).Length, "A bake must not restore a ladder that no longer exists" );
		ladder.Tags.Add( "ladder" );
		scene.NavUpdate();
		Assert.AreEqual( 1, Links( scene ).Length );
		scene.Remove( platform );
		Assert.IsTrue( await scene.NavMesh.Generate( scene.PhysicsWorld ) );
		Assert.AreEqual( 0, Links( scene ).Length );
	}

	[TestMethod]
	[DataRow( 0f, false, false )]
	[DataRow( 30f, false, false )]
	[DataRow( 45f, false, false )]
	[DataRow( 0f, true, false )]
	[DataRow( 30f, true, false )]
	[DataRow( 0f, false, true )]
	[DataRow( 30f, true, true )]
	public async Task NpcsClimbGeneratedLaddersInBothDirections( float pitch, bool roofRim, bool rebuildWhileOccupied )
	{
		var (scene, _, platform) = Create( pitch: pitch );
		using var scope = scene.Push();
		// A thin projecting top rung has no walkable polygon of its own.
		if ( roofRim ) scene.Box( new Vector3( 500, 512, 260 ), new Vector3( 4, 300, 8 ) );
		Assert.IsTrue( await scene.NavMesh.Generate( scene.PhysicsWorld ) );
		var link = Links( scene ).Single();
		if ( roofRim ) Assert.IsTrue( link.ClimbExitLift > 0, "The roof rim needs a raised exit" );
		var foot = link.WorldStartPosition;
		var axis = link.ClimbDirection;
		var start = foot + Vector3.Left * 80;
		var agent = scene.Agent( start );
		Rigidbody body = null;
		if ( rebuildWhileOccupied )
		{
			body = agent.GameObject.Components.Create<Rigidbody>();
			var collider = agent.GameObject.Components.Create<BoxCollider>();
			collider.Center = Vector3.Up * 32;
			collider.Scale = new Vector3( 32, 32, 64 );
		}
		agent.GameObject.Components.Create<AnimationLayer>();
		var navigation = agent.GameObject.Components.Create<NavigationLayer>();
		var npc = agent.GameObject.Components.Create<NpcTests.NpcTestKit.TestNpc>();
		scene.GameTick( 0.02f );

		foreach ( var target in new[] { platform.WorldPosition.WithZ( 256 ), start } )
		{
			navigation.MoveTo( target );
			int climbingTicks = 0;
			bool crossed = false;
			bool rebuilt = false;
			for ( int i = 0; i < 1000; i++ )
			{
				scene.GameTick( 0.02f );
				if ( navigation.LinkTraversal is { } traversal )
				{
					crossed = true;
					Assert.AreSame( NavMeshLinkType.Ladder, traversal.Type );
					if ( roofRim )
					{
						var center = npc.WorldPosition + Vector3.Up * 33;
						var clearance = scene.Trace.Cylinder( 63, 16, center, center ).IgnoreGameObjectHierarchy( npc.GameObject ).Run();
						Assert.IsFalse( clearance.StartedSolid, $"Clipped the roof rim at {npc.WorldPosition}" );
					}
					if ( npc.WorldPosition.z > 32 && npc.WorldPosition.z < 224 )
					{
						climbingTicks++;
						if ( rebuildWhileOccupied && !rebuilt )
						{
							Assert.AreEqual( PhysicsBodyType.Keyframed, body.PhysicsBody.BodyType );
							Assert.IsTrue( link.IsOccupied );
							await scene.NavMesh.GenerateTile( scene.PhysicsWorld, foot );
							Assert.AreSame( link, Links( scene ).Single(), "The occupant must not invalidate its own ladder" );
							Assert.IsTrue( agent.IsTraversingLink );
							rebuilt = true;
						}
						var onAxis = foot + axis * ((npc.WorldPosition.z - foot.z) / axis.z);
						Assert.IsTrue( npc.WorldPosition.Distance( onAxis ) < 0.5f, $"Left the ladder axis at {npc.WorldPosition}, expected {onAxis}" );
					}
				}
				if ( crossed && !navigation.IsTraversingLink && npc.WorldPosition.Distance( target ) < 20 ) break;
			}
			Assert.IsTrue( climbingTicks > 20, $"Never climbed toward {target}; stopped at {npc.WorldPosition}" );
			Assert.IsTrue( npc.WorldPosition.Distance( target ) < 20, $"Did not reach {target}: {npc.WorldPosition}" );
		}
	}

	[TestMethod]
	public async Task EntrySearchFindsClearSpaceBesideTheLadder()
	{
		var (scene, _, _) = Create();
		using var scope = scene.Push();
		// A wall detail projects 24 units into the climb. The first entry point is blocked,
		// but moving the climb line a little farther out still reaches both landings.
		scene.Box( new Vector3( 500, 512, 100 ), new Vector3( 24, 100, 8 ) );
		Assert.IsTrue( await scene.NavMesh.Generate( scene.PhysicsWorld ) );
		var link = Links( scene ).Single();
		Assert.IsTrue( link.WorldStartPosition.x < 480, $"Did not move away from the obstruction: {link.WorldStartPosition}" );
	}

	[TestMethod]
	public async Task RoofRimsHigherThanAStepStillBlockTheExit()
	{
		var (scene, _, _) = Create();
		using var scope = scene.Push();
		scene.Box( new Vector3( 512, 512, 288 ), new Vector3( 16, 300, 64 ) );
		Assert.IsTrue( await scene.NavMesh.Generate( scene.PhysicsWorld ) );
		Assert.AreEqual( 0, Links( scene ).Length );
	}

	[TestMethod]
	public async Task OnlyNearbyTileChangesRecheckLadderClearance()
	{
		var (scene, _, _) = Create();
		using var scope = scene.Push();
		var distant = new Vector3( 4096, 4096, 0 );
		scene.Floor( distant );
		Assert.IsTrue( await scene.NavMesh.Generate( scene.PhysicsWorld ) );
		var link = Links( scene ).Single();
		var foot = link.WorldStartPosition;
		var obstacle = scene.Box( new Vector3( 470, 512, 150 ), new Vector3( 64, 128, 8 ) );

		// Geometry changes take effect when their tiles are requested, not when a link moves elsewhere.
		var other = scene.Link<NavMeshLink>( distant, distant + Vector3.Forward * 80 );
		for ( int i = 0; i < 3; i++ )
		{
			other.WorldPosition += Vector3.Left * 8;
			scene.NavUpdate();
			Assert.AreSame( link, Links( scene ).Single() );
		}
		await scene.NavMesh.GenerateTile( scene.PhysicsWorld, distant );
		Assert.AreSame( link, Links( scene ).Single() );
		await scene.NavMesh.GenerateTile( scene.PhysicsWorld, foot );
		Assert.AreEqual( 0, Links( scene ).Length );
		scene.Remove( obstacle );
		await scene.NavMesh.GenerateTile( scene.PhysicsWorld, foot );
		Assert.AreEqual( 1, Links( scene ).Length );
		scene.NavMesh.UnloadTile( foot );
		scene.NavUpdate();
		Assert.AreEqual( 0, Links( scene ).Length );
		await scene.NavMesh.GenerateTile( scene.PhysicsWorld, foot );
		Assert.AreEqual( 1, Links( scene ).Length );
	}

	[TestMethod]
	public async Task UnchangedAreaJobsDoNotRecheckLadderClearance()
	{
		var (scene, _, _) = Create();
		using var scope = scene.Push();
		// The volume overlaps the tiles in XY but is above every walkable span. Moving it
		// rebuilds from the cached field and returns Unchanged, without collecting new geometry.
		var area = scene.Area( new Vector3( 512, 512, 400 ), new Vector3( 900, 900, 16 ) );
		Assert.IsTrue( await scene.NavMesh.Generate( scene.PhysicsWorld ) );
		Assert.IsTrue( scene.NavMesh.mesh.GetStats().CacheBytes > 0 );
		var link = Links( scene ).Single();
		var foot = link.WorldStartPosition;
		scene.Box( new Vector3( 470, 512, 150 ), new Vector3( 64, 128, 8 ) );
		area.WorldPosition += Vector3.Left * 4;
		await scene.Settle();
		Assert.AreSame( link, Links( scene ).Single(), "A cached area-only job must not re-sweep changed physics geometry" );

		// Once geometry is explicitly rebuilt, the new obstacle still invalidates the ladder.
		await scene.NavMesh.GenerateTile( scene.PhysicsWorld, foot );
		Assert.AreEqual( 0, Links( scene ).Length );
	}

	[TestMethod]
	[DataRow( 45f )]
	[DataRow( -45f )]
	public async Task DiagonalPolygonBoundsDoNotReachPastTheEndOfThePlatform( float yaw )
	{
		var (scene, ladder, _) = Create( yaw: yaw );
		using var scope = scene.Push();
		ladder.WorldPosition += Rotation.FromYaw( yaw ) * Vector3.Left * 180;
		Assert.IsTrue( await scene.NavMesh.Generate( scene.PhysicsWorld ) );
		Assert.AreEqual( 0, Links( scene ).Length, "The roof's AABB overlaps the search, but its polygon is too far along the wall" );
	}

	[TestMethod]
	[DataRow( 8f, 0f )]
	[DataRow( 32f, 18f )]
	public async Task NonDefaultAgentSettingsStillConnectNearbyLandings( float radius, float step )
	{
		var (scene, _, _) = Create();
		using var scope = scene.Push();
		scene.NavMesh.AgentRadius = radius;
		scene.NavMesh.AgentStepSize = step;
		Assert.IsTrue( await scene.NavMesh.Generate( scene.PhysicsWorld ) );
		Assert.AreEqual( 1, Links( scene ).Length );
	}

	[TestMethod]
	[DataRow( "npc" )]
	[DataRow( "player" )]
	[DataRow( "prop" )]
	public async Task KeyframedCharactersDoNotBecomeNavigationGeometry( string kind )
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var go = scene.Floor( Vector3.Zero );
		var body = go.Components.Create<Rigidbody>();
		if ( kind == "npc" ) go.Components.Create<NpcTests.NpcTestKit.TestNpc>();
		if ( kind == "player" ) go.Components.Create<PlayerController>();
		body.MotionEnabled = false;
		body.UpdateBody();
		Assert.AreEqual( PhysicsBodyType.Keyframed, body.PhysicsBody.BodyType );
		await scene.NavMesh.Generate( scene.PhysicsWorld );
		Assert.AreEqual( kind == "prop", scene.NavMesh.OnMesh( Vector3.Zero ), "Only the ordinary frozen prop may contribute a walkable surface" );
	}

	[TestMethod]
	[DataRow( "npc", false )]
	[DataRow( "npc", true )]
	[DataRow( "player", false )]
	[DataRow( "player", true )]
	public async Task CharacterChildrenAreFilteredByTheirBodyOwner( string kind, bool separateBody )
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var character = scene.CreateObject();
		var parentBody = character.Components.Create<Rigidbody>();
		parentBody.MotionEnabled = false;
		if ( kind == "npc" ) character.Components.Create<NpcTests.NpcTestKit.TestNpc>();
		else character.Components.Create<PlayerController>();
		var platform = scene.CreateObject();
		platform.Parent = character;
		platform.WorldPosition = Vector3.Down * 16;
		var body = separateBody ? platform.Components.Create<Rigidbody>() : parentBody;
		var collider = platform.Components.Create<BoxCollider>();
		collider.Scale = new Vector3( 400, 400, 32 );
		body.MotionEnabled = false;
		body.UpdateBody();
		Assert.AreSame( body.PhysicsBody, collider.PhysicsBody );
		Assert.AreEqual( PhysicsBodyType.Keyframed, body.PhysicsBody.BodyType );
		await scene.NavMesh.Generate( scene.PhysicsWorld );
		Assert.AreEqual( separateBody, scene.NavMesh.OnMesh( new Vector3( 100, 0, 0 ) ),
			"A separate platform keeps its geometry; child colliders sharing the character's body do not" );
	}
}
