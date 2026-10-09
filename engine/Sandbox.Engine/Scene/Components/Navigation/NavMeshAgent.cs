using Sandbox.Navigation.Pathfinding;

using Sandbox.Engine.Resources;
using Sandbox.Navigation;

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

	internal SimulationAgent agentInternal;
	internal TraversalFilter QueryFilter = TraversalFilter.Unrestricted;
	internal float groundTraceZ;
	internal TimeUntil timeUntilNextGroundTrace;
	private bool applyingTransform;

	/// <summary>The current simulated position, including when UpdatePosition is false.</summary>
	public Vector3 AgentPosition => agentInternal?.State.Position is Vector3 position ? NavMesh.FromNav( position ) : WorldPosition;
	/// <summary>The requested target, or null when stopped.</summary>
	public Vector3? TargetPosition => agentInternal?.State.Target is Vector3 target ? NavMesh.FromNav( target ) : null;
	public bool IsNavigating => agentInternal?.State.Navigating ?? false;
	public Vector3 WishVelocity => agentInternal?.State.WishVelocity is Vector3 velocity ? NavMesh.FromNav( velocity ) : default;
	public Vector3 Velocity
	{
		get => agentInternal?.State.Velocity is Vector3 velocity ? NavMesh.FromNav( velocity ) : default;
		set { var agent = agentInternal; if ( agent is not null ) lock ( agent.Owner.Gate ) agent.Velocity = NavMesh.ToNav( value ); }
	}

	/// <summary>Explicitly repositions the simulated agent. Runtime GameObject transforms do not move it.</summary>
	public void SetAgentPosition( Vector3 position ) => agentInternal?.SetPosition( NavMesh.ToNav( position ) );
	public void MoveTo( Vector3 targetPosition ) => agentInternal?.MoveTo( NavMesh.ToNav( targetPosition ) );
	public void Stop() => agentInternal?.Stop();
	/// <summary>Ends custom link traversal at the current AgentPosition and resumes navigation.</summary>
	public void CompleteLinkTraversal() => agentInternal?.CompleteLink();

	/// <summary>Assigns a path calculated on this navmesh. The start must project onto the path's first polygon.</summary>
	public void SetPath( NavMeshPath path )
	{
		var agent = agentInternal;
		if ( agent is null || !path.IsValid || path.Polygons is not { Length: > 0 } ) return;
		lock ( agent.Owner.Gate )
		{
			if ( path.Owner != Scene.NavMesh || path.Generation != Scene.NavMesh.Generation ) return;
			var settings = agent.Options;
			var radius = MathF.Max( Scene.NavMesh.AgentRadius, Radius ) * 2.1f;
			var extents = new Vector3( radius, MathF.Max( Scene.NavMesh.AgentHeight, Height ) * 1.51f, radius );
			var status = agent.Query.FindNearestPoly( agent.Position, extents, settings.Filter, out var poly, out var position, out _ );
			if ( status.Failed() || poly == 0 || poly != path.Polygons[0] ) return;
			foreach ( var polygon in path.Polygons ) if ( !agent.Query.IsValidPolyRef( polygon, settings.Filter ) ) return;
			agent.Position = position;
			agent.Path.Clear();
			agent.Path.AddRange( path.Polygons );
			agent.PathRequestedTarget = NavMesh.ToNav( path.RequestedTarget );
			agent.Target = agent.PathRequestedTarget;
			agent.PathTarget = NavMesh.ToNav( path.Points[^1].Position );
			agent.Partial = path.Status == NavMeshPathStatus.Partial;
			agent.NeedsPath = false;
			agent.HasRoute = true;
			agent.RestingPosition = null;
			agent.Link = null;
			agent.PathRevision = agent.Owner.Revision;
		}
	}

	public NavMeshPath GetPath()
	{
		var agent = agentInternal;
		if ( agent is null ) return new() { Status = NavMeshPathStatus.PathNotFound };
		lock ( agent.Owner.Gate )
		{
			if ( !agent.State.Navigating ) return new() { Status = NavMeshPathStatus.PathNotFound };
			return Scene.NavMesh.CreatePathResult( agent.Query, agent.Position, agent.PathTarget,
				agent.Path, agent.Partial, NavMesh.FromNav( agent.PathRequestedTarget ) );
		}
	}

	protected override void OnEnabled()
	{
		Transform.OnTransformChanged += OnTransformChanged;
		Scene.NavMesh.OnInit += OnNavMeshInit;
		if ( Scene.NavMesh.Simulation is not null ) OnNavMeshInit();
	}

	protected override void OnDisabled()
	{
		Transform.OnTransformChanged -= OnTransformChanged;
		Scene.NavMesh.OnInit -= OnNavMeshInit;
		if ( agentInternal is not null ) agentInternal.Owner.Remove( agentInternal );
		agentInternal = null;
		CurrentLinkTraversal = null;
	}

	private SimulationSettings CreateAgentSettings()
	{
		var filter = Scene.NavMesh.CreateFilter( this );
		return new( MathF.Max( 0.01f, Radius ), MathF.Max( 0.01f, Height ), MathF.Max( 0, MaxSpeed ),
			MathF.Max( 0, Acceleration ), Math.Clamp( Separation, 0, 1 ), AutoTraverseLinks, filter );
	}

	void UpdateAgentParameters()
	{
		if ( agentInternal is null ) return;
		lock ( agentInternal.Owner.Gate )
		{
			agentInternal.Options = CreateAgentSettings();
			agentInternal.WallRevision = -1;
			agentInternal.NeedsPath = (agentInternal.Target ?? agentInternal.RestingPosition).HasValue;
		}
	}

	private void OnNavMeshInit()
	{
		var target = TargetPosition;
		if ( agentInternal is not null ) agentInternal.Owner.Remove( agentInternal );
		agentInternal = Scene.NavMesh.Simulation.Add( NavMesh.ToNav( WorldPosition ), CreateAgentSettings() );
		groundTraceZ = WorldPosition.z;
		if ( target is Vector3 position ) MoveTo( position );
	}

	private void OnTransformChanged()
	{
		if ( !applyingTransform && Scene.IsEditor && !Game.IsPlaying )
			SetAgentPosition( Transform.TargetWorld.Position );
	}

	[Obsolete]
	public bool SyncAgentPosition { get; set; } = true;
	public Action LinkEnter { get; set; }
	public Action LinkExit { get; set; }
	public bool IsTraversingLink => CurrentLinkTraversal.HasValue;

	public readonly record struct LinkTraversalData
	{
		public Vector3 LinkEnterPosition { get; init; }
		public Vector3 LinkExitPosition { get; init; }
		public Vector3 AgentInitialPosition { get; init; }
		public NavMeshLink LinkComponent { get; init; }
	}
	public LinkTraversalData? CurrentLinkTraversal;

	protected override void OnFixedUpdate()
	{
		if ( agentInternal is null ) return;
		// Legacy mutable area sets are captured on the component thread. Workers
		// never read components, resource properties, or these HashSets.
		if ( !Scene.NavMesh.FilterMatches( agentInternal.Options.Filter, this ) ) UpdateAgentParameters();
		var state = agentInternal.State;
		if ( CurrentLinkTraversal is null && state.Link is SimulationLink link )
		{
			NavMeshLink component = null;
			lock ( Scene.NavMesh.SyncRoot )
			{
				if ( Scene.NavMesh.navmeshInternal.GetTileAndPolyByRef( link.Polygon, out var tile, out var poly ).Succeeded() )
					foreach ( var connection in tile.data.offMeshCons )
						if ( connection.poly == poly.index ) component = connection.userData as NavMeshLink;
			}
			CurrentLinkTraversal = new() { LinkEnterPosition = NavMesh.FromNav( link.Start ), LinkExitPosition = NavMesh.FromNav( link.End ), AgentInitialPosition = NavMesh.FromNav( link.Initial ), LinkComponent = component };
			LinkEnter?.Invoke();
			if ( component.IsValid() ) component.TriggetEntered( this );
		}
		if ( CurrentLinkTraversal is LinkTraversalData previous && state.Link is null )
		{
			CurrentLinkTraversal = null;
			LinkExit?.Invoke();
			if ( previous.LinkComponent.IsValid() ) previous.LinkComponent.TriggetExited( this );
		}
	}

	protected override void OnUpdate()
	{
		if ( agentInternal is null ) return;
		var state = agentInternal.State;
		var transform = WorldTransform;
		if ( UpdatePosition )
		{
			var position = NavMesh.FromNav( state.Position );
			if ( state.Link is null ) position.z = groundTraceZ;
			transform.Position = WorldPosition.LerpTo( position, Math.Clamp( Time.Delta * 20, 0, 1 ) );
		}
		if ( UpdateRotation )
		{
			var direction = NavMesh.FromNav( state.WishVelocity ).WithZ( 0 );
			if ( direction.LengthSquared > 0.01f ) transform.Rotation = Rotation.Slerp( WorldRotation, Rotation.LookAt( direction.Normal ), Math.Clamp( Time.Delta * 3, 0, 1 ) );
		}
		applyingTransform = true;
		try { WorldTransform = transform; }
		finally { applyingTransform = false; }
	}

	public Vector3 GetLookAhead( float distance )
	{
		var agent = agentInternal;
		if ( agent is null ) return WorldPosition;
		lock ( agent.Owner.Gate )
		{
			var position = AgentPosition;
			for ( int i = 0; i < agent.CornerCount; i++ )
			{
				if ( (agent.Corners[i].flags & StraightPathFlags.STRAIGHTPATH_START) != 0 ) continue;
				var next = NavMesh.FromNav( agent.Corners[i].pos );
				float length = (next - position).Length;
				if ( length > distance ) return Vector3.Lerp( position, next, MathF.Max( 0, distance ) / length );
				distance -= length;
				position = next;
			}
			return position;
		}
	}

	protected override void DrawGizmos()
	{
		if ( !Gizmo.IsSelected ) return;
		Gizmo.Transform = new Transform( AgentPosition, WorldRotation );
		Gizmo.Draw.Color = Color.Orange.WithAlpha( 0.8f );
		Gizmo.Draw.SolidCylinder( 0, Vector3.Up * Height, Radius );
		Gizmo.Draw.Color = Color.Orange;
		Gizmo.Draw.LineCylinder( 0, Vector3.Up * Height, Radius, Radius, 16 );
	}
}
