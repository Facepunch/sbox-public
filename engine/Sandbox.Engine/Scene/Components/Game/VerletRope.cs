using System.Text.Json.Serialization;

namespace Sandbox;

/// <summary>
/// Verlet integration-based rope physics simulation component.
/// </summary>
[Title( "Rope" ), Category( "Game" ), Icon( "cable" )]
public class VerletRope : Component, Component.ExecuteInEditor
{
	/// <summary>
	/// The GameObject the end of the rope attaches to.
	/// </summary>
	[Property, Group( "Attachment" ), MakeDirty]
	public GameObject Attachment { get; set; }

	/// <summary>
	/// The LineRenderer used to visualize the rope.
	/// </summary>
	[Property, Group( "Attachment" )]
	public LineRenderer LinkedRenderer { get; set; }

	/// <summary>
	/// Additional slack, added to the rope length.
	/// </summary>
	[Property, Group( "Simulation" )]
	public float Slack
	{
		get;
		set
		{
			field = MathF.Max( 0f, value );
			WakeRope();
		}
	} = 0f;

	/// <summary>
	/// Number of segments in the rope. Higher values increase visual fidelity and collision accuracy but quickly reduce performance.
	/// </summary>
	[Property, Group( "Simulation" ), MakeDirty, Range( 1, 64 ), Step( 1 )]
	public int SegmentCount { get; set; } = 16;

	/// <summary>
	/// Radius of the rope for collision detection.
	/// </summary>
	[Property, Group( "Simulation" )]
	public float Radius { get; set; } = 1f;

	/// <summary>
	/// Controls the rope's length directly, will override the initial length and slack will not be applied.
	/// When set to 0, the rope's initial length between attachment points is used.
	/// </summary>
	[Property, Group( "Advanced", StartFolded = true )]
	public float LengthOverride
	{
		get;
		set
		{
			field = MathF.Max( 0f, value );
			WakeRope();
		}
	} = 0f;

	/// <summary>
	/// Rope stiffness factor. Higher values make the rope more rigid.
	/// </summary>
	[Property, Group( "Advanced", StartFolded = true )]
	public float Stiffness { get; set; } = 0.7f;

	/// <summary>
	/// Dampens rope movement. Higher values make the rope settle faster.
	/// </summary>
	[Property, Group( "Advanced", StartFolded = true )]
	public float DampingFactor { get; set; } = 0.2f;

	/// <summary>
	/// Controls how easily the rope bends. Lower values allow more bending, higher values make it stiffer.
	/// </summary>
	[Property, Group( "Advanced", StartFolded = true )]
	public float SoftBendFactor { get; set; } = 0.3f;

	/// <summary>
	/// The rope length when it initialized.
	/// </summary>
	[JsonInclude]
	private float initialRopeLength { get; set; } = 0f;

	/// <summary>
	/// The length the rope would like to have.
	/// </summary>
	private float targetRopeLength => (LengthOverride > 0f ? LengthOverride : initialRopeLength + Slack);

	/// <summary>
	/// Set on Initialize based on distance between attachment points and slack.
	/// </summary>
	private float targetSegmentLength => targetRopeLength / SegmentCount;

	/// <summary>
	/// Gets the actual gravity to use.
	/// </summary>
	private Vector3 simulationGravity => Scene.PhysicsWorld.Gravity;

	/// <summary>
	/// Number of iterations to solve constraints. Higher values increase rigidity but reduce performance.
	/// </summary>
	private int constraintIterations => Math.Min( MathX.CeilToInt( SegmentCount * 1.5f ), 64 );

	/// <summary>
	/// Base velocity threshold used for scaling the rest detection
	/// </summary>
	private const float baseRestVelocityThreshold = 0.03f;

	/// <summary>
	/// Base segment length used for calibrating various calculations.
	/// </summary>
	private const float baseSegmentLength = 16f;

	/// <summary>
	/// Consecutive frames of no movement required to consider the rope at rest.
	/// </summary>
	private int restFramesRequired { get; set; } = 8;

	/// <summary>
	/// Factor after which we consider a rope to be stretched.
	/// </summary>
	private const float collisionMaxRopeStretchFactor = 1.1f;

	/// <summary>
	/// Ignore collisions when segment is stretched beyond this factor
	/// </summary>
	private const float collisionMaxRopeSegmentStretchFactor = 1.7f;

	/// <summary>
	/// Velocity threshold below which we consider the rope to be at rest.
	/// Scale the rest velocity threshold based on segment length.
	/// </summary>
	private float restVelocityThreshold => baseRestVelocityThreshold * (targetSegmentLength / baseSegmentLength);

	private float slidingVelocityThreshold => restVelocityThreshold * 5f;

	// Stretch detection
	private float currentRopeLength;
	private float averageSegmentLength;

	// Rest detection variables
	private Vector3 lastStartPos;
	private Vector3 lastEndPos;
	private TimeSince timeSinceRest;
	private bool isAtRest = false;
	private int currentRestFrameCount = 0;

	// Used for interpolation between physics updates.
	private TimeSince timeSinceSimulate;

	private struct RopePoint
	{
		public Vector3 Position;
		public Vector3 Previous;
		public Vector3 Acceleration;
		public bool IsAttached;
		public float MovementSinceLastCollision;
	}

	/// <summary>
	/// We use an array because we can acces members by ref.
	/// Which is faster than using a list.
	/// </summary>
	private RopePoint[] points = Array.Empty<RopePoint>();

	protected override void OnEnabled()
	{
		Initialize();

		for ( int i = 0; i < 10; i++ )
		{
			Simulate( 1.0f / 60.0f );
		}

		timeSinceSimulate = 0;
		timeSinceRest = 0;

		// Try to find a LineRenderer if none is assigned
		LinkedRenderer ??= GetComponent<LineRenderer>();
	}

	protected override void OnUpdate()
	{
		if ( Scene.IsEditor && Attachment.IsValid() )
		{
			var start = WorldPosition;
			var end = Attachment.WorldPosition;

			var span = end - start;

			initialRopeLength = span.Length;
		}
		Draw();
	}

	protected override void OnDirty()
	{
		base.OnDirty();

		Initialize();
	}

	void Initialize()
	{
		if ( !Attachment.IsValid() ) return;

		if ( SegmentCount < 1 ) SegmentCount = 1;

		var start = WorldPosition;
		var end = Attachment.WorldPosition;

		var span = end - start;

		initialRopeLength = span.Length;

		int lastIndex = SegmentCount;
		float denom = lastIndex == 0 ? 1f : lastIndex;

		var pointCount = SegmentCount + 1;
		if ( points.Length != pointCount )
		{
			points = new RopePoint[pointCount];
		}

		for ( int i = 0; i < pointCount; i++ )
		{
			float t = i / denom; // 0..1
			var pos = start + span * t;
			bool isAttached = (i == 0) || (i == lastIndex);
			points[i] = new RopePoint
			{
				Position = pos,
				Previous = pos,
				IsAttached = isAttached
			};
		}

		// Initialize attachment tracking
		lastStartPos = WorldPosition;
		lastEndPos = Attachment.WorldPosition;
	}

	protected override void OnDestroy()
	{
		base.OnDestroy();
	}

	internal void Simulate( float dt )
	{
		if ( !Attachment.IsValid() ) return;

		CheckAndWakeRope();

		if ( isAtRest ) return;

		ApplyForces();

		VerletIntegration( dt );

		UpdateRopeLengths();

		ApplyConstraints();

		HandleCollisions();

		CheckRestState();

		timeSinceSimulate = 0;
	}

	private void CheckAndWakeRope()
	{
		if ( isAtRest )
		{
			bool startMoved = (WorldPosition - lastStartPos).LengthSquared > 0.01f;
			bool endMoved = (Attachment.WorldPosition - lastEndPos).LengthSquared > 0.01f;

			if ( startMoved || endMoved || timeSinceRest > 2f ) // Occasionally wake up ropes, so we can react to external collisions
			{
				WakeRope();

				if ( timeSinceRest > 2f )
				{
					// only tick a single frame when waking up from a long rest
					currentRestFrameCount = restFramesRequired - 1;
				}
			}
		}

		// Update attachment positions for tracking
		lastStartPos = WorldPosition;
		lastEndPos = Attachment?.WorldPosition ?? lastEndPos;
	}

	private void WakeRope()
	{
		isAtRest = false;
		currentRestFrameCount = 0;
	}

	void VerletIntegration( float dt )
	{
		for ( int i = 0; i < points.Length; i++ )
		{
			ref var p = ref points[i];

			if ( p.IsAttached )
			{
				// Update attached points position
				if ( i == 0 )
					p.Position = WorldPosition;
				else if ( i == points.Length - 1 && Attachment != null )
					p.Position = Attachment.WorldPosition;

				points[i] = p;
				continue;
			}

			Vector3 velocity = p.Position - p.Previous;

			var currentPosition = p.Position;

			// Apply damping directly to velocity in one place only
			velocity *= (1.0f - DampingFactor * dt);

			// Standard Verlet integration step
			p.Position = currentPosition + velocity + p.Acceleration * (dt * dt);
			p.Previous = currentPosition;
		}
	}
	private void UpdateRopeLengths()
	{
		float totalLength = 0f;
		int segments = 0;

		for ( int i = 0; i < points.Length - 1; i++ )
		{
			float segmentLength = (points[i + 1].Position - points[i].Position).Length;
			totalLength += segmentLength;
			segments++;
		}

		currentRopeLength = totalLength;
		averageSegmentLength = segments > 0 ? totalLength / segments : targetSegmentLength;
	}

	void ApplyForces()
	{
		var gravity = simulationGravity;
		for ( int i = 0; i < points.Length; i++ )
		{
			ref var p = ref points[i];

			if ( p.IsAttached )
				continue;

			// Only apply gravity - remove the drag force as damping is handled in integration
			var totalAcceleration = gravity;

			p.Acceleration = totalAcceleration;
		}
	}

	void ApplyConstraints()
	{
		// Apply overall rope length constraint first
		// This drastically reduces the number of iterations we need
		// And only causes minimal artifacts
		// See https://toqoz.fyi/game-rope.html # Number of iterations
		ApplyOverallRopeConstraint();

		// Apply both stiffness and bending constraints in each iteration
		for ( var iteration = 0; iteration < constraintIterations; iteration++ )
		{
			for ( var i = 0; i < points.Length - 1; i++ )
			{
				// Stiffness constraints for adjacent points
				ref var p1 = ref points[i];
				ref var p2 = ref points[i + 1];

				var segment = p2.Position - p1.Position;
				var segmentLength = MathF.Sqrt( segment.LengthSquared );
				var stretch = segmentLength - this.targetSegmentLength;
				var direction = segment / segmentLength;
				var stretchStiffness = stretch * direction * Stiffness;

				if ( p1.IsAttached )
				{
					p2.Position -= stretchStiffness;
				}
				else if ( p2.IsAttached )
				{
					p1.Position += stretchStiffness;
				}
				else
				{
					p1.Position += stretchStiffness * 0.5f;
					p2.Position -= stretchStiffness * 0.5f;
				}

				// Bending constraints for points two segments apart
				if ( i < points.Length - 2 )
				{
					ref var p3 = ref points[i + 2];

					var delta = p3.Position - p1.Position;
					var distSq = delta.LengthSquared;
					if ( distSq > 0.000001f )
					{
						var dist = MathF.Sqrt( distSq );
						var diff = (dist - this.targetSegmentLength * 2.0f) / dist;
						var offset = delta * SoftBendFactor * diff;

						if ( !p1.IsAttached )
							p1.Position += offset;

						if ( !p3.IsAttached )
							p3.Position -= offset;
					}
				}
			}
		}
	}

	void ApplyOverallRopeConstraint()
	{
		// Only apply if both ends are attached
		if ( points.Length < 2 || !points[0].IsAttached || !points[points.Length - 1].IsAttached )
			return;

		ref var first = ref points[0];
		ref var last = ref points[points.Length - 1];

		float currentDistance = (last.Position - first.Position).Length;

		// Only constrain if the rope is stretched beyond its maximum length
		if ( currentDistance >= targetRopeLength - 0.001f )
		{
			var direction = (last.Position - first.Position).Normal;

			// Adjust the non-attached points along the rope
			for ( int i = 1; i < points.Length - 1; i++ )
			{
				if ( points[i].IsAttached )
					continue;

				float t = (float)i / (points.Length - 1);
				Vector3 idealPos = first.Position + direction * targetRopeLength * t;

				ref var p = ref points[i];
				p.Position = Vector3.Lerp( p.Position, idealPos, 0.3f );
			}
		}
	}

	/// <summary>
	/// This method checks each segment of the rope for collisions and adjusts their positions accordingly.
	/// It skips collision checks for segments that are excessively stretched to prevent the rope from becoming unstable.
	/// If the rope is extremely stretched, all collision checks are bypassed to allow the rope to recover.
	/// </summary>
	void HandleCollisions()
	{
		var segmentSlideIgnoreLength = averageSegmentLength * collisionMaxRopeSegmentStretchFactor;
		var isRopeStretched = currentRopeLength > targetSegmentLength * SegmentCount * collisionMaxRopeStretchFactor;

		// Last resort disable all collisions briefly in an attempt to recover the rope
		var isExtremelyStretched = currentRopeLength > targetSegmentLength * SegmentCount * 4;
		if ( isExtremelyStretched )
		{
			return;
		}

		for ( int i = 1; i < points.Length; i++ )
		{
			if ( points[i].IsAttached ) continue;

			ref var p = ref points[i];

			var plannedMovementDistanceSquared = (p.Position - p.Previous).LengthSquared;
			p.MovementSinceLastCollision += plannedMovementDistanceSquared;


			if ( p.MovementSinceLastCollision < 0.01f * 0.01f )
			{
				// Skip if movement is too small
				continue;
			}


			// Skip collision check for stretched segments
			// This is our attempt to unfuck the rope if it got dragged across the map
			if ( isRopeStretched )
			{
				ref var prevPoint = ref points[i - 1];
				if ( plannedMovementDistanceSquared > segmentSlideIgnoreLength * segmentSlideIgnoreLength )
				{
					continue;
				}
			}

			p.MovementSinceLastCollision = 0.0f; // Reset movement after processing

			// First check for movement-based collisions (from previous to current position)
			var moveTrace = Scene.Trace.Sphere( Radius, p.Previous, p.Position )
				.UseHitPosition( true )
				.Run();

			if ( moveTrace.Hit )
			{
				var originalMove = p.Position - p.Previous;

				Vector3 newPosition;
				// Determine base collision response position
				if ( moveTrace.Normal.z < -0.5f )
				{
					// Prevent clipping through ground
					newPosition = moveTrace.HitPosition + Vector3.Up;
				}
				else
				{
					// Hit something during movement
					newPosition = moveTrace.EndPosition + moveTrace.Normal * 0.01f;
				}

				// Apply sliding behavior with surface friction

				// Calculate sliding component (project movement onto surface plane)
				float dot = Vector3.Dot( originalMove, moveTrace.Normal );
				Vector3 normalComponent = moveTrace.Normal * dot;
				Vector3 slideComponent = originalMove - normalComponent;

				// Apply surface friction to the slide
				float frictionFactor = Math.Clamp( moveTrace.Surface.Friction, 0.1f, 0.95f );
				slideComponent *= (1.0f - frictionFactor);

				// Dont apply slide if it's too small
				// so rope comes to rest faster
				if ( slideComponent.LengthSquared > slidingVelocityThreshold * slidingVelocityThreshold )
				{
					// Add the dampened slide to our position
					newPosition += slideComponent;
				}


				p.Position = newPosition;
			}
		}
	}


	private void CheckRestState()
	{
		if ( isAtRest )
			return;

		bool isMoving = false;
		float velocityThresholdSq = restVelocityThreshold * restVelocityThreshold;

		// Check if any non-attached point is moving significantly
		for ( int i = 0; i < points.Length; i++ )
		{
			ref var p = ref points[i];

			// Skip attached points as they're controlled externally
			if ( p.IsAttached )
				continue;

			var velocitySq = (p.Position - p.Previous).LengthSquared;

			if ( velocitySq > velocityThresholdSq )
			{
				isMoving = true;
				break;
			}
		}

		if ( !isMoving )
		{
			currentRestFrameCount++;
			if ( currentRestFrameCount >= restFramesRequired )
			{
				isAtRest = true;
				timeSinceRest = 0;
			}
		}
		else
		{
			currentRestFrameCount = 0;
		}
	}

	void Draw()
	{
		if ( LinkedRenderer is null ) return;

		if ( !Attachment.IsValid() )
		{
			LinkedRenderer.Enabled = false;
			return;
		}

		// We could use InterpolationBuffer here but i feel like that would be overkill
		// Also it's private/internal.
		float fixedDelta = 1f / ProjectSettings.Physics.FixedUpdateFrequency.Clamp( 1, 1000 );
		float lerpFactor = Math.Min( timeSinceSimulate / fixedDelta, 1.0f );

		LinkedRenderer.UseVectorPoints = true;
		LinkedRenderer.VectorPoints ??= new();
		LinkedRenderer.VectorPoints.Clear();

		for ( int i = 0; i < points.Length; i++ )
		{
			ref var point = ref points[i];

			// For attached points, always use their current position
			if ( point.IsAttached )
			{
				if ( i == 0 )
					LinkedRenderer.VectorPoints.Add( WorldPosition );
				else if ( i == points.Length - 1 && Attachment != null )
					LinkedRenderer.VectorPoints.Add( Attachment.WorldPosition );
				else
					LinkedRenderer.VectorPoints.Add( point.Position );
			}
			else
			{
				// For non-attached points, lerp between previous and current position
				Vector3 lerpedPosition = Vector3.Lerp( point.Previous, point.Position, lerpFactor );
				LinkedRenderer.VectorPoints.Add( lerpedPosition );
			}
		}
	}
}
