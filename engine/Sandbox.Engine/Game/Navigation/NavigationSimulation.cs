using Nav;
using NVector3 = System.Numerics.Vector3;

namespace Sandbox.Navigation;

/// <summary>
/// The crowd of one navmesh. Agent members may be called from any thread, so every call into the crowd takes the gate.
/// </summary>
internal sealed class NavigationSimulation
{
	internal readonly object Gate = new();
	internal readonly NavCrowd Crowd;
	internal readonly NavMesh Owner;
	internal Nav.NavMesh Mesh;

	internal readonly NavPath Path = new();

	// By crowd id
	private SimulationAgent[] agents = [];
	private int count;
	private NavQuery query;
	private readonly List<(SimulationAgent Agent, AgentState State)> linkChanges = new();

	private static readonly Action<int, Action<int>> run = static ( count, body ) => JobDispatcher.For( count, body, static ( body, index ) => body( index ), 1 );

	internal NavigationSimulation( NavMesh owner, Nav.NavMesh mesh )
	{
		Owner = owner;
		Crowd = new NavCrowd( mesh, Environment.ProcessorCount );
		SetMesh( mesh );
	}

	internal int Count { get { lock ( Gate ) return count; } }

	internal void SetMesh( Nav.NavMesh mesh )
	{
		lock ( Gate )
		{
			if ( Mesh is not null ) Crowd.SetMesh( mesh );
			Mesh = mesh;
			query = new NavQuery( mesh );

			Crowd.PlacementExtents = Crowd.TargetExtents = new NVector3( NavMesh.SnapDistance, NavMesh.SnapDistance, mesh.Settings.AgentHeight * NavMesh.SnapHeights );
		}
	}

	// The crowd leaves an agent its owner moves where it was put, so its place on its polygon is looked up
	internal AgentState StateOf( int id )
	{
		var state = Crowd.States[id];
		if ( Crowd.Params[id].External && query.ClosestPoint( state.Poly, state.Position, out var closest ) ) state.Position = closest;
		return state;
	}

	internal SimulationAgent Add( NavMeshAgent component, Vector3 position, in AgentParams options )
	{
		lock ( Gate )
		{
			int id = Crowd.Add( position, options );
			if ( id >= agents.Length ) Array.Resize( ref agents, Math.Max( 16, id * 2 ) );
			count++;
			return agents[id] = new SimulationAgent( this, id, component );
		}
	}

	internal void Remove( SimulationAgent agent )
	{
		lock ( Gate )
		{
			if ( agent.Removed ) return;
			agent.Retire( StateOf( agent.Id ) );
			Crowd.Remove( agent.Id );
			agents[agent.Id] = null;
			count--;
		}
	}

	internal void Invalidate()
	{
		lock ( Gate )
		{
			foreach ( var agent in agents )
			{
				if ( agent is not null ) Remove( agent );
			}
		}
	}

	internal void Update( float delta )
	{
		lock ( Gate )
		{
			Crowd.Step( delta, run );

			// At most one link transition per agent per step
			var states = Crowd.States;
			for ( int id = 0; id < states.Length; id++ )
			{
				var agent = agents[id];
				if ( agent is null || agent.Link == states[id].Link ) continue;
				agent.Link = states[id].Link;
				linkChanges.Add( (agent, states[id]) );
			}
		}

		// Outside the gate: these end in game code, which calls back into agents
		foreach ( var (agent, state) in linkChanges )
		{
			if ( !agent.Removed ) agent.Component.LinkChanged( state, Owner.LinkComponent( state.Link ) );
		}
		linkChanges.Clear();
	}
}

/// <summary>
/// One agent component's place in the crowd. Crowd ids are given out again, this is not.
/// </summary>
internal sealed class SimulationAgent
{
	internal readonly NavigationSimulation Owner;
	internal readonly int Id;
	internal readonly NavMeshAgent Component;

	// The link the component was last told about
	internal int Link = -1;

	// Once the agent has left the crowd its state stays readable as it was then
	internal bool Removed;
	private AgentState last;

	internal SimulationAgent( NavigationSimulation owner, int id, NavMeshAgent component )
	{
		Owner = owner;
		Id = id;
		Component = component;
	}

	internal void Retire( in AgentState state )
	{
		last = state;
		Removed = true;
	}

	internal AgentState State { get { lock ( Owner.Gate ) return Removed ? last : Owner.StateOf( Id ); } }

	internal AgentParams Options
	{
		get { lock ( Owner.Gate ) return Removed ? default : Owner.Crowd.Params[Id]; }
		set { lock ( Owner.Gate ) if ( !Removed ) Owner.Crowd.SetParams( Id, value ); }
	}

	internal void MoveTo( Vector3 target ) { lock ( Owner.Gate ) if ( !Removed ) Owner.Crowd.MoveTo( Id, target ); }
	internal void Stop() { lock ( Owner.Gate ) if ( !Removed ) Owner.Crowd.Stop( Id ); }
	internal void SetPosition( Vector3 position ) { lock ( Owner.Gate ) if ( !Removed ) Owner.Crowd.SetPosition( Id, position ); }
	internal void SetVelocity( Vector3 velocity ) { lock ( Owner.Gate ) if ( !Removed ) Owner.Crowd.SetVelocity( Id, velocity ); }
	internal void CompleteLink() { lock ( Owner.Gate ) if ( !Removed ) Owner.Crowd.CompleteLink( Id ); }

	// Ignored unless the path was made on the mesh the agent is on now
	internal void SetPath( in NavMeshPath path )
	{
		lock ( Owner.Gate )
		{
			if ( Removed || path.Mesh != Owner.Mesh ) return;
			Owner.Crowd.SetPath( Id, path.Polygons, path.Points[^1].Position, path.RequestedTarget, path.Status == NavMeshPathStatus.Partial );
		}
	}

	internal NavMeshPath GetPath()
	{
		lock ( Owner.Gate )
		{
			if ( Removed || Owner.Crowd.GetPath( Id, Owner.Path ) < PathStatus.Partial ) return new() { Status = NavMeshPathStatus.PathNotFound };
			return NavMesh.CreatePathResult( Owner.Mesh, Owner.Path, Owner.Crowd.States[Id].RouteTarget );
		}
	}

	internal Vector3 GetLookAhead( float distance )
	{
		lock ( Owner.Gate )
		{
			if ( Removed ) return last.Position;
			Vector3 position = Owner.StateOf( Id ).Position;
			foreach ( Vector3 next in Owner.Crowd.GetCorners( Id ) )
			{
				float length = (next - position).Length;
				if ( length > distance ) return Vector3.Lerp( position, next, MathF.Max( 0, distance ) / length );
				distance -= length;
				position = next;
			}
			return position;
		}
	}
}
