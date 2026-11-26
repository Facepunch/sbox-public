using NavigationTests;
using Sandbox.Npcs.Layers;

namespace NpcTests;

[TestClass]
public class NpcGroundingTests
{
	[DataTestMethod]
	[DataRow( true, 0f )]
	[DataRow( false, 8f )]
	public async Task NavigationGroundIgnoresOtherNpcsOnlyForNpcs( bool npc, float expectedHeight )
	{
		var scene = new Scene();
		using var scope = scene.Push();
		scene.Floor( Vector3.Zero );
		Assert.IsTrue( await scene.NavMesh.Generate( scene.PhysicsWorld ) );
		var other = scene.Box( new Vector3( 0, 0, 4 ), new Vector3( 100, 100, 8 ) );
		other.Tags.Add( "npc" );
		other.Components.Create<Rigidbody>().MotionEnabled = false;
		var agent = scene.Agent();
		if ( npc ) agent.Tags.Add( "npc" );
		for ( int i = 0; i < 10; i++ ) scene.GameTick( 0.02f );

		Assert.IsNotNull( agent.agentInternal );
		Assert.AreEqual( expectedHeight, agent.groundTraceZ, 0.1f );
	}

	[DataTestMethod]
	[DataRow( true )]
	[DataRow( false )]
	public void NpcGroundProbeIgnoresNpcsButKeepsPlatforms( bool otherIsNpc )
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var floor = scene.Floor( Vector3.Zero );
		var platform = scene.Box( new Vector3( 0, 0, 4 ), new Vector3( 100, 100, 8 ) );
		if ( otherIsNpc ) platform.Tags.Add( "npc" );
		platform.Components.Create<Rigidbody>().MotionEnabled = false;
		var go = scene.CreateObject();
		go.Components.Create<NavMeshAgent>();
		go.Components.Create<NavigationLayer>();
		var animation = go.Components.Create<AnimationLayer>();
		go.Components.Create<Rigidbody>().MotionEnabled = false;
		var npc = go.Components.Create<NpcTestKit.TestNpc>();
		npc.Animation ??= animation;
		for ( int i = 0; i < 5; i++ ) scene.GameTick( 0.02f );

		Assert.AreSame( otherIsNpc ? floor : platform, npc.GroundObject );
	}

	[DataTestMethod]
	[DataRow( true )]
	[DataRow( false )]
	public void RestingContactDoesNotGroundOnAnotherNpc( bool otherIsNpc )
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var other = scene.Box( new Vector3( 100, 0, 0 ), new Vector3( 10 ) );
		if ( otherIsNpc ) other.Tags.Add( "npc" );
		var otherBody = other.Components.Create<Rigidbody>();
		otherBody.MotionEnabled = false;
		var go = scene.CreateObject();
		go.Components.Create<NavMeshAgent>();
		go.Components.Create<NavigationLayer>();
		var animation = go.Components.Create<AnimationLayer>();
		go.Components.Create<Rigidbody>().Gravity = false;
		var npc = go.Components.Create<NpcTestKit.TestNpc>();
		npc.Animation ??= animation;
		scene.GameTick( 0.02f );
		var contact = new CollisionSource( new PhysicsContact.Target( otherBody.PhysicsBody, null, null ) );
		((Component.ICollisionListener)npc).OnCollisionStart( new Collision( default, contact, default ) );
		for ( int i = 0; i < 40 && !npc.IsOnGround; i++ ) scene.GameTick( 0.02f );

		Assert.AreSame( otherIsNpc ? null : other, npc.GroundObject );
	}
}
