namespace Sandbox;

/// <summary>
/// Updates NavMeshAgent ground positions in parallel during PrePhysicsStep.
/// </summary>
internal sealed class NavMeshGameSystem : GameObjectSystem
{
	public NavMeshGameSystem( Scene scene ) : base( scene )
	{
		// Listen to StartFixedUpdate to run before physics
		Listen( Stage.StartFixedUpdate, -100, UpdateAgentGoundZ, "UpdateAgentGroundZ" );
	}

	void UpdateAgentGoundZ()
	{
		var agents = Scene.GetAll<NavMeshAgent>();
		if ( agents.Count() == 0 ) return;

		Sandbox.Utility.Parallel.ForEach( agents, FindPhysicsGroundZ );
	}

	/// <summary>
	/// We are tracing in the following interval (scale not accurate)
	///     x
	///     |
	///     |     We start a certain distance above the agents capsules center
	///     |
	///     | 
	///     |
	///  -------
	///  |  |  | 
	///  |  x  | -- Trace Start (in lower third)
	///  |  |  |
	///  -------
	///     |
	///     |
	///  ~~~~~~~ -- Potential ground
	///     |
	///     |
	///     |
	///     x      We trace down the same distance
	///          
	/// In case of multiple hits we prefer the once closest to the agent's capsule center
	/// </summary>
	private void FindPhysicsGroundZ( NavMeshAgent agent )
	{
		if ( agent.agentInternal == null ) return;

		if ( agent.timeUntilNextGroundTrace > 0f )
		{
			return;
		}

		// Introduce some random jitter so not all agents trace on the same frame
		agent.timeUntilNextGroundTrace = Random.Shared.Int( 2, 4 ) * Time.Delta;

		var footRadius = agent.agentInternal.option.radius * 0.1f;
		var traceStartOffset = MathF.Max( 64f, Scene.NavMesh.AgentHeight ) * 8f;

		var navMeshPos = agent.AgentPosition;

		var traceStart = navMeshPos + Vector3.Up * Scene.NavMesh.AgentHeight * 0.3f;

		var baseTrace =
			Scene.Trace
				.IgnoreDynamic()
				.IgnoreGameObjectHierarchy( agent.GameObject )
				.WithAnyTags( Scene.NavMesh.IncludedBodies )
				.WithCollisionRules( agent.Tags )
				.WithoutTags( Scene.NavMesh.ExcludedBodies );

		if ( !Scene.NavMesh.IncludeStaticBodies )
		{
			baseTrace = baseTrace.IgnoreStatic();
		}
		if ( !Scene.NavMesh.IncludeKeyframedBodies )
		{
			baseTrace = baseTrace.IgnoreKeyframed();
		}

		var downTrace = baseTrace.Sphere( footRadius, traceStart, traceStart + Vector3.Down * traceStartOffset );
		var upTrace = baseTrace.Sphere( footRadius, traceStart, traceStart + Vector3.Up * traceStartOffset );

		var downResult = downTrace.Run();
		var upResult = upTrace.Run();

		var bestZ = 0f;
		var closestDistanceToTraceStart = float.MaxValue;

		// Process downTrace result
		if ( downResult.Hit )
		{
			var distance = MathF.Abs( downResult.HitPosition.z - traceStart.z );
			if ( distance < closestDistanceToTraceStart )
			{
				bestZ = downResult.HitPosition.z;
				closestDistanceToTraceStart = distance;
			}
		}

		// Process upTrace result
		if ( upResult.Hit )
		{
			var distance = MathF.Abs( upResult.HitPosition.z - traceStart.z );
			if ( distance < closestDistanceToTraceStart )
			{
				bestZ = upResult.HitPosition.z;
			}
		}

		agent.groundTraceZ = bestZ;
	}
}
