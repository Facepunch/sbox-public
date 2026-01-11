namespace Sandbox;

/// <summary>
/// Physics for a model. This is primarily used for ragdolls and other physics driven models, otherwise you should be using a Rigidbody.
/// </summary>
[Expose]
[Title( "Model Physics" )]
[Category( "Physics" )]
[Icon( "check_box_outline_blank" )]
public sealed partial class ModelPhysics : Component, IScenePhysicsEvents, IHasModel
{
	[Obsolete( "No longer in use" )]
	public PhysicsGroup PhysicsGroup { get; private set; }

	private Model _model;
	private SkinnedModelRenderer _renderer;
	private RigidbodyFlags _rigidBodyFlags;
	private PhysicsLock _locking;
	private bool _motionEnabled = true;
	private bool _onEnabled = false;

	[Property, Hide]
	public bool PhysicsWereCreated { get; set; }

	/// <summary>
	/// Number of times the physics have been rebuilt. Debugging use only.
	/// </summary>
	internal int PhysicsRebuildCount { get; set; }

	/// <summary>
	/// Number of times the physics have been rebuilt. Debugging use only.
	/// </summary>
	internal int PhysicsDestroyCount { get; set; }

	/// <summary>
	/// The model used to generate physics bodies, collision shapes, and joints.
	/// </summary>
	[Property]
	public Model Model
	{
		get => _model;
		set
		{
			if ( _model == value )
				return;

			_model = value;

			OnModelChanged();
		}
	}

	/// <summary>
	/// The renderer that receives transform updates from physics bodies.
	/// </summary>
	[Property]
	public SkinnedModelRenderer Renderer
	{
		get => _renderer;
		set
		{
			if ( _renderer == value )
				return;

			if ( _renderer.IsValid() )
			{
				_renderer.ClearPhysicsBones();
			}

			_renderer = value;
		}
	}

	/// <summary>
	/// If true, the root physics body will not drive this component's transform.
	/// </summary>
	[Property]
	public bool IgnoreRoot { get; set; }

	/// <summary>
	/// Rigidbody flags applied to all bodies.
	/// </summary>
	[Property, Group( "Physics" )]
	public RigidbodyFlags RigidbodyFlags
	{
		get => _rigidBodyFlags;
		set
		{
			if ( _rigidBodyFlags == value )
				return;

			_rigidBodyFlags = value;

			foreach ( var body in Bodies )
			{
				if ( !body.Component.IsValid() )
					continue;

				body.Component.RigidbodyFlags = value;
			}
		}
	}

	/// <summary>
	/// Rigidbody locking applied to all bodies.
	/// </summary>
	[Property, Group( "Physics" )]
	public PhysicsLock Locking
	{
		get => _locking;
		set
		{
			_locking = value;

			foreach ( var body in Bodies )
			{
				if ( !body.Component.IsValid() )
					continue;

				body.Component.Locking = value;
			}
		}
	}

	/// <summary>
	/// All bodies will be put to sleep on start.
	/// </summary>
	[Property, Group( "Physics" )]
	public bool StartAsleep { get; set; }

	/// <summary>
	/// Enable to drive renderer from physics, disable to drive physics from renderer.
	/// </summary>
	[Property, Group( "Physics" )]
	public bool MotionEnabled
	{
		get => _motionEnabled;
		set
		{
			if ( _motionEnabled == value )
				return;

			_motionEnabled = value;

			foreach ( var body in Bodies )
			{
				if ( !body.Component.IsValid() )
					continue;

				body.Component.MotionEnabled = value;
			}
		}
	}

	/// <summary>
	/// Returns the total mass of every <see cref="Rigidbody"/>
	/// </summary>
	public float Mass => Bodies.Sum( x => x.Component?.Mass ?? default );

	/// <summary>
	/// Returns the center of mass of every <see cref="Rigidbody"/> in world-space
	/// </summary>
	public Vector3 MassCenter
	{
		get
		{
			var world = WorldTransform;

			var mass = 0.0f;
			var center = Vector3.Zero;

			foreach ( var body in Bodies )
			{
				var rb = body.Component;
				if ( !rb.IsValid() ) continue;

				mass += rb.Mass;
				center += rb.Mass * world.PointToWorld( rb.MassCenter );
			}

			return mass > 0.0f ? center / mass : world.Position;
		}
	}

	private void OnModelChanged()
	{
		if ( !_onEnabled ) return;
		if ( !Active ) return;
		if ( IsProxy ) return;
		if ( GameObject.IsDeserializing ) return;

		DestroyPhysics();
		CreatePhysics();
	}

	/// <summary>
	/// Apply body transforms to renderer bones.
	/// </summary>
	private void PositionRendererBonesFromPhysics()
	{
		var rootBody = GetComponentInChildren<Rigidbody>( true, true );

		if ( Scene.IsEditor )
		{
			if ( rootBody.IsValid() && rootBody.PhysicsBody.MotionEnabled )
			{
				WorldTransform = rootBody.WorldTransform;
			}
		}
		else if ( !IgnoreRoot && rootBody.IsValid() && rootBody.MotionEnabled )
		{
			WorldTransform = rootBody.WorldTransform;
		}

		if ( !Renderer.IsValid() )
			return;

		var so = Renderer.SceneModel;
		if ( !so.IsValid() )
			return;

		var world = WorldTransform;

		foreach ( var body in Bodies )
		{
			var rb = body.Component;
			if ( !rb.IsValid() )
				continue;

			if ( !rb.GameObject.Flags.Contains( GameObjectFlags.PhysicsBone ) )
				continue;

			// Only drive physics from animation if we have motion disabled.
			if ( !MotionEnabled && !rb.MotionEnabled )
			{
				// Use the animation pose when body motion is disabled.
				var local = so.Transform.ToLocal( so.GetWorldSpaceAnimationTransform( body.Bone ) );
				so.SetBoneOverride( body.Bone, local );

				// Move object to animation pose, physics object will move towards it in pre physics step.
				if ( rb.Transform.SetLocalTransformFast( world.ToWorld( local ) ) )
					rb.Transform.TransformChanged( true );
			}
			else
			{
				// Bone overrides are in modelspace, strip off our world transform from body world transform.
				so.SetBoneOverride( body.Bone, world.ToLocal( rb.WorldTransform ) );
			}
		}
	}

	/// <summary>
	/// Smooth move bodies to animated bone transforms for bodies that have motion disabled.
	/// </summary>
	private void MovePhysicsFromAnimation()
	{
		if ( Scene.IsEditor )
			return;

		if ( !Renderer.IsValid() )
			return;

		var so = Renderer.SceneModel;
		if ( !so.IsValid() )
			return;

		foreach ( var body in Bodies )
		{
			if ( !body.Component.IsValid() )
				continue;

			if ( body.Component.MotionEnabled )
				continue;

			if ( !body.Component.PhysicsBody.IsValid() )
				continue;

			// Move kinematic body to object transform.
			body.Component.PhysicsBody.Move( body.Component.WorldTransform, Time.Delta );
		}
	}

	/// <summary>
	/// Adjust joint points for body scaling.
	/// </summary>
	private void UpdateJointScale()
	{
		foreach ( var joint in Joints )
		{
			if ( !joint.Component.IsValid() )
				continue;

			var point1 = joint.Component.Point1;
			point1.LocalPosition = joint.LocalFrame1.Position * joint.Component.WorldTransform.UniformScale;
			joint.Component.Point1 = point1;

			var point2 = joint.Component.Point2;
			point2.LocalPosition = joint.LocalFrame2.Position * joint.Component.Body.WorldTransform.UniformScale;
			joint.Component.Point2 = point2;
		}
	}

	/// <summary>
	/// Put all bodies to sleep.
	/// </summary>
	private void Sleep()
	{
		foreach ( var body in Bodies )
		{
			if ( !body.Component.IsValid() )
				continue;

			body.Component.Sleeping = true;
		}
	}

	protected override void OnAwake()
	{
		_renderer ??= GetComponent<SkinnedModelRenderer>();

		// Auto set the model using the renderer model if it's not already set.
		if ( _model is null && _renderer.IsValid() )
		{
			_model = _renderer.Model;
		}
	}

	protected override void OnStart()
	{
		if ( StartAsleep )
		{
			Sleep();
		}
	}

	protected override void OnUpdate()
	{
		// Proxy only needs to sync renderer to physics.
		if ( IsProxy )
		{
			UpdateProxyTransforms();

			return;
		}

		PositionRendererBonesFromPhysics();

		// In editor we need to move the bodies with this transform while keeping the local pose.
		if ( Scene.IsEditor )
		{
			foreach ( var body in Bodies )
			{
				if ( !body.Component.IsValid() )
					continue;

				if ( body.Component.PhysicsBody.MotionEnabled )
				{
					// Update local transform pose when simulating physics in editor.
					// TODO
					//body.LocalTransform = WorldTransform.ToLocal( body.Component.WorldTransform );
				}
				else
				{
					// Move body when moving this gameobject in editor.
					body.Component.WorldTransform = WorldTransform.ToWorld( body.LocalTransform );
				}
			}
		}
	}

	protected override void OnFixedUpdate()
	{
		if ( IsProxy )
			return;

		UpdateJointScale();

	}

	void IScenePhysicsEvents.PrePhysicsStep()
	{
		if ( IsProxy )
		{
			// Move proxy bodies to their networked transform.
			MoveProxyBodies();
		}
		else if ( !MotionEnabled )
		{
			// Only drive physics from animation if we have motion disabled.
			MovePhysicsFromAnimation();
		}
	}

	void IScenePhysicsEvents.PostPhysicsStep()
	{
		if ( !IsProxy )
		{
			// Network body transforms.
			SetBodyTransforms();
		}
	}

	protected override void OnEnabled()
	{
		_onEnabled = true;

		if ( PhysicsWereCreated )
		{
			EnableComponents();
		}
		else
		{
			CreatePhysics();
		}
	}

	protected override void OnDisabled()
	{
		DisableComponents();

		_onEnabled = false;
	}

	protected override void OnDestroy()
	{
		DestroyPhysics();

		_onEnabled = false;
	}

	//
	// These are temporary holders so you can set the bone positions before the component is enabled
	// They are used once on initializing and then removed
	//
	Transform[] _rendererBonePosition;
	SkinnedModelRenderer.BoneVelocity[] _rendererBoneVelocity;

	private void PositionPhysicsFromRendererBones()
	{
		if ( _rendererBonePosition is null || _rendererBoneVelocity is null )
		{
			if ( Renderer is not null && Renderer.SceneModel.IsValid() )
			{
				_rendererBonePosition = Renderer.GetBoneTransforms( true );
				_rendererBoneVelocity = Renderer.GetBoneVelocities();
			}
		}

		if ( _rendererBonePosition is null || _rendererBoneVelocity is null )
			return;

		var boneObjects = Model.CreateBoneObjects( GameObject );

		foreach ( var pair in boneObjects )
		{
			if ( pair.Key is null )
				continue;

			if ( !pair.Value.IsValid() )
				continue;

			if ( _rendererBonePosition.Length <= pair.Key.Index )
				continue;

			if ( _rendererBoneVelocity.Length <= pair.Key.Index )
				continue;

			var tx = _rendererBonePosition[pair.Key.Index];
			var vl = _rendererBoneVelocity[pair.Key.Index];

			if ( pair.Value.GetComponent<Rigidbody>() is { } body )
			{
				body.WorldTransform = tx;
				body.Velocity = vl.Linear;
				body.AngularVelocity = vl.Angular;
			}
		}

		_rendererBoneVelocity = default;
		_rendererBonePosition = default;
	}

	/// <summary>
	/// Copy the bone positions and velocities from a different SkinnedModelRenderer
	/// </summary>
	public void CopyBonesFrom( SkinnedModelRenderer source, bool teleport )
	{
		if ( !source.IsValid() )
			return;

		_rendererBonePosition = source.GetBoneTransforms( true );
		_rendererBoneVelocity = source.GetBoneVelocities();

		PositionPhysicsFromRendererBones();
		PositionRendererBonesFromPhysics();
	}

	void IHasModel.OnModelReloaded()
	{
		OnModelReloaded();
	}
}
