using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Nav.Lib.Tests.Sim;

namespace Nav.Lib.Tests;

/// <summary>
/// What a NavMeshAgent needs from the crowd: targets, routes, links, teleports, agents moved by the
/// game (External), a changed mesh (SetMesh), statuses, and one link transition per step.
/// </summary>
[TestClass]
public class CrowdAgentTests
{
	static readonly Vector3 Snap = new( 16.8f, 16.8f, 48.32f );

	// Counts link entries and exits the way the engine does: by comparing States between steps.
	sealed class LinkWatch
	{
		public int Entries, Exits;
		public readonly List<int> Entered = new();
		AgentStatus status;
		int link = -1;

		public void See( AgentState state )
		{
			bool was = status == AgentStatus.OnLink, now = state.Status == AgentStatus.OnLink;
			if ( was && (!now || state.Link != link) ) Exits++;
			if ( now && (!was || state.Link != link) )
			{
				Entries++;
				Entered.Add( state.Link );
			}
			status = state.Status;
			link = state.Link;
		}
	}

	[TestMethod]
	[DataRow( 80 )]
	[DataRow( -80 )]
	public void CustomLinkCompletionKeepsTheSuppliedAgentPosition( int offset )
	{
		var sim = new Sim( new() { UpperFloor = true, Link = true } );
		var target = At( offset > 0 ? 550 : 90, 320, 201 );
		int agent = sim.Add( At( 250, 320 ), target, Walker( autoLinks: false ) );
		var watch = new LinkWatch();
		for ( int i = 0; i < 100 && !sim.OnLink( agent ); i++ )
		{
			sim.Step();
			watch.See( sim[agent] );
		}
		Assert.IsTrue( sim.OnLink( agent ) );
		Assert.AreEqual( 0, sim[agent].Link );
		Assert.AreEqual( At( 320, 320 ), sim[agent].LinkStart );
		Assert.AreEqual( At( 320, 320, 201 ), sim[agent].LinkEnd );
		Assert.IsTrue( Flat( sim[agent].LinkEntry, sim[agent].LinkStart ) <= 8 );

		var landing = sim[agent].LinkEnd + new Vector3( offset, 0, 0 );
		sim.Crowd.SetPosition( agent, landing );
		sim.Crowd.CompleteLink( agent );
		Assert.AreEqual( landing, sim.Position( agent ) );
		sim.Step();
		watch.See( sim[agent] );
		Assert.IsTrue( (sim.Position( agent ).X - landing.X) * offset >= 0, "Resuming walking must not pull the agent back to the endpoint" );
		Assert.IsFalse( sim.OnLink( agent ) );
		for ( int i = 0; i < 200; i++ )
		{
			sim.Step();
			watch.See( sim[agent] );
			Assert.IsTrue( (sim.Position( agent ).X - landing.X) * offset >= 0 );
		}
		Assert.IsTrue( Vector3.Distance( sim.Position( agent ), target ) < 1 );
		Assert.AreEqual( 1, watch.Entries );
		Assert.AreEqual( 1, watch.Exits );
	}

	[TestMethod]
	public void PendingRetargetKeepsTheActivePathSnapshotConsistent()
	{
		var sim = new Sim();
		var firstTarget = At( 300, 320 );
		int agent = sim.Add( At( 100, 320 ), firstTarget );
		sim.Step();
		var active = new NavPath();
		Assert.AreEqual( PathStatus.Complete, sim.Crowd.GetPath( agent, active ) );

		sim.Crowd.MoveTo( agent, At( 200, 420 ) );
		var pending = new NavPath();
		Assert.AreEqual( PathStatus.Complete, sim.Crowd.GetPath( agent, pending ) );
		Assert.AreEqual( At( 200, 420 ), sim[agent].Target );
		Assert.AreEqual( firstTarget, sim[agent].RouteTarget, "The route is still the one planned for the first target" );
		Assert.AreEqual( active.Corners[^1], pending.Corners[^1] );
		Assert.AreEqual( active.End, pending.End );

		Assert.IsTrue( sim.Crowd.SetPath( agent, pending.Polys, pending.End, sim[agent].RouteTarget, false ) );
		Assert.AreEqual( firstTarget, sim[agent].Target );
		sim.Run( 200 );
		Assert.IsTrue( Vector3.Distance( sim.Position( agent ), firstTarget ) < 1, "An assigned path cancels the pending retarget" );
	}

	[TestMethod]
	public void AssigningAPartialPathPreservesItsRequestedTarget()
	{
		var sim = new Sim( new() { UpperFloor = true } );
		var target = At( 400, 320, 201 );
		int agent = sim.Add( At( 100, 320 ) );
		var path = new NavPath();
		Assert.AreEqual( PathStatus.Partial, sim.Query.FindPath( At( 100, 320 ), target, Snap, default, path ) );
		Assert.IsTrue( sim.Crowd.SetPath( agent, path.Polys, path.End, target, true ) );
		Assert.AreEqual( target, sim[agent].Target );
		Assert.AreEqual( AgentStatus.Moving, sim.Status( agent ) );

		var own = new NavPath();
		Assert.AreEqual( PathStatus.Partial, sim.Crowd.GetPath( agent, own ) );
		Assert.IsTrue( sim.Crowd.SetPath( agent, own.Polys, own.End, sim[agent].RouteTarget, true ) );
		Assert.AreEqual( target, sim[agent].Target );

		sim.Run( 300 );
		Assert.AreEqual( AgentStatus.Blocked, sim.Status( agent ), "The end of a partial route is not an arrival" );
		Assert.AreEqual( target, sim[agent].Target );
		Assert.IsTrue( Flat( sim.Position( agent ), target ) < 1 );
		Assert.AreEqual( 1f, sim.Position( agent ).Z );
	}

	[TestMethod]
	public void SetPathJoinsACorridorThatStartsElsewhereAndRejectsABadOne()
	{
		var sim = new Sim( new() { Obstacles = true } );
		var target = At( 600, 320 );
		var path = new NavPath();
		Assert.AreEqual( PathStatus.Complete, sim.Query.FindPath( At( 100, 320 ), target, Snap, default, path ) );

		// A1: the agent stands further along the corridor, then in a polygon next to it.
		int ahead = sim.Add( At( 250, 320 ) );
		Assert.IsTrue( sim.Crowd.SetPath( ahead, path.Polys, path.End, target, false ) );
		Assert.AreEqual( sim.PolyAt( At( 250, 320 ) ), sim[ahead].Poly );
		int beside = sim.Add( At( 30, 40 ) );
		Assert.IsTrue( sim.Crowd.SetPath( beside, path.Polys, path.End, target, false ) );
		sim.Run( 600 );
		Assert.IsTrue( Vector3.Distance( sim.Position( ahead ), target ) < 20 );
		Assert.IsTrue( Vector3.Distance( sim.Position( beside ), target ) < 20 );

		int other = sim.Add( At( 100, 320 ), At( 100, 600 ) );
		sim.Step();
		Assert.IsFalse( sim.Crowd.SetPath( other, default, target, target, false ) );
		Assert.IsFalse( sim.Crowd.SetPath( other, [default], target, target, false ), "A stale entry" );
		var forbidden = Walker();
		forbidden.Filter = new NavFilter( 1ul << 1 );
		int barred = sim.Add( At( 100, 320 ), forbidden );
		Assert.IsFalse( sim.Crowd.SetPath( barred, path.Polys, path.End, target, false ), "An area the agent may not cross" );
		Assert.AreEqual( AgentStatus.Moving, sim.Status( other ), "A rejected path changes nothing" );
		Assert.AreEqual( At( 100, 600 ), sim[other].Target );
	}

	[TestMethod]
	public void CalculatedPathUsesMeshPlacementRadius_10594()
	{
		var sim = new Sim();
		var small = Walker();
		small.Radius = 6;
		// 14 outside the mesh: within 2.1 mesh radii, not within 2.1 of the agent's own.
		int agent = sim.Add( At( -14, 320 ), small );
		sim.Step();
		Assert.IsFalse( sim[agent].Poly.IsNone, "Placement uses the larger of the mesh's and the agent's size" );
		Assert.AreEqual( 0f, sim.Position( agent ).X );

		var path = new NavPath();
		Assert.AreEqual( PathStatus.Complete, sim.Query.FindPath( At( -14, 320 ), At( 320, 320 ), Snap, default, path ) );
		Assert.IsTrue( sim.Crowd.SetPath( agent, path.Polys, path.End, At( 320, 320 ), false ) );
		Assert.AreEqual( AgentStatus.Moving, sim.Status( agent ) );
		Assert.AreEqual( PathStatus.Complete, sim.Crowd.GetPath( agent, path ) );
	}

	[TestMethod]
	public void CrowdingAgainstAWallNeverPushesAgentsIntoIt()
	{
		var sim = new Sim( new() { Doorway = true } );
		for ( int i = 0; i < 8; i++ ) sim.Add( At( 270 + i % 2 * 20, 380 + i / 2 * 30 ), At( 285, 600 ) );
		// One agent walks the other way so the group is squeezed against the wall at x = 300.
		sim.Add( At( 285, 600 ), At( 285, 380 ) );
		float deepest = float.MinValue;
		for ( int tick = 0; tick < 400; tick++ )
		{
			sim.Step();
			for ( int i = 0; i < 9; i++ ) deepest = MathF.Max( deepest, sim.Position( i ).X );
		}
		Assert.IsTrue( deepest <= 300.001f, $"An agent was pushed {deepest - 300:0.000} units past the edge of the mesh" );
	}

	[TestMethod]
	public void AddRemoveSetParamsAndIdReuse()
	{
		var sim = new Sim();
		Assert.AreEqual( 0, sim.Crowd.States.Length );
		int first = sim.Add( At( 100, 100 ), At( 500, 100 ) );
		int second = sim.Add( At( 100, 200 ) );
		int third = sim.Add( At( 100, 300 ) );
		Assert.AreEqual( (0, 1, 2), (first, second, third) );
		Assert.AreEqual( 3, sim.Crowd.States.Length );

		var value = AgentParams.Create( 32, 80, 240, 480 );
		value.Separation = 0.5f;
		value.AutoLinks = false;
		sim.Crowd.SetParams( second, value );
		var seen = sim.Crowd.Params[second];
		Assert.AreEqual( (32f, 80f, 240f, 480f, 0.5f, false), (seen.Radius, seen.Height, seen.MaxSpeed, seen.Acceleration, seen.Separation, seen.AutoLinks) );

		// Radius and height have a floor of a tenth of eps (cell / 400), the rest is clamped to its range.
		sim.Crowd.SetParams( third, new AgentParams { Radius = -1, Height = 0, MaxSpeed = -5, Acceleration = float.NaN, Separation = 7 } );
		seen = sim.Crowd.Params[third];
		Assert.AreEqual( (0.025f, 0.025f, 0f, 0f, 1f), (seen.Radius, seen.Height, seen.MaxSpeed, seen.Acceleration, seen.Separation) );

		sim.Run( 10 );
		sim.Crowd.Remove( first );
		Assert.AreEqual( AgentStatus.Free, sim.Status( first ) );
		Assert.AreEqual( 3, sim.Crowd.States.Length );
		sim.Run( 10 );
		Assert.AreEqual( AgentStatus.Free, sim.Status( first ) );

		// Commands on a free id do nothing.
		sim.Crowd.MoveTo( first, At( 300, 300 ) );
		sim.Crowd.SetPosition( first, At( 300, 300 ) );
		sim.Crowd.Remove( first );
		Assert.AreEqual( AgentStatus.Free, sim.Status( first ) );
		Assert.AreEqual( 0, sim.Crowd.GetCorners( first ).Length );

		// The lowest free id comes back, with nothing left of its previous owner but a generation that
		// tells the two apart.
		uint generation = sim[first].Generation;
		Assert.AreEqual( 1u, generation );
		int again = sim.Add( At( 400, 400 ) );
		Assert.AreEqual( first, again );
		Assert.AreEqual( generation + 1, sim[again].Generation );
		Assert.AreEqual( 1u, sim[second].Generation );
		Assert.AreEqual( AgentStatus.Idle, sim.Status( again ) );
		Assert.AreEqual( -1, sim[again].Link );
		sim.Run( 10 );
		Assert.AreEqual( At( 400, 400 ), sim.Position( again ) );
		Assert.AreEqual( AgentStatus.Idle, sim.Status( again ) );

		sim.Crowd.Remove( third );
		Assert.AreEqual( 2, sim.Crowd.States.Length, "The views end at the highest id in use" );
		sim.Crowd.Remove( second );
		sim.Crowd.Remove( again );
		Assert.AreEqual( 0, sim.Crowd.States.Length );
		Assert.AreEqual( 0, sim.Add( At( 100, 100 ) ) );
	}

	[TestMethod]
	public void RemovingAnAgentOnALinkLeavesNoLinkOnItsId()
	{
		var sim = new Sim( new() { UpperFloor = true, Link = true } );
		int agent = sim.Add( At( 320, 320 ), At( 500, 320, 201 ), Walker( autoLinks: false ) );
		sim.Step();
		Assert.IsTrue( sim.OnLink( agent ) );
		int keeper = sim.Add( At( 600, 600 ) );
		sim.Crowd.Remove( agent );
		Assert.AreEqual( AgentStatus.Free, sim.Status( agent ) );
		Assert.AreEqual( -1, sim[agent].Link );
		int next = sim.Add( At( 100, 100 ), At( 200, 100 ) );
		Assert.AreEqual( AgentStatus.Idle, sim.Status( keeper ) );
		Assert.AreEqual( agent, next );
		var watch = new LinkWatch();
		for ( int i = 0; i < 100; i++ )
		{
			sim.Step();
			watch.See( sim[next] );
		}
		Assert.AreEqual( 0, watch.Entries + watch.Exits );
		Assert.AreEqual( AgentStatus.Arrived, sim.Status( next ) );
	}

	[TestMethod]
	public void AgentMoveToWalksAcrossFloor()
	{
		var sim = new Sim();
		var target = At( 500, 320 );
		int agent = sim.Add( At( 100, 320 ), target );
		float fastest = 0;
		bool moving = false;
		for ( int i = 0; i < 250 && Vector3.Distance( sim.Position( agent ), target ) >= 25; i++ )
		{
			sim.Step();
			fastest = MathF.Max( fastest, sim[agent].Velocity.Length() );
			moving |= sim.Status( agent ) == AgentStatus.Moving;
			Assert.AreEqual( sim.PolyAt( sim.Position( agent ) ), sim[agent].Poly );
		}
		Assert.IsTrue( moving );
		Assert.AreEqual( 120f, fastest, 0.01f );
		Assert.IsTrue( Vector3.Distance( sim.Position( agent ), target ) < 25 );
		sim.Run( 100 );
		Assert.AreEqual( AgentStatus.Arrived, sim.Status( agent ) );
		Assert.IsTrue( Vector3.Distance( sim.Position( agent ), target ) < 0.3f );
		Assert.AreEqual( Vector3.Zero, sim[agent].Velocity );
		Assert.AreEqual( Vector3.Zero, sim[agent].DesiredVelocity );
	}

	[TestMethod]
	public void AgentPathQueriesAndStop()
	{
		var sim = new Sim();
		Vector3 start = At( 70, 70 ), target = At( 570, 570 );
		int agent = sim.Add( start );
		var path = new NavPath();
		Assert.AreEqual( PathStatus.NotFound, sim.Crowd.GetPath( agent, path ), "No target, no path" );
		Assert.AreEqual( PathStatus.Complete, sim.Query.FindPath( start, target, Snap, default, path ) );

		Assert.IsTrue( sim.Crowd.SetPath( agent, path.Polys, path.End, target, false ) );
		Assert.AreEqual( AgentStatus.Moving, sim.Status( agent ), "SetPath makes the agent navigate at once" );
		Assert.AreEqual( target, sim[agent].Target );

		var own = new NavPath();
		Assert.AreEqual( PathStatus.Complete, sim.Crowd.GetPath( agent, own ) );
		Assert.IsTrue( own.IsValid && own.Corners.Length >= 2 );
		Assert.IsTrue( Vector3.Distance( own.Corners[0], start ) < 1 );
		Assert.IsTrue( Vector3.Distance( own.Corners[^1], target ) < 1 );
		Assert.AreEqual( Vector3.Distance( start, target ), own.Length, 1f );

		sim.Run( 50 );
		Assert.IsTrue( sim.Crowd.GetCorners( agent ).Length >= 2 );
		Assert.AreEqual( sim.Position( agent ).X, sim.Crowd.GetCorners( agent )[0].X, 3f );
		Assert.AreEqual( target, sim.Crowd.GetCorners( agent )[^1] );

		sim.Crowd.Stop( agent );
		Assert.AreEqual( AgentStatus.Idle, sim.Status( agent ) );
		Assert.AreEqual( Vector3.Zero, sim[agent].Velocity );
		Assert.AreEqual( PathStatus.NotFound, sim.Crowd.GetPath( agent, own ) );
		Assert.AreEqual( 0, sim.Crowd.GetCorners( agent ).Length );
		var stopped = sim.Position( agent );
		sim.Run( 20 );
		Assert.AreEqual( stopped, sim.Position( agent ) );
	}

	[TestMethod]
	public void ExternalAgentIsPlannedForButMovedByTheCaller()
	{
		var sim = new Sim( new() { Doorway = true } );
		var external = Walker();
		external.External = true;
		Vector3 start = At( 100, 100 ), target = At( 540, 100 );
		int agent = sim.Add( start, target, external );
		sim.Crowd.SetVelocity( agent, new Vector3( 3, 4, 0 ) );
		sim.Run( 20 );
		Assert.AreEqual( start, sim.Position( agent ), "The crowd never moves an External agent" );
		Assert.AreEqual( new Vector3( 3, 4, 0 ), sim[agent].Velocity, "Its velocity is what the caller says" );
		Assert.AreEqual( AgentStatus.Moving, sim.Status( agent ) );
		Assert.AreEqual( 120f, sim[agent].DesiredVelocity.Length(), 0.01f );
		var corners = sim.Crowd.GetCorners( agent ).ToArray();
		Assert.IsTrue( corners.Length >= 3, "Start, the door, the target" );
		var toCorner = Vector3.Normalize( corners[1] - start );
		Assert.IsTrue( Vector3.Distance( toCorner * 120, sim[agent].DesiredVelocity ) < 0.01f, "DesiredVelocity points at the next corner" );

		// The caller moves it, a little off course and not exactly: it follows the route and arrives.
		for ( int i = 0; i < 600 && sim.Status( agent ) == AgentStatus.Moving; i++ )
		{
			var wobble = new Vector3( 0, MathF.Sin( i * 0.3f ) * 0.4f, 0 );
			sim.Crowd.SetPosition( agent, sim.Position( agent ) + sim[agent].DesiredVelocity * 0.02f + wobble );
			sim.Step();
		}
		Assert.AreEqual( AgentStatus.Arrived, sim.Status( agent ) );
		Assert.IsTrue( Flat( sim.Position( agent ), target ) <= 2, "Arrival of an External agent is a quarter radius" );
		Assert.AreEqual( Vector3.Zero, sim[agent].DesiredVelocity );

		// It keeps its rest point while the caller keeps reporting its position.
		sim.Crowd.SetPosition( agent, sim.Position( agent ) );
		sim.Step();
		Assert.AreEqual( AgentStatus.Arrived, sim.Status( agent ) );
		sim.Crowd.SetPosition( agent, target + new Vector3( 0, 30, 0 ) );
		sim.Step();
		Assert.AreEqual( AgentStatus.Arrived, sim.Status( agent ) );
		Assert.IsTrue( sim[agent].DesiredVelocity.Y < -1, "Moved off its rest point it wants back" );
	}

	[TestMethod]
	public void OthersAvoidAnExternalBodyAndItIsNeverPushed()
	{
		var sim = new Sim();
		var body = Walker();
		body.External = true;
		var place = At( 320, 320 );
		int still = sim.Add( place, body );
		int walker = sim.Add( At( 200, 320 ), At( 440, 320 ) );
		// One that starts inside the body is pushed all the way out, the body not at all.
		int inside = sim.Add( At( 324, 320 ) );
		float closest = float.MaxValue;
		for ( int i = 0; i < 400; i++ )
		{
			sim.Step();
			if ( i > 10 ) closest = MathF.Min( closest, MathF.Min( Flat( sim.Position( walker ), place ), Flat( sim.Position( inside ), place ) ) );
			Assert.AreEqual( place, sim.Position( still ) );
		}
		Assert.AreEqual( AgentStatus.Idle, sim.Status( still ) );
		Assert.IsFalse( sim[still].Poly.IsNone, "A body without a target is still on the mesh" );
		Assert.IsTrue( closest >= 15.5f, $"An agent came within {closest:0.0} of a body of combined radius 16" );
		Assert.IsTrue( Vector3.Distance( sim.Position( walker ), At( 440, 320 ) ) < 1 );
		Assert.IsTrue( Flat( sim.Position( inside ), place ) >= 15.9f );
	}

	[TestMethod]
	[DataRow( true )]
	[DataRow( false )]
	public void ExternalAgentIsTakenAcrossALinkOrWaitsForCompleteLink( bool autoLinks )
	{
		var sim = new Sim( new() { UpperFloor = true, Link = true } );
		var external = Walker( autoLinks );
		external.External = true;
		var target = At( 400, 320, 201 );
		int agent = sim.Add( At( 300, 320 ), target, external );
		void Follow() => sim.Crowd.SetPosition( agent, sim.Position( agent ) + sim[agent].DesiredVelocity * 0.02f );
		for ( int i = 0; i < 100 && !sim.OnLink( agent ); i++ )
		{
			Follow();
			sim.Step();
		}
		Assert.IsTrue( sim.OnLink( agent ) );
		var entry = sim.Position( agent );
		sim.Step();
		Assert.AreEqual( entry, sim.Position( agent ), "The crowd never moves an External agent, on a link either" );

		if ( autoLinks )
		{
			// DesiredVelocity points at the far end, straight up here, and the link ends when the
			// caller has brought the agent within its radius of it.
			Assert.IsTrue( Vector3.Distance( sim[agent].DesiredVelocity, Vector3.Normalize( sim[agent].LinkEnd - entry ) * 120 ) < 0.01f );
			int steps = 0;
			for ( ; steps < 200 && sim.OnLink( agent ); steps++ )
			{
				Follow();
				sim.Step();
			}
			Assert.IsFalse( sim.OnLink( agent ) );
			Assert.IsTrue( Vector3.Distance( sim.Position( agent ), sim[agent].LinkEnd ) <= 8.01f );
			Assert.IsTrue( steps > 70, "It took the caller the time of the climb, the crowd did not end the link early" );
		}
		else
		{
			sim.Run( 50 );
			Assert.IsTrue( sim.OnLink( agent ) );
			Assert.AreEqual( Vector3.Zero, sim[agent].DesiredVelocity );
			sim.Crowd.SetPosition( agent, sim[agent].LinkEnd );
			sim.Crowd.CompleteLink( agent );
		}

		for ( int i = 0; i < 200 && sim.Status( agent ) != AgentStatus.Arrived; i++ )
		{
			Follow();
			sim.Step();
		}
		Assert.AreEqual( AgentStatus.Arrived, sim.Status( agent ) );
		Assert.IsTrue( Flat( sim.Position( agent ), target ) <= 2.5f, "Its height is the caller's business" );
	}

	[TestMethod]
	public void ExternalAgentKeepsExactlyThePositionItWasGiven()
	{
		var sim = new Sim();
		var external = Walker();
		external.External = true;
		var target = At( 500, 320 );
		// On the mesh but not at its height, beside it within the placement box, far beside it, high above it.
		Vector3[] places = [At( 100, 320, 9.25f ), At( -10.5f, 300, 1 ), At( -200, 320, 1 ), At( 300, 300, 400 )];
		bool[] onMesh = [true, true, false, false];
		for ( int i = 0; i < places.Length; i++ ) sim.Add( places[i], target, external );
		sim.Run( 3 );
		for ( int i = 0; i < places.Length; i++ )
		{
			Assert.AreEqual( places[i], sim.Position( i ), $"Agent {i} was moved" );
			Assert.AreEqual( onMesh[i], !sim[i].Poly.IsNone, $"Agent {i}" );
			Assert.AreEqual( onMesh[i] ? AgentStatus.Moving : AgentStatus.Unreachable, sim.Status( i ), $"Agent {i}" );
			Assert.AreEqual( onMesh[i] ? 120f : 0f, sim[i].DesiredVelocity.Length(), 0.01f );
		}
		Assert.AreEqual( sim.PolyAt( At( 0, 300 ) ), sim[1].Poly, "The polygon is the one nearest to it" );

		// Every tick it is where the caller says, also across a SetPath and a stop.
		var path = new NavPath();
		for ( int tick = 0; tick < 50; tick++ )
		{
			for ( int i = 0; i < places.Length; i++ )
			{
				places[i] += new Vector3( 1.5f, 0.25f, i == 0 ? 0.125f : 0 );
				sim.Crowd.SetPosition( i, places[i] );
			}
			if ( tick == 20 )
			{
				Assert.AreEqual( PathStatus.Complete, sim.Query.FindPath( places[1], target, Snap, default, path ) );
				Assert.IsTrue( sim.Crowd.SetPath( 1, path.Polys, path.End, target, false ) );
			}
			if ( tick == 30 ) sim.Crowd.Stop( 0 );
			sim.Step();
			for ( int i = 0; i < places.Length; i++ ) Assert.AreEqual( places[i], sim.Position( i ), $"Agent {i} at tick {tick}" );
		}
		Assert.AreEqual( AgentStatus.Moving, sim.Status( 1 ) );
		Assert.AreEqual( AgentStatus.Idle, sim.Status( 0 ) );
	}

	[TestMethod]
	public void ExternalVelocityIsToldByItsPositionsUnlessTheCallerTellsIt()
	{
		var sim = new Sim();
		var external = Walker();
		external.External = true;
		int derived = sim.Add( At( 100, 300 ), external );
		int told = sim.Add( At( 100, 400 ), external );
		sim.Step();
		Assert.AreEqual( Vector3.Zero, sim[derived].Velocity, "It has not been moved yet" );

		// Moved 2.4 units a tick of a fiftieth: 120 units per time unit. Also off the mesh and in the air.
		sim.Crowd.SetVelocity( told, new Vector3( 0, 30, 0 ) );
		for ( int tick = 0; tick < 10; tick++ )
		{
			sim.Crowd.SetPosition( derived, sim.Position( derived ) + new Vector3( 2.4f, 0, tick > 5 ? 3 : 0 ) );
			sim.Crowd.SetPosition( told, sim.Position( told ) + new Vector3( 2.4f, 0, 0 ) );
			sim.Step();
			Assert.IsTrue( Vector3.Distance( sim[derived].Velocity, new Vector3( 120, 0, tick > 5 ? 150 : 0 ) ) < 0.05f, $"Tick {tick}: {sim[derived].Velocity}" );
			Assert.AreEqual( new Vector3( 0, 30, 0 ), sim[told].Velocity, "What the caller told stays" );
		}

		// Standing still, and with a Step of another length.
		sim.Step();
		Assert.AreEqual( Vector3.Zero, sim[derived].Velocity );
		sim.Crowd.SetPosition( derived, sim.Position( derived ) + new Vector3( 0, 5, 0 ) );
		sim.Step( 0.1f );
		Assert.IsTrue( Vector3.Distance( sim[derived].Velocity, new Vector3( 0, 50, 0 ) ) < 0.05f );

		// A walker sees that velocity: the body comes at it from the side and it gives way before they touch.
		int walker = sim.Add( At( 400, 250 ), At( 400, 420 ) );
		int runner = sim.Add( At( 300, 330 ), external );
		float closest = float.MaxValue;
		for ( int tick = 0; tick < 100; tick++ )
		{
			sim.Crowd.SetPosition( runner, sim.Position( runner ) + new Vector3( 2.4f, 0, 0 ) );
			sim.Step();
			closest = MathF.Min( closest, Flat( sim.Position( walker ), sim.Position( runner ) ) );
		}
		Assert.IsTrue( closest >= 15.5f, $"The walker came within {closest:0.0} of a body it could see coming" );
	}

	[TestMethod]
	public void ExternalBodyOffTheMeshIsAvoidedByThoseItOverlapsInHeight()
	{
		var sim = new Sim();
		var body = Walker();
		body.External = true;
		// Hovering 60 above the floor, more than the placement box is high: it has no polygon. It spans
		// z 61 to 93, in the way of an agent 80 tall and above the head of one 32 tall.
		var place = At( 320, 320, 61 );
		int hovering = sim.Add( place, body );
		var tall = Walker();
		tall.Height = 80;
		int giant = sim.Add( At( 200, 320 ), At( 440, 320 ), tall );
		int small = sim.Add( At( 200, 322 ), At( 440, 322 ) );
		// Keep the two walkers out of each other's way: they are compared one at a time.
		float giantClosest = float.MaxValue, smallAside = 0;
		for ( int pass = 0; pass < 2; pass++ )
		{
			int walker = pass == 0 ? giant : small;
			sim.Crowd.SetPosition( pass == 0 ? small : giant, At( 600, 600 ) );
			sim.Crowd.Stop( pass == 0 ? small : giant );
			if ( pass == 1 )
			{
				sim.Crowd.SetPosition( small, At( 200, 320 ) );
				sim.Crowd.MoveTo( small, At( 440, 320 ) );
			}
			for ( int i = 0; i < 400; i++ )
			{
				sim.Step();
				if ( pass == 0 ) giantClosest = MathF.Min( giantClosest, Flat( sim.Position( walker ), place ) );
				else smallAside = MathF.Max( smallAside, MathF.Abs( sim.Position( walker ).Y - 320 ) );
				Assert.AreEqual( place, sim.Position( hovering ) );
			}
			Assert.IsTrue( Vector3.Distance( sim.Position( walker ), At( 440, 320 ) ) < 1, $"Pass {pass}: {sim.Position( walker )}" );
		}
		Assert.IsTrue( sim[hovering].Poly.IsNone );
		Assert.AreEqual( AgentStatus.Idle, sim.Status( hovering ) );
		Assert.IsTrue( giantClosest >= 15.5f, $"The tall agent came within {giantClosest:0.0} of the body in its way" );
		Assert.IsTrue( smallAside < 0.01f, $"The small agent stepped {smallAside:0.00} aside for a body above its head" );
	}

	[TestMethod]
	public void WidenedExtentsPlaceAnAgentAndSnapATargetFarFromTheMesh()
	{
		var sim = new Sim( new() { Obstacles = true } );
		Assert.AreEqual( new Vector3( 16.8f, 16.8f, 48.32f ), sim.Crowd.PlacementExtents );
		Assert.AreEqual( new Vector3( 128 ), sim.Crowd.TargetExtents );

		// With the defaults an agent 200 off the mesh is not placed and a target 200 off is not reached for.
		int lost = sim.Add( At( -200, 320 ), At( 320, 320 ) );
		int aimless = sim.Add( At( 30, 600 ), At( 840, 600 ) );
		sim.Run( 5 );
		Assert.AreEqual( AgentStatus.Unreachable, sim.Status( lost ) );
		Assert.AreEqual( At( -200, 320 ), sim.Position( lost ) );
		Assert.AreEqual( AgentStatus.Unreachable, sim.Status( aimless ) );

		// Widened, the same two are placed and planned for once something tells the crowd to look again.
		sim.Crowd.PlacementExtents = new Vector3( 256, 256, 48.32f );
		sim.Crowd.TargetExtents = new Vector3( 256 );
		sim.Crowd.SetPosition( lost, At( -200, 320 ) );
		sim.Crowd.MoveTo( aimless, At( 840, 600 ) );
		int placed = sim.Add( At( 320, -200 ), At( 320, 840 ) );
		sim.Step();
		Assert.IsTrue( Vector3.Distance( sim.Position( lost ), At( 0, 320 ) ) < 0.2f, "Placed at the nearest point of the mesh" );
		Assert.IsTrue( Vector3.Distance( sim.Position( placed ), At( 320, 0 ) ) < 0.2f );
		Assert.AreEqual( AgentStatus.Moving, sim.Status( lost ) );
		Assert.AreEqual( AgentStatus.Moving, sim.Status( aimless ) );
		sim.Run( 600 );
		Assert.AreEqual( AgentStatus.Arrived, sim.Status( aimless ) );
		Assert.IsTrue( Vector3.Distance( sim.Position( aimless ), At( 640, 600 ) ) < 0.3f, "The target's nearest point on the mesh" );
		Assert.IsTrue( Vector3.Distance( sim.Position( placed ), At( 320, 640 ) ) < 0.3f );
		Assert.AreEqual( AgentStatus.Arrived, sim.Status( lost ) );

		// The same box serves every later look for the agent's polygon: a teleport next to the mesh,
		// with the route kept, and a path handed to an agent that stands off the mesh.
		int walker = sim.Add( At( 30, 30 ), At( 600, 30 ) );
		sim.Run( 10 );
		sim.Crowd.SetPosition( walker, At( 300, -150 ) );
		sim.Step();
		Assert.AreEqual( AgentStatus.Moving, sim.Status( walker ) );
		Assert.IsTrue( Vector3.Distance( sim.Position( walker ), At( 300, 0 ) ) < 5 );

		var path = new NavPath();
		Assert.AreEqual( PathStatus.Complete, sim.Query.FindPath( At( 30, 300 ), At( 30, 600 ), Snap, default, path ) );
		int beside = sim.Add( At( -150, 300 ) );
		Assert.IsTrue( sim.Crowd.SetPath( beside, path.Polys, path.End, At( 30, 600 ), false ) );
		Assert.AreEqual( At( 0, 300 ), sim.Position( beside ) );
		sim.Crowd.PlacementExtents = new Vector3( 16.8f, 16.8f, 48.32f );
		int outOfReach = sim.Add( At( -150, 300 ) );
		Assert.IsFalse( sim.Crowd.SetPath( outOfReach, path.Polys, path.End, At( 30, 600 ), false ) );

		// Another mesh: what the caller set stays, what was left alone follows the new mesh.
		sim.Crowd.SetMesh( CrowdMesh.Create( new() { Scale = 2 } ) );
		Assert.AreEqual( new Vector3( 33.6f, 33.6f, 96.64f ), sim.Crowd.PlacementExtents );
		Assert.AreEqual( new Vector3( 256 ), sim.Crowd.TargetExtents );
	}

	[TestMethod]
	public void ATargetIsLookedForAtLeastAsFarAsTheAgentItself()
	{
		var sim = new Sim();
		Assert.AreEqual( new Vector3( 128 ), sim.Crowd.TargetExtents );

		// 140 above the floor: out of the target box, inside 1.51 heights of an agent 100 tall.
		var tall = Sim.Walker();
		tall.Height = 100;
		int giant = sim.Add( At( 30, 30 ), At( 300, 30, 140 ), tall );
		int walker = sim.Add( At( 30, 100 ), At( 300, 100, 140 ) );
		sim.Step();
		Assert.AreEqual( AgentStatus.Moving, sim.Status( giant ) );
		Assert.AreEqual( AgentStatus.Unreachable, sim.Status( walker ) );

		// The same sideways, with 2.1 radii of an agent 80 wide against a box of 100.
		sim.Crowd.TargetExtents = new Vector3( 100 );
		var wide = Sim.Walker();
		wide.Radius = 80;
		int broad = sim.Add( At( 300, 300 ), At( -150, 300 ), wide );
		int slim = sim.Add( At( 300, 400 ), At( -150, 400 ) );
		sim.Step();
		Assert.AreEqual( AgentStatus.Moving, sim.Status( broad ) );
		Assert.AreEqual( AgentStatus.Unreachable, sim.Status( slim ) );
	}

	[TestMethod]
	public void CostArrayEditedInPlaceIsNoticedBySetParams()
	{
		var sim = new Sim( new() { Ring = true } );
		// Both ways round the block are five polygons long. North is area 2, south area 3.
		var costs = new float[8];
		Array.Fill( costs, 1f );
		costs[3] = 4;
		var walker = Walker();
		walker.Filter = new NavFilter( 0, costs );
		var target = At( 540, 320 );
		int agent = sim.Add( At( 100, 320 ), target, walker );
		sim.Run( 10 );
		Assert.IsTrue( sim[agent].DesiredVelocity.Y > 1, "North, the cheap way" );

		costs[3] = 1;
		costs[2] = 4;
		sim.Step();
		Assert.IsTrue( sim[agent].DesiredVelocity.Y > 1, "An array edited in place is not looked at until it is handed in" );
		sim.Crowd.SetParams( agent, walker );
		sim.Step();
		Assert.IsTrue( sim[agent].DesiredVelocity.Y < -1, "South now" );
		Assert.AreEqual( At( 200, 280 ), sim.Crowd.GetCorners( agent )[1] );

		// The same contents in another array are no change: no search, the corners stay.
		var copy = walker;
		copy.Filter = new NavFilter( 0, (float[])costs.Clone() );
		var before = sim.Crowd.GetCorners( agent ).ToArray();
		sim.Crowd.SetParams( agent, copy );
		sim.Step();
		Assert.AreEqual( before[1], sim.Crowd.GetCorners( agent )[1] );
		sim.Run( 500 );
		Assert.AreEqual( AgentStatus.Arrived, sim.Status( agent ) );
	}

	[TestMethod]
	public void ReplannedRouteOfTheSameLengthDropsItsOldCorners()
	{
		// The corners are kept while polygon, corridor length and end are the same. A search can change
		// the way and leave all three as they were: north closes, and the way south has as many polygons.
		var sim = new Sim( new() { Ring = true } );
		var costs = new float[8];
		Array.Fill( costs, 1f );
		costs[3] = 4;
		var walker = Walker();
		walker.Filter = new NavFilter( 0, costs );
		int agent = sim.Add( At( 100, 320 ), At( 540, 320 ), walker );
		sim.Run( 10 );
		Assert.AreEqual( At( 200, 360 ), sim.Crowd.GetCorners( agent )[1] );
		var path = new NavPath();
		sim.Crowd.GetPath( agent, path );
		int length = path.Polys.Length;

		walker.Filter = new NavFilter( 1ul << 2 );
		sim.Crowd.SetParams( agent, walker );
		sim.Step();
		sim.Crowd.GetPath( agent, path );
		Assert.AreEqual( length, path.Polys.Length, "The case this test is about" );
		Assert.AreEqual( At( 200, 280 ), sim.Crowd.GetCorners( agent )[1] );
		Assert.IsTrue( sim[agent].DesiredVelocity.Y < -1, "It turns at once" );
	}

	[TestMethod]
	public void MoveAcrossMoreThanThirtyTwoPolygonsEndsWhereItShould()
	{
		// One polygon per cell of 10: a stride of 630 crosses 63 of them, more than the move reports.
		var fast = AgentParams.Create( 8, 32, 20000, 1e9f );
		// Along a row and along a column: straight routes, so the whole stride is one move. (Round a
		// corner a stride this long overshoots, as any stride does by its length at most.)
		foreach ( var (start, target) in new[] { (new Vector3( 5, 325, 0 ), new Vector3( 635, 325, 0 )), (new Vector3( 415, 635, 0 ), new Vector3( 415, 5, 0 )) } )
		{
			var sim = new Sim( GridMesh.Mesh( (0, 0) ) );
			int agent = sim.Add( start, target, fast );
			sim.Step( 0.05f );
			Assert.IsTrue( Vector3.Distance( sim.Position( agent ), target ) < 0.3f, $"After one stride: {sim.Position( agent )}" );
			Assert.AreEqual( sim.PolyAt( target ), sim[agent].Poly, "The corridor starts where the agent is" );
			sim.Step( 0.05f );
			Assert.AreEqual( AgentStatus.Arrived, sim.Status( agent ) );

			// And back in strides of 20 polygons and more, past a neighbour standing in the way.
			var slower = AgentParams.Create( 8, 32, 4500, 1e9f );
			sim.Crowd.SetParams( agent, slower );
			sim.Crowd.MoveTo( agent, start );
			int other = sim.Add( (start + target) / 2 );
			for ( int i = 0; i < 40; i++ )
			{
				sim.Step( 0.05f );
				Assert.IsTrue( sim.Status( agent ) is AgentStatus.Moving or AgentStatus.Arrived, $"Step {i}: {sim.Status( agent )}" );
				Assert.AreEqual( sim.PolyAt( sim.Position( agent ) ), sim[agent].Poly, $"Step {i}" );
				Assert.AreEqual( sim.PolyAt( sim.Position( other ) ), sim[other].Poly, $"Step {i}" );
			}
			Assert.AreEqual( AgentStatus.Arrived, sim.Status( agent ) );
			Assert.IsTrue( Vector3.Distance( sim.Position( agent ), start ) < 0.3f );
		}
	}

	[TestMethod]
	public void SetMeshRelocatesAgentsAndKeepsTargets()
	{
		var first = CrowdMesh.Create( new() { UpperFloor = true, Link = true } );
		var sim = new Sim( first );
		var target = At( 540, 100 );
		int walker = sim.Add( At( 100, 100 ), target );
		int rester = sim.Add( At( 100, 500 ), At( 120, 500 ) );
		int climber = sim.Add( At( 320, 320 ), At( 500, 320, 201 ), Walker( autoLinks: false ) );
		int idle = sim.Add( At( 600, 600 ) );
		sim.Run( 60 );
		Assert.AreEqual( AgentStatus.Moving, sim.Status( walker ) );
		Assert.AreEqual( AgentStatus.Arrived, sim.Status( rester ) );
		Assert.AreEqual( AgentStatus.OnLink, sim.Status( climber ) );
		var before = sim.Position( walker );

		// A mesh with a wall across the middle and no upper floor.
		var second = CrowdMesh.Create( new() { Doorway = true } );
		sim.Crowd.SetMesh( second );
		Assert.AreEqual( AgentStatus.Pending, sim.Status( walker ), "No route on the new mesh yet" );
		Assert.AreEqual( target, sim[walker].Target );
		Assert.AreEqual( AgentStatus.Arrived, sim.Status( rester ) );
		Assert.AreEqual( AgentStatus.Pending, sim.Status( climber ), "Taken off the link of the old mesh" );
		Assert.AreEqual( -1, sim[climber].Link );
		Assert.AreEqual( AgentStatus.Idle, sim.Status( idle ) );

		sim.Step();
		var query = new NavQuery( second );
		Assert.AreEqual( AgentStatus.Moving, sim.Status( walker ) );
		Assert.IsTrue( query.IsValid( sim[walker].Poly, default ), "The polygon is one of the new mesh" );
		Assert.IsTrue( Vector3.Distance( sim.Position( walker ), before ) < 5 );
		Assert.IsTrue( query.IsValid( sim[idle].Poly, default ) );

		sim.Run( 600 );
		Assert.AreEqual( AgentStatus.Arrived, sim.Status( walker ) );
		Assert.IsTrue( Vector3.Distance( sim.Position( walker ), target ) < 1, "Through the door of the new mesh" );
		Assert.IsTrue( Vector3.Distance( sim.Position( rester ), At( 120, 500 ) ) < 2 );
		Assert.AreEqual( AgentStatus.Arrived, sim.Status( rester ) );
		// The target on the upper floor is 200 above the only floor left: out of reach of the snap box.
		Assert.AreEqual( AgentStatus.Unreachable, sim.Status( climber ) );
	}

	[TestMethod]
	public void SetMeshInAnotherUnitRescalesTheTolerances()
	{
		var sim = new Sim();
		const float scale = 0.0254f;
		var metres = CrowdMesh.Create( new() { Scale = scale } );
		sim.Crowd.SetMesh( metres );
		var value = AgentParams.Create( 8 * scale, 32 * scale, 120 * scale, 240 * scale );
		var target = At( 500, 320 ) * scale;
		int agent = sim.Crowd.Add( At( 100, 320 ) * scale, value );
		sim.Crowd.MoveTo( agent, target );
		sim.Run( 300 );
		Assert.AreEqual( AgentStatus.Arrived, sim.Status( agent ) );
		Assert.IsTrue( Vector3.Distance( sim.Position( agent ), target ) < 0.3f * scale );
	}

	[TestMethod]
	public void TeleportKeepsTheTargetAndAnArrivedAgentForgetsItsRestPoint()
	{
		var sim = new Sim( new() { Obstacles = true } );
		var target = At( 600, 600 );
		int agent = sim.Add( At( 30, 30 ), target );
		sim.Run( 20 );
		// Behind a pillar, far from the corridor.
		sim.Crowd.SetPosition( agent, At( 30, 600 ) );
		Assert.AreEqual( At( 30, 600 ), sim.Position( agent ) );
		Assert.AreEqual( AgentStatus.Moving, sim.Status( agent ) );
		sim.Step();
		Assert.AreEqual( AgentStatus.Moving, sim.Status( agent ) );
		Assert.IsTrue( Vector3.Distance( sim.Position( agent ), At( 30, 600 ) ) < 5 );
		Assert.AreEqual( target, sim[agent].Target );
		sim.Run( 500 );
		Assert.AreEqual( AgentStatus.Arrived, sim.Status( agent ) );

		sim.Crowd.SetPosition( agent, At( 100, 100 ) );
		Assert.AreEqual( AgentStatus.Idle, sim.Status( agent ), "A teleported agent does not walk back to where it rested" );
		sim.Run( 50 );
		Assert.AreEqual( At( 100, 100 ), sim.Position( agent ) );
		Assert.IsFalse( sim[agent].Poly.IsNone );

		// Off the mesh and back.
		sim.Crowd.MoveTo( agent, target );
		sim.Crowd.SetPosition( agent, At( 2000, 2000 ) );
		sim.Run( 5 );
		Assert.AreEqual( AgentStatus.Unreachable, sim.Status( agent ) );
		Assert.IsTrue( sim[agent].Poly.IsNone );
		Assert.AreEqual( At( 2000, 2000 ), sim.Position( agent ) );
		sim.Crowd.SetPosition( agent, At( 500, 600 ) );
		sim.Run( 200 );
		Assert.AreEqual( AgentStatus.Arrived, sim.Status( agent ) );
	}

	// Three tiles in a row with gaps between them, joined by two links that meet on the middle tile.
	static Sim TwoLinks( float gap, out int firstLink, out int secondLink )
	{
		var mesh = new NavMesh( CrowdMesh.Settings() );
		mesh.CommitTiles( [CrowdMesh.Tile( default ), CrowdMesh.Tile( default, 2 ), CrowdMesh.Tile( default, 4 )] );
		firstLink = mesh.AddLink( new NavLink { Start = At( 630, 320 ), End = At( 1290, 320 ), Radius = 12, Area = 1 } );
		secondLink = mesh.AddLink( new NavLink { Start = At( 1290 + gap, 320 ), End = At( 2570, 320 ), Radius = 12, Area = 1 } );
		mesh.Commit();
		return new Sim( mesh );
	}

	[TestMethod]
	public void BackToBackLinksGiveOneTransitionPerStep()
	{
		// The second link starts 2 units from where the first ends, within the agent's reach.
		var sim = TwoLinks( 2, out int firstLink, out int secondLink );
		var fast = Walker();
		fast.MaxSpeed = 100000;
		fast.Acceleration = 1e9f;
		var target = At( 2700, 320 );
		int agent = sim.Add( At( 625, 320 ), target, fast );
		var watch = new LinkWatch();
		var seen = new List<(AgentStatus, int)>();
		for ( int i = 0; i < 12; i++ )
		{
			// Eight substeps: without the rule both links would be over within one step.
			sim.Step( 0.4f );
			watch.See( sim[agent] );
			seen.Add( (sim.Status( agent ), sim[agent].Link) );
		}
		string trace = string.Join( " ", seen.Select( s => $"{s.Item1}:{s.Item2}" ) );
		Assert.AreEqual( (AgentStatus.OnLink, firstLink), seen[0], trace );
		Assert.AreEqual( (AgentStatus.Moving, -1), seen[1], "A link entered in a step is completed in the next, and no other is entered in that one: " + trace );
		Assert.AreEqual( (AgentStatus.OnLink, secondLink), seen[2], trace );
		Assert.AreNotEqual( AgentStatus.OnLink, seen[3].Item1, trace );
		Assert.AreEqual( 2, watch.Entries );
		Assert.AreEqual( 2, watch.Exits );
		CollectionAssert.AreEqual( new[] { firstLink, secondLink }, watch.Entered );
		Assert.AreEqual( AgentStatus.Arrived, sim.Status( agent ), trace );
		Assert.IsTrue( Vector3.Distance( sim.Position( agent ), target ) < 1 );
	}

	[TestMethod]
	public void AutoLinksWalkEachLinkAtFullSpeedAndPublishItsEnds()
	{
		var sim = TwoLinks( 200, out int firstLink, out int secondLink );
		var target = At( 2700, 320 );
		int agent = sim.Add( At( 500, 320 ), target );
		var watch = new LinkWatch();
		float linkSpeed = 0;
		for ( int i = 0; i < 2500 && sim.Status( agent ) != AgentStatus.Arrived; i++ )
		{
			var before = sim[agent];
			sim.Step();
			watch.See( sim[agent] );
			if ( before.Status != AgentStatus.OnLink || !sim.OnLink( agent ) ) continue;
			linkSpeed = MathF.Max( linkSpeed, Vector3.Distance( before.Position, sim.Position( agent ) ) / 0.02f );
			bool onFirst = sim[agent].Link == firstLink;
			Assert.AreEqual( onFirst ? At( 630, 320 ) : At( 1490, 320 ), sim[agent].LinkStart );
			Assert.AreEqual( onFirst ? At( 1290, 320 ) : At( 2570, 320 ), sim[agent].LinkEnd );
			Assert.IsTrue( sim[agent].Poly.IsNone, "No polygon while on a link" );
			Assert.AreEqual( Vector3.Zero, sim[agent].DesiredVelocity );
		}
		Assert.AreEqual( AgentStatus.Arrived, sim.Status( agent ) );
		Assert.AreEqual( 120f, linkSpeed, 0.01f );
		CollectionAssert.AreEqual( new[] { firstLink, secondLink }, watch.Entered );
		Assert.AreEqual( 2, watch.Exits );
	}

	[TestMethod]
	public void OneWayLinkIsNotTakenBackwards()
	{
		var sim = TwoLinks( 200, out _, out _ );
		var target = At( 100, 320 );
		int agent = sim.Add( At( 1500, 320 ), target );
		sim.Run( 300 );
		Assert.AreNotEqual( AgentStatus.OnLink, sim.Status( agent ) );
		Assert.IsTrue( sim.Position( agent ).X >= 1280, "Still on the middle tile" );
		Assert.IsTrue( sim.HasTarget( agent ) );
	}

	[TestMethod]
	public void AgentBelowItsGoalOnAnotherFloorHasNotArrived_A36()
	{
		var sim = new Sim( new() { UpperFloor = true, Link = true } );
		var target = At( 500, 320, 201 );
		int agent = sim.Add( At( 500, 320 ), target );
		sim.Step();
		Assert.AreEqual( AgentStatus.Moving, sim.Status( agent ), "Directly below the goal is not at the goal" );
		sim.Run( 600 );
		Assert.AreEqual( AgentStatus.Arrived, sim.Status( agent ) );
		Assert.IsTrue( Vector3.Distance( sim.Position( agent ), target ) < 1 );

		// And a link entry is not entered from the floor below it.
		var down = At( 100, 320 );
		int upper = sim.Add( At( 100, 320, 201 ), down );
		int lower = sim.Add( At( 320, 320 ), At( 330, 320 ) );
		sim.Step();
		Assert.AreEqual( AgentStatus.Moving, sim.Status( upper ) );
		Assert.AreNotEqual( AgentStatus.OnLink, sim.Status( lower ) );
	}

	[TestMethod]
	public void UnreachableAndOffMeshTargets()
	{
		var sim = new Sim( new() { UpperFloor = true } );
		// On the other floor: the route ends below the target and the agent is Blocked there.
		var above = At( 300, 300, 201 );
		int blocked = sim.Add( At( 100, 300 ), above );
		// Off the mesh but within 16 mesh radii: the nearest point of the mesh is the route's end.
		int near = sim.Add( At( 100, 100 ), At( 740, 100 ) );
		// Further out than that: no route.
		int far = sim.Add( At( 100, 500 ), At( 900, 500 ) );
		int nowhere = sim.Add( At( 300, 500 ), new Vector3( float.NaN, 0, 0 ) );
		sim.Step();
		Assert.AreEqual( AgentStatus.Moving, sim.Status( blocked ) );
		Assert.AreEqual( AgentStatus.Moving, sim.Status( near ) );
		Assert.AreEqual( AgentStatus.Unreachable, sim.Status( far ) );
		Assert.AreEqual( AgentStatus.Unreachable, sim.Status( nowhere ) );
		sim.Run( 500 );

		Assert.AreEqual( AgentStatus.Blocked, sim.Status( blocked ) );
		Assert.AreEqual( above, sim[blocked].Target );
		Assert.IsTrue( Flat( sim.Position( blocked ), above ) < 0.3f );
		Assert.AreEqual( 1f, sim.Position( blocked ).Z );
		var path = new NavPath();
		Assert.AreEqual( PathStatus.Partial, sim.Crowd.GetPath( blocked, path ) );

		Assert.AreEqual( AgentStatus.Arrived, sim.Status( near ) );
		Assert.IsTrue( Vector3.Distance( sim.Position( near ), At( 640, 100 ) ) < 0.3f );

		Assert.AreEqual( AgentStatus.Unreachable, sim.Status( far ) );
		Assert.AreEqual( At( 100, 500 ), sim.Position( far ), "An agent without a route stays where it is" );
		Assert.AreEqual( At( 900, 500 ), sim[far].Target );
		Assert.AreEqual( AgentStatus.Unreachable, sim.Status( nowhere ) );
		Assert.AreEqual( PathStatus.NotFound, sim.Crowd.GetPath( far, path ) );

		// Blocked returns to the end of its route when pushed away, and stays Blocked.
		sim.Crowd.SetVelocity( blocked, default );
		int pusher = sim.Add( sim.Position( blocked ), At( 300, 500 ) );
		float displaced = 0;
		for ( int i = 0; i < 300; i++ )
		{
			sim.Step();
			displaced = MathF.Max( displaced, Flat( sim.Position( blocked ), above ) );
			Assert.AreEqual( AgentStatus.Blocked, sim.Status( blocked ) );
		}
		Assert.IsTrue( displaced > 2 );
		Assert.IsTrue( Flat( sim.Position( blocked ), above ) <= 2 );
		Assert.IsTrue( sim.Status( pusher ) == AgentStatus.Arrived );
	}

	[TestMethod]
	public void UnreachableLastsUntilSomethingChanges()
	{
		var sim = new Sim();
		// A target 260 from the mesh, and an agent 30 beside it: no way for either.
		int far = sim.Add( At( 100, 500 ), At( 900, 500 ) );
		int beside = sim.Add( At( -30, 300 ), At( 300, 300 ) );
		int stays = sim.Add( At( 100, 100 ), At( 900, 100 ) );
		Assert.AreEqual( AgentStatus.Pending, sim.Status( far ), "Pending is the time until the next Step" );
		sim.Step();
		Assert.AreEqual( AgentStatus.Unreachable, sim.Status( far ) );
		Assert.AreEqual( AgentStatus.Unreachable, sim.Status( beside ) );
		sim.Run( 20 );
		Assert.AreEqual( AgentStatus.Unreachable, sim.Status( far ) );
		Assert.AreEqual( At( 900, 500 ), sim[far].Target, "The target is kept" );
		var path = new NavPath();
		Assert.AreEqual( PathStatus.NotFound, sim.Crowd.GetPath( far, path ) );

		// Params that change nothing leave it; a size that reaches the mesh makes it Pending, then Moving.
		sim.Crowd.SetParams( far, Walker() );
		Assert.AreEqual( AgentStatus.Unreachable, sim.Status( far ) );
		var wide = Walker();
		wide.Radius = 20;
		sim.Crowd.SetParams( beside, wide );
		Assert.AreEqual( AgentStatus.Pending, sim.Status( beside ) );
		sim.Step();
		Assert.AreEqual( AgentStatus.Moving, sim.Status( beside ) );

		// A new target, also the same one again.
		sim.Crowd.MoveTo( far, At( 900, 500 ) );
		Assert.AreEqual( AgentStatus.Pending, sim.Status( far ) );
		sim.Step();
		Assert.AreEqual( AgentStatus.Unreachable, sim.Status( far ) );
		sim.Crowd.MoveTo( far, At( 600, 500 ) );
		Assert.AreEqual( AgentStatus.Pending, sim.Status( far ) );
		sim.Step();
		Assert.AreEqual( AgentStatus.Moving, sim.Status( far ) );

		// A new position.
		sim.Crowd.MoveTo( far, At( 900, 500 ) );
		sim.Step();
		Assert.AreEqual( AgentStatus.Unreachable, sim.Status( far ) );
		sim.Crowd.SetPosition( far, At( 120, 500 ) );
		Assert.AreEqual( AgentStatus.Pending, sim.Status( far ) );
		sim.Step();
		Assert.AreEqual( AgentStatus.Unreachable, sim.Status( far ) );

		// A changed mesh is planned on in the next Step: the tile the target is on appears.
		Assert.AreEqual( AgentStatus.Unreachable, sim.Status( stays ) );
		sim.Mesh.CommitTiles( [CrowdMesh.Tile( default, 1 )] );
		Assert.AreEqual( AgentStatus.Unreachable, sim.Status( stays ), "The crowd has not seen the mesh change yet" );
		sim.Step();
		Assert.IsTrue( sim.Status( far ) is AgentStatus.Moving or AgentStatus.Blocked, "As near as it gets is a route now" );
		Assert.IsTrue( sim.Status( stays ) is AgentStatus.Moving or AgentStatus.Blocked );

		// Stop ends it like any target.
		sim.Crowd.MoveTo( beside, At( 2000, 2000 ) );
		sim.Step();
		Assert.AreEqual( AgentStatus.Unreachable, sim.Status( beside ) );
		sim.Crowd.Stop( beside );
		Assert.AreEqual( AgentStatus.Idle, sim.Status( beside ) );
	}

	[TestMethod]
	public void TeleportFollowedByANewTargetPlansFromTheNewPlace()
	{
		var sim = new Sim( new() { Doorway = true } );
		int agent = sim.Add( At( 100, 100 ), At( 250, 100 ) );
		sim.Run( 10 );
		// Across the wall, then a target: both before the next Step.
		sim.Crowd.SetPosition( agent, At( 500, 500 ) );
		sim.Crowd.MoveTo( agent, At( 600, 500 ) );
		Assert.IsTrue( sim[agent].Poly.IsNone, "Unknown until the Step has looked" );
		sim.Step();
		Assert.AreEqual( sim.PolyAt( At( 500, 500 ) ), sim[agent].Poly );
		Assert.AreEqual( AgentStatus.Moving, sim.Status( agent ) );
		Assert.IsTrue( Vector3.Distance( sim.Position( agent ), At( 500, 500 ) ) < 3 );
		var path = new NavPath();
		Assert.AreEqual( PathStatus.Complete, sim.Crowd.GetPath( agent, path ) );
		Assert.AreEqual( 100f, path.Length, 3f, "Straight there, not back through the door" );
		sim.Run( 100 );
		Assert.AreEqual( AgentStatus.Arrived, sim.Status( agent ) );

		// The same with params and a stop in between.
		sim.Crowd.MoveTo( agent, At( 400, 500 ) );
		sim.Step();
		sim.Crowd.SetPosition( agent, At( 100, 500 ) );
		sim.Crowd.SetParams( agent, Walker() );
		Assert.IsTrue( sim[agent].Poly.IsNone );
		sim.Step();
		Assert.AreEqual( sim.PolyAt( At( 100, 500 ) ), sim[agent].Poly );
		Assert.AreEqual( AgentStatus.Moving, sim.Status( agent ) );
		sim.Crowd.GetPath( agent, path );
		Assert.IsTrue( path.Length > 300, "From the other side of the wall now" );
	}

	[TestMethod]
	public void MoveToDeadBandAppliesOnlyWhileMoving_A37()
	{
		var sim = new Sim( new() { UpperFloor = true } );
		var target = At( 500, 100 );
		int agent = sim.Add( At( 100, 100 ), target );
		sim.Step();
		// A quarter cell is 2.5 here.
		sim.Crowd.MoveTo( agent, target + new Vector3( 2, -2, 0 ) );
		Assert.AreEqual( target, sim[agent].Target, "A moving agent ignores a target this close to the one it has" );
		sim.Crowd.MoveTo( agent, target + new Vector3( 3, 0, 0 ) );
		Assert.AreEqual( target + new Vector3( 3, 0, 0 ), sim[agent].Target );
		Assert.AreEqual( target, sim[agent].RouteTarget );
		sim.Step();
		Assert.AreEqual( sim[agent].Target, sim[agent].RouteTarget );

		// Asking a Blocked agent for the same target again plans again.
		var above = At( 300, 300, 201 );
		int blocked = sim.Add( At( 290, 300 ), above );
		sim.Run( 100 );
		Assert.AreEqual( AgentStatus.Blocked, sim.Status( blocked ) );
		sim.Crowd.MoveTo( blocked, above );
		Assert.AreEqual( AgentStatus.Pending, sim.Status( blocked ) );
		CrowdMesh.AddLink( sim.Mesh );
		sim.Run( 300 );
		Assert.AreEqual( AgentStatus.Arrived, sim.Status( blocked ) );
		Assert.AreEqual( 201f, sim.Position( blocked ).Z );

		// The same for an agent that has arrived.
		sim.Crowd.MoveTo( blocked, above );
		Assert.AreEqual( AgentStatus.Pending, sim.Status( blocked ) );
		sim.Step();
		Assert.AreEqual( AgentStatus.Arrived, sim.Status( blocked ) );
	}

	[TestMethod]
	public void ChangedFilterIsNoticedOnTheRoute_M9()
	{
		var sim = new Sim( new() { AreaStrip = true } );
		var target = At( 600, 320 );
		int agent = sim.Add( At( 100, 320 ), target );
		sim.Run( 50 );
		Assert.AreEqual( AgentStatus.Moving, sim.Status( agent ) );
		var barred = Walker();
		barred.Filter = new NavFilter( 1ul << 2 );
		sim.Crowd.SetParams( agent, barred );
		sim.Run( 400 );
		Assert.AreEqual( AgentStatus.Blocked, sim.Status( agent ), "The strip of area 2 is closed to it now" );
		Assert.IsTrue( sim.Position( agent ).X <= 300.001f );
		Assert.AreEqual( 300f, sim.Position( agent ).X, 0.3f );

		sim.Crowd.SetParams( agent, Walker() );
		sim.Run( 400 );
		Assert.AreEqual( AgentStatus.Arrived, sim.Status( agent ) );
	}

	[TestMethod]
	public void StaleCorridorAheadIsCutAndPlannedAgainWithoutLosingTheRoute_M2()
	{
		// Three tiles in a row. Without the middle one the target is on an island of its own.
		var mesh = new NavMesh( CrowdMesh.Settings() );
		var borders = new CrowdMesh.Options { TileBorders = true };
		mesh.CommitTiles( [CrowdMesh.Tile( borders ), CrowdMesh.Tile( borders, 1 ), CrowdMesh.Tile( borders, 2 )] );
		var sim = new Sim( mesh );
		var target = At( 1300, 320 );
		int agent = sim.Add( At( 100, 320 ), target );
		sim.Run( 50 );
		Assert.AreEqual( AgentStatus.Moving, sim.Status( agent ) );

		mesh.RemoveTile( 1, 0 );
		mesh.Commit();
		for ( int i = 0; i < 400; i++ )
		{
			sim.Step();
			Assert.IsTrue( sim.Status( agent ) is AgentStatus.Moving or AgentStatus.Blocked, $"Step {i}: {sim.Status( agent )}" );
		}
		Assert.AreEqual( AgentStatus.Blocked, sim.Status( agent ) );
		Assert.AreEqual( 640f, sim.Position( agent ).X, 0.3f );

		mesh.CommitTiles( [CrowdMesh.Tile( borders, 1 )] );
		sim.Run( 400 );
		Assert.AreEqual( AgentStatus.Arrived, sim.Status( agent ) );
		Assert.IsTrue( Vector3.Distance( sim.Position( agent ), target ) < 1 );
	}

	[TestMethod]
	public void StepIgnoresBadDeltasAndCapsLongOnes()
	{
		var sim = new Sim();
		int agent = sim.Add( At( 100, 320 ), At( 600, 320 ) );
		sim.Step( 0 );
		sim.Step( -1 );
		sim.Step( float.NaN );
		sim.Step( float.PositiveInfinity );
		Assert.AreEqual( AgentStatus.Pending, sim.Status( agent ) );
		Assert.AreEqual( At( 100, 320 ), sim.Position( agent ) );

		// Eight substeps of 0.05 at most: a minute's pause moves the agent 0.4 time units.
		sim.Step( 60 );
		float moved = sim.Position( agent ).X - 100;
		Assert.IsTrue( moved > 5 && moved <= 120 * 0.4f + 0.01f, $"Moved {moved}" );

		// Other units of time: the same walk in steps of 20 with times in milliseconds.
		var slow = new Sim();
		slow.Crowd.SubstepTime = 50;
		slow.Crowd.AvoidHorizon = 750;
		var value = AgentParams.Create( 8, 32, 0.12f, 0.00024f );
		int other = slow.Add( At( 100, 320 ), At( 600, 320 ), value );
		var reference = new Sim();
		int same = reference.Add( At( 100, 320 ), At( 600, 320 ) );
		for ( int i = 0; i < 100; i++ )
		{
			slow.Step( 20 );
			reference.Step( 0.02f );
		}
		Assert.IsTrue( Vector3.Distance( slow.Position( other ), reference.Position( same ) ) < 0.01f, $"{slow.Position( other )} versus {reference.Position( same )}" );
	}
}
