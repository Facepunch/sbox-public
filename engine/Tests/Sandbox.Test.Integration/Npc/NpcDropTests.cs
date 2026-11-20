using NavigationTests;
using Sandbox.Engine.Resources;
using Sandbox.Npcs;
using Sandbox.Npcs.Layers;
using System;

namespace NpcTests;

[TestClass]
public class NpcDropTests
{
	[TestMethod]
	[DataRow( 96f, 16f, false, false, false )]
	[DataRow( 256f, 16f, false, false, false )]
	[DataRow( 256f, 32f, false, false, false )]
	[DataRow( 256f, 16f, true, false, false )]
	[DataRow( 256f, 16f, false, true, false )]
	[DataRow( 256f, 16f, false, false, true )]
	public async Task ADropClearsTheLedgeBeforeDescending( float height, float radius, bool generated, bool rim, bool overhang )
	{
		var type = NavigationTestScene.ShippedLinkType( "drop" );
		var scene = new Scene();
		using var scope = scene.Push();
		scene.Floor( Vector3.Zero, 1200 );
		scene.Box( new Vector3( -160, 0, height / 2 ), new Vector3( 320, 320, height ) );
		if ( rim ) scene.Box( new Vector3( -4, 0, height + 4 ), new Vector3( 8, 320, 8 ) );
		if ( overhang ) scene.Box( new Vector3( -152, 0, height - 8 ), new Vector3( 336, 320, 16 ) );
		var link = scene.Link<NavMeshLink>( new Vector3( -40, 0, height ), new Vector3( overhang ? 24 : 64, 0, 0 ) );
		link.Type = type;
		link.ConnectionRadius = 64;
		link.IsBiDirectional = false;
		if ( generated )
		{
			scene.Remove( link.GameObject );
			scene.NavMesh.GenerateLinks = true;
			scene.NavMesh.AgentMaxDrop = height + 32;
		}
		Assert.IsTrue( await scene.NavMesh.Generate( scene.PhysicsWorld ) );
		var go = scene.CreateObject();
		go.WorldPosition = new Vector3( -180, 0, height );
		go.Components.Create<NavMeshAgent>().Radius = radius;
		var animation = go.Components.Create<AnimationLayer>();
		var navigation = go.Components.Create<NavigationLayer>();
		var npc = go.Components.Create<NpcTestKit.TestNpc>();
		npc.Animation ??= animation;
		scene.GameTick( 0.02f );
		navigation.MoveTo( new Vector3( 240, 0, 0 ), 4, 100 );
		var crossed = false;
		var overlap = 0f;
		var approachFrames = 0;
		var fallingFrames = 0;
		var walked = false;
		var fell = false;
		var previous = npc.WorldPosition;
		for ( var i = 0; i < 1000; i++ )
		{
			scene.GameTick( 0.02f );
			crossed |= navigation.IsTraversingLink;
			var p = npc.WorldPosition;
			var delta = p - previous;
			previous = p;
			approachFrames = navigation.IsTraversingLink && MathF.Abs( delta.z ) < 0.001f && delta.WithZ( 0 ).Length > 0.1f ? approachFrames + 1 : 0;
			fallingFrames = navigation.IsTraversingLink && delta.z < -0.1f ? fallingFrames + 1 : 0;
			// Allow two frames for the pose to settle at each phase boundary.
			if ( approachFrames > 2 )
			{
				walked = true;
				Assert.IsTrue( npc.Animation.IsGrounded, "Walking approach" );
				Assert.IsNull( npc.Animation.Traversal );
			}
			if ( fallingFrames > 2 )
			{
				fell = true;
				Assert.IsFalse( npc.Animation.IsGrounded, "Falling phase" );
				Assert.AreEqual( type, npc.Animation.Traversal );
			}
			if ( p.z < height && p.z + 72 > 0 && MathF.Abs( p.y ) < 160 + radius )
				overlap = MathF.Max( overlap, MathF.Min( radius - p.x, height - p.z ) );
			if ( rim && p.x + radius > -8 && p.x - radius < 0 && p.z < height + 8 && p.z + 72 > height )
				overlap = MathF.Max( overlap, MathF.Min( MathF.Min( p.x + radius + 8, radius - p.x ), height + 8 - p.z ) );
			if ( overhang && p.x - radius < 16 && p.z < height && p.z + 72 > height - 16 )
				overlap = MathF.Max( overlap, MathF.Min( radius + 16 - p.x, height - p.z ) );
			if ( crossed && !navigation.IsTraversingLink && p.x > 150 ) break;
		}
		Assert.IsTrue( crossed, "The NPC entered the drop link" );
		Assert.IsTrue( walked && fell, "Both the walking approach and fall must be exercised" );
		Assert.IsTrue( overlap <= 0.1f, $"The NPC's hull penetrated the ledge by {overlap:0.00} units" );
		Assert.IsTrue( npc.WorldPosition.x > 150 && npc.WorldPosition.z < 8, $"NPC stopped at {npc.WorldPosition}" );
	}

	[TestMethod]
	public async Task AnObstructedDropStopsInsteadOfMovingThroughTheObstacle()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		scene.Floor( Vector3.Zero, 1200 );
		scene.Box( new Vector3( -160, 0, 128 ), new Vector3( 320, 320, 256 ) );
		var link = scene.Link<NavMeshLink>( new Vector3( -40, 0, 256 ), new Vector3( 64, 0, 0 ) );
		link.Type = NavigationTestScene.ShippedLinkType( "drop" );
		link.ConnectionRadius = 64;
		link.IsBiDirectional = false;
		Assert.IsTrue( await scene.NavMesh.Generate( scene.PhysicsWorld ) );
		var go = scene.CreateObject();
		go.WorldPosition = new Vector3( -180, 0, 256 );
		go.Components.Create<NavMeshAgent>();
		var animation = go.Components.Create<AnimationLayer>();
		var navigation = go.Components.Create<NavigationLayer>();
		var npc = go.Components.Create<NpcTestKit.TestNpc>();
		npc.Animation ??= animation;
		scene.GameTick( 0.02f );
		navigation.MoveTo( new Vector3( 240, 0, 0 ), 4, 100 );
		var obstructed = false;
		for ( var i = 0; i < 1000; i++ )
		{
			scene.GameTick( 0.02f );
			// Add a solid shelf after departure: the link was valid when it was entered.
			if ( !obstructed && navigation.IsTraversingLink && npc.WorldPosition.z < 230 )
			{
				scene.Box( new Vector3( 80, 0, 112 ), new Vector3( 160, 320, 32 ) );
				obstructed = true;
			}
			if ( obstructed && !navigation.IsTraversingLink ) break;
		}
		Assert.IsTrue( obstructed );
		Assert.IsFalse( navigation.IsTraversingLink );
		Assert.AreEqual( MovementLayer.MovementStatus.Failed, navigation.Status );
		Assert.IsTrue( npc.WorldPosition.z >= 127, $"The feet must stop at the shelf, allowing the one-unit trace skin: {npc.WorldPosition}" );
	}
}
