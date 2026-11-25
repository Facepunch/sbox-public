using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Nav.Lib.Tests.Sim;

namespace Nav.Lib.Tests;

/// <summary>
/// Link type rules on a crowd agent: changing them through SetParams plans the route again.
/// The mesh is the ring: a block in the middle with a way round on either side, and a link of type
/// Jump straight across the block.
/// </summary>
[TestClass]
public class CrowdLinkTypeTests
{
	const byte Jump = 3;

	static readonly Vector3 Start = At( 100, 320 ), Target = At( 540, 320 );

	static int AddJump( NavMesh mesh )
	{
		int id = mesh.AddLink( new NavLink { Start = At( 190, 320 ), End = At( 450, 320 ), Radius = 12, Bidirectional = true, Area = 1, Type = Jump } );
		mesh.Commit();
		return id;
	}

	static AgentParams Walker( in NavFilter filter )
	{
		var walker = Sim.Walker();
		walker.Filter = filter;
		return walker;
	}

	static bool RouteHasLink( Sim sim, int agent )
	{
		var path = new NavPath();
		Assert.AreEqual( PathStatus.Complete, sim.Crowd.GetPath( agent, path ) );
		return path.Polys.ToArray().Any( p => p.IsLink );
	}

	static void AssertHeadsForTheLink( Sim sim, int agent )
	{
		Assert.IsTrue( RouteHasLink( sim, agent ) );
		Assert.IsTrue( sim[agent].DesiredVelocity.X > 100 && MathF.Abs( sim[agent].DesiredVelocity.Y ) < 10, $"Heading {sim[agent].DesiredVelocity}" );
	}

	static void AssertWalksAround( Sim sim, int agent )
	{
		Assert.AreEqual( AgentStatus.Moving, sim.Status( agent ) );
		Assert.IsFalse( RouteHasLink( sim, agent ) );
		Assert.IsTrue( MathF.Abs( sim[agent].DesiredVelocity.Y ) > 30, $"Heading {sim[agent].DesiredVelocity}" );
	}

	[TestMethod]
	public void ForbiddingALinkTypeReplansOntoTheWalkAroundAndBack()
	{
		var sim = new Sim( new() { Ring = true } );
		int link = AddJump( sim.Mesh );
		int agent = sim.Add( Start, Target );
		sim.Run( 5 );
		AssertHeadsForTheLink( sim, agent );

		sim.Crowd.SetParams( agent, Walker( Filters.All.WithForbiddenLinks( 1ul << Jump ) ) );
		sim.Step();
		AssertWalksAround( sim, agent );

		// Another type forbidden is a changed filter too, and this link is open again.
		sim.Crowd.SetParams( agent, Walker( Filters.All.WithForbiddenLinks( 1ul << Jump + 1 ) ) );
		sim.Step();
		AssertHeadsForTheLink( sim, agent );

		bool crossed = false;
		for ( int i = 0; i < 600 && sim.Status( agent ) != AgentStatus.Arrived; i++ )
		{
			sim.Step();
			crossed |= sim.OnLink( agent ) && sim[agent].Link == link;
		}
		Assert.IsTrue( crossed );
		Assert.AreEqual( AgentStatus.Arrived, sim.Status( agent ) );
	}

	[TestMethod]
	public void LinkPenaltyAndCostChangedAtRuntimeReplan()
	{
		var sim = new Sim( new() { Ring = true } );
		var walker = Walker( Filters.All );
		int agent = sim.Add( Start, Target, walker );
		sim.Run( 5 );
		AssertWalksAround( sim, agent );

		// The link appears. The route the agent has is still good, so nothing plans it again.
		AddJump( sim.Mesh );
		sim.Step();
		AssertWalksAround( sim, agent );

		// The same rules made again are no change: a search would take the link.
		sim.Crowd.SetParams( agent, Walker( NavFilter.Create() ) );
		sim.Step();
		AssertWalksAround( sim, agent );
		sim.Crowd.SetParams( agent, walker );
		sim.Step();
		AssertWalksAround( sim, agent );

		// A small penalty is a change, and no reason to walk round the block.
		sim.Crowd.SetParams( agent, Walker( Filters.All.WithLinkPenalty( Jump, 5 ) ) );
		sim.Step();
		AssertHeadsForTheLink( sim, agent );

		// A large one is.
		walker = Walker( Filters.All.WithLinkPenalty( Jump, 1000 ) );
		sim.Crowd.SetParams( agent, walker );
		sim.Step();
		AssertWalksAround( sim, agent );

		// A filter is a value: a changed one counts once it is handed in.
		walker.Filter = walker.Filter.WithLinkPenalty( Jump, 0 );
		sim.Step();
		AssertWalksAround( sim, agent );
		sim.Crowd.SetParams( agent, walker );
		sim.Step();
		AssertHeadsForTheLink( sim, agent );

		// The multiplier, the same way.
		sim.Crowd.SetParams( agent, Walker( Filters.All.WithLinkCost( Jump, 50 ) ) );
		sim.Step();
		AssertWalksAround( sim, agent );

		sim.Run( 600 );
		Assert.AreEqual( AgentStatus.Arrived, sim.Status( agent ) );
		Assert.IsTrue( Flat( sim.Position( agent ), Target ) < 1 );
	}
}
