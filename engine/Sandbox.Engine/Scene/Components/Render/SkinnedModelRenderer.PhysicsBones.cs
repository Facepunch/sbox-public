
namespace Sandbox;

partial class SkinnedModelRenderer
{
	/// <summary>
	/// Simulates bones using physics defined on the model.
	/// </summary>
	internal BonePhysics Physics { get; private set; }
	internal class BonePhysics
	{
		/// <summary>
		/// Isolated physics world to simulate physics bones.
		/// </summary>
		PhysicsWorld _physicsWorld;

		readonly record struct Body( PhysicsBody PhysicsBody, int Bone )
		{
			public Transform WorldTransform { get; init; }
			public Transform SimulatedWorld { get; init; }
			public Transform TargetWorld { get; init; }

			public PhysicsBody ParentBody { get; init; }
			public int ParentBone { get; init; }
			public int ParentBodyIndex { get; init; }
		}
		readonly record struct Joint( PhysicsJoint PhysicsJoint, Body Body1, Body Body2 );

		readonly List<Body> _bodies = [];
		readonly List<Joint> _joints = [];

		readonly SkinnedModelRenderer _renderer;

		internal BonePhysics( SkinnedModelRenderer renderer, SkinnedModelRenderer parent )
		{
			_renderer = renderer;

			if ( _physicsWorld is not null ) return;
			if ( !_renderer.HasBonePhysics() ) return;

			_physicsWorld = new PhysicsWorld
			{
				SleepingEnabled = false,
				SimulationMode = PhysicsSimulationMode.Discrete,
				DebugSceneWorld = _renderer.Scene.DebugSceneWorld,
				Scene = _renderer.Scene,
				MaximumLinearSpeed = 1000,
			};

			var physics = _renderer.Model.Physics;

			CreateBodies( physics, parent );
			CreateJoints( physics );
		}

		public void Destroy()
		{
			if ( _physicsWorld is null ) return;

			_renderer.ClearPhysicsBones();

			_physicsWorld.Delete();
			_physicsWorld = null;

			_bodies.Clear();
			_joints.Clear();
		}

		public void Update()
		{
			if ( _physicsWorld is null ) return;

			var so = _renderer.SceneModel;
			if ( !so.IsValid() ) return;

			_renderer.ClearPhysicsBones();

			var world = so.Transform;

			for ( var i = 0; i < _bodies.Count; i++ )
			{
				var body = _bodies[i];

				if ( body.PhysicsBody.BodyType == PhysicsBodyType.Dynamic )
				{
					// We're not attached to a kinematic ancestor, bail.
					if ( body.ParentBody is null ) continue;

					// Get where the physics body is.
					var bodyWorld = body.PhysicsBody.GetLerpedTransform( Time.Now );

					// Get where the kinematic root physics body is.
					var parentBodyWorld = body.ParentBody.GetLerpedTransform( Time.Now );

					// Transform physics into localspace relative to kinematic root.
					var bodyLocal = parentBodyWorld.ToLocal( bodyWorld );

					// Transform physics back into rendering worldspace.
					var parentBoneWorld = so.GetWorldSpaceAnimationTransform( body.ParentBone );
					var boneWorld = parentBoneWorld.ToWorld( bodyLocal );

					_bodies[i] = body with { WorldTransform = boneWorld };

					// Transform bone world to modelspace.
					var boneLocal = world.ToLocal( boneWorld );
					so.SetBoneOverride( body.Bone, boneLocal );
				}
				else
				{
					// Set the physics bone to the rendering transform.
					var boneWorld = so.GetWorldSpaceAnimationTransform( body.Bone );
					var local = world.ToLocal( boneWorld );
					so.SetBoneOverride( body.Bone, local );

					// Store the target transform of this keyframe body so it can be moved to it in the physics step.
					_bodies[i] = body with { WorldTransform = boneWorld };
				}
			}
		}

		public void Step()
		{
			if ( !_physicsWorld.IsValid() ) return;

			foreach ( var body in _bodies )
			{
				if ( body.PhysicsBody.BodyType == PhysicsBodyType.Keyframed )
				{
					// Move the keyframe to target using velocity.
					body.PhysicsBody.Move( body.WorldTransform, Time.Delta );
				}
			}

			// Run at max substeps until there's a reason not to.
			_physicsWorld.Step( Time.Now, Time.Delta, 64 );

			for ( var i = 0; i < _bodies.Count; i++ )
			{
				var body = _bodies[i];

				_bodies[i] = body with
				{
					SimulatedWorld = body.PhysicsBody.Transform,
					TargetWorld = body.WorldTransform
				};
			}

			for ( var i = 0; i < _bodies.Count; i++ )
			{
				var body = _bodies[i];

				if ( body.PhysicsBody.BodyType == PhysicsBodyType.Dynamic )
				{
					if ( body.ParentBody is null ) continue;
					if ( body.ParentBodyIndex < 0 ) continue;

					var parent = _bodies[body.ParentBodyIndex];
					var local = parent.SimulatedWorld.ToLocal( body.SimulatedWorld );
					body.PhysicsBody.Transform = parent.TargetWorld.ToWorld( local );
				}
				else
				{
					body.PhysicsBody.Transform = body.TargetWorld;
				}
			}
		}

		public void DebugDraw()
		{
			if ( !_physicsWorld.IsValid() ) return;

			_physicsWorld.DebugDraw();
		}

		void CreateBodies( PhysicsGroupDescription physics, SkinnedModelRenderer parent )
		{
			var bones = _renderer.Model.Bones;
			var targetBones = parent.Model.Bones;
			var world = _renderer.WorldTransform;
			var boneToBody = new Dictionary<int, PhysicsBody>();

			foreach ( var part in physics.Parts )
			{
				var bone = bones.GetBone( part.BoneName );

				if ( !_renderer.TryGetBoneTransform( bone, out var boneWorld ) )
					boneWorld = world.ToWorld( part.Transform );

				var body = new PhysicsBody( _physicsWorld )
				{
					Transform = boneWorld,
					LinearDamping = part.LinearDamping,
					AngularDamping = part.AngularDamping,
					Mass = part.Mass,
					OverrideMassCenter = part.OverrideMassCenter,
					LocalMassCenter = part.MassCenterOverride,
					BodyType = targetBones.HasBone( bone.Name ) ? PhysicsBodyType.Keyframed : PhysicsBodyType.Dynamic,
					UseController = true,
					EnableCollisionSounds = false,
				};

				boneToBody[bone.Index] = body;

				_bodies.Add( new Body( body, bone.Index )
				{
					WorldTransform = boneWorld,
					ParentBody = null,
					ParentBone = -1,
					ParentBodyIndex = -1
				} );

				foreach ( var sphere in part.Spheres )
					body.AddSphereShape( sphere.Sphere ).Surface = sphere.Surface;

				foreach ( var capsule in part.Capsules )
					body.AddCapsuleShape( capsule.Capsule.CenterA, capsule.Capsule.CenterB, capsule.Capsule.Radius ).Surface = capsule.Surface;

				foreach ( var hull in part.Hulls )
					body.AddHullShape( Vector3.Zero, Rotation.Identity, hull.GetPoints().ToList() ).Surface = hull.Surface;
			}

			var bodyToIndex = new Dictionary<PhysicsBody, int>( _bodies.Count );
			for ( var i = 0; i < _bodies.Count; i++ )
				bodyToIndex[_bodies[i].PhysicsBody] = i;

			for ( var i = 0; i < _bodies.Count; i++ )
			{
				var body = _bodies[i];
				if ( body.PhysicsBody.BodyType != PhysicsBodyType.Dynamic ) continue;

				var bone = bones.AllBones[body.Bone];
				if ( bone.Parent is null ) continue;

				PhysicsBody parentBody = null;
				var parentBone = -1;
				var parentBodyIndex = -1;

				var parentIndex = bone.Parent.Index;

				while ( parentIndex >= 0 )
				{
					if ( boneToBody.TryGetValue( parentIndex, out var physicsBody ) &&
						 physicsBody.BodyType == PhysicsBodyType.Keyframed )
					{
						parentBody = physicsBody;
						parentBone = parentIndex;
						parentBodyIndex = bodyToIndex[physicsBody];
						break;
					}

					parentIndex = bones.AllBones[parentIndex].Parent?.Index ?? -1;
				}

				_bodies[i] = body with
				{
					ParentBody = parentBody,
					ParentBone = parentBone,
					ParentBodyIndex = parentBodyIndex
				};
			}
		}

		void CreateJoints( PhysicsGroupDescription physics )
		{
			foreach ( var jointDesc in physics.Joints )
			{
				var body1 = _bodies[jointDesc.Body1];
				var body2 = _bodies[jointDesc.Body2];

				var localFrame1 = jointDesc.Frame1;
				var localFrame2 = jointDesc.Frame2;

				var point1 = new PhysicsPoint( body1.PhysicsBody, localFrame1.Position, localFrame1.Rotation );
				var point2 = new PhysicsPoint( body2.PhysicsBody, localFrame2.Position, localFrame2.Rotation );

				PhysicsJoint joint = null;

				if ( jointDesc.Type == PhysicsGroupDescription.JointType.Hinge )
				{
					var hingeJoint = PhysicsJoint.CreateHinge( point1, point2 );

					if ( jointDesc.EnableTwistLimit )
					{
						hingeJoint.MinAngle = jointDesc.TwistMin;
						hingeJoint.MaxAngle = jointDesc.TwistMax;
					}

					if ( jointDesc.EnableAngularMotor )
					{
						var worldFrame1 = body1.PhysicsBody.Transform.ToWorld( localFrame1 );
						var hingeAxis = worldFrame1.Rotation.Up;
						var targetVelocity = hingeAxis.Dot( jointDesc.AngularTargetVelocity );

						hingeJoint.native.SetAngularMotor( targetVelocity, jointDesc.MaxTorque );
					}

					joint = hingeJoint;
				}
				else if ( jointDesc.Type == PhysicsGroupDescription.JointType.Ball )
				{
					var ballJoint = PhysicsJoint.CreateBallSocket( point1, point2 );

					if ( jointDesc.EnableSwingLimit )
					{
						ballJoint.SwingLimitEnabled = true;
						ballJoint.SwingLimit = new Vector2( jointDesc.SwingMin, jointDesc.SwingMax );
					}

					if ( jointDesc.EnableTwistLimit )
					{
						ballJoint.TwistLimitEnabled = true;
						ballJoint.TwistLimit = new Vector2( jointDesc.TwistMin, jointDesc.TwistMax );
					}

					joint = ballJoint;
				}
				else if ( jointDesc.Type == PhysicsGroupDescription.JointType.Fixed )
				{
					var fixedJoint = PhysicsJoint.CreateFixed( point1, point2 );
					fixedJoint.SpringLinear = new PhysicsSpring( jointDesc.LinearFrequency, jointDesc.LinearDampingRatio );
					fixedJoint.SpringAngular = new PhysicsSpring( jointDesc.AngularFrequency, jointDesc.AngularDampingRatio );

					joint = fixedJoint;
				}

				if ( joint.IsValid() )
				{
					_joints.Add( new Joint( joint, body1, body2 ) );
				}
			}
		}
	}
}
