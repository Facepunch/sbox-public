/*
Copyright (c) 2009-2010 Mikko Mononen memon@inside.org
recast4j copyright (c) 2015-2019 Piotr Piastucki piotr@jtilia.org
DotRecast Copyright (c) 2023-2024 Choi Ikpil ikpil@naver.com
Copyright (c) 2024 Facepunch Studios Ltd

This software is provided 'as-is', without any express or implied
warranty.  In no event will the authors be held liable for any damages
arising from the use of this software.
Permission is granted to anyone to use this software for any purpose,
including commercial applications, and to alter it and redistribute it
freely, subject to the following restrictions:
1. The origin of this software must not be misrepresented; you must not
 claim that you wrote the original software. If you use this software
 in a product, an acknowledgment in the product documentation would be
 appreciated but is not required.
2. Altered source versions must be plainly marked as such, and must not be
 misrepresented as being the original software.
3. This notice may not be removed or altered from any source distribution.
*/

using System.Diagnostics;
using System.Threading;

namespace DotRecast.Detour.Crowd
{
	///////////////////////////////////////////////////////////////////////////

	// This section contains detailed documentation for members that don't have
	// a source file. It reduces clutter in the main section of the header.

	/**

	@defgroup crowd Crowd

	Members in this module implement local steering and dynamic avoidance features.

	The crowd is the big beast of the navigation features. It not only handles a
	lot of the path management for you, but also local steering and dynamic
	avoidance between members of the crowd. I.e. It can keep your agents from
	running into each other.

	Main class: #dtCrowd

	The #dtNavMeshQuery and #dtPathCorridor classes provide perfectly good, easy
	to use path planning features. But in the end they only give you points that
	your navigation client should be moving toward. When it comes to deciding things
	like agent velocity and steering to avoid other agents, that is up to you to
	implement. Unless, of course, you decide to use #dtCrowd.

	Basically, you add an agent to the crowd, providing various configuration
	settings such as maximum speed and acceleration. You also provide a local
	target to more toward. The crowd manager then provides, with every update, the
	new agent position and velocity for the frame. The movement will be
	constrained to the navigation mesh, and steering will be applied to ensure
	agents managed by the crowd do not collide with each other.

	This is very powerful feature set. But it comes with limitations.

	The biggest limitation is that you must give control of the agent's position
	completely over to the crowd manager. You can update things like maximum speed
	and acceleration. But in order for the crowd manager to do its thing, it can't
	allow you to constantly be giving it overrides to position and velocity. So
	you give up direct control of the agent's movement. It belongs to the crowd.

	The second biggest limitation revolves around the fact that the crowd manager
	deals with local planning. So the agent's target should never be more than
	256 polygons aways from its current position. If it is, you risk
	your agent failing to reach its target. So you may still need to do long
	distance planning and provide the crowd manager with intermediate targets.

	Other significant limitations:

	- All agents using the crowd manager will use the same #dtQueryFilter.
	- Crowd management is relatively expensive. The maximum agents under crowd
	  management at any one time is between 20 and 30.  A good place to start
	  is a maximum of 25 agents for 0.5ms per frame.

	@note This is a summary list of members.  Use the index or search
	feature to find minor members.

	@struct dtCrowdAgentParams
	@see dtCrowdAgent, dtCrowd::addAgent(), dtCrowd::updateAgentParameters()

	@var dtCrowdAgentParams::obstacleAvoidanceType
	@par

	#dtCrowd permits agents to use different avoidance configurations.  This value
	is the index of the #dtObstacleAvoidanceParams within the crowd.

	@see dtObstacleAvoidanceParams, dtCrowd::setObstacleAvoidanceParams(),
		 dtCrowd::getObstacleAvoidanceParams()

	@var dtCrowdAgentParams::collisionQueryRange
	@par

	Collision elements include other agents and navigation mesh boundaries.

	This value is often based on the agent radius and/or maximum speed. E.g. radius * 8

	@var dtCrowdAgentParams::separationWeight
	@par

	A higher value will result in agents trying to stay farther away from each other at
	the cost of more difficult steering in tight spaces.
	*/
	/// Provides local steering behaviors for a group of agents. 
	/// @ingroup crowd
	internal class DtCrowd
	{
		private volatile int _agentIdx;
		private readonly Dictionary<int, DtCrowdAgent> _agents;
		private readonly List<DtCrowdAgent> _activeAgents;

		private readonly DtPathQueue _pathQ;

		private readonly DtObstacleAvoidanceParams[] _obstacleQueryParams;
		private readonly DtObstacleAvoidanceQuery[] _obstacleQueries;
		private readonly List<Task> _activeAgentTasks;

		private readonly DtProximityGrid _grid;

		private int _maxPathResult;
		internal readonly Vector3 _agentPlacementHalfExtents;

		private readonly DtCrowdConfig _config;

		private DtNavMeshQuery _navQuery;

		private DtNavMesh _navMesh;

		public DtCrowd( DtCrowdConfig config, DtNavMesh nav )
		{
			_config = config;
			_config.defaultFilter ??= new DtQueryDefaultFilter( nav );
			_agentPlacementHalfExtents = new Vector3( config.maxAgentRadius * 2.1f, config.maxAgentHeight * 1.51f, config.maxAgentRadius * 2.1f );

			_obstacleQueries = new DtObstacleAvoidanceQuery[config.obstacleAvoidanceParallelism];
			for ( int i = 0; i < config.obstacleAvoidanceParallelism; ++i )
			{
				_obstacleQueries[i] = new DtObstacleAvoidanceQuery( config.maxObstacleAvoidanceCircles, config.maxObstacleAvoidanceSegments );
			}

			// Init obstacle query option.
			_obstacleQueryParams = new DtObstacleAvoidanceParams[DtCrowdConst.DT_CROWD_MAX_OBSTAVOIDANCE_PARAMS];
			for ( int i = 0; i < DtCrowdConst.DT_CROWD_MAX_OBSTAVOIDANCE_PARAMS; ++i )
			{
				_obstacleQueryParams[i] = new DtObstacleAvoidanceParams();
			}

			// Allocate temp buffer for merging paths.
			_maxPathResult = DtCrowdConst.MAX_PATH_RESULT;
			_pathQ = new DtPathQueue( config );
			_agentIdx = 0;
			_agents = new Dictionary<int, DtCrowdAgent>();
			_activeAgents = new List<DtCrowdAgent>();
			_activeAgentTasks = new List<Task>();
			_grid = new DtProximityGrid( _config.maxAgentRadius * 3 );

			// The navQuery is mostly used for local searches, no need for large node pool.
			SetNavMesh( nav );
		}

		public void SetNavMesh( DtNavMesh nav )
		{
			_navMesh = nav;
			_navQuery = new DtNavMeshQuery( nav );
		}

		public DtNavMesh GetNavMesh()
		{
			return _navMesh;
		}

		public DtNavMeshQuery GetNavMeshQuery()
		{
			return _navQuery;
		}

		public IDtQueryFilter GetDefaultFilter()
		{
			return _config.defaultFilter;
		}

		/// Sets the shared avoidance configuration for the specified index.
		/// @param[in] idx The index. [Limits: 0 &lt;= value &lt; #DT_CROWD_MAX_OBSTAVOIDANCE_PARAMS]
		/// @param[in] option The new configuration.
		public void SetObstacleAvoidanceParams( int idx, DtObstacleAvoidanceParams option )
		{
			if ( idx >= 0 && idx < DtCrowdConst.DT_CROWD_MAX_OBSTAVOIDANCE_PARAMS )
			{
				_obstacleQueryParams[idx] = option;
			}
		}

		/// Gets the shared avoidance configuration for the specified index.
		/// @param[in] idx The index of the configuration to retreive.
		/// [Limits: 0 &lt;= value &lt; #DT_CROWD_MAX_OBSTAVOIDANCE_PARAMS]
		/// @return The requested configuration.
		public DtObstacleAvoidanceParams GetObstacleAvoidanceParams( int idx )
		{
			if ( idx >= 0 && idx < DtCrowdConst.DT_CROWD_MAX_OBSTAVOIDANCE_PARAMS )
			{
				return _obstacleQueryParams[idx];
			}

			return new DtObstacleAvoidanceParams();
		}

		/// Updates the specified agent's configuration.
		/// @param[in] idx The agent index. [Limits: 0 &lt;= value &lt; #GetAgentCount()]
		/// @param[in] params The new agent configuration.
		public void UpdateAgentParameters( DtCrowdAgent agent, DtCrowdAgentParams option )
		{
			agent.option = option;
		}

		/// @par
		///
		/// The agent's position will be constrained to the surface of the navigation mesh.
		/// Adds a new agent to the crowd.
		///  @param[in]		pos		The requested position of the agent. [(x, y, z)]
		///  @param[in]		params	The configuration of the agent.
		/// @return The index of the agent in the agent pool. Or -1 if the agent could not be added.
		public DtCrowdAgent AddAgent( Vector3 pos, DtCrowdAgentParams option )
		{
			var idx = Interlocked.Increment( ref _agentIdx );
			idx = idx - 1;

			DtCrowdAgent ag = new DtCrowdAgent( idx );
			ag.corridor.Init( _maxPathResult );
			AddAgent( ag );
			UpdateAgentParameters( ag, option );

			// Find nearest position on navmesh and place the agent there.
			var status = _navQuery.FindNearestPoly( pos, _agentPlacementHalfExtents, ag.option.filter, out var refs, out var nearestPt, out var _ );
			if ( status.Failed() )
			{
				nearestPt = pos;
				refs = 0;
			}

			ag.corridor.Reset( refs, nearestPt );
			ag.boundary.Reset();
			ag.partial = false;

			// Randomize in case we add a lot of agents in the same frame
			ag.timeSinceTopologyOptimization = Random.Shared.Float( 0, _config.topologyOptimizationTimeThreshold );
			ag.timeSinceLastTargetReplan = 0;
			ag.timeSinceLastRecoveryCheck = 0;
			ag.nneis = 0;

			ag.dvel = Vector3.Zero;
			ag.nvel = Vector3.Zero;
			ag.vel = Vector3.Zero;
			ag.npos = nearestPt;

			ag.desiredSpeed = 0;

			if ( refs != 0 )
			{
				ag.state = DtCrowdAgentState.DT_CROWDAGENT_STATE_WALKING;
			}
			else
			{
				ag.state = DtCrowdAgentState.DT_CROWDAGENT_STATE_INVALID;
			}

			ag.targetState = DtMoveRequestState.DT_CROWDAGENT_TARGET_NONE;

			return ag;
		}

		public void SetAgentPosition( DtCrowdAgent ag, Vector3 pos )
		{
			// Set to a high value to trigger invalidation check immediately
			ag.timeSinceLastRecoveryCheck = float.MaxValue;
			ag.npos = pos;
		}

		public DtCrowdAgent GetAgent( int idx )
		{
			return _agents.GetValueOrDefault( idx );
		}

		// Add the agent from the crowd.
		public void AddAgent( DtCrowdAgent agent )
		{
			if ( _agents.TryAdd( agent.idx, agent ) )
			{
				_activeAgents.Add( agent );
			}
		}

		// Removes the agent from the crowd.
		public void RemoveAgent( DtCrowdAgent agent )
		{
			if ( _agents.Remove( agent.idx ) )
			{
				_activeAgents.Remove( agent );
			}
		}

		private bool RequestMoveTargetReplan( DtCrowdAgent ag, long refs, Vector3 pos )
		{
			ag.SetTarget( refs, pos );
			ag.targetReplan = true;
			return true;
		}

		/// Submits a new move request for the specified agent.
		/// @param[in] idx The agent index. [Limits: 0 &lt;= value &lt; #GetAgentCount()]
		/// @param[in] ref The position's polygon reference.
		/// @param[in] pos The position within the polygon. [(x, y, z)]
		/// @return True if the request was successfully submitted.
		///
		/// This method is used when a new target is set.
		///
		/// The position will be constrained to the surface of the navigation mesh.
		///
		/// The request will be processed during the next #Update().
		public bool RequestMoveTarget( DtCrowdAgent agent, long refs, Vector3 pos )
		{
			if ( refs == 0 )
			{
				return false;
			}

			// Initialize request.
			agent.SetTarget( refs, pos );
			agent.targetReplan = false;
			return true;
		}

		/// Submits a new move request for the specified agent.
		/// @param[in] idx The agent index. [Limits: 0 &lt;= value &lt; #GetAgentCount()]
		/// @param[in] vel The movement velocity. [(x, y, z)]
		/// @return True if the request was successfully submitted.
		public bool RequestMoveVelocity( DtCrowdAgent agent, Vector3 vel )
		{
			// Initialize request.
			agent.targetRef = 0;
			agent.targetPos = vel;
			agent.targetPathQueryResult = null;
			agent.targetReplan = false;
			agent.targetState = DtMoveRequestState.DT_CROWDAGENT_TARGET_VELOCITY;

			return true;
		}

		/// Resets any request for the specified agent.
		/// @param[in] idx The agent index. [Limits: 0 &lt;= value &lt; #GetAgentCount()]
		/// @return True if the request was successfully reseted.
		public bool ResetMoveTarget( DtCrowdAgent agent )
		{
			// Initialize request.
			agent.targetRef = 0;
			agent.targetPos = Vector3.Zero;
			agent.dvel = Vector3.Zero;
			agent.targetPathQueryResult = null;
			agent.targetReplan = false;
			agent.targetState = DtMoveRequestState.DT_CROWDAGENT_TARGET_NONE;
			return true;
		}

		/// Gets the active agents int the agent pool.
		///
		/// @return List of active agents
		public IList<DtCrowdAgent> GetActiveAgents()
		{
			return _activeAgents;
		}

		public Vector3 GetQueryExtents()
		{
			return _agentPlacementHalfExtents;
		}

		public DtProximityGrid GetGrid()
		{
			return _grid;
		}

		public DtPathQueue GetPathQueue()
		{
			return _pathQ;
		}

		public DtCrowdConfig Config()
		{
			return _config;
		}

		public void Update( float dt, DtCrowdAgentDebugInfo debug )
		{
			IList<DtCrowdAgent> agents = GetActiveAgents();

			if ( agents.Count == 0 ) return;

			// Not really worth parallizing on it's own
			foreach ( var agent in agents )
			{
				// Check that all agents still have valid paths.
				CheckPathValidity( agent, dt );
			}

			// Update async move request and path finder.
			UpdateMoveRequest( agents, dt );

			// Optimize path topology.
			UpdateTopologyOptimization( agents, dt );

			// Register agents to proximity grid.
			BuildProximityGrid( agents );

			// Get nearby navmesh segments and agents to collide with.
			BuildNeighbours( agents );

			// Find next corner to steer to.
			FindCorners( agents );

			// Trigger off-mesh connections (depends on corners).
			TriggerOffMeshConnections( agents );

			_activeAgentTasks.Clear();

			int numThreads = Math.Min( Config().obstacleAvoidanceParallelism, agents.Count + 1 );
			var chunkSize = (agents.Count + numThreads - 1) / numThreads;

			for ( int threadId = 0; threadId < numThreads; ++threadId )
			{
				var threadIdCopy = threadId;
				_activeAgentTasks.Add(
					Task.Run( () =>
					{
						// Process agents for this thread
						int startIdx = threadIdCopy * chunkSize;
						int endIdx = Math.Min( (threadIdCopy + 1) * chunkSize, agents.Count );

						for ( int agentIdx = startIdx; agentIdx < endIdx; ++agentIdx )
						{
							var agent = agents[agentIdx];

							// We don't want to do any of this if a navlink took over control over the agent
							if ( agent.state == DtCrowdAgentState.DT_CROWDAGENT_STATE_OFFMESH ) continue;

							// Calculate steering.
							CalculateSteering( agent );
							// Velocity planning.
							PlanVelocity( agent, _obstacleQueries[threadIdCopy] );
							// Integrate.
							Integrate( agent, dt );
						}
					} )
				);
			}
			Task.WaitAll( _activeAgentTasks );

			// Handle collisions.
			HandleCollisions( agents );

			MoveAgents( agents );

			// Update agents using off-mesh connection.
			UpdateOffMeshConnections( agents, dt );
		}


		private void CheckPathValidity( DtCrowdAgent ag, float dt )
		{
			ag.timeSinceLastRecoveryCheck += dt;

			var distanceToCorridorSquared = ag.corridor.GetPos().DistanceSquared( ag.npos );
			var maxDistanceToPathSquared = 4 * ag.option.radius * ag.option.radius;

			// Check if agent has drifted too far from its corridor
			var isDriftedFromCorridor = (ag.state != DtCrowdAgentState.DT_CROWDAGENT_STATE_OFFMESH && distanceToCorridorSquared > maxDistanceToPathSquared);

			const float recoverCheckDelay = 1.0f;
			var needsRecovery = ag.state == DtCrowdAgentState.DT_CROWDAGENT_STATE_INVALID || (isDriftedFromCorridor && ag.timeSinceLastRecoveryCheck > recoverCheckDelay);

			if ( needsRecovery )
			{
				if ( isDriftedFromCorridor ) ag.timeSinceLastRecoveryCheck = 0;

				var status = _navQuery.FindNearestPoly( ag.npos, new Vector3( ag.option.radius ), DtQueryNoOpFilter.Shared, out var refs, out var nearestPt, out var _ );
				if ( status.Succeeded() && refs != 0 )
				{
					// we come from an invalid state so we reset most of the agent's state
					ag.npos = nearestPt;

					ag.corridor.Reset( refs, nearestPt );
					ag.boundary.Reset();
					ag.partial = false;

					ag.timeSinceTopologyOptimization = 0;
					ag.timeSinceLastTargetReplan = 0;
					ag.nneis = 0;
					ag.desiredSpeed = 0;

					ag.dvel = Vector3.Zero;
					ag.nvel = Vector3.Zero;
					ag.vel = Vector3.Zero;
					ag.state = DtCrowdAgentState.DT_CROWDAGENT_STATE_WALKING;

					if ( ag.targetState != DtMoveRequestState.DT_CROWDAGENT_TARGET_NONE && ag.targetState != DtMoveRequestState.DT_CROWDAGENT_TARGET_VELOCITY )
					{
						RequestMoveTargetReplan( ag, ag.targetRef, ag.targetPos );
					}

					return;
				}
			}

			if ( ag.state != DtCrowdAgentState.DT_CROWDAGENT_STATE_WALKING )
			{
				return;
			}

			ag.timeSinceLastTargetReplan += dt;

			bool replan = false;

			// First check that the current location is valid.
			Vector3 agentPos = ag.npos;
			long agentRef = ag.corridor.GetFirstPoly();

			if ( !_navQuery.IsValidPolyRef( agentRef, ag.option.filter ) )
			{
				// Current location is not valid, try to reposition.
				// TODO: this can snap agents, how to handle that?
				_navQuery.FindNearestPoly( ag.npos, _agentPlacementHalfExtents, ag.option.filter, out agentRef, out var nearestPt, out var _ );
				agentPos = nearestPt;

				if ( agentRef == 0 )
				{
					// Could not find location in navmesh, set state to invalid.
					ag.corridor.Reset( 0, agentPos );
					ag.partial = false;
					ag.boundary.Reset();
					ag.state = DtCrowdAgentState.DT_CROWDAGENT_STATE_INVALID;
					return;
				}

				// Make sure the first polygon is valid, but leave other valid
				// polygons in the path so that replanner can adjust the path
				// better.
				ag.corridor.FixPathStart( agentRef, agentPos );
				ag.boundary.Reset();
				ag.npos = agentPos;

				replan = true;
			}

			// If the agent does not have move target or is controlled by
			// velocity, no need to recover the target nor replan.
			if ( ag.targetState == DtMoveRequestState.DT_CROWDAGENT_TARGET_NONE
				|| ag.targetState == DtMoveRequestState.DT_CROWDAGENT_TARGET_VELOCITY )
			{
				return;
			}

			// Try to recover move request position.
			if ( ag.targetState != DtMoveRequestState.DT_CROWDAGENT_TARGET_NONE
				&& ag.targetState != DtMoveRequestState.DT_CROWDAGENT_TARGET_FAILED )
			{
				if ( !_navQuery.IsValidPolyRef( ag.targetRef, ag.option.filter ) )
				{
					// Current target is not valid, try to reposition.
					_navQuery.FindNearestPoly( ag.targetPos, _agentPlacementHalfExtents, ag.option.filter, out ag.targetRef, out var nearestPt, out var _ );
					ag.targetPos = nearestPt;
					replan = true;
				}

				if ( ag.targetRef == 0 )
				{
					// Failed to reposition target, fail moverequest.
					ag.corridor.Reset( agentRef, agentPos );
					ag.partial = false;
					ag.targetState = DtMoveRequestState.DT_CROWDAGENT_TARGET_NONE;
				}
			}

			// If nearby corridor is not valid, replan.
			if ( !ag.corridor.IsValid( _config.checkLookAhead, _navQuery, ag.option.filter ) )
			{
				replan = true;
			}

			// If the end of the path is near and it is not the requested location, replan.
			if ( ag.targetState == DtMoveRequestState.DT_CROWDAGENT_TARGET_VALID )
			{
				if ( ag.timeSinceLastTargetReplan > _config.targetReplanDelay && ag.corridor.GetPathCount() < _config.checkLookAhead
																	&& ag.corridor.GetLastPoly() != ag.targetRef )
				{
					replan = true;
				}
			}

			// Try to replan path to goal.
			if ( replan )
			{
				if ( ag.targetState != DtMoveRequestState.DT_CROWDAGENT_TARGET_NONE )
				{
					RequestMoveTargetReplan( ag, ag.targetRef, ag.targetPos );
				}
			}
		}


		private List<long> reqPathCache = new( 32 );
		PriorityQueue<DtCrowdAgent, float> queue = new();

		private void UpdateMoveRequest( IList<DtCrowdAgent> agents, float dt )
		{
			queue.Clear();
			// Fire off new requests.
			reqPathCache.Clear();
			foreach ( var ag in agents )
			{
				if ( ag.state == DtCrowdAgentState.DT_CROWDAGENT_STATE_INVALID )
				{
					continue;
				}

				if ( ag.targetState == DtMoveRequestState.DT_CROWDAGENT_TARGET_NONE
					|| ag.targetState == DtMoveRequestState.DT_CROWDAGENT_TARGET_VELOCITY )
				{
					continue;
				}

				if ( ag.targetState == DtMoveRequestState.DT_CROWDAGENT_TARGET_REQUESTING )
				{
					List<long> path = ag.corridor.GetPath();
					if ( 0 == path.Count )
					{
						throw new ArgumentException( "Empty path" );
					}


					// Quick search towards the goal.
					_navQuery.InitSlicedFindPath( path[0], ag.targetRef, ag.npos, ag.targetPos, ag.option.filter, 0 );
					_navQuery.UpdateSlicedFindPath( _config.maxTargetFindPathIterations, out var _ );

					DtStatus status;
					if ( ag.targetReplan ) // && npath > 10)
					{
						// Try to use existing steady path during replan if possible.
						status = _navQuery.FinalizeSlicedFindPathPartial( path, path.Count, ref reqPathCache );
					}
					else
					{
						// Try to move towards target when goal changes.
						status = _navQuery.FinalizeSlicedFindPath( ref reqPathCache );
					}

					Vector3 reqPos = new Vector3();
					if ( status.Succeeded() && reqPathCache.Count > 0 )
					{
						// In progress or succeed.
						if ( reqPathCache[reqPathCache.Count - 1] != ag.targetRef )
						{
							// Partial path, constrain target position inside the
							// last polygon.
							var cr = _navQuery.ClosestPointOnPoly( reqPathCache[reqPathCache.Count - 1], ag.targetPos, out reqPos, out var _ );
							if ( cr.Failed() )
							{
								reqPathCache.Clear();
							}
						}
						else
						{
							reqPos = ag.targetPos;
						}
					}
					else
					{
						// Could not find path, start the request from current
						// location.
						reqPos = ag.npos;
						reqPathCache.Clear();
						reqPathCache.Add( path[0] );
					}

					ag.corridor.SetCorridor( reqPos, reqPathCache );
					ag.boundary.Reset();
					ag.partial = false;

					if ( reqPathCache[reqPathCache.Count - 1] == ag.targetRef )
					{
						ag.targetState = DtMoveRequestState.DT_CROWDAGENT_TARGET_VALID;
						ag.timeSinceLastTargetReplan = 0;
					}
					else
					{
						// The path is longer or potentially unreachable, full plan.
						ag.targetState = DtMoveRequestState.DT_CROWDAGENT_TARGET_WAITING_FOR_QUEUE;
					}

					ag.targetReplanWaitTime = 0;
				}

				if ( ag.targetState == DtMoveRequestState.DT_CROWDAGENT_TARGET_WAITING_FOR_QUEUE )
				{
					queue.Enqueue( ag, ag.timeSinceLastTargetReplan );
				}
			}

			while ( queue.TryDequeue( out var ag, out _ ) )
			{
				ag.targetPathQueryResult = _pathQ.Request( ag.corridor.GetLastPoly(), ag.targetRef, ag.corridor.GetTarget(), ag.targetPos, ag.option.filter );
				if ( ag.targetPathQueryResult != null )
				{
					ag.targetState = DtMoveRequestState.DT_CROWDAGENT_TARGET_WAITING_FOR_PATH;
				}
				else
				{
					ag.targetReplanWaitTime += dt;
				}
			}

			// Update requests.
			_pathQ.Update( _navMesh );

			// Process path results.
			foreach ( var ag in agents )
			{
				if ( ag.targetState == DtMoveRequestState.DT_CROWDAGENT_TARGET_NONE
					|| ag.targetState == DtMoveRequestState.DT_CROWDAGENT_TARGET_VELOCITY )
				{
					continue;
				}

				if ( ag.targetState == DtMoveRequestState.DT_CROWDAGENT_TARGET_WAITING_FOR_PATH )
				{
					// _telemetry.RecordPathWaitTime(ag.targetReplanTime);
					// Poll path queue.
					DtStatus status = ag.targetPathQueryResult.status;
					if ( status.Failed() )
					{
						// Path find failed, retry if the target location is still
						// valid.
						ag.targetPathQueryResult = null;
						if ( ag.targetRef != 0 )
						{
							ag.targetState = DtMoveRequestState.DT_CROWDAGENT_TARGET_REQUESTING;
						}
						else
						{
							ag.targetState = DtMoveRequestState.DT_CROWDAGENT_TARGET_FAILED;
						}

						ag.timeSinceLastTargetReplan = 0;
					}
					else if ( status.Succeeded() )
					{
						List<long> path = ag.corridor.GetPath();
						if ( 0 == path.Count )
						{
							throw new ArgumentException( "Empty path" );
						}

						// Apply results.
						var targetPos = ag.targetPos;

						bool valid = true;
						List<long> res = ag.targetPathQueryResult.path;
						if ( status.Failed() || 0 == res.Count )
						{
							valid = false;
						}

						if ( status.IsPartial() )
						{
							ag.partial = true;
						}
						else
						{
							ag.partial = false;
						}

						// Merge result and existing path.
						// The agent might have moved whilst the request is
						// being processed, so the path may have changed.
						// We assume that the end of the path is at the same
						// location
						// where the request was issued.

						// The last ref in the old path should be the same as
						// the location where the request was issued..
						if ( valid && path[path.Count - 1] != res[0] )
						{
							valid = false;
						}

						if ( valid )
						{
							// Put the old path infront of the old path.
							if ( path.Count > 1 )
							{
								path.RemoveAt( path.Count - 1 );
								path.AddRange( res );
								res = path;
								// Remove trackbacks
								for ( int j = 1; j < res.Count - 1; ++j )
								{
									if ( j - 1 >= 0 && j + 1 < res.Count )
									{
										if ( res[j - 1] == res[j + 1] )
										{
											res.RemoveAt( j + 1 );
											res.RemoveAt( j );
											j -= 2;
										}
									}
								}
							}

							// Check for partial path.
							if ( res[res.Count - 1] != ag.targetRef )
							{
								// Partial path, constrain target position inside
								// the last polygon.
								var cr = _navQuery.ClosestPointOnPoly( res[res.Count - 1], targetPos, out var nearest, out var _ );
								if ( cr.Succeeded() )
								{
									targetPos = nearest;
								}
								else
								{
									valid = false;
								}
							}
						}

						if ( valid )
						{
							// Set current corridor.
							ag.corridor.SetCorridor( targetPos, res );
							// Force to update boundary.
							ag.boundary.Reset();
							ag.targetState = DtMoveRequestState.DT_CROWDAGENT_TARGET_VALID;
						}
						else
						{
							// Something went wrong.
							ag.targetState = DtMoveRequestState.DT_CROWDAGENT_TARGET_FAILED;
						}

						ag.timeSinceLastTargetReplan = 0;
					}

					ag.targetReplanWaitTime += dt;
				}
			}
		}

		private void UpdateTopologyOptimization( IList<DtCrowdAgent> agents, float dt )
		{
			foreach ( var ag in agents )
			{
				if ( ag.state != DtCrowdAgentState.DT_CROWDAGENT_STATE_WALKING )
				{
					continue;
				}

				if ( ag.targetState == DtMoveRequestState.DT_CROWDAGENT_TARGET_NONE
					|| ag.targetState == DtMoveRequestState.DT_CROWDAGENT_TARGET_VELOCITY )
				{
					continue;
				}

				if ( (ag.option.updateFlags & DtCrowdAgentUpdateFlags.DT_CROWD_OPTIMIZE_TOPO) == 0 )
				{
					continue;
				}

				ag.timeSinceTopologyOptimization += dt;
				if ( ag.timeSinceTopologyOptimization >= _config.topologyOptimizationTimeThreshold )
				{
					ag.corridor.OptimizePathTopology( _navQuery, ag.option.filter, _config.maxTopologyOptimizationIterations );
					ag.timeSinceTopologyOptimization = 0;
				}
			}
		}

		private void BuildProximityGrid( IList<DtCrowdAgent> agents )
		{
			_grid.Clear();

			for ( var i = 0; i < agents.Count; i++ )
			{
				var ag = agents[i];
				Vector3 p = ag.npos;
				float r = ag.option.radius;
				_grid.AddItem( ag, p.x - r, p.z - r, p.x + r, p.z + r );
			}
		}

		private void BuildNeighbours( IList<DtCrowdAgent> agents )
		{
			for ( var i = 0; i < agents.Count; i++ )
			{
				var ag = agents[i];
				if ( ag.state != DtCrowdAgentState.DT_CROWDAGENT_STATE_WALKING )
				{
					continue;
				}

				// Update the collision boundary after certain distance has been passed or
				// if it has become invalid.
				float updateThr = ag.option.collisionQueryRange * 0.25f;
				float updateThrSqr = updateThr * updateThr;
				if ( DtUtils.DistanceBetween2DSqr( ag.npos, ag.boundary.GetCenter() ) > updateThrSqr
					|| !ag.boundary.IsValid( _navQuery, ag.option.filter ) )
				{
					ag.boundary.Update( ag.corridor.GetFirstPoly(), ag.npos, ag.option.collisionQueryRange, _navQuery, ag.option.filter );
				}

				// Query neighbour agents
				ag.nneis = GetNeighbours( ag.npos, ag.option.height, ag.option.collisionQueryRange, ag, ag.neis, DtCrowdConst.DT_CROWDAGENT_MAX_NEIGHBOURS, _grid );
			}
		}

		public static int AddNeighbour( DtCrowdAgent idx, float dist, Span<DtCrowdNeighbour> neis, int nneis, int maxNeis )
		{
			// Insert neighbour based on the distance.
			int nei = 0;
			if ( 0 == nneis )
			{
				nei = nneis;
			}
			else if ( dist >= neis[nneis - 1].dist )
			{
				if ( nneis >= maxNeis )
					return nneis;
				nei = nneis;
			}
			else
			{
				int i;
				for ( i = 0; i < nneis; ++i )
				{
					if ( dist <= neis[i].dist )
					{
						break;
					}
				}

				int tgt = i + 1;
				int n = Math.Min( nneis - i, maxNeis - tgt );

				Debug.Assert( tgt + n <= maxNeis );

				if ( n > 0 )
				{
					neis.Slice( i, n ).CopyTo( neis.Slice( tgt, n ) );
				}

				nei = i;
			}

			neis[nei] = new DtCrowdNeighbour( idx, dist );

			return Math.Min( nneis + 1, maxNeis );
		}

		private int GetNeighbours( Vector3 pos, float height, float range, DtCrowdAgent skip, Span<DtCrowdNeighbour> result, int maxResult, DtProximityGrid grid )
		{
			int n = 0;

			float rangeSqr = range * range;
			const int MAX_NEIS = 32;
			Span<int> ids = stackalloc int[MAX_NEIS];
			int nids = grid.QueryItems( pos.x - range, pos.z - range,
				pos.x + range, pos.z + range,
				ids, ids.Length );

			for ( int i = 0; i < nids; ++i )
			{
				var ag = GetAgent( ids[i] );
				if ( ag == skip )
				{
					continue;
				}

				// Check for overlap.
				Vector3 diff = pos - ag.npos;
				if ( MathF.Abs( diff.y ) >= (height + ag.option.height) / 2.0f )
				{
					continue;
				}

				diff.y = 0;
				float distSqr = diff.LengthSquared;
				if ( distSqr > rangeSqr )
				{
					continue;
				}

				n = AddNeighbour( ag, distSqr, result, n, maxResult );
			}

			return n;
		}

		private void FindCorners( IList<DtCrowdAgent> agents )
		{
			foreach ( var ag in agents )
			{
				if ( ag.targetState == DtMoveRequestState.DT_CROWDAGENT_TARGET_NONE
				|| ag.targetState == DtMoveRequestState.DT_CROWDAGENT_TARGET_VELOCITY )
				{
					continue;
				}

				// Find corners for steering
				ag.ncorners = ag.corridor.FindCorners( ag.corners, DtCrowdConst.DT_CROWDAGENT_MAX_CORNERS, _navQuery, ag.option.filter );
			}
		}

		private void TriggerOffMeshConnections( IList<DtCrowdAgent> agents )
		{
			Span<long> refs = stackalloc long[2];
			foreach ( var ag in agents )
			{
				if ( ag.state != DtCrowdAgentState.DT_CROWDAGENT_STATE_WALKING ) continue;

				if ( ag.targetState == DtMoveRequestState.DT_CROWDAGENT_TARGET_NONE
				|| ag.targetState == DtMoveRequestState.DT_CROWDAGENT_TARGET_VELOCITY )
				{
					continue;
				}

				// Check
				float triggerRadius = ag.option.radius;
				if ( ag.OverOffmeshConnection( triggerRadius ) )
				{

					// Prepare to off-mesh connection.
					DtCrowdAgentAnimation anim = ag.animation;

					// Adjust the path over the off-mesh connection.
					if ( ag.corridor.MoveOverOffmeshConnection( ag.corners[ag.ncorners - 1].refs, refs, ref anim.startPos,
							ref anim.endPos, _navQuery ) )
					{
						anim.initPos = ag.npos;
						anim.polyRef = refs[1];
						anim.active = true;
						anim.t = 0.0f;
						anim.tmax = DtUtils.DistanceBetween2D( anim.startPos, anim.endPos ) / ag.option.maxSpeed;
						anim.prevFramePos = anim.initPos;

						ag.state = DtCrowdAgentState.DT_CROWDAGENT_STATE_OFFMESH;
						ag.ncorners = 0;
						ag.nneis = 0;
						return;
					}
					else
					{
						// Path validity check will ensure that bad/blocked connections will be replanned.
					}
				}
			}
		}

		private void CalculateSteering( DtCrowdAgent ag )
		{
			if ( ag.targetState == DtMoveRequestState.DT_CROWDAGENT_TARGET_NONE )
			{
				return;
			}

			Vector3 dvel = new Vector3();

			if ( ag.targetState == DtMoveRequestState.DT_CROWDAGENT_TARGET_VELOCITY )
			{
				dvel = ag.targetPos;
				ag.desiredSpeed = ag.targetPos.Length;
			}
			else
			{
				// Calculate steering direction.
				if ( (ag.option.updateFlags & DtCrowdAgentUpdateFlags.DT_CROWD_ANTICIPATE_TURNS) != 0 )
				{
					dvel = ag.CalcSmoothSteerDirection();
				}
				else
				{
					dvel = ag.CalcStraightSteerDirection();
				}

				// Calculate speed scale, which tells the agent to slowdown at the end of the path.

				float velocitySquared = ag.vel.LengthSquared;
				// v^2 / 2 * a = d
				float decelerationDistance = velocitySquared / (2.0f * ag.option.maxAcceleration);

				// Radius after which we shoulds start checking for deceleration
				float maxDecelerationDistance = (ag.option.maxSpeed * ag.option.maxSpeed) / (2.0f * ag.option.maxAcceleration);

				float distanceToGoal = ag.GetDistanceToGoal( maxDecelerationDistance * 2f );

				// Avoid oscillation by stopping completly once we reached the goal
				// If majority of our cylinder is already inside the goal, stop
				if ( distanceToGoal <= ag.option.radius / 4 )
				{
					ag.desiredSpeed = 0f;
				}
				// If distance to goal is smaller than the distance we need to decelerate to 0
				// start decelerating so we can actually make it
				else if ( distanceToGoal <= decelerationDistance + 0.5f )
				{
					// speed we need to get to 0 until we reach the goal
					ag.desiredSpeed = Math.Min( ag.option.maxSpeed, MathF.Sqrt( 2.0f * ag.option.maxAcceleration * distanceToGoal ) );
				}
				else
				{
					ag.desiredSpeed = ag.option.maxSpeed;
				}

				dvel = dvel * ag.desiredSpeed;
			}

			// Separation
			if ( (ag.option.updateFlags & DtCrowdAgentUpdateFlags.DT_CROWD_SEPARATION) != 0 )
			{
				float separationDist = ag.option.collisionQueryRange;
				float invSeparationDist = 1.0f / separationDist;
				float separationWeight = ag.option.separationWeight;

				float w = 0;
				Vector3 disp = new Vector3();

				for ( int j = 0; j < ag.nneis; ++j )
				{
					DtCrowdAgent nei = ag.neis[j].agent;

					Vector3 diff = ag.npos - nei.npos;
					diff.y = 0;

					float distSqr = diff.LengthSquared;
					if ( distSqr < 0.00001f )
					{
						continue;
					}

					if ( distSqr > separationDist * separationDist )
					{
						continue;
					}

					float dist = MathF.Sqrt( distSqr );
					float weight = separationWeight * (1.0f - (dist * invSeparationDist) * (dist * invSeparationDist));

					disp = disp + diff * (weight / dist);
					w += 1.0f;
				}

				if ( w > 0.0001f )
				{
					// Adjust desired velocity.
					dvel = dvel + disp * (1.0f / w);
					// Clamp desired velocity to desired speed.
					float speedSqr = dvel.LengthSquared;
					float desiredSqr = ag.desiredSpeed * ag.desiredSpeed;
					if ( speedSqr > desiredSqr )
					{
						dvel = dvel * (desiredSqr / speedSqr);
					}
				}
			}

			// Set the desired velocity.
			ag.dvel = dvel;
		}

		private void PlanVelocity( DtCrowdAgent ag, DtObstacleAvoidanceQuery obstacleQuery )
		{
			if ( (ag.option.updateFlags & DtCrowdAgentUpdateFlags.DT_CROWD_OBSTACLE_AVOIDANCE) != 0 )
			{
				obstacleQuery.Reset();
				// Add neighbours as obstacles.
				for ( int j = 0; j < ag.nneis; ++j )
				{
					DtCrowdAgent nei = ag.neis[j].agent;
					obstacleQuery.AddCircle( nei.npos, nei.option.radius, nei.vel, nei.dvel );
				}

				// A goal on the navmesh boundary sits on a wall segment, and avoidance won't steer into a
				// wall. Suppress the stretch within a radius of the goal, clipping rather than dropping as
				// segments are polygon edges and can span a tile. targetPos is a velocity unless VALID.
				bool clipSegmentsAtGoal = ag.targetState == DtMoveRequestState.DT_CROWDAGENT_TARGET_VALID;
				float goalSegmentRangeSqr = ag.option.radius * ag.option.radius;

				// Append neighbour segments as obstacles.
				for ( int j = 0; j < ag.boundary.GetSegmentCount(); ++j )
				{
					Span<Vector3> s = ag.boundary.GetSegment( j );
					Vector3 s3 = s[1];
					//RcArrays.Copy(s, 3, s3, 0, 3);
					if ( DtUtils.TriArea2D( ag.npos, s[0], s3 ) < 0.0f )
					{
						continue;
					}

					if ( clipSegmentsAtGoal )
					{
						var goalDistSqr = DtUtils.DistancePtSegSqr2D( ag.targetPos, s[0], s3, out var goalT );
						if ( goalDistSqr < goalSegmentRangeSqr )
						{
							var segLengthSqr = DtUtils.DistanceBetween2DSqr( s[0], s3 );
							if ( segLengthSqr < 0.0001f )
							{
								continue;
							}

							// Half chord the goal circle cuts from the segment, as a fraction.
							var halfChord = MathF.Sqrt( (goalSegmentRangeSqr - goalDistSqr) / segLengthSqr );
							var tMin = goalT - halfChord;
							var tMax = goalT + halfChord;

							if ( tMin > 0.0f )
							{
								obstacleQuery.AddSegment( s[0], Vector3.Lerp( s[0], s3, tMin ) );
							}

							if ( tMax < 1.0f )
							{
								obstacleQuery.AddSegment( Vector3.Lerp( s[0], s3, tMax ), s3 );
							}

							continue;
						}
					}

					obstacleQuery.AddSegment( s[0], s3 );
				}


				// Sample new safe velocity.
				bool adaptive = true;
				int ns = 0;

				ref DtObstacleAvoidanceParams option = ref _obstacleQueryParams[ag.option.obstacleAvoidanceType];

				if ( adaptive )
				{
					ns = obstacleQuery.SampleVelocityAdaptive( ag.npos, ag.option.radius, ag.desiredSpeed,
						ag.vel, ag.dvel, out ag.nvel, option );
				}
				else
				{
					ns = obstacleQuery.SampleVelocityGrid( ag.npos, ag.option.radius,
						ag.desiredSpeed, ag.vel, ag.dvel, out ag.nvel, option );
				}
			}
			else
			{
				// If not using velocity planning, new velocity is directly the desired velocity.
				ag.nvel = ag.dvel;
			}
		}

		private void Integrate( DtCrowdAgent ag, float dt )
		{
			ag.Integrate( dt );
		}

		private void HandleCollisions( IList<DtCrowdAgent> agents )
		{
			for ( int iter = 0; iter < 4; ++iter )
			{
				for ( var i = 0; i < agents.Count; i++ )
				{
					var ag = agents[i];
					long idx0 = ag.idx;
					if ( ag.state != DtCrowdAgentState.DT_CROWDAGENT_STATE_WALKING )
					{
						continue;
					}

					ag.disp = Vector3.Zero;

					float w = 0;

					for ( int j = 0; j < ag.nneis; ++j )
					{
						DtCrowdAgent nei = ag.neis[j].agent;
						long idx1 = nei.idx;
						Vector3 diff = ag.npos - nei.npos;
						diff.y = 0;

						float distSqr = diff.LengthSquared;
						if ( distSqr > (ag.option.radius + nei.option.radius) * (ag.option.radius + nei.option.radius) )
						{
							continue;
						}

						var dist = MathF.Sqrt( distSqr );
						float pen = (ag.option.radius + nei.option.radius) - dist;
						if ( dist < 0.0001f )
						{
							// Agents on top of each other, try to choose diverging separation directions.
							if ( idx0 > idx1 )
							{
								diff = new Vector3( -ag.dvel.z, 0, ag.dvel.x );
							}
							else
							{
								diff = new Vector3( ag.dvel.z, 0, -ag.dvel.x );
							}

							pen = 0.01f;
						}
						else
						{
							pen = (1.0f / dist) * (pen * 0.5f) * _config.collisionResolveFactor;
						}

						ag.disp = ag.disp + diff * pen;

						w += 1.0f;
					}

					if ( w > 0.0001f )
					{
						float iw = 1.0f / w;
						ag.disp = ag.disp * iw;
					}
				}

				for ( var i = 0; i < agents.Count; i++ )
				{
					var ag = agents[i];
					if ( ag.state != DtCrowdAgentState.DT_CROWDAGENT_STATE_WALKING )
					{
						continue;
					}

					ag.npos = ag.npos + ag.disp;
				}
			}
		}

		private void MoveAgents( IList<DtCrowdAgent> agents )
		{
			for ( var i = 0; i < agents.Count; i++ )
			{
				var ag = agents[i];
				if ( ag.state != DtCrowdAgentState.DT_CROWDAGENT_STATE_WALKING )
				{
					continue;
				}

				// Move along navmesh.
				ag.corridor.MovePosition( ag.npos, _navQuery, ag.option.filter );
				// Get valid constrained position back.
				ag.npos = ag.corridor.GetPos();

				// If not using path, truncate the corridor to just one poly.
				if ( ag.targetState == DtMoveRequestState.DT_CROWDAGENT_TARGET_NONE
					|| ag.targetState == DtMoveRequestState.DT_CROWDAGENT_TARGET_VELOCITY )
				{
					ag.corridor.Reset( ag.corridor.GetFirstPoly(), ag.npos );
					ag.partial = false;
				}
			}
		}

		private void UpdateOffMeshConnections( IList<DtCrowdAgent> agents, float dt )
		{
			for ( var i = 0; i < agents.Count; i++ )
			{
				var ag = agents[i];
				DtCrowdAgentAnimation anim = ag.animation;
				if ( !anim.active )
				{
					continue;
				}

				if ( ag.option.autoTraverseOffMeshLink )
				{
					anim.t += dt;
					if ( anim.t > anim.tmax )
					{
						CompleteLink( ag );
						continue;
					}

					// Update position
					float ta = anim.tmax * 0.15f;
					float tb = anim.tmax;
					Vector3 newPos = Vector3.Zero;
					if ( anim.t < ta )
					{
						float u = Tween( anim.t, 0.0f, ta );
						newPos = Vector3.Lerp( anim.initPos, anim.startPos, u );
					}
					else
					{
						float u = Tween( anim.t, ta, tb );
						newPos = Vector3.Lerp( anim.startPos, anim.endPos, u );
					}

					anim.prevFramePos = ag.npos;
					ag.npos = newPos;

					ag.vel = (ag.npos - anim.prevFramePos) / dt;
					ag.dvel = ag.vel;
				}
				else
				{
					// position has been modified by the user, update velocity
					ag.vel = (ag.npos - anim.prevFramePos) / dt;
					ag.dvel = (ag.npos - anim.prevFramePos) / dt;

					anim.prevFramePos = ag.npos;
				}
			}
		}

		public void CompleteLink( DtCrowdAgent ag )
		{
			// Reset animation
			ag.animation.active = false;
			// Prepare agent for walking.
			ag.state = DtCrowdAgentState.DT_CROWDAGENT_STATE_WALKING;
		}

		private float Tween( float t, float t0, float t1 )
		{
			return Math.Clamp( (t - t0) / (t1 - t0), 0.0f, 1.0f );
		}
	}
}
