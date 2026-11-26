using Sandbox.Npcs.Layers;

namespace Sandbox.Npcs;

// A hit too hard to stand ragdolls the NPC's own body; it gets up where it comes to rest
public partial class BaseNpc : IScenePhysicsEvents
{
	// The hitter's speed into us, scaled by sqrt( its mass / both masses ): a car at 300, a barrel thrown at 480
	private const float KnockdownImpact = 280f;

	// Speed changed this much in one tick by anything but our mover and gravity: a blast, a slam
	private const float KnockdownFling = 400f;

	// Landing from a fall this fast, about two storeys, we go over. Lower ones we land on our feet.
	private const float KnockdownLanding = 620f;

	// Carried, the carrier jerks the body about all the time: only this much change within a tenth of a second, a flick
	private const float KnockdownSwing = 900f;

	// Pushed off our way this fast by something at least as heavy, we keep our feet only this long
	private const float KnockdownShoveSpeed = 120f;
	private const float KnockdownShoveTime = 0.5f;

	// Down, it's at rest once its pelvis moves and turns no faster than this against what it lies on
	private const float RestSpeed = 20f;
	private const float RestSpin = 1f;

	// Getting up lasts as long as the pelvis takes to rise and the furthest joint to turn, at these rates
	private const float GetUpSpeed = 60f;
	private const float GetUpTurnSpeed = 180f;
	private const float GetUpMinTime = 0.5f;
	private const float GetUpMaxTime = 1.5f;
	private const float GetUpStandTime = 0.45f;

	// Down, only the big parts hitting something hurt: flailing limbs don't
	private const float RagdollSafeImpactSpeed = 550f;
	private const float RagdollFatalImpactSpeed = 1700f;
	private const float RagdollImpactMassShare = 0.08f;

	// Our own body rides along fixed to the pelvis, so whatever held it still holds the ragdoll
	private const float KnockdownRootMass = 5f;

	internal enum KnockdownStates { None, Down, GettingUp }

	[Sync( SyncFlags.FromHost ), Expose] internal KnockdownStates KnockdownState { get; private set; }

	private ModelPhysics _knockdownPhysics;
	private Rigidbody _pelvis;
	private FixedJoint _rootJoint;
	private NpcRagdollImpacts _ragdollImpacts;
	// The hull's shapes and how they collided, out of the ragdoll's way while we're down
	private readonly List<(PhysicsShape Shape, bool Solid, bool Touch)> _knockedShapes = [];
	private TimeSince _sinceKnockdown;
	private TimeSince _sinceSpotSearch;
	private TimeSince _sinceSwingSample;
	// Heavy bodies touching us, counted per shape pair, and how long they have pushed us off our way
	private readonly Dictionary<PhysicsBody, int> _shovers = [];
	private TimeSince _shovedFor;
	private Vector3 _swingVelocity;
	private TimeSince _sinceRagdollMoved;
	private Vector3 _lastBodyVelocity;
	private bool _hadMovingBody;
	private bool _fallingFromLink;

	// The bones as the ragdoll last drew them, which getting up eases from; on every machine
	private Transform[] _ragdollPose;
	private Transform[] _getUpPose;
	private float[] _lyingHeight;
	private bool _onFront;

	// Off its front it gets onto all fours first: the crouch pitched forward this far about the hips
	private const float GetUpCrawlPitch = 60f;
	private int _getUpRoot = -1;
	private float _getUpTime;
	private TimeSince _sinceGettingUp;
	private KnockdownStates _shownKnockdown;

	private bool CanKnockdown => Networking.IsHost && CanBeHurt && KnockdownState != KnockdownStates.Down && UsesGround
		&& _rigidbody.IsValid() && Renderer.IsValid() && Renderer.Model?.Physics is { Parts.Count: > 1 };

	void IScenePhysicsEvents.PostPhysicsStep()
	{
		if ( !Networking.IsHost || _dead ) return;

		if ( KnockdownState == KnockdownStates.Down )
			return;

		var moving = _rigidbody.IsValid() && _rigidbody.MotionEnabled;
		var velocity = moving ? _rigidbody.Velocity : default;

		// What changed our speed this step other than our own mover steering and gravity
		var change = velocity - _lastBodyVelocity - (Movement.IsValid() ? Movement.SteeringChange : default);
		if ( moving && _rigidbody.Gravity )
			change -= Scene.PhysicsWorld.Gravity * _rigidbody.GravityScale * Time.Delta;

		// The ground stopping our fall is a landing, not a fling
		var landing = Math.Clamp( change.z, 0f, MathF.Max( -_lastBodyVelocity.z, 0f ) );
		change.z -= landing;

		// Launched or crossing a link, our speed is the mover's doing, down to the landing of a fall a link started
		var crossing = Movement.IsValid() && Movement.IsCrossingLink;
		var deliberate = _launched || crossing || _fallingFromLink;
		_fallingFromLink = crossing || (_fallingFromLink && moving && velocity.z < -1f);
		if ( moving && _hadMovingBody && !deliberate )
		{
			if ( PhysicsCarrier.IsValid() ? Flicked( velocity ) : change.Length >= KnockdownFling || landing >= KnockdownLanding )
				Knockdown( velocity );
			else
				UpdateShove( velocity );
		}
		else
		{
			_shovedFor = 0;
		}

		_lastBodyVelocity = velocity;
		_hadMovingBody = moving;
	}

	private bool Flicked( Vector3 velocity )
	{
		if ( _sinceSwingSample < 0.1f ) return false;

		var flicked = (velocity - _swingVelocity).Length >= KnockdownSwing;
		_swingVelocity = velocity;
		_sinceSwingSample = 0;
		return flicked;
	}

	// Players and NPCs walking into us, and the world itself, never knock us over
	private static bool Harmless( PhysicsBody other )
	{
		if ( other is null || other.BodyType == PhysicsBodyType.Static ) return true;

		var root = other.GameObject?.Root;
		return root is null || root.Tags.Has( "npc" ) || root.Tags.Has( "player" );
	}

	// Something heavy or fast enough hitting us
	private bool KnockdownOnHit( in Collision collision )
	{
		if ( !CanKnockdown ) return false;

		var other = collision.Other.Body;
		if ( Harmless( other ) ) return false;

		var share = MassShare( other );

		// Only how fast it came at us, our own speed taken from before physics slowed both
		var point = collision.Contact.Point;
		var into = collision.Normal;
		var approach = MathF.Abs( collision.Contact.NormalSpeed ) - MathF.Max( Vector3.Dot( _lastBodyVelocity, into ), 0f );
		if ( approach * MathF.Sqrt( share ) < KnockdownImpact ) return false;

		// The struck parts move off with the blow, the rest go on: a car low down sweeps the legs
		Knockdown( _lastBodyVelocity, point, Thrown( -into * approach * share ) );
		return KnockdownState == KnockdownStates.Down;
	}

	private void Touched( PhysicsBody other )
	{
		if ( !_rigidbody.IsValid() || Harmless( other ) || MassShare( other ) < 0.5f ) return;

		// Bodies destroyed while touching us never say they stopped
		foreach ( var body in _shovers.Keys )
			if ( !body.IsValid ) _shovers.Remove( body );

		_shovers[other] = _shovers.TryGetValue( other, out var pairs ) ? pairs + 1 : 1;
	}

	private void Untouched( PhysicsBody other )
	{
		if ( other is null || !_shovers.TryGetValue( other, out var pairs ) ) return;

		if ( pairs > 1 ) _shovers[other] = pairs - 1;
		else _shovers.Remove( other );
	}

	// Pushed off our mover's way on our feet by something heavy we don't stand on, for long enough
	private void UpdateShove( Vector3 velocity )
	{
		var pushed = false;
		if ( _shovers.Count > 0 && IsOnGround )
		{
			var intended = Movement.IsValid() ? Movement.IntendedVelocity : default;
			if ( (velocity - intended).WithZ( 0f ).Length >= KnockdownShoveSpeed )
			{
				var ground = GroundObject?.Root;
				foreach ( var body in _shovers.Keys )
					pushed |= body.IsValid && body.GameObject?.Root != ground;
			}
		}

		if ( !pushed )
		{
			_shovedFor = 0;
			return;
		}

		if ( _shovedFor >= KnockdownShoveTime )
			Knockdown( velocity );
	}

	// How much of the two masses the other body is. Something physics moves on its own path counts as all of it.
	private float MassShare( PhysicsBody other )
	{
		if ( other.BodyType != PhysicsBodyType.Dynamic ) return 1f;

		var own = OwnMass();
		return other.Mass / MathF.Max( other.Mass + own, 1f );
	}

	// A body physics doesn't move reports no mass of its own
	private float OwnMass() => _rigidbody.MassOverride > 0f ? _rigidbody.MassOverride : _rigidbody.Mass > 0f ? _rigidbody.Mass : 100f;

	// A body physics moves already took the hit; one it doesn't gets its share of the hitter's momentum
	private Vector3 Thrown( Vector3 shareOfHit ) => _rigidbody.MotionEnabled ? _rigidbody.Velocity : shareOfHit;

	// Ragdolls our own model from the pose it's in, moving at this velocity, the parts near a hit point at another
	private void Knockdown( Vector3 velocity, Vector3? hitPoint = null, Vector3 struckVelocity = default )
	{
		if ( !CanKnockdown ) return;

		// Knocked over again getting up: the ragdoll has the bones
		EndGetUp();

		// Out of the ragdoll's way before it's made where we stand
		KnockHull( true );

		var physics = Renderer.AddComponent<ModelPhysics>( false );
		physics.Flags |= ComponentFlags.NotSaved | ComponentFlags.NotCloned;
		physics.Model = Renderer.Model;
		physics.Renderer = Renderer;
		physics.IgnoreRoot = true;
		physics.Enabled = true;

		if ( physics.Bodies.Count == 0 )
		{
			physics.Destroy();
			KnockHull( false );
			return;
		}

		// A blow at a point lands on the parts within a hand's width of the nearest
		var bodies = physics.Bodies;
		var at = hitPoint ?? default;
		var nearest = float.MaxValue;
		for ( var i = 0; hitPoint.HasValue && i < bodies.Count; i++ )
		{
			if ( bodies[i].Component.IsValid() )
				nearest = MathF.Min( nearest, bodies[i].Component.WorldPosition.Distance( at ) );
		}

		_pelvis = null;
		var pelvisBone = int.MaxValue;
		for ( var i = 0; i < bodies.Count; i++ )
		{
			var part = bodies[i].Component;
			if ( !part.IsValid() ) continue;

			part.Velocity = hitPoint.HasValue && part.WorldPosition.Distance( at ) <= nearest + 12f ? struckVelocity : velocity;

			// Limbs knocking about don't hurt us, or a ragdoll swung around dies of its own flailing. What hits them still does.
			part.EnableImpactDamage = false;

			foreach ( var component in part.GameObject.Components.GetAll() )
			{
				if ( component is Rigidbody or Collider or Joint ) component.Flags |= ComponentFlags.NotSaved | ComponentFlags.NotCloned;
			}

			if ( bodies[i].Bone < pelvisBone )
			{
				pelvisBone = bodies[i].Bone;
				_pelvis = part;
			}
		}

		// Bodies on bone objects a client may not have: send the renderer's whole tree
		Network.Refresh( Renderer.GameObject );

		_ragdollImpacts = Renderer.AddComponent<NpcRagdollImpacts>( false );
		_ragdollImpacts.Flags |= ComponentFlags.Hidden | ComponentFlags.NotSaved | ComponentFlags.NotNetworked | ComponentFlags.NotCloned;
		_ragdollImpacts.Npc = this;
		_ragdollImpacts.Enabled = true;

		// Off our feet, as a launch is: the mover lets go and keeps its move for when we're up
		UpdateGroundFromTraceResult( default );
		_rigidbody.Velocity = _pelvis.Velocity;

		// At the pelvis, light and free to turn, so a rope or grab on us pulls the ragdoll by its middle
		WorldPosition = _pelvis.WorldPosition;
		if ( _rigidbody.PhysicsBody is { IsValid: true } root )
		{
			root.Position = WorldPosition;
			root.Mass = KnockdownRootMass;
			root.Locking = default;

			// Its hull still reaches up from here: keep its weight at the pelvis, not hanging off the chest
			root.OverrideMassCenter = true;
			root.LocalMassCenter = Vector3.Zero;
		}

		// Held where it is against the pelvis
		_rootJoint = AddComponent<FixedJoint>( false );
		_rootJoint.Flags |= ComponentFlags.Hidden | ComponentFlags.NotSaved | ComponentFlags.NotNetworked | ComponentFlags.NotCloned;
		_rootJoint.Body = _pelvis.GameObject;
		_rootJoint.Attachment = Joint.AttachmentMode.LocalFrames;
		_rootJoint.LocalFrame1 = global::Transform.Zero;
		_rootJoint.LocalFrame2 = _pelvis.WorldTransform.WithScale( 1f ).ToLocal( WorldTransform.WithScale( 1f ) );
		_rootJoint.EnableCollision = false;
		_rootJoint.LinearFrequency = 0f;
		_rootJoint.AngularFrequency = 0f;
		_rootJoint.Enabled = true;

		_knockdownPhysics = physics;
		_sinceKnockdown = 0;
		_sinceRagdollMoved = 0;
		_launched = false;
		KnockdownState = KnockdownStates.Down;
		UpdateBodyOwner();
		_shovers.Clear();
	}

	// Disabled down or getting up: let go of the ragdoll and stand where we are
	private void ClearKnockdown()
	{
		if ( Networking.IsHost && KnockdownState == KnockdownStates.Down && !GameObject.IsDestroyed )
			GetUp( WorldPosition );

		EndGetUp();
		KnockHull( false );
		if ( Networking.IsHost ) KnockdownState = KnockdownStates.None;
	}

	// A big part of the ragdoll hitting something that isn't us
	internal void OnRagdollCollision( in Collision collision )
	{
		if ( !Networking.IsHost || KnockdownState != KnockdownStates.Down || !_knockdownPhysics.IsValid() ) return;
		if ( MathF.Abs( collision.Contact.NormalSpeed ) <= RagdollSafeImpactSpeed ) return;

		var other = collision.Other.GameObject;
		if ( other.IsValid() && other.Root == GameObject.Root ) return;

		var self = collision.Self.Body;
		if ( self is null || self.Mass < _knockdownPhysics.Mass * RagdollImpactMassShare ) return;

		var normal = -collision.Normal;
		TakeImpact( MathF.Abs( collision.Contact.NormalSpeed ), collision.Contact.Point, normal, MathF.Abs( normal.z ) > 0.7f ? "fall" : "crush",
			RagdollSafeImpactSpeed, RagdollFatalImpactSpeed );
	}

	private void UpdateKnockdown()
	{
		if ( !_knockdownPhysics.IsValid() || !_pelvis.IsValid() || _pelvis.PhysicsBody is null || !Renderer.IsValid() )
		{
			GetUp( WorldPosition );
			return;
		}

		// Not while something holds it up
		if ( PhysicsCarrier.IsValid() || !RagdollAtRest() )
		{
			_sinceRagdollMoved = 0;
			return;
		}

		if ( _sinceKnockdown < 1.5f || _sinceRagdollMoved < 0.75f || _sinceSpotSearch < 0.25f ) return;

		_sinceSpotSearch = 0;

		if ( FindGetUpSpot( _pelvis.WorldPosition ) is { } spot )
			GetUp( spot );
	}

	// Lying on something and still against it, whatever the limbs twitch: not spinning, hanging or at the top of a flight
	private bool RagdollAtRest()
	{
		var bounds = _pelvis.PhysicsBody.GetBounds();
		var at = bounds.Center;
		var below = StandTrace( Scene.Trace.Ray( at, at + Vector3.Down * bounds.Size.z ) ).Run();
		if ( !below.Hit ) return false;

		var under = below.Body;
		var speed = (_pelvis.Velocity - (under?.GetVelocityAtPoint( at ) ?? default)).Length;
		var spin = (_pelvis.AngularVelocity - (under?.AngularVelocity ?? default)).Length;
		return speed < RestSpeed && spin < RestSpin;
	}

	// The closest point near the ragdoll the mover may stand on, if nothing is between them and there's room to stand
	private Vector3? FindGetUpSpot( Vector3 pelvis )
	{
		var agent = GetComponent<NavMeshAgent>( true );
		var radius = agent.IsValid() ? agent.Radius : 16f;
		var height = agent.IsValid() ? agent.Height : 72f;

		var reach = new Vector3( radius * 3f, radius * 3f, height * 0.5f );
		var near = new BBox( pelvis - reach, pelvis + reach );

		// No navmesh near, as on a car roof: stand where we lie
		var closest = agent.IsValid() ? Scene.NavMesh?.GetClosestPoint( near, agent ) : Movement?.ProjectPoint( pelvis );
		var spot = closest is { } point && near.Contains( point ) ? point : pelvis;

		// Narrow, as the pelvis can lie against a wall. Not through one, nor through the floor it lies on to another.
		var lift = Vector3.Up * (Movement.IsValid() ? Movement.StepHeight : 0f);
		if ( StandTrace( Scene.Trace.Sphere( radius * 0.25f, pelvis, spot + lift ) ).Run().Hit ) return null;

		var hull = new Capsule( Vector3.Up * radius, Vector3.Up * MathF.Max( height - lift.z - radius, radius ), radius );
		// Down onto what's under it, the spot or the pelvis lying above it
		var stand = StandTrace( Scene.Trace.Capsule( hull, spot + lift, spot - lift - Vector3.Up * height * 0.5f ) ).Run();
		return stand.Hit && !stand.StartedSolid ? stand.EndPosition : null;
	}

	private SceneTrace StandTrace( SceneTrace trace ) => trace.IgnoreGameObjectHierarchy( GameObject ).WithCollisionRules( Tags );

	private void GetUp( Vector3 at )
	{
		var facing = GetUpFacing();
		if ( _rootJoint.IsValid() ) _rootJoint.Destroy();
		_rootJoint = null;
		if ( _ragdollImpacts.IsValid() ) _ragdollImpacts.Destroy();
		_ragdollImpacts = null;

		// The body's own mass, mass centre and locks back, then stood up where we get up
		if ( _rigidbody.IsValid() ) _rigidbody.UpdateBody();
		WorldPosition = at;
		WorldRotation = facing;
		if ( _rigidbody.PhysicsBody is { IsValid: true } body )
			body.Transform = WorldTransform.WithScale( 1f );

		if ( _knockdownPhysics.IsValid() )
		{
			// Let go of the bones now, so they're ours to ease this frame
			_knockdownPhysics.Renderer = null;
			_knockdownPhysics.Destroy();
			if ( Renderer.IsValid() ) Network.Refresh( Renderer.GameObject );
		}

		_knockdownPhysics = null;
		_pelvis = null;
		KnockdownState = KnockdownStates.GettingUp;
		_shownKnockdown = KnockdownStates.GettingUp;
		StartGetUp();
		if ( _getUpRoot >= 0 ) ApplyGetUpPose();

		KnockHull( false );

		if ( _rigidbody.IsValid() ) _rigidbody.Velocity = default;
		_lastBodyVelocity = default;
		_hadMovingBody = false;

		// Lying down wasn't a fall
		_wasAirborne = false;
		_airVelocity = default;
		_timeSinceImpact = 0;

		// The mover, or one picked while we were down, takes the body back and waits until we're up
		if ( _pendingMover.IsValid() ) EnableOnly( _pendingMover );
		_pendingMover = null;
		_activeMover = null;
		UpdateMovement();
		if ( Movement.IsValid() && _getUpRoot >= 0 ) Movement.PauseMovement( _getUpTime );
	}

	// Down, the hull only gives our body mass; set on the shapes so nothing saved or copied keeps it
	private void KnockHull( bool down )
	{
		if ( down == _knockedShapes.Count > 0 ) return;

		if ( down )
		{
			foreach ( var collider in GetComponentsInChildren<Collider>() )
			{
				if ( collider.IsTrigger || collider.GameObject.Flags.Contains( GameObjectFlags.PhysicsBone ) ) continue;

				foreach ( var shape in collider.Shapes )
				{
					_knockedShapes.Add( (shape, shape.EnableSolidCollisions, shape.EnableTouch) );
					shape.DisableAllCollision();
					shape.IgnoreTraces = true;
				}
			}

			return;
		}

		foreach ( var (shape, solid, touch) in _knockedShapes )
		{
			if ( !shape.IsValid ) continue;

			shape.EnableSolidCollisions = solid;
			shape.EnableTouch = touch;
			shape.IgnoreTraces = shape.Collider?.ColliderFlags.Contains( ColliderFlags.IgnoreTraces ) ?? false;
		}

		_knockedShapes.Clear();

		// Contacts start over with the hull back
		_shovers.Clear();
	}

	// The ragdoll's root bone: the first bone with a body, which the rest of the ragdoll hangs off
	private int RagdollRoot()
	{
		if ( !Renderer.IsValid() || Renderer.Model is not { Physics: { } physics } model ) return -1;

		var root = -1;
		foreach ( var part in physics.Parts )
		{
			if ( model.Bones.GetBone( part.BoneName ) is { } bone && (root < 0 || bone.Index < root) )
				root = bone.Index;
		}

		return root;
	}

	// Facing that stands the root bone up with the least turn: its feet from its back, its head from its front
	private Rotation GetUpFacing()
	{
		var root = RagdollRoot();
		if ( root < 0 || _ragdollPose is null || root >= _ragdollPose.Length || _ragdollPose[root] == default )
			return Rotation.FromYaw( WorldRotation.Yaw() );

		var lying = _ragdollPose[root].Rotation;
		var standing = WorldRotation.Inverse * Renderer.WorldRotation * Renderer.Model.Bones.AllBones[root].LocalTransform.Rotation;
		var turned = Rotation.FromAxis( Vector3.Up, 180f ) * standing;

		// Any yaw of the standing root is a mix of it and its half turn: this one comes closest to how it lies
		static float Dot( Rotation a, Rotation b ) => a.x * b.x + a.y * b.y + a.z * b.z + a.w * b.w;
		var half = MathF.Atan2( Dot( lying, turned ), Dot( lying, standing ) );
		return Rotation.FromAxis( Vector3.Up, (2f * half).RadianToDegree() );
	}

	// Run on every machine, before anything moves us this frame
	private void UpdateKnockdownPose()
	{
		var state = KnockdownState;
		KnockHull( state == KnockdownStates.Down );

		if ( state == KnockdownStates.Down )
		{
			EndGetUp();
			CaptureRagdollPose();
		}
		else if ( state == KnockdownStates.GettingUp && _shownKnockdown == KnockdownStates.Down )
		{
			StartGetUp();
		}

		_shownKnockdown = state;

		if ( _getUpRoot >= 0 && (state != KnockdownStates.GettingUp || !ApplyGetUpPose()) )
			EndGetUp();

		if ( Networking.IsHost && state == KnockdownStates.GettingUp && _getUpRoot < 0 )
			KnockdownState = KnockdownStates.None;
	}

	// Only while the ragdoll drives the bones: on a client it can go before the state says we're getting up
	private void CaptureRagdollPose()
	{
		if ( !Renderer.IsValid() || Renderer.Model is not { } model || !Renderer.SceneModel.IsValid() ) return;
		if ( Renderer.GetComponent<ModelPhysics>() is not { Bodies.Count: > 0 } ) return;

		if ( _ragdollPose?.Length != model.BoneCount )
			_ragdollPose = new Transform[model.BoneCount];

		Renderer.SceneModel.GetBoneWorldTransforms( _ragdollPose );
	}

	// Eases from the captured ragdoll, with the time it takes from how far it lies from standing
	private void StartGetUp()
	{
		var root = RagdollRoot();
		var bones = Renderer.IsValid() ? Renderer.Model?.Bones.AllBones : null;
		if ( bones is null || root < 0 || _ragdollPose?.Length != bones.Count || _ragdollPose[root] == default ) return;

		if ( _lyingHeight?.Length != bones.Count ) _lyingHeight = new float[bones.Count];
		for ( var i = 0; i < bones.Count; i++ )
			_lyingHeight[i] = _ragdollPose[i].Position.z;

		// Parents come before their children
		for ( var i = bones.Count - 1; i > root; i-- )
		{
			if ( bones[i].Parent is { } parent )
				_ragdollPose[i] = _ragdollPose[parent.Index].ToLocal( _ragdollPose[i] );
		}

		var turn = 0f;
		for ( var i = root + 1; i < bones.Count; i++ )
		{
			var parent = bones[i].Parent;
			if ( parent is null || parent.Index < root || _ragdollPose[parent.Index] == default )
			{
				_ragdollPose[i] = default;
				continue;
			}

			var standing = parent.LocalTransform.ToLocal( bones[i].LocalTransform );
			turn = MathF.Max( turn, _ragdollPose[i].Rotation.Distance( standing.Rotation ) );
		}

		var lying = _ragdollPose[root];
		var stood = Renderer.WorldTransform.ToWorld( bones[root].LocalTransform );
		turn = MathF.Max( turn, lying.Rotation.Distance( stood.Rotation ) );

		// Lying on its front: the way the model faces standing points down
		_onFront = (lying.Rotation * (stood.Rotation.Inverse * Renderer.WorldRotation.Forward)).z < 0f;

		_getUpTime = Math.Clamp( MathF.Max( lying.Position.Distance( stood.Position ) / GetUpSpeed, turn / GetUpTurnSpeed ), GetUpMinTime, GetUpMaxTime ) + GetUpStandTime;
		_getUpRoot = root;
		_sinceGettingUp = 0;
		if ( _getUpPose?.Length != bones.Count ) _getUpPose = new Transform[bones.Count];
	}

	// Gathers into a crouch, each joint turning the shortest way from how it lay, then the animation stands it up. False once done.
	private bool ApplyGetUpPose()
	{
		var scene = Renderer.IsValid() ? Renderer.SceneModel : null;
		if ( _sinceGettingUp >= _getUpTime || !scene.IsValid() || Renderer.Model?.BoneCount != _ragdollPose.Length ) return false;

		static float Smooth( float t ) => t * t * t * (t * (6f * t - 15f) + 10f);
		var gather = _getUpTime - GetUpStandTime;

		// The limbs tuck in before the body turns over them
		var limbs = Smooth( MathF.Min( _sinceGettingUp / (gather * 0.6f), 1f ) );
		var ease = Smooth( MathF.Min( _sinceGettingUp / gather, 1f ) );
		var bones = Renderer.Model.Bones.AllBones;
		var model = Renderer.WorldTransform;

		// Rising before the gather quite ends, so the crouch is only passed through
		var rise = gather * 0.7f;
		var risen = Smooth( Math.Clamp( (_sinceGettingUp - rise) / (_getUpTime - rise), 0f, 1f ) );
		var pitch = Rotation.FromAxis( model.Rotation.Left, _onFront ? GetUpCrawlPitch * (1f - risen) : 0f );

		// Grounded: no bone sinks below both where it lay and where it's animated, and the lowest of them touches
		var lift = float.NegativeInfinity;
		for ( var i = _getUpRoot; i < bones.Count; i++ )
		{
			var from = _ragdollPose[i];
			if ( from == default ) continue;

			var animated = scene.GetWorldSpaceAnimationTransform( i );
			if ( i == _getUpRoot )
			{
				var target = pitch * animated.Rotation;
				_getUpPose[i] = model.ToLocal( new Transform( Vector3.Lerp( from.Position, animated.Position, ease ), Rotation.Slerp( from.Rotation, target, ease ), animated.Scale ) );
			}
			else
			{
				var parent = bones[i].Parent.Index;
				var to = scene.GetWorldSpaceAnimationTransform( parent ).ToLocal( animated );
				_getUpPose[i] = _getUpPose[parent].ToWorld( new Transform( to.Position, Rotation.Slerp( from.Rotation, to.Rotation, limbs ), to.Scale ) );
			}

			var lowest = MathF.Min( _lyingHeight[i], animated.Position.z );
			lift = MathF.Max( lift, lowest - model.PointToWorld( _getUpPose[i].Position ).z );
		}

		var up = model.PointToLocal( model.Position + Vector3.Up * (float.IsFinite( lift ) ? lift : 0f) );
		for ( var i = _getUpRoot; i < bones.Count; i++ )
		{
			if ( _ragdollPose[i] != default )
				scene.SetBoneOverride( i, _getUpPose[i].WithPosition( _getUpPose[i].Position + up ) );
		}

		Animation.Crouch = 1f - risen;
		return true;
	}

	private void EndGetUp()
	{
		if ( _getUpRoot < 0 ) return;

		_getUpRoot = -1;

		// Made relative to the parents getting up: the next knockdown captures it afresh
		if ( _ragdollPose is not null ) Array.Clear( _ragdollPose );
		if ( Renderer.IsValid() ) Renderer.ClearPhysicsBones();
		if ( Animation.IsValid() ) Animation.Crouch = 0f;
	}
}

/// <summary>
/// Hears a knocked down NPC's ragdoll collisions, which go to its renderer's object rather than to the NPC.
/// </summary>
internal sealed class NpcRagdollImpacts : Component, Component.ICollisionListener
{
	public BaseNpc Npc { get; set; }

	void Component.ICollisionListener.OnCollisionStart( Collision collision )
	{
		if ( Npc.IsValid() ) Npc.OnRagdollCollision( collision );
	}
}
