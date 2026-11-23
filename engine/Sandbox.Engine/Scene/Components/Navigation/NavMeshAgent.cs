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
	/// Can the agent take links of every type? When false it takes plain links and the types in <see cref="AllowedLinkTypes"/> only.
	/// </summary>
	[Group( "Constraints" )]
	[Property]
	public bool AllowAllLinkTypes { get; set; } = true;

	/// <summary>
	/// The link types the agent may take when <see cref="AllowAllLinkTypes"/> is false. Empty means plain links only.
	/// </summary>
	[Group( "Constraints" )]
	[Property, ShowIf( nameof( AllowAllLinkTypes ), false )]
	public HashSet<NavMeshLinkType> AllowedLinkTypes { get; set; } = new();

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
	private Nav.NavFilter capturedFilter;
	internal float groundTraceZ;
	internal TimeUntil timeUntilNextGroundTrace;

	/// <summary>The current simulated position, including when UpdatePosition is false.</summary>
	public Vector3 AgentPosition => agentInternal is { } agent ? agent.State.Position : WorldPosition;
	/// <summary>The requested target, or null when stopped.</summary>
	public Vector3? TargetPosition => agentInternal?.State is { Status: not (Nav.AgentStatus.Free or Nav.AgentStatus.Idle or Nav.AgentStatus.Arrived) } state ? (Vector3)state.Target : null;
	/// <summary>True while the agent follows a route. False once it has arrived, also at the end of a route that stops short of the target.</summary>
	public bool IsNavigating => agentInternal is { Removed: false } agent && agent.State.Status is Nav.AgentStatus.Moving or Nav.AgentStatus.OnLink;
	public Vector3 WishVelocity => agentInternal is { } agent ? agent.State.DesiredVelocity : default;
	public Vector3 Velocity
	{
		get => agentInternal is { } agent ? agent.State.Velocity : default;
		set => agentInternal?.SetVelocity( value );
	}

	/// <summary>Explicitly repositions the simulated agent. Runtime GameObject transforms do not move it.</summary>
	public void SetAgentPosition( Vector3 position ) => agentInternal?.SetPosition( position );
	public void MoveTo( Vector3 targetPosition ) => agentInternal?.MoveTo( targetPosition );
	public void Stop() => agentInternal?.Stop();
	/// <summary>Ends custom link traversal at the current AgentPosition and resumes navigation.</summary>
	public void CompleteLinkTraversal() => agentInternal?.CompleteLink();

	/// <summary>Assigns a path calculated on this navmesh. The agent joins it where it stands, or by a short search when it stands beside it.</summary>
	public void SetPath( NavMeshPath path )
	{
		if ( path.IsValid && path.Polygons is { Length: > 0 } ) agentInternal?.SetPath( path );
	}

	public NavMeshPath GetPath() => agentInternal?.GetPath() ?? new() { Status = NavMeshPathStatus.PathNotFound };

	protected override void OnEnabled()
	{
		Scene.NavMesh.OnInit += OnNavMeshInit;
		if ( Scene.NavMesh.Simulation is not null ) OnNavMeshInit();
	}

	protected override void OnDisabled()
	{
		Scene.NavMesh.OnInit -= OnNavMeshInit;
		agentInternal?.Owner.Remove( agentInternal );
		agentInternal = null;
		CurrentLinkTraversal = null;
	}

	// The engine takes any size, speed and separation, the library refuses what it cannot work with:
	// sizes go up to the least the navmesh gives an agent, speeds up to 0, separation into 0 to 1.
	private Nav.AgentParams CreateAgentSettings()
	{
		static float AtLeast( float value, float least ) => value > least && float.IsFinite( value ) ? value : least;

		capturedFilter = Scene.NavMesh.CreateFilter( this );
		return new()
		{
			Radius = AtLeast( Radius, NavMesh.LeastAgentSize ),
			Height = AtLeast( Height, NavMesh.LeastAgentSize ),
			MaxSpeed = AtLeast( MaxSpeed, 0 ),
			Acceleration = AtLeast( Acceleration, 0 ),
			Separation = Separation > 0 ? MathF.Min( Separation, 1 ) : 0,
			AutoLinks = AutoTraverseLinks,
			// An agent without a speed of its own is moved by its owner
			External = MaxSpeed <= 0,
			Filter = capturedFilter
		};
	}

	void UpdateAgentParameters()
	{
		if ( agentInternal is not null ) agentInternal.Options = CreateAgentSettings();
	}

	private void OnNavMeshInit()
	{
		var target = TargetPosition;
		agentInternal?.Owner.Remove( agentInternal );
		agentInternal = Scene.NavMesh.Simulation.Add( this, WorldPosition, CreateAgentSettings() );
		groundTraceZ = WorldPosition.z;
		if ( target is Vector3 position ) MoveTo( position );
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
		/// <summary>The type of the link being crossed, null for a plain link.</summary>
		public NavMeshLinkType Type { get; init; }
	}
	public LinkTraversalData? CurrentLinkTraversal;

	// The link the agent stands at until it is free. It is on the link in the crowd, held there.
	internal NavMeshLink waitingFor;

	// Called right after the navigation update in which the agent entered or left a link
	internal void LinkChanged( in Nav.AgentState state, NavMeshLink link )
	{
		// Left the link it waited for without crossing it: there is nothing to exit
		if ( waitingFor is not null ) ReleaseLink();
		else ExitLink();

		if ( state.Link < 0 ) return;

		// First come, first served: an agent arriving while others wait queues behind them
		if ( link.IsValid() && link.Occupancy == NavMeshLinkOccupancy.OneAtATime && (link.IsOccupied || link.queued > 0) )
		{
			waitingFor = link;
			link.queued++;
			agentInternal.Owner.Waiting.Add( this );
			agentInternal.Held = true;
			return;
		}

		EnterLink( state, link );
	}

	// The link it waited for is free
	internal void StartWaitedLink()
	{
		var link = waitingFor;
		ReleaseLink();
		var state = agentInternal.State;
		if ( state.Link >= 0 ) EnterLink( state, link );
	}

	// The link it waited for is gone: it leaves the link where it stands and plans without it
	internal void StopWaiting()
	{
		ReleaseLink();
		agentInternal?.CompleteLink();
	}

	// The link it crosses is gone: it stops crossing where it stands
	internal void AbandonLink()
	{
		agentInternal?.CompleteLink();
		ExitLink();
	}

	// Gives up the link the agent waits for or crosses, without callbacks
	internal void ReleaseLink()
	{
		if ( waitingFor is { } link )
		{
			link.queued--;
			waitingFor = null;
			agentInternal?.Owner.Waiting.Remove( this );
			if ( agentInternal is not null ) agentInternal.Held = false;
		}
		CurrentLinkTraversal?.LinkComponent?.occupants?.Remove( this );
	}

	private void ExitLink()
	{
		if ( CurrentLinkTraversal is not LinkTraversalData previous ) return;
		ReleaseLink();
		CurrentLinkTraversal = null;
		try
		{
			LinkExit?.Invoke();
			if ( previous.LinkComponent.IsValid() ) previous.LinkComponent.TriggetExited( this );
		}
		catch ( Exception e ) { Log.Error( e, $"Exception when calling 'LinkExit' on {this}" ); }
	}

	private void EnterLink( in Nav.AgentState state, NavMeshLink link )
	{
		if ( link.IsValid() ) (link.occupants ??= new()).Add( this );
		CurrentLinkTraversal = new() { LinkEnterPosition = state.LinkStart, LinkExitPosition = state.LinkEnd, AgentInitialPosition = state.LinkEntry, LinkComponent = link, Type = link?.Type };
		try
		{
			LinkEnter?.Invoke();
			if ( link.IsValid() ) link.TriggetEntered( this );
		}
		catch ( Exception e ) { Log.Error( e, $"Exception when calling 'LinkEnter' on {this}" ); }
	}

	protected override void OnFixedUpdate()
	{
		if ( agentInternal is null ) return;
		// Game code edits the area and link type collections in place, and costs of areas and link types change without notice
		if ( !Scene.NavMesh.FilterMatches( capturedFilter, this ) ) UpdateAgentParameters();
	}

	protected override void OnUpdate()
	{
		if ( agentInternal is null ) return;
		var state = agentInternal.State;
		var transform = WorldTransform;
		if ( UpdatePosition )
		{
			Vector3 position = state.Position;
			if ( state.Link < 0 || waitingFor is not null ) position.z = groundTraceZ;
			transform.Position = WorldPosition.LerpTo( position, Math.Clamp( Time.Delta * 20, 0, 1 ) );
		}
		if ( UpdateRotation )
		{
			Vector3 direction = ((Vector3)state.DesiredVelocity).WithZ( 0 );
			if ( direction.LengthSquared > 0.01f ) transform.Rotation = Rotation.Slerp( WorldRotation, Rotation.LookAt( direction.Normal ), Math.Clamp( Time.Delta * 3, 0, 1 ) );
		}
		WorldTransform = transform;
	}

	/// <summary>
	/// The point this far ahead of the agent on its route.
	/// </summary>
	public Vector3 GetLookAhead( float distance ) => agentInternal?.GetLookAhead( distance ) ?? WorldPosition;

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
