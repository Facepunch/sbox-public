using DotRecast.Detour;
using DotRecast.Detour.Crowd;
using Sandbox.Engine.Resources;
using Sandbox.Navigation;
using System.Buffers;

namespace Sandbox;

/// <summary>
/// An agent that can navigate the navmesh defined in the scene.
/// </summary>
[Expose]
[Title( "NavMesh - Agent" )]
[Category( "Navigation" )]
[Icon( "smart_toy" )]
[EditorHandle( "materials/gizmo/navmeshagent.png" )]
[Alias( "NavAgent" )]
public sealed class NavMeshAgent : Component
{
	[Group( "Physical Properties" )]
	[Property]
	public float Height
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;

			UpdateAgentParameters();
		}
	} = 64;

	[Group( "Physical Properties" )]
	[Property]
	public float Radius
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;

			UpdateAgentParameters();
		}
	} = 16;

	[Group( "Movement" )]
	[Property]
	public float MaxSpeed
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;

			UpdateAgentParameters();
		}
	} = 120f;

	/// <summary>
	/// The maximum acceleration a agent can have. This is how fast the agent can change its velocity.
	/// If you want snappy movement this should be as high or higher than <see cref="MaxSpeed"/>.
	/// </summary>
	[Group( "Movement" )]
	[Property]
	public float Acceleration
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;

			UpdateAgentParameters();
		}
	} = 120f;

	/// <summary>
	/// Set the Position of the GameObject to the agent position every frame. You can turn this off and handle it yourself by using the AgentPosition property.
	/// </summary>
	[Group( "Movement" ), Title( "Update GameObject Position" )]
	[Property]
	public bool UpdatePosition { get; set; } = true;

	/// <summary>
	/// This will simply face the direction it is moving. It is not configurable on purpose, so you should really turn this off and be doing this yourself if you need it to do anything specific.
	/// </summary>
	[Group( "Movement" ), Title( "Update GameObject Rotation" )]
	[Property]
	public bool UpdateRotation { get; set; } = false;

	/// <summary>
	/// What areas the agent is allowed to travel on. If empty, all areas are allowed.
	/// </summary>
	[Group( "Constraints" )]
	[Property]
	public HashSet<NavMeshAreaDefinition> AllowedAreas { get; set; } = new();

	/// <summary>
	/// What areas the agent is not allowed to travel on. If empty, no areas are forbidden.
	/// </summary>
	[Group( "Constraints" )]
	[Property]
	public HashSet<NavMeshAreaDefinition> ForbiddenAreas { get; set; } = new();

	/// <summary>
	/// Is the agent allowed to travel on the default area?
	/// </summary>
	[Group( "Constraints" )]
	[Property]
	public bool AllowDefaultArea { get; set; } = true;

	/// <summary>
	/// Should the agent automatically traverse links when it reaches them? Or do you want to implement your own link traversal logic?
	/// </summary>
	[Group( "Constraints" )]
	[Property]
	public bool AutoTraverseLinks
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;

			UpdateAgentParameters();
		}
	} = true;

	/// <summary>
	/// Gets or sets the separation factor used to control how strongly agents avoid crowding each other.
	/// </summary>
	[Group( "Avoidance" )]
	[Property, Range( 0, 1 )]
	public float Separation
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;

			UpdateAgentParameters();
		}
	} = 0.25f;

	/// <summary>
	/// Updated  with the agent's position, even if UpdatePosition is false
	/// </summary>
	public Vector3 AgentPosition => agentInternal == null ? WorldPosition : NavMesh.FromNav( agentInternal.npos );

	/// <summary>
	/// Gets the current target position for the agent, if one is set.
	/// </summary>
	public Vector3? TargetPosition => !IsNavigating ? null : NavMesh.FromNav( agentInternal.targetPos );

	public Vector3 Velocity
	{
		get => agentInternal == null ? default : NavMesh.FromNav( agentInternal.vel );
		set
		{
			if ( agentInternal is null ) return;

			agentInternal.vel = NavMesh.ToNav( value );
		}
	}

	/// <summary>
	/// The velocity the agent would like to move at, you can pass this into a PlayerController.
	/// </summary>
	public Vector3 WishVelocity
	{
		get => agentInternal == null ? default : NavMesh.FromNav( agentInternal.dvel );
	}

	/// <summary>
	/// Returns true if the agent is currently navigating to a target.
	/// </summary>
	public bool IsNavigating => agentInternal != null &&
								agentInternal.targetState == DtMoveRequestState.DT_CROWDAGENT_TARGET_VALID &&
								(agentInternal.state == DtCrowdAgentState.DT_CROWDAGENT_STATE_WALKING ||
								 agentInternal.state == DtCrowdAgentState.DT_CROWDAGENT_STATE_OFFMESH);

	/// <summary>
	/// Updated by <see cref="NavMeshGameSystem"/>
	/// </summary>
	internal float groundTraceZ = 0.0f;

	/// <summary>
	/// Rate Limiter for ground traces
	/// </summary>
	internal TimeUntil timeUntilNextGroundTrace = 0.0f;

	/// <summary>
	/// If you want to move the agent from one position to another
	/// </summary>
	public void SetAgentPosition( Vector3 position )
	{
		if ( agentInternal is null )
			return;

		Scene.NavMesh.crowd.SetAgentPosition( agentInternal, NavMesh.ToNav( position ) );
	}

	internal DtCrowdAgent agentInternal;

	/// <summary>
	/// Navigate to the position
	/// </summary>
	public void MoveTo( Vector3 targetPosition )
	{
		if ( agentInternal is null )
		{
			return;
		}

		var foundTarget = Scene.NavMesh.query.FindNearestPoly( NavMesh.ToNav( targetPosition ), new Vector3( 256, 256, 256 ), DtQueryNoOpFilter.Shared, out var targetPoly, out var targetPos, out _ );
		if ( foundTarget.Failed() || targetPoly == 0 )
		{
			return;
		}

		// people tend to spam call this function
		// which leads to a lot of replans messing with the pathfinding
		// so we only request if we need to
		if ( agentInternal.targetState == DtMoveRequestState.DT_CROWDAGENT_TARGET_FAILED ||
			 agentInternal.targetState == DtMoveRequestState.DT_CROWDAGENT_TARGET_NONE ||
			 !agentInternal.targetPos.AlmostEqual( targetPos, 1 ) )
		{
			Scene.NavMesh.crowd.RequestMoveTarget( agentInternal, targetPoly, targetPos );
		}
	}

	/// <summary>
	/// Assigns a precalculated path for the agent to follow.
	/// The agent will attempt to follow the path, but may adjust its movement to avoid obstacles or other agents.
	/// If the path becomes invalid during navigation, it may be recalculated completely.
	/// </summary>
	public void SetPath( NavMeshPath path )
	{
		if ( !path.IsValid() )
		{
			Log.Warning( "Cannot use Invalid path in Agent.SetPath" );
			return;
		}

		if ( path.Points[0].Position.DistanceSquared( AgentPosition ) > Radius * Radius * 4 )
		{
			Log.Warning( "The path provided to Agent.SetPath needs to start close to the agents position" );
			return;
		}

		agentInternal.targetRef = path.Polygons[^1];
		agentInternal.targetPos = NavMesh.ToNav( path.Points[^1].Position );
		agentInternal.corridor.SetCorridor( NavMesh.ToNav( path.Points[^1].Position ), path.Polygons );
		agentInternal.boundary.Reset();
		agentInternal.partial = false;

		agentInternal.targetState = DtMoveRequestState.DT_CROWDAGENT_TARGET_VALID;
		agentInternal.targetReplanWaitTime = 0;
		agentInternal.timeSinceLastTargetReplan = 0;
	}

	/// <summary>
	/// Returns the agent's current path as a NavMeshPath. This is not free, so avoid calling it every frame.
	/// </summary>
	/// <returns>A NavMeshPath containing the agent's current path information.</returns>
	public NavMeshPath GetPath()
	{
		NavMeshPath result = new();

		if ( agentInternal == null ||
			agentInternal.corridor == null ||
			agentInternal.targetState != DtMoveRequestState.DT_CROWDAGENT_TARGET_VALID )
		{
			result.Status = NavMeshPathStatus.PathNotFound;
			return result;
		}

		// Get the polygon path from the agent's corridor
		result.Polygons = [.. agentInternal.corridor.GetPath()];

		if ( result.Polygons.Count == 0 )
		{
			result.Status = NavMeshPathStatus.PathNotFound;
			return result;
		}

		// Get start and end positions
		var startLocation = NavMesh.ToNav( AgentPosition );
		var targetLocation = agentInternal.targetPos;

		// Calculate the straight path through the polygons
		var straightPathCache = ArrayPool<DtStraightPath>.Shared.Rent( 4096 );
		var dtStatus = Scene.NavMesh.query.FindStraightPath(
			startLocation,
			targetLocation,
			result.Polygons,
			result.Polygons.Count,
			straightPathCache,
			out var filledPointCount,
			straightPathCache.Length,
			0 );

		if ( dtStatus.Failed() )
		{
			ArrayPool<DtStraightPath>.Shared.Return( straightPathCache );
			result.Status = NavMeshPathStatus.PathNotFound;
			return result;
		}

		// Convert the straight path points to NavMeshPathPoints
		var points = new List<NavMeshPathPoint>( filledPointCount );
		for ( int i = 0; i < filledPointCount; i++ )
		{
			points.Add( new NavMeshPathPoint { Position = NavMesh.FromNav( straightPathCache[i].pos ) } );
		}
		ArrayPool<DtStraightPath>.Shared.Return( straightPathCache );
		result.Points = points;

		// Determine path status
		if ( agentInternal.partial )
		{
			result.Status = NavMeshPathStatus.Partial;
		}
		else
		{
			result.Status = NavMeshPathStatus.Complete;
		}

		return result;
	}

	/// <summary>
	/// Stop moving, or whatever we're doing
	/// </summary>
	public void Stop()
	{
		if ( agentInternal is null )
			return;

		agentInternal.animation.active = false;
		agentInternal.state = DtCrowdAgentState.DT_CROWDAGENT_STATE_WALKING;
		Scene.NavMesh.crowd.ResetMoveTarget( agentInternal );
	}

	/// <summary>
	/// Finish link traversal, must be called after traversing a link if AutoTraverseLinks is false.
	/// </summary>
	public void CompleteLinkTraversal()
	{
		if ( agentInternal is null )
			return;

		Scene.NavMesh.crowd.CompleteLink( agentInternal );
	}

	protected override void OnEnabled()
	{
		Transform.OnTransformChanged += OnTransformChanged;
		Scene.NavMesh.OnInit += OnNavMeshInit;

		if ( Scene.NavMesh == null || Scene.NavMesh.crowd == null )
		{
			return;
		}

		OnNavMeshInit();
	}

	private DtCrowdAgentParams CreateAgentParams()
	{
		DtCrowdAgentParams agentParams = new DtCrowdAgentParams();
		agentParams.radius = Radius;
		agentParams.height = Height;
		agentParams.maxAcceleration = Acceleration;
		agentParams.maxSpeed = MaxSpeed;
		agentParams.collisionQueryRange = agentParams.radius * 16.0f;
		agentParams.separationWeight = Separation * 12f;
		agentParams.updateFlags = DtCrowdAgentUpdateFlags.DT_CROWD_ANTICIPATE_TURNS | DtCrowdAgentUpdateFlags.DT_CROWD_OBSTACLE_AVOIDANCE | DtCrowdAgentUpdateFlags.DT_CROWD_SEPARATION | DtCrowdAgentUpdateFlags.DT_CROWD_OPTIMIZE_TOPO;
		agentParams.obstacleAvoidanceType = 0;
		agentParams.filter = new NavMeshQueryFilter( Scene.NavMesh, this );
		agentParams.userData = this;
		agentParams.autoTraverseOffMeshLink = AutoTraverseLinks;
		return agentParams;
	}

	protected override void OnDisabled()
	{
		Transform.OnTransformChanged -= OnTransformChanged;
		Scene.NavMesh.OnInit -= OnNavMeshInit;

		if ( agentInternal is null )
			return;

		Scene.NavMesh.crowd.RemoveAgent( agentInternal );
		agentInternal = null;
	}

	void UpdateAgentParameters()
	{
		if ( agentInternal is null )
			return;

		Scene.NavMesh.crowd.UpdateAgentParameters( agentInternal, CreateAgentParams() );
	}

	protected override void DrawGizmos()
	{
		if ( Gizmo.IsSelected )
		{
			Gizmo.Transform = new Transform( AgentPosition, WorldRotation );

			Gizmo.Draw.Color = Color.Orange.WithAlpha( 0.8f );
			Gizmo.Draw.SolidCylinder( 0, Vector3.Up * Height, Radius );

			Gizmo.Draw.Color = Color.Orange;
			Gizmo.Draw.LineCylinder( 0, Vector3.Up * Height, Radius, Radius, 16 );

			for ( int i = 0; i < 2000; i += 20 )
			{
				var pos = GetLookAhead( i );
				pos = Gizmo.Transform.PointToLocal( pos );
				Gizmo.Draw.LineBBox( BBox.FromPositionAndSize( pos, 5 ) );
			}
		}
	}

	[Obsolete]
	public bool SyncAgentPosition { get; set; } = true;

	/// <summary>
	/// Emitted when the agent enters a link.
	/// </summary>
	public Action LinkEnter { get; set; }

	/// <summary>
	/// Emitted when the agent exits a link.
	/// </summary>
	public Action LinkExit { get; set; }

	/// <summary>
	/// Returns true if the agent is currently traversing a link.
	/// </summary>
	public bool IsTraversingLink => CurrentLinkTraversal != null;

	/// <summary>
	/// Holds information about the current link the agent is traversing.
	/// </summary>
	public readonly record struct LinkTraversalData
	{
		/// <summary>
		/// The start position of the traversal.
		/// Depending on the direction traversing,
		/// this is either LinkComponent.WorldStartPositionOnNavMesh or LinkComponent.WorldEndPositionOnNavMesh.
		/// </summary>
		public readonly Vector3 LinkEnterPosition { init; get; }

		/// <summary>
		/// The end position of the traversal. Where the agent should exit.
		/// Depending on the direction traversing,
		/// this is either LinkComponent.WorldStartPositionOnNavMesh or LinkComponent.WorldEndPositionOnNavMesh.
		/// </summary>
		public readonly Vector3 LinkExitPosition { init; get; }

		/// <summary>
		/// The position at which the agent entered the link.
		/// </summary>
		public readonly Vector3 AgentInitialPosition { init; get; }

		/// <summary>
		/// The Link component that the agent is traversing.
		/// May be null if the agent is traversing a link created without a NavMeshLink component.
		/// </summary>
		public readonly NavMeshLink LinkComponent { init; get; }
	}

	/// <summary>
	/// Information about the current link traversal.
	/// </summary>
	public LinkTraversalData? CurrentLinkTraversal;

	protected override void OnFixedUpdate()
	{
		if ( agentInternal is null )
			return;

		if ( CurrentLinkTraversal is null && agentInternal.state == DtCrowdAgentState.DT_CROWDAGENT_STATE_OFFMESH )
		{
			var polyId = DtDetour.DecodePolyIdPoly( agentInternal.animation.polyRef );
			var tileId = DtDetour.DecodePolyIdTile( agentInternal.animation.polyRef );

			var status = Scene.NavMesh.navmeshInternal.GetTileAndPolyByRef( agentInternal.animation.polyRef, out var offmeshTile, out var offMeshPoly );
			if ( status.Failed() )
			{
				// Fucked, let's get out of here
				CompleteLinkTraversal();
			}
			else
			{
				NavMeshLink currentLink = null;
				foreach ( var offmeshCon in offmeshTile.data.offMeshCons )
				{
					if ( offmeshCon.poly == offMeshPoly.index )
					{
						currentLink = (NavMeshLink)offmeshCon.userData;
					}
				}

				CurrentLinkTraversal = new LinkTraversalData
				{
					LinkEnterPosition = NavMesh.FromNav( agentInternal.animation.startPos ),
					LinkExitPosition = NavMesh.FromNav( agentInternal.animation.endPos ),
					AgentInitialPosition = NavMesh.FromNav( agentInternal.animation.initPos ),
					LinkComponent = currentLink
				};

				LinkEnter?.Invoke();

				if ( currentLink.IsValid() )
				{
					currentLink.TriggetEntered( this );
				}
			}
		}

		if ( CurrentLinkTraversal is not null && agentInternal.state != DtCrowdAgentState.DT_CROWDAGENT_STATE_OFFMESH )
		{
			LinkExit?.Invoke();

			if ( CurrentLinkTraversal.Value.LinkComponent.IsValid() )
			{
				CurrentLinkTraversal.Value.LinkComponent.TriggetExited( this );
			}

			CurrentLinkTraversal = null;
		}
	}


	protected override void OnUpdate()
	{
		if ( agentInternal is null )
			return;

		var worldTransform = Transform.World;
		bool requiresTransformUpdate = false;

		if ( UpdatePosition && !(IsTraversingLink && AutoTraverseLinks) )
		{
			if ( IsTraversingLink )
			{
				// Check if we are within the connection radius of start/end
				// if not we are probably in the air so use the z pos of the link
				var start = NavMesh.FromNav( agentInternal.animation.startPos );
				var end = NavMesh.FromNav( agentInternal.animation.endPos );
				var distanceToStart = (start - AgentPosition).LengthSquared;
				var distanceToEnd = (end - AgentPosition).LengthSquared;
				if ( distanceToStart > CurrentLinkTraversal.Value.LinkComponent.ConnectionRadius * CurrentLinkTraversal.Value.LinkComponent.ConnectionRadius ||
					 distanceToEnd > CurrentLinkTraversal.Value.LinkComponent.ConnectionRadius * CurrentLinkTraversal.Value.LinkComponent.ConnectionRadius )
				{
					groundTraceZ = MathF.Max( groundTraceZ, AgentPosition.z );
				}
			}

			var newPos = AgentPosition.WithZ( groundTraceZ );

			worldTransform.Position = WorldPosition.LerpTo( newPos, Time.Delta * 20.0f );

			// Only update if we moved sufficiently to save on transform updates
			if ( !worldTransform.Position.AlmostEqual( WorldPosition ) ) requiresTransformUpdate = true;
		}

		if ( UpdateRotation && agentInternal.corners.Length > 0 )
		{
			var pos = GetLookAhead( 30.0f );
			var dir = pos - WorldPosition;
			// we dont have corners while traversing a link so pick endpos of offmesh
			if ( agentInternal.state == DtCrowdAgentState.DT_CROWDAGENT_STATE_OFFMESH )
			{
				dir = NavMesh.FromNav( agentInternal.animation.endPos ) - NavMesh.FromNav( agentInternal.animation.startPos );
			}
			dir.z = 0;

			if ( dir.Length > 0.1f )
			{
				var rotationAim = Rotation.LookAt( dir.Normal );
				worldTransform.Rotation = Rotation.Slerp( WorldRotation, rotationAim, Time.Delta * 3.0f );
				requiresTransformUpdate = true;
			}
		}

		if ( requiresTransformUpdate )
		{
			Transform.World = worldTransform;
		}
	}

	private void OnTransformChanged()
	{
		if ( Scene.IsEditor && !Game.IsPlaying && agentInternal is not null )
			SetAgentPosition( Transform.TargetWorld.Position );
	}

	private void OnNavMeshInit()
	{
		if ( agentInternal != null )
		{
			Scene.NavMesh.crowd.RemoveAgent( agentInternal );
			agentInternal = null;
		}
		DtCrowdAgentParams agentParams = CreateAgentParams();

		agentInternal = Scene.NavMesh.crowd.AddAgent( NavMesh.ToNav( WorldPosition ), agentParams );
	}

	/// <summary>
	/// Get a point on the current path, distance away from here. This is a simplified path so 
	/// only includes the first few corners.
	/// </summary>
	public Vector3 GetLookAhead( float distance )
	{
		var pos = WorldPosition;

		if ( agentInternal is null )
			return pos;

		var corners = agentInternal.corners;

		for ( int i = 0; i < agentInternal.ncorners; i++ )
		{
			var next = NavMesh.FromNav( agentInternal.corners[i].pos );
			var deltaToText = next - pos;
			var distanceToNext = deltaToText.Length;

			if ( distanceToNext > distance )
			{
				return Vector3.Lerp( pos, next, distance / distanceToNext );
			}

			distance -= distanceToNext;
			pos = next;
		}


		return pos;
	}
}
