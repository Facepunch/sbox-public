using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Nav.Lib.Tests.Sim;

namespace Nav.Lib.Tests;

/// <summary>
/// Steered External agents: their owner moves them along AgentState.SteeredVelocity, which the crowd steers round
/// neighbours and walls and turns at the agent's own acceleration and turn rate, from wherever the owner left it, and
/// by AgentState.Push, separation's push out of overlaps.
/// </summary>
[TestClass]
public class CrowdExternalTests
{
	const float Delta = 0.02f;

	static AgentParams Owned()
	{
		var value = Walker();
		value.Mode = AgentMode.Steered;
		return value;
	}

	// The owner moves the agent a share of its SteeredVelocity over a tick, all of it for an owner that keeps up, and by
	// its push.
	static void Follow( Sim sim, int agent, float share, float tick = Delta ) => sim.Crowd.SetPosition( agent, sim.Position( agent ) + sim[agent].SteeredVelocity * (tick * share) + sim[agent].Push );

	[TestMethod]
	public void AModeThatIsNoAgentModeIsRefused()
	{
		var value = Walker();
		value.Mode = (AgentMode)3;
		Assert.IsFalse( value.IsValid( out var reason ) );
		StringAssert.Contains( reason, "Mode" );
		Assert.IsTrue( Owned().IsValid( out _ ) );
	}

	[TestMethod]
	[DataRow( 1f )]
	[DataRow( 0.6f )]
	public void TwoExternalAgentsPassHeadOnInACorridor( float share )
	{
		var sim = new Sim( CrowdMesh.Corridor() );
		Vector3[] targets = [At( 600, 320 ), At( 40, 320 )];
		int[] agents = [sim.Add( At( 40, 320 ), targets[0], Owned() ), sim.Add( At( 600, 320 ), targets[1], Owned() )];
		float closest = float.MaxValue, jolt = 0, most = 240 * Delta, turn = 120 / (1.5f * 8) * Delta;
		var before = new Vector3[2];
		for ( int step = 0; step < 1000 && agents.Any( a => sim.Status( a ) != AgentStatus.Arrived ); step++ )
		{
			foreach ( int a in agents ) Follow( sim, a, share );
			sim.Step( Delta );
			closest = MathF.Min( closest, Flat( sim.Position( 0 ), sim.Position( 1 ) ) );
			foreach ( int a in agents )
			{
				Assert.IsTrue( MathF.Abs( sim.Position( a ).Y - 320 ) <= 12.01f, $"Agent {a} left the corridor at {sim.Position( a )}" );
				// Away from the end, where it lands instead of stepping past.
				var steered = sim[a].SteeredVelocity;
				float speed = before[a].Length();
				if ( Flat( sim.Position( a ), targets[a] ) > 16 ) jolt = MathF.Max( jolt, Vector3.Distance( steered, before[a] ) - (MathF.Max( speed * turn, most ) + most) );
				before[a] = steered;
			}
		}

		for ( int k = 0; k < 2; k++ )
		{
			Assert.AreEqual( AgentStatus.Arrived, sim.Status( agents[k] ), $"Agent {k} at {sim.Position( agents[k] )}" );
			Assert.IsTrue( Flat( sim.Position( agents[k] ), targets[k] ) <= 2, $"Agent {k} at {sim.Position( agents[k] )}" );
		}
		Assert.IsTrue( closest >= 15, $"They came within {closest:0.0} of each other, radii 8 and 8" );
		Assert.IsTrue( jolt <= 0.01f, $"SteeredVelocity changed {jolt:0.00} more in a step than acceleration and turn rate allow" );
	}

	// A Steered External agent and one the crowd moves meet head-on: both step aside, sharing the dodge, where one
	// the crowd moves would go all the way round an External agent that is not Steered.
	[TestMethod]
	[DataRow( true )]
	[DataRow( false )]
	public void AnExternalAgentAndOneTheCrowdMovesShareTheDodge( bool steered )
	{
		var sim = new Sim( CrowdMesh.Corridor() );
		var owned = Owned();
		owned.Mode = steered ? AgentMode.Steered : AgentMode.Planned;
		Vector3[] targets = [At( 600, 320 ), At( 40, 320 )];
		int external = sim.Add( At( 40, 320 ), targets[0], owned );
		int moved = sim.Add( At( 600, 320 ), targets[1] );
		float closest = float.MaxValue, externalAside = 0, movedAside = 0;
		for ( int step = 0; step < 1000 && (sim.Status( external ) != AgentStatus.Arrived || sim.Status( moved ) != AgentStatus.Arrived); step++ )
		{
			// An owner that is not steered follows DesiredVelocity: it goes straight through.
			if ( steered ) Follow( sim, external, 1 );
			else sim.Crowd.SetPosition( external, sim.Position( external ) + sim[external].DesiredVelocity * Delta );
			sim.Step( Delta );
			closest = MathF.Min( closest, Flat( sim.Position( external ), sim.Position( moved ) ) );
			externalAside = MathF.Max( externalAside, MathF.Abs( sim.Position( external ).Y - 320 ) );
			movedAside = MathF.Max( movedAside, MathF.Abs( sim.Position( moved ).Y - 320 ) );
		}

		Assert.AreEqual( AgentStatus.Arrived, sim.Status( external ), $"External at {sim.Position( external )}" );
		Assert.AreEqual( AgentStatus.Arrived, sim.Status( moved ), $"Moved at {sim.Position( moved )}" );
		if ( steered )
		{
			Assert.IsTrue( closest >= 15, $"They came within {closest:0.0} of each other, radii 8 and 8" );
			Assert.IsTrue( externalAside >= 4 && movedAside >= 4, $"Each steps aside: external {externalAside:0.0}, moved {movedAside:0.0}" );
		}
		else
		{
			Assert.IsTrue( externalAside < 0.01f, "An owner that follows DesiredVelocity is not steered" );
			Assert.IsTrue( movedAside >= 12, $"The agent the crowd moves goes round: {movedAside:0.0}" );
		}
	}

	// Two rows of five cross each other, moved by owners that follow SteeredVelocity, or by the crowd. Separation keeps
	// Steered External agents apart as it keeps those the crowd moves: all arrive, end apart by their two radii, and
	// overlap as little on the way.
	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void TwoRowsOfExternalAgentsCrossAndKeepApart( bool external )
	{
		var sim = new Sim();
		var value = Walker();
		value.Mode = external ? AgentMode.Steered : AgentMode.Moved;
		var agents = new List<int>();
		var targets = new List<Vector3>();
		for ( int k = -2; k <= 2; k++ )
		{
			agents.Add( sim.Add( At( 200, 320 + k * 18 ), At( 440, 320 + k * 18 ), value ) );
			targets.Add( At( 440, 320 + k * 18 ) );
			agents.Add( sim.Add( At( 440, 329 + k * 18 ), At( 200, 329 + k * 18 ), value ) );
			targets.Add( At( 200, 329 + k * 18 ) );
		}
		float deepest = 0;
		for ( int step = 0; step < 1500 && agents.Any( a => sim.Status( a ) != AgentStatus.Arrived ); step++ )
		{
			if ( external ) foreach ( int a in agents ) Follow( sim, a, 1 );
			sim.Step( Delta );
			deepest = MathF.Max( deepest, 16 - Closest() );
		}
		float Closest()
		{
			float closest = float.MaxValue;
			foreach ( int a in agents )
			{
				foreach ( int b in agents )
				{
					if ( a < b ) closest = MathF.Min( closest, Flat( sim.Position( a ), sim.Position( b ) ) );
				}
			}
			return closest;
		}

		for ( int k = 0; k < agents.Count; k++ )
		{
			Assert.AreEqual( AgentStatus.Arrived, sim.Status( agents[k] ), $"Agent {k} at {sim.Position( agents[k] )}" );
			Assert.IsTrue( Flat( sim.Position( agents[k] ), targets[k] ) <= 2.5f, $"Agent {k} at {sim.Position( agents[k] )}" );
		}
		Assert.IsTrue( Closest() >= 15.9f, $"Two ended {Closest():0.00} apart, radii 8 and 8" );
		Assert.IsTrue( deepest <= 6, $"Two overlapped by {deepest:0.00} on the way" );
	}

	// Owners that tick slower than the crowd's substeps, down to 2.5 a second: a Step of several substeps plans the
	// agent from where its owner left it every substep, and its owner moves it once. It still lands on its goal, near
	// and far, without overshooting, stalling or swinging round it.
	[TestMethod]
	[DataRow( 0.1f, 4f, 10000f )]
	[DataRow( 0.15f, 4f, 10000f )]
	[DataRow( 0.2f, 400f, 240f )]
	[DataRow( 0.4f, 400f, 240f )]
	public void ALongStepLandsOnTheGoal( float tick, float distance, float acceleration )
	{
		var sim = new Sim( CrowdMesh.Corridor() );
		var value = Owned();
		value.Acceleration = acceleration;
		var target = At( 100 + distance, 320 );
		int agent = sim.Add( At( 100, 320 ), target, value );
		float furthest = 0;
		int steps = 0;
		for ( ; steps < 200 && sim.Status( agent ) != AgentStatus.Arrived; steps++ )
		{
			sim.Step( tick );
			Follow( sim, agent, 1, tick );
			furthest = MathF.Max( furthest, sim.Position( agent ).X );
		}
		Assert.AreEqual( AgentStatus.Arrived, sim.Status( agent ), $"At {sim.Position( agent )} after {steps} steps" );
		Assert.IsTrue( Flat( sim.Position( agent ), target ) <= 2, $"At {sim.Position( agent )}" );
		Assert.IsTrue( furthest <= target.X + 0.5f, $"It went to x {furthest:0.00}, past the goal at {target.X}" );
		Assert.IsTrue( steps * tick <= distance / 120 + 120f / acceleration + 1, $"It took {steps * tick:0.00} s" );
	}

	// Two Steered External agents overlap by 2 where their owners have them off the walkable edge. The push parts them
	// along the edge and does not pull them back onto the mesh: where the owner stands is the owner's business.
	[TestMethod]
	[DataRow( 0f )]
	[DataRow( 2f )]
	[DataRow( 6f )]
	public void ThePushDoesNotPullAnAgentOffTheMeshBack( float outside )
	{
		var sim = new Sim( CrowdMesh.Corridor() );
		int left = sim.Add( At( 300, 340 + outside ), Owned() );
		int right = sim.Add( At( 314, 340 + outside ), Owned() );
		sim.Step( Delta );
		Assert.IsTrue( sim[left].Push.X < -0.1f && sim[right].Push.X > 0.1f, $"Pushes {sim[left].Push} and {sim[right].Push}" );
		Assert.IsTrue( MathF.Abs( sim[left].Push.Y ) < 0.01f && MathF.Abs( sim[right].Push.Y ) < 0.01f, $"Pushes {sim[left].Push} and {sim[right].Push}" );
		Assert.AreEqual( Vector3.Zero, sim[left].SteeredVelocity, "Separation is not in SteeredVelocity" );
	}

	// A Steered External agent waits on a link its owner crosses, and an agent the crowd moves stands half in it. On
	// the link the waiting agent is not separated, so the other moves out of the whole overlap, as from any External one.
	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void AnAgentOnALinkLeavesTheWholeOverlapToTheOther( bool steered )
	{
		var sim = new Sim( new() { UpperFloor = true, Link = true } );
		var value = Walker( autoLinks: false );
		value.Mode = steered ? AgentMode.Steered : AgentMode.Planned;
		int waiting = sim.Add( At( 320, 320 ), At( 400, 320, 201 ), value );
		for ( int i = 0; i < 10 && !sim.OnLink( waiting ); i++ ) sim.Step( Delta );
		Assert.IsTrue( sim.OnLink( waiting ) );
		int other = sim.Add( At( 330, 320 ) );
		sim.Step( Delta );
		Assert.IsTrue( Flat( sim.Position( waiting ), sim.Position( other ) ) >= 15.5f, $"{Flat( sim.Position( waiting ), sim.Position( other ) ):0.00} apart, radii 8 and 8" );
		Assert.AreEqual( Vector3.Zero, sim[waiting].Push );
	}

	// An owner's body is held still, then let go, then knocked sideways. Steering runs on from what it last told the
	// agent, so while held it asks for its full speed; the owner reports what happened with SetVelocity, for that Step
	// only, and steering starts again from the stop and from the knock instead of lurching off or ignoring it.
	[TestMethod]
	public void AnOwnerReportsAHoldAndAKnock()
	{
		var sim = new Sim( CrowdMesh.Corridor() );
		int agent = sim.Add( At( 40, 320 ), At( 600, 320 ), Owned() );
		sim.Run( 30, Delta );
		Assert.IsTrue( sim[agent].SteeredVelocity.Length() > 100, $"Held, it still asks for {sim[agent].SteeredVelocity}" );

		sim.Crowd.SetVelocity( agent, Vector3.Zero );
		sim.Step( Delta );
		Assert.IsTrue( sim[agent].SteeredVelocity.Length() <= 240 * Delta + 0.01f, $"Let go, it starts from the stop: {sim[agent].SteeredVelocity}" );

		for ( int i = 0; i < 40; i++ )
		{
			Follow( sim, agent, 1 );
			sim.Step( Delta );
		}
		sim.Crowd.SetVelocity( agent, new Vector3( 0, 150, 0 ) );
		sim.Step( Delta );
		Assert.IsTrue( sim[agent].SteeredVelocity.Y > 100, $"Knocked, it turns from the knock: {sim[agent].SteeredVelocity}" );
		Assert.AreEqual( new Vector3( 0, 150, 0 ), sim[agent].Velocity );
		Follow( sim, agent, 1 );
		sim.Step( Delta );
		Assert.IsTrue( sim[agent].Velocity.Y < 150, "The next Step its positions tell its velocity again" );
	}
}
