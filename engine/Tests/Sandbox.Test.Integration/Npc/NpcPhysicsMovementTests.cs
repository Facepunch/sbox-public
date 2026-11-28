using NavigationTests;
using Sandbox.Npcs;
using Sandbox.Npcs.Layers;
using System;
using System.Collections.Generic;
using System.Linq;
using static NpcTests.PhysicsNpcKit;

namespace NpcTests;

/// <summary>
/// <see cref="NavigationLayer.MovementModes.Physics"/> with a body shaped like the shipped NPC prefabs.
/// </summary>
[TestClass]
public class NpcPhysicsMovementTests
{
	static async Task<Scene> Build( Action<Scene> build )
	{
		var scene = new Scene();
		using var scope = scene.Push();
		scene.Floor( Vector3.Zero, 1200 );
		build( scene );
		Assert.IsTrue( await scene.NavMesh.Generate( scene.PhysicsWorld ) );
		return scene;
	}

	static void Link( Scene scene, string type, Vector3 from, Vector3 to )
	{
		var link = scene.Link<NavMeshLink>( from, to );
		link.Type = NavigationTestScene.ShippedLinkType( type );
		link.ConnectionRadius = 48;
	}

	// A 64 high block with a link up its face
	static Task<Scene> Ledge( string type ) => Build( scene =>
	{
		scene.Box( new Vector3( 200, 0, 32 ), new Vector3( 200, 300, 64 ) );
		Link( scene, type, new Vector3( 60, 0, 0 ), new Vector3( 130, 0, 64 ) );
	} );

	// 192 wide between walls
	static Task<Scene> Corridor() => Build( scene =>
	{
		scene.Box( new Vector3( 0, 120, 40 ), new Vector3( 1200, 16, 80 ) );
		scene.Box( new Vector3( 0, -120, 40 ), new Vector3( 1200, 16, 80 ) );
	} );

	static GameObject LooseProp( Scene scene, Vector3 center, Vector3 size, float mass )
	{
		var go = scene.Box( center, size );
		go.Components.Create<Prop>();
		go.Components.Create<Rigidbody>().MassOverride = mass;
		return go;
	}

	static bool Blocked( GameObject prop ) => prop.GetComponent<NpcDetourBlocker>().IsValid();

	[TestMethod]
	public async Task WalksADynamicBodyToTheTarget()
	{
		var scene = await Ground();
		using var scope = scene.Push();
		var npc = Standing( scene, new Vector3( -300, 0, 0 ) );

		Navigation( npc ).MoveTo( new Vector3( 300, 0, 0 ), 10, 150 );
		var fastest = 0f;
		Run( scene, () => Arrived( npc ), "arrived", each: () =>
		{
			Assert.AreEqual( PhysicsBodyType.Dynamic, Body( npc ).PhysicsBody.BodyType );
			fastest = MathF.Max( fastest, Body( npc ).Velocity.WithZ( 0 ).Length );
		} );

		Assert.IsTrue( fastest > 140, $"Friction held it to {fastest} of 150" );
		Assert.IsTrue( npc.WorldRotation.Up.z > 0.999f, "Upright" );
	}

	// Risers its box can't climb by itself: it steps up them
	[TestMethod]
	public async Task ClimbsStairs()
	{
		var scene = await Build( scene =>
		{
			for ( int s = 0; s < 3; s++ )
				scene.Box( new Vector3( s * 40, 0, 8 * (s + 1) ), new Vector3( 40, 200, 16 * (s + 1) ) );
			scene.Box( new Vector3( 200, 0, 24 ), new Vector3( 200, 200, 48 ) );
		} );
		using var scope = scene.Push();
		var npc = Standing( scene, new Vector3( -200, 0, 0 ) );

		Navigation( npc ).MoveTo( new Vector3( 260, 0, 48 ), 16, 120 );
		Run( scene, () => Arrived( npc ), "climbed the stairs" );
		Assert.IsTrue( npc.WorldPosition.z > 46, $"Stands at {npc.WorldPosition}" );
	}

	[TestMethod]
	[DataRow( 26f, 0f )]
	[DataRow( 16f, 68f )]
	public async Task NeverStepsUpWhereTheBodyWouldNotFit( float riser, float headroom )
	{
		var scene = await Ground();
		using var scope = scene.Push();

		// Unknown to the navmesh, so the route runs straight into it. The headroom fits the agent's height, not the body.
		scene.Box( new Vector3( 200, 0, riser * 0.5f ), new Vector3( 400, 600, riser ) );
		if ( headroom > 0 )
			scene.Box( new Vector3( 200, 0, riser + headroom + 8 ), new Vector3( 400, 600, 16 ) );

		var npc = Standing( scene, new Vector3( -100, 0, 0 ) );
		Navigation( npc ).MoveTo( new Vector3( 200, 0, 0 ), 16, 150 );
		Ticks( scene, 100, () => Assert.IsTrue( npc.WorldPosition.z < 4, $"Stepped up to {npc.WorldPosition}" ) );
		Assert.IsTrue( npc.WorldPosition.x > -20, $"Never reached the riser, at {npc.WorldPosition}" );
	}

	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public async Task ABodyWithNothingSolidIsPlacedByTheAgent( bool trigger )
	{
		var scene = await Ground();
		using var scope = scene.Push();
		var npc = PhysicsNpc( scene, new Vector3( -200, 0, 0 ), solid: false );
		if ( !trigger ) npc.GetComponent<BoxCollider>().Destroy();

		// Nothing to land on, so it is set down by hand
		scene.GameTick( Tick );
		npc.WorldPosition = new Vector3( -200, 0, 1 );
		Run( scene, () => npc.IsOnGround, "landed" );
		Assert.IsFalse( Body( npc ).MotionEnabled );

		Navigation( npc ).MoveTo( new Vector3( 200, 0, 0 ) );
		Run( scene, () => Arrived( npc ), "arrived" );
		Assert.IsTrue( npc.WorldPosition.z > -1, $"Fell to {npc.WorldPosition}" );
	}

	[TestMethod]
	public async Task NpcsMeetingHeadOnPassAsBodies()
	{
		var scene = await Ground();
		using var scope = scene.Push();
		var a = Standing( scene, new Vector3( -300, 0, 0 ) );
		var b = Standing( scene, new Vector3( 300, 4, 0 ) );

		Navigation( a ).MoveTo( new Vector3( 300, 0, 0 ), 16 );
		Navigation( b ).MoveTo( new Vector3( -300, 0, 0 ), 16 );
		var closest = float.MaxValue;
		Run( scene, () => Arrived( a ) && Arrived( b ), "both arrived", each: () =>
		{
			Assert.IsTrue( Body( a ).MotionEnabled && Body( b ).MotionEnabled );
			closest = MathF.Min( closest, a.WorldPosition.WithZ( 0 ).Distance( b.WorldPosition.WithZ( 0 ) ) );
		} );

		Assert.IsTrue( closest > 16, $"Overlapped, {closest} apart" );
	}

	[TestMethod]
	public async Task SomethingSweepingThroughACrowdHitsIt()
	{
		var scene = await Ground();
		using var scope = scene.Push();
		var crowd = new[] { Standing( scene, new Vector3( 0, -20, 0 ) ), Standing( scene, new Vector3( 0, 20, 0 ) ) };
		foreach ( var npc in crowd )
			Navigation( npc ).MoveTo( npc.WorldPosition.WithX( 900 ), 16, 100 );

		// A keyframed bar catching them up, like a sweeper
		var bar = scene.Box( new Vector3( -150, 0, 40 ), new Vector3( 40, 300, 80 ) ).Components.Create<Rigidbody>();
		bar.MotionEnabled = false;
		Ticks( scene, 120, () =>
		{
			foreach ( var npc in crowd )
				Assert.IsTrue( npc.WorldPosition.x - bar.PhysicsBody.Position.x > 26, $"The bar went through {npc.WorldPosition}" );
			bar.PhysicsBody.Velocity = Vector3.Forward * 300;
		} );

		Assert.IsTrue( bar.PhysicsBody.Position.x > 400, "The bar caught them up" );
	}

	[TestMethod]
	[DataRow( "jump" )]
	[DataRow( "drop" )]
	public async Task LinksAreFlownBothWays( string type )
	{
		var scene = await Ledge( type );
		using var scope = scene.Push();
		var npc = Standing( scene, new Vector3( -100, 0, 0 ) );

		foreach ( var target in new[] { new Vector3( 220, 0, 64 ), new Vector3( -100, 0, 0 ) } )
		{
			Navigation( npc ).MoveTo( target, 16 );
			var flown = false;
			Run( scene, () => Arrived( npc ), $"reached {target}", each: () =>
				flown |= Navigation( npc ).IsTraversingLink && Body( npc ).PhysicsBody.BodyType == PhysicsBodyType.Dynamic && Body( npc ).Velocity.z > 50 );
			Assert.IsTrue( flown, $"Launched toward {target}" );
			Assert.IsTrue( MathF.Abs( npc.WorldPosition.z - target.z ) < 8, $"Landed at {npc.WorldPosition}" );
		}
	}

	[TestMethod]
	public async Task AJumpBumpedAtTakeoffStillMakesIt()
	{
		var scene = await Ledge( "jump" );
		using var scope = scene.Push();
		var jumper = Standing( scene, new Vector3( -100, 0, 0 ) );
		Standing( scene, new Vector3( 82, 0, 0 ) );

		Navigation( jumper ).MoveTo( new Vector3( 220, 0, 64 ), 16 );
		Run( scene, () => Arrived( jumper ), "made the jump" );
		Assert.IsTrue( jumper.WorldPosition.z > 60, $"Stands at {jumper.WorldPosition}" );
	}

	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public async Task AJumpKnockedBackOntoItsTakeoffGoesAgain( bool everyTime )
	{
		// A wall too high to step, jumped over level ground
		var scene = await Build( scene =>
		{
			scene.Box( new Vector3( 0, 0, 11 ), new Vector3( 8, 1200, 22 ) );
			Link( scene, "jump", new Vector3( -60, 0, 0 ), new Vector3( 60, 0, 0 ) );
		} );
		using var scope = scene.Push();
		var npc = Standing( scene, new Vector3( -200, 0, 0 ) );

		Navigation( npc ).MoveTo( new Vector3( 200, 0, 0 ), 16 );
		var launches = 0;
		var bumped = 0;
		var rising = false;
		Run( scene, () => Navigation( npc ).Status != MovementLayer.MovementStatus.Moving, "finished", each: () =>
		{
			// Gone again it can be back on the link within the tick, so a launch is told by its lift
			var launched = Navigation( npc ).IsTraversingLink && Body( npc ).Velocity.z > 50;
			if ( launched && !rising ) launches++;
			rising = launched;

			// Clear of the floor, which would bounce it straight back up
			if ( launched && npc.WorldPosition.z > 8 && bumped < launches && (everyTime || launches == 1) )
			{
				bumped = launches;
				Body( npc ).Velocity = new Vector3( -250, 0, -50 );
			}
		} );

		Assert.AreEqual( everyTime ? MovementLayer.MovementStatus.Failed : MovementLayer.MovementStatus.Arrived, Navigation( npc ).Status );
		Assert.AreEqual( everyTime ? 3 : 2, launches, "Jumped again, a few times before failing" );
		if ( everyTime )
			Assert.IsTrue( npc.WorldPosition.x < 0, $"Crossed at {npc.WorldPosition}" );
	}

	[TestMethod]
	public async Task ModeSwitchesHandTheBodyOverWalkingAndMidFlight()
	{
		var scene = await Ledge( "jump" );
		using var scope = scene.Push();
		var npc = Standing( scene, new Vector3( -400, 0, 0 ) );
		var agent = npc.GetComponent<NavMeshAgent>();

		Navigation( npc ).MoveTo( new Vector3( 220, 0, 64 ), 16 );
		Ticks( scene, 30 );
		Assert.IsTrue( Body( npc ).MotionEnabled && !agent.UpdatePosition );

		Navigation( npc ).MovementMode = NavigationLayer.MovementModes.Kinematic;
		Ticks( scene, 30 );
		Assert.IsTrue( !Body( npc ).MotionEnabled && agent.UpdatePosition );

		Navigation( npc ).MovementMode = NavigationLayer.MovementModes.Physics;
		Run( scene, () => Navigation( npc ).IsTraversingLink && Body( npc ).Velocity.z > 50, "took off" );
		Navigation( npc ).MovementMode = NavigationLayer.MovementModes.Kinematic;

		Run( scene, () => Arrived( npc ), "arrived kinematic" );
		Assert.IsFalse( Body( npc ).MotionEnabled );
		Assert.IsTrue( npc.WorldPosition.z > 60, $"Stands at {npc.WorldPosition}" );
	}

	[TestMethod]
	public async Task CarriedLaunchedAndHandedToAnotherMoverItWalksOnAfter()
	{
		var scene = await Ground();
		using var scope = scene.Push();
		var npc = Standing( scene, new Vector3( -400, 0, 0 ) );
		var navigation = Navigation( npc );
		var agent = npc.GetComponent<NavMeshAgent>();
		var flying = npc.GameObject.Components.Create<FlyingLayer>( false );

		navigation.MoveTo( new Vector3( 400, 0, 0 ), 16 );
		Ticks( scene, 20 );

		npc.Launch( new Vector3( 0, 200, 300 ) );
		Run( scene, () => npc.IsAirborne, "took off" );
		Run( scene, () => npc.IsOnGround && agent.Enabled, "landed from the launch" );
		Run( scene, () => navigation.IsMoving, "walked on after the launch", ticks: 60 );

		var carrier = scene.CreateObject();
		npc.StartCarry( carrier );
		scene.GameTick( Tick );
		Assert.IsFalse( agent.Enabled );
		npc.StopCarry( carrier );
		Run( scene, () => npc.IsOnGround && agent.Enabled, "put down" );

		npc.SetMovement( flying );
		scene.GameTick( Tick );
		Assert.IsFalse( agent.Enabled );
		npc.SetMovement( navigation );
		Run( scene, () => npc.IsOnGround && agent.Enabled, "back on the navmesh" );
		Assert.IsFalse( agent.UpdatePosition );

		navigation.MoveTo( new Vector3( 400, 0, 0 ), 16 );
		Run( scene, () => Arrived( npc ), "arrived" );
	}

	[TestMethod]
	public async Task APlayerInTheWayIsNeverBlockedOut()
	{
		var scene = await Corridor();
		using var scope = scene.Push();
		var player = scene.Box( new Vector3( 0, -40, 40 ), new Vector3( 60, 60, 80 ) );
		player.Tags.Add( "player" );
		player.Components.Create<Rigidbody>().MassOverride = 20000;
		var npc = Standing( scene, new Vector3( -300, -40, 0 ) );

		Navigation( npc ).MoveTo( new Vector3( 300, -40, 0 ), 16 );
		Run( scene, () => Navigation( npc ).Status is not MovementLayer.MovementStatus.Moving, "gave up", ticks: 400, each: () =>
			Assert.IsFalse( Blocked( player ) ) );
	}

	[TestMethod]
	public async Task ALightPropInTheWayIsPushedNotRoutedRoundNorHurt()
	{
		var scene = await Ground();
		using var scope = scene.Push();
		var light = LooseProp( scene, new Vector3( 0, 0, 16 ), new Vector3( 32 ), 10 );
		light.GetComponent<Prop>().Health = 10;
		var npc = Standing( scene, new Vector3( -300, 0, 0 ) );

		Navigation( npc ).MoveTo( new Vector3( 300, 0, 0 ), 16, 300 );
		Run( scene, () => Arrived( npc ), "arrived", each: () => Assert.IsFalse( Blocked( light ), "Routed round a prop it could push" ) );
		Assert.IsTrue( light.WorldPosition.WithZ( 0 ).Length > 40, $"Never pushed it, it is at {light.WorldPosition}" );
		Assert.AreEqual( 10f, light.GetComponent<Prop>().Health, "Running into it hurt it" );
	}

	[TestMethod]
	public async Task OnlyTheHeavyPropInTheWayIsRoutedRound()
	{
		var scene = await Corridor();
		using var scope = scene.Push();
		var inTheWay = LooseProp( scene, new Vector3( 0, -40, 40 ), new Vector3( 60, 60, 80 ), 20000 );
		var alongside = LooseProp( scene, new Vector3( 0, -92, 40 ), new Vector3( 36, 36, 80 ), 20000 );
		var npc = Standing( scene, new Vector3( -300, -40, 0 ) );

		Navigation( npc ).MoveTo( new Vector3( 300, -40, 0 ), 16 );
		Run( scene, () => Arrived( npc ), "arrived", each: () => Assert.IsFalse( Blocked( alongside ), "Blocked a prop that was not in the way" ) );
		Assert.IsTrue( Blocked( inTheWay ), "Routed round the prop in the way" );
	}

	[TestMethod]
	public async Task APileItCannotGetThroughFailsTheMove()
	{
		var scene = await Corridor();
		using var scope = scene.Push();

		// Too heavy to shove, wall to wall
		var pile = new List<GameObject>();
		for ( var y = -84; y <= 84; y += 56 )
			pile.Add( LooseProp( scene, new Vector3( 0, y, 40 ), new Vector3( 54, 54, 80 ), 20000 ) );
		var npc = Standing( scene, new Vector3( -300, 0, 0 ) );

		Navigation( npc ).MoveTo( new Vector3( 300, 0, 0 ), 16 );
		Run( scene, () => Navigation( npc ).Status is not MovementLayer.MovementStatus.Moving, "gave up" );
		Assert.AreEqual( MovementLayer.MovementStatus.Failed, Navigation( npc ).Status );
		Assert.IsTrue( pile.Any( Blocked ), "Never tried a way round" );
	}

	[TestMethod]
	public async Task ABlockerAppearingAheadIsWalkedRoundWithoutMovingAgain()
	{
		var scene = await Ground();
		using var scope = scene.Push();
		var npc = Standing( scene, new Vector3( -300, 0, 0 ) );
		Navigation( npc ).MoveTo( new Vector3( 300, 0, 0 ), 16, 150 );
		Ticks( scene, 10 );

		// Nothing solid in the way: only the crowd replanning on the rebuilt tiles takes it round
		scene.Area( new Vector3( 0, 0, 40 ), new Vector3( 80, 80, 80 ) ).Margin = 16;
		Run( scene, () => Arrived( npc ), "arrived", each: () =>
			Assert.IsFalse( MathF.Abs( npc.WorldPosition.x ) < 40 && MathF.Abs( npc.WorldPosition.y ) < 40, $"Walked through the blocker at {npc.WorldPosition}" ) );
	}

	[TestMethod]
	public async Task TheBlockerTakesThePropsSolidShapes()
	{
		var scene = await Ground();
		using var scope = scene.Push();
		var go = scene.CreateObject();
		go.WorldPosition = new Vector3( 0, 0, 1 );
		go.WorldRotation = Rotation.FromYaw( 30 );
		go.WorldScale = 2;
		var box = go.Components.Create<BoxCollider>();
		box.Center = new Vector3( 10, 0, 10 );
		box.Scale = new Vector3( 20, 10, 20 );
		var trigger = go.Components.Create<SphereCollider>();
		trigger.Radius = 100;
		trigger.IsTrigger = true;
		go.Components.Create<Prop>();
		go.Components.Create<Rigidbody>().Gravity = false;
		scene.GameTick( Tick );

		NpcDetourBlocker.Refresh( go );
		var blocked = go.GetComponent<NavMeshArea>().SceneVolume.Box;
		Assert.IsTrue( blocked.Mins.Distance( new Vector3( 0, -5, 0 ) ) < 0.5f && blocked.Maxs.Distance( new Vector3( 20, 5, 20 ) ) < 0.5f, $"Blocks {blocked}" );

		await scene.Settle();
		Assert.IsFalse( scene.NavMesh.OnMesh( go.WorldTransform.PointToWorld( box.Center ).WithZ( 0 ), 2 ), "Not blocked where the box is" );
	}

	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public async Task TheBlockerGoesOnceThePropIsMovedOrRemoved( bool remove )
	{
		var scene = await Ground();
		using var scope = scene.Push();
		var prop = LooseProp( scene, new Vector3( 0, 0, 21 ), new Vector3( 40 ), 50 );
		prop.GetComponent<Rigidbody>().Gravity = false;
		NpcDetourBlocker.Refresh( prop );
		await scene.Settle();

		// Within an agent radius of its side
		var beside = new Vector3( 0, 30, 0 );
		Assert.IsFalse( scene.NavMesh.OnMesh( beside, 2 ), "Never blocked" );

		if ( remove ) scene.Remove( prop );
		else prop.WorldPosition += Vector3.Forward * 200;
		Ticks( scene, 2 );
		await scene.Settle();

		Assert.IsTrue( scene.NavMesh.OnMesh( beside, 2 ), "Still blocked where it was" );
		if ( !remove )
			Assert.IsTrue( scene.NavMesh.OnMesh( beside + Vector3.Forward * 200, 2 ), "The block followed it" );
	}

	[TestMethod]
	public async Task ChangingOnlyTheMarginRebuildsTheTiles()
	{
		var scene = await Ground( 800 );
		using var scope = scene.Push();
		var area = scene.Area( Vector3.Zero, new Vector3( 64, 64, 64 ) );
		await scene.Settle();
		var beside = new Vector3( 48, 0, 0 );
		bool Walkable() => scene.NavMesh.GetClosestPoint( beside ) is { } point && point.WithZ( 0 ).Distance( beside ) < 2;
		Assert.IsTrue( Walkable() );

		area.Margin = 32;
		await scene.Settle();
		Assert.IsFalse( Walkable() );
	}

	[TestMethod]
	public async Task SlidOntoALedgesEdgeItStandsThere()
	{
		var scene = await Build( scene => scene.Box( new Vector3( -100, 0, 32 ), new Vector3( 200, 300, 64 ) ) );
		using var scope = scene.Push();
		var npc = Standing( scene, new Vector3( -60, 0, 64 ) );

		// Its middle over the drop, a corner of the box still on the ledge
		npc.WorldPosition = new Vector3( 6, 0, 64.2f );
		Body( npc ).Velocity = default;
		Ticks( scene, 60, () => Assert.IsTrue( npc.IsOnGround, "Off the edge" ) );
	}

	[TestMethod]
	public async Task RidesAPlatformMovedByItsTransform()
	{
		var scene = await Ground();
		using var scope = scene.Push();
		var platform = scene.Box( new Vector3( 0, 0, 4 ), new Vector3( 160, 116, 8 ) );
		platform.Components.Create<Rigidbody>().MotionEnabled = false;
		var npc = Standing( scene, new Vector3( 0, 0, 9 ) );

		Ticks( scene, 100, () => platform.WorldPosition += Vector3.Forward * 120f * Tick );
		Assert.IsTrue( npc.WorldPosition.z > 6, $"Fell off to {npc.WorldPosition}" );
		Assert.IsTrue( MathF.Abs( npc.WorldPosition.x - platform.WorldPosition.x ) < 20, $"Left behind at {npc.WorldPosition}, platform at {platform.WorldPosition}" );
	}
}
