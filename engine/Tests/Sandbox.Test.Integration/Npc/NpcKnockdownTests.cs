using NavigationTests;
using Sandbox.Npcs;
using Sandbox.Npcs.Layers;
using System;
using System.Collections.Generic;
using System.Linq;
using static NpcTests.PhysicsNpcKit;

namespace NpcTests;

/// <summary>
/// Knockdown: what puts a physics-walked NPC down as a ragdoll of its own model, what doesn't, and getting up after.
/// </summary>
[TestClass]
public class NpcKnockdownTests
{
	// A small humanoid with a ragdoll: pelvis, spine and head up, two legs of two parts down
	static Model Humanoid()
	{
		var builder = Model.Builder;
		builder.AddBone( "pelvis", Vector3.Up * 36, Rotation.Identity );
		builder.AddBone( "spine", Vector3.Up * 54, Rotation.Identity, "pelvis" );
		builder.AddBone( "head", Vector3.Up * 68, Rotation.Identity, "spine" );
		builder.AddBone( "thigh_L", new Vector3( 0, 6, 36 ), Rotation.Identity, "pelvis" );
		builder.AddBone( "shin_L", new Vector3( 0, 6, 18 ), Rotation.Identity, "thigh_L" );
		builder.AddBone( "thigh_R", new Vector3( 0, -6, 36 ), Rotation.Identity, "pelvis" );
		builder.AddBone( "shin_R", new Vector3( 0, -6, 18 ), Rotation.Identity, "thigh_R" );

		var binds = new List<Vector3>();
		void Part( string bone, Vector3 at, float mass, float length, float radius )
		{
			binds.Add( at );
			builder.AddBody( mass, boneName: bone ).SetBindPose( new Transform( at ) )
				.AddCapsule( new Capsule( Vector3.Zero, Vector3.Up * length, radius ) );
		}

		Part( "pelvis", Vector3.Up * 36, 15, 14, 8 );
		Part( "spine", Vector3.Up * 54, 20, 10, 9 );
		Part( "head", Vector3.Up * 68, 5, 4, 5 );
		Part( "thigh_L", new Vector3( 0, 6, 18 ), 8, 16, 4 );
		Part( "shin_L", new Vector3( 0, 6, 2 ), 5, 14, 3 );
		Part( "thigh_R", new Vector3( 0, -6, 18 ), 8, 16, 4 );
		Part( "shin_R", new Vector3( 0, -6, 2 ), 5, 14, 3 );

		void Joint( int a, int b, Vector3 at )
		{
			var frame1 = new Transform( at - binds[a] );
			var frame2 = new Transform( at - binds[b] );
			builder.AddBallJoint( a, b, frame1, frame2 ).WithSwingLimit( 60 );
		}

		Joint( 0, 1, Vector3.Up * 52 );
		Joint( 1, 2, Vector3.Up * 66 );
		Joint( 0, 3, new Vector3( 0, 6, 36 ) );
		Joint( 3, 4, new Vector3( 0, 6, 18 ) );
		Joint( 0, 5, new Vector3( 0, -6, 36 ) );
		Joint( 5, 6, new Vector3( 0, -6, 18 ) );
		return builder.Create();
	}

	static NpcTestKit.TestNpc Npc( Scene scene, Vector3 position ) => PhysicsNpc( scene, position, model: Humanoid() );
	static NpcTestKit.TestNpc Standing( Scene scene, Vector3 position ) => PhysicsNpcKit.Standing( scene, position, Humanoid() );

	// Something heavy moving at a set speed, not pushed back by what it hits
	static Rigidbody Hitter( Scene scene, Vector3 position, Vector3 size, float mass )
	{
		var go = scene.CreateObject();
		go.WorldPosition = position;
		go.Components.Create<BoxCollider>().Scale = size;
		var body = go.Components.Create<Rigidbody>();
		body.MassOverride = mass;
		body.Gravity = false;
		body.EnableImpactDamage = false;
		body.Locking = new PhysicsLock { Pitch = true, Yaw = true, Roll = true, Z = true };
		return body;
	}

	static bool Down( BaseNpc npc ) => npc.KnockdownState == BaseNpc.KnockdownStates.Down;
	static bool Up( BaseNpc npc ) => npc.KnockdownState == BaseNpc.KnockdownStates.None;
	static IEnumerable<Rigidbody> Ragdoll( BaseNpc npc ) => npc.Renderer.GetComponent<ModelPhysics>().Bodies.Select( x => x.Component );
	static Rigidbody Pelvis( BaseNpc npc ) => npc.Renderer.GetComponent<ModelPhysics>().Bodies.MinBy( x => x.Bone ).Component;

	static void KnockedOver( Scene scene, BaseNpc npc, Vector3 velocity )
	{
		Body( npc ).Velocity = velocity;
		Run( scene, () => Down( npc ), "knocked down", 10 );
	}

	static void GetsUp( Scene scene, BaseNpc npc, string what ) => Run( scene, () => Up( npc ), what, 900 );

	// Ticks while it stays up, or down, failing if it doesn't
	static void Stays( Scene scene, BaseNpc npc, int ticks, string what, Action each = null, bool down = false )
	{
		for ( int i = 0; i < ticks; i++ )
		{
			each?.Invoke();
			scene.GameTick( Tick );
			Assert.IsTrue( down ? Down( npc ) : Up( npc ), what );
		}
	}

	[TestMethod]
	public async Task AHeavyFastHitPutsItDownAndItGetsUp()
	{
		var scene = await Ground();
		using var scope = scene.Push();
		var npc = Standing( scene, Vector3.Zero );

		var car = Hitter( scene, new Vector3( -120, 0, 30 ), new Vector3( 80, 60, 40 ), 1000 );
		Run( scene, () => Down( npc ), "knocked down", 120, () => car.Velocity = new Vector3( 500, 0, 0 ) );
		car.GameObject.Destroy();

		Assert.IsTrue( npc.Renderer.GetComponent<ModelPhysics>() is { Bodies.Count: > 1 }, "Ragdolls its own model" );
		GetsUp( scene, npc, "got up" );
		Run( scene, () => npc.IsOnGround, "landed after getting up" );

		Assert.IsNull( npc.Renderer.GetComponent<ModelPhysics>(), "The ragdoll goes" );
		Assert.AreEqual( 100f, Body( npc ).PhysicsBody.Mass, 0.01f, "The body's own mass back" );
		Assert.IsTrue( Body( npc ).PhysicsBody.Locking.Pitch && Body( npc ).PhysicsBody.Locking.Roll, "Upright locks back" );
		Assert.IsTrue( npc.Health > 0 );
	}

	[TestMethod]
	public async Task LightOrSlowThingsDoNotPutItDown()
	{
		var scene = await Ground();
		using var scope = scene.Push();
		var npc = Standing( scene, Vector3.Zero );

		// A crate thrown lightly
		var crate = Hitter( scene, new Vector3( -60, 0, 40 ), new Vector3( 20 ), 30 );
		Stays( scene, npc, 60, "a light crate", () => { if ( crate.IsValid() ) crate.Velocity = new Vector3( 250, 0, 0 ); } );
		crate.GameObject.Destroy();

		// A heavy thing rolling up slowly
		var cart = Hitter( scene, new Vector3( -100, 0, 30 ), new Vector3( 60, 60, 40 ), 1000 );
		Stays( scene, npc, 30, "a slow heavy thing", () => cart.Velocity = new Vector3( 60, 0, 0 ) );
	}

	[TestMethod]
	public async Task AHeavyThingShovingItAlongPutsItDown()
	{
		var scene = await Ground();
		using var scope = scene.Push();
		var npc = Standing( scene, Vector3.Zero );

		var cart = Hitter( scene, new Vector3( -70, 0, 30 ), new Vector3( 60, 60, 40 ), 1000 );
		Run( scene, () => Down( npc ), "shoved over", 200, () => cart.Velocity = new Vector3( 200, 0, 0 ) );
	}

	[TestMethod]
	public async Task WalkingStoppingAndBumpingIntoAnotherDoNot()
	{
		var scene = await Ground();
		using var scope = scene.Push();
		var a = Standing( scene, new Vector3( -300, 0, 0 ) );
		var b = Standing( scene, new Vector3( 300, 0, 0 ) );

		Navigation( a ).MoveTo( new Vector3( 300, 0, 0 ), 10, 300 );
		Navigation( b ).MoveTo( new Vector3( -300, 0, 0 ), 10, 300 );
		Ticks( scene, 300, () => Assert.IsTrue( Up( a ) && Up( b ), "running through each other" ) );
	}


	[TestMethod]
	public async Task RidingAMovingPlatformDoesNot()
	{
		var scene = await Ground();
		using var scope = scene.Push();

		// A heavy platform carrying it along, moved by physics and by its transform
		var platform = Hitter( scene, new Vector3( 0, 0, 10 ), new Vector3( 200, 200, 20 ), 5000 );
		var npc = Standing( scene, new Vector3( 60, 0, 22 ) );
		Stays( scene, npc, 150, "riding a platform", () => platform.Velocity = new Vector3( 200, 0, 0 ) );
	}

	[TestMethod]
	public async Task ItLandsAFewMetresOnItsFeetAndGoesOverFromHigh()
	{
		var scene = await Ground();
		using var scope = scene.Push();

		var low = Standing( scene, new Vector3( -200, 0, 160 ) );
		Stays( scene, low, 20, "a 4 m fall" );

		var high = Npc( scene, new Vector3( 200, 0, 360 ) );
		Run( scene, () => Down( high ), "knocked down by a 9 m fall", 100 );
		Assert.IsTrue( high.Health > 0 );
	}

	[TestMethod]
	public async Task ADropLinkNeverPutsItDown()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		scene.Floor( Vector3.Zero, 2400 );
		scene.Box( new Vector3( -160, 0, 200 ), new Vector3( 320, 320, 400 ) );
		var link = scene.Link<NavMeshLink>( new Vector3( -40, 0, 400 ), new Vector3( 64, 0, 0 ) );
		link.Type = NavigationTestScene.ShippedLinkType( "drop" );
		link.ConnectionRadius = 64;
		link.IsBiDirectional = false;
		Assert.IsTrue( await scene.NavMesh.Generate( scene.PhysicsWorld ) );

		var npc = Standing( scene, new Vector3( -180, 0, 400 ) );
		Navigation( npc ).MoveTo( new Vector3( 240, 0, 0 ), 16 );
		Run( scene, () => Arrived( npc ), "dropped 10 m and arrived", each: () => Assert.IsFalse( Down( npc ), "Knocked down by the drop" ) );
	}

	[TestMethod]
	public async Task CarriedGentlyItStaysUpFlickedItGoesDownAndGetsUpOnceLetGo()
	{
		var scene = await Ground();
		using var scope = scene.Push();
		var npc = Standing( scene, Vector3.Zero );
		var carrier = scene.CreateObject();
		npc.StartCarry( carrier );

		// Lifted and walked about at a carrying pace, then flicked
		Stays( scene, npc, 60, "carried gently", () => Body( npc ).Velocity = new Vector3( 150, 0, 60 ) );
		KnockedOver( scene, npc, new Vector3( 1500, 0, 0 ) );

		Stays( scene, npc, 150, "Stays down while held still", () => { foreach ( var part in Ragdoll( npc ) ) part.Velocity = default; }, down: true );
		npc.StopCarry( carrier );
		GetsUp( scene, npc, "got up once let go" );
	}

	[TestMethod]
	public async Task ItCarriesOnWithItsMoveAfterGettingUp()
	{
		var scene = await Ground();
		using var scope = scene.Push();
		var npc = Standing( scene, new Vector3( -400, 0, 0 ) );
		Navigation( npc ).MoveTo( new Vector3( 600, 0, 0 ), 16, 150 );
		Run( scene, () => npc.WorldPosition.x > -300, "set off" );

		KnockedOver( scene, npc, new Vector3( 0, 900, 300 ) );
		GetsUp( scene, npc, "got up" );
		Run( scene, () => Arrived( npc ), "arrived", 1200 );
	}

	[TestMethod]
	public async Task DyingWhileDownLeavesACorpse()
	{
		var scene = await Ground();
		using var scope = scene.Push();
		var npc = Standing( scene, Vector3.Zero );
		KnockedOver( scene, npc, new Vector3( 900, 0, 300 ) );
		Ticks( scene, 20 );

		var calls = npc.RagdollCalls;
		npc.Health = 0;
		scene.GameTick( Tick );
		Assert.IsFalse( npc.IsValid(), "Destroyed" );
		Assert.AreEqual( calls + 1, npc.RagdollCalls, "Asked for a corpse" );
	}

	[TestMethod]
	public async Task WithNoRoomToStandItStaysDownUntilThereIs()
	{
		var scene = await Ground();
		using var scope = scene.Push();
		var npc = Standing( scene, Vector3.Zero );

		KnockedOver( scene, npc, new Vector3( 300, 0, 450 ) );
		Run( scene, () => Pelvis( npc ).WorldPosition.z < 20f, "fell", 150 );

		var at = Pelvis( npc ).WorldPosition;
		var ceiling = scene.Box( new Vector3( at.x, at.y, 45 ), new Vector3( 1200, 1200, 10 ) );
		Stays( scene, npc, 450, "Stays down under a low ceiling", down: true );

		scene.Remove( ceiling );
		GetsUp( scene, npc, "got up once there was room" );
	}

	[TestMethod]
	public async Task ItDoesNotGetUpOnTheFarSideOfAWall()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		scene.Floor( Vector3.Zero, 2400 );

		// A strip too narrow for the navmesh, the near side of it a thin wall with navmesh close behind
		var wall = scene.Box( new Vector3( 2, 0, 100 ), new Vector3( 4, 2400, 200 ) );
		scene.Box( new Vector3( -45, 0, 100 ), new Vector3( 30, 2400, 200 ) );
		Assert.IsTrue( await scene.NavMesh.Generate( scene.PhysicsWorld ) );

		var npc = Standing( scene, new Vector3( -15, 0, 0 ) );
		KnockedOver( scene, npc, new Vector3( 0, 300, 450 ) );
		Stays( scene, npc, 150, "Stays down rather than get up through the wall", down: true );

		scene.Remove( wall );
		GetsUp( scene, npc, "got up once the wall was gone" );
		Assert.IsTrue( npc.WorldPosition.x > 4f, "Up where the wall was in the way" );
	}

	[TestMethod]
	public async Task ARagdollSpinningOrHangingStillInTheAirIsNotAtRest()
	{
		var scene = await Ground();
		using var scope = scene.Push();
		var npc = Standing( scene, Vector3.Zero );
		KnockedOver( scene, npc, new Vector3( 900, 0, 300 ) );
		Ticks( scene, 60 );

		Stays( scene, npc, 150, "Stays down while spinning", () => Pelvis( npc ).AngularVelocity = new Vector3( 0, 0, 20 ), down: true );

		foreach ( var part in Ragdoll( npc ) ) part.Velocity = Vector3.Up * 600;
		Run( scene, () => Pelvis( npc ).WorldPosition.z > 120f, "flew up", 60 );
		Stays( scene, npc, 150, "Stays down held still in the air", () =>
		{
			Body( npc ).Velocity = default;
			foreach ( var part in Ragdoll( npc ) )
				part.Velocity = part.AngularVelocity = default;
		}, down: true );

		GetsUp( scene, npc, "got up once it landed and stopped" );
	}
}
