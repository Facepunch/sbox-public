
namespace Sandbox;

public sealed partial class ModelPhysics
{
	/// <summary>
	/// Create all the bodies, colliders and joints.
	/// </summary>
	private void CreatePhysics()
	{
		if ( !Active ) return;
		if ( IsProxy ) return;
		if ( PhysicsWereCreated ) return;

		DestroyPhysics();

		if ( !Model.IsValid() ) return;

		var physics = Model.Physics;
		if ( physics is null || physics.Parts.Count == 0 )
			return;

		PhysicsRebuildCount++;

		var componentFlags = ComponentFlags.None;

		if ( Scene.IsEditor )
		{
			componentFlags |= ComponentFlags.NotEditable;
		}

		var world = WorldTransform;

		CreateParts( physics, world, componentFlags );
		CreateJoints( physics, componentFlags );

		foreach ( var body in Bodies )
		{
			body.Component.Enabled = true;
			Network?.Refresh( body.Component );
		}

		foreach ( var joint in Joints )
		{
			joint.Component.Enabled = true;
			Network?.Refresh( joint.Component );
		}

		PhysicsWereCreated = true;
	}

	private void CreateJoints( PhysicsGroupDescription physics, ComponentFlags componentFlags )
	{
		var jointFlags = componentFlags;

		foreach ( var jointDesc in physics.Joints )
		{
			var body1 = Bodies[jointDesc.Body1];
			var body2 = Bodies[jointDesc.Body2];

			var localFrame1 = jointDesc.Frame1;
			var localFrame2 = jointDesc.Frame2;

			Sandbox.Joint joint = null;

			if ( jointDesc.Type == PhysicsGroupDescription.JointType.Hinge )
			{
				var hingeJoint = body1.Component.AddComponent<HingeJoint>( false );

				if ( jointDesc.EnableTwistLimit )
				{
					hingeJoint.MinAngle = jointDesc.TwistMin;
					hingeJoint.MaxAngle = jointDesc.TwistMax;
				}

				if ( jointDesc.EnableAngularMotor )
				{
					var worldFrame1 = body1.Component.WorldTransform.ToWorld( localFrame1 );
					var hingeAxis = worldFrame1.Rotation.Up;
					var targetVelocity = hingeAxis.Dot( jointDesc.AngularTargetVelocity );

					hingeJoint.Motor = HingeJoint.MotorMode.TargetVelocity;
					hingeJoint.TargetVelocity = targetVelocity.RadianToDegree();
					hingeJoint.MaxTorque = jointDesc.MaxTorque;
				}

				joint = hingeJoint;
			}
			else if ( jointDesc.Type == PhysicsGroupDescription.JointType.Ball )
			{
				var ballJoint = body1.Component.AddComponent<BallJoint>( false );

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
				var fixedJoint = body1.Component.AddComponent<FixedJoint>( false );
				fixedJoint.LinearFrequency = jointDesc.LinearFrequency;
				fixedJoint.LinearDamping = jointDesc.LinearDampingRatio;
				fixedJoint.AngularFrequency = jointDesc.AngularFrequency;
				fixedJoint.AngularDamping = jointDesc.AngularDampingRatio;

				joint = fixedJoint;
			}
			else if ( jointDesc.Type == PhysicsGroupDescription.JointType.Slider )
			{
				var sliderJoint = body1.Component.AddComponent<SliderJoint>( false );

				if ( jointDesc.EnableLinearLimit )
				{
					sliderJoint.MinLength = jointDesc.LinearMin;
					sliderJoint.MaxLength = jointDesc.LinearMax;
				}

				var rot = Rotation.FromPitch( -90 );
				localFrame1 = localFrame1.WithRotation( rot * localFrame1.Rotation );
				localFrame2 = localFrame2.WithRotation( rot * localFrame2.Rotation );

				joint = sliderJoint;
			}

			if ( joint.IsValid() )
			{
				joint.Flags |= jointFlags;
				joint.Body = body2.Component.GameObject;
				joint.Attachment = Sandbox.Joint.AttachmentMode.LocalFrames;
				joint.LocalFrame1 = localFrame1.WithPosition( jointDesc.Frame1.Position * body1.Component.WorldScale );
				joint.LocalFrame2 = localFrame2.WithPosition( jointDesc.Frame2.Position * body2.Component.WorldScale );
				joint.EnableCollision = jointDesc.EnableCollision;
				joint.BreakForce = jointDesc.LinearStrength;
				joint.BreakTorque = jointDesc.AngularStrength;

				Joints.Add( new Joint( joint, body1, body2, localFrame1, localFrame2 ) );
			}
		}
	}

	private void CreateParts( PhysicsGroupDescription physics, Transform world, ComponentFlags componentFlags )
	{
		var boneObjects = Model.CreateBoneObjects( GameObject );
		var bones = Model.Bones;
		var bodyFlags = componentFlags;
		var colliderFlags = componentFlags;

		foreach ( var part in physics.Parts )
		{
			var bone = bones.GetBone( part.BoneName );

			if ( bone is null )
				continue;

			if ( !boneObjects.TryGetValue( bone, out var go ) )
				continue;

			if ( !go.Flags.Contains( GameObjectFlags.Absolute | GameObjectFlags.PhysicsBone ) )
			{
				go.Flags |= GameObjectFlags.Absolute | GameObjectFlags.PhysicsBone;

				if ( !Renderer.IsValid() || !Renderer.TryGetBoneTransform( bone, out var boneWorld ) )
				{
					// There's no renderer bones, use physics bind pose
					boneWorld = world.ToWorld( part.Transform );
				}

				go.WorldTransform = boneWorld;
			}

			var body = go.AddComponent<Rigidbody>( false );
			body.Flags |= bodyFlags;
			body.RigidbodyFlags = RigidbodyFlags;
			body.Locking = Locking;
			body.MotionEnabled = MotionEnabled;
			body.LinearDamping = part.LinearDamping;
			body.AngularDamping = part.AngularDamping;
			body.StartAsleep = StartAsleep;
			body.MassOverride = part.Mass;
			body.OverrideMassCenter = part.OverrideMassCenter;
			body.MassCenterOverride = part.MassCenterOverride;
			body.GravityScale = part.GravityScale;
			body.GameObjectSource = GameObject;

			var bodyTransform = body.WorldTransform;

			BodyTransforms.Set( Bodies.Count, bodyTransform );
			Bodies.Add( new Body( body, bone.Index, WorldTransform.ToLocal( bodyTransform ) ) );

			foreach ( var sphere in part.Spheres )
			{
				var collider = go.AddComponent<SphereCollider>();
				collider.Flags |= colliderFlags;
				collider.Center = sphere.Sphere.Center;
				collider.Radius = sphere.Sphere.Radius;
				collider.Surface = sphere.Surface;
			}

			foreach ( var capsule in part.Capsules )
			{
				var collider = go.AddComponent<CapsuleCollider>();
				collider.Flags |= colliderFlags;
				collider.Start = capsule.Capsule.CenterA;
				collider.End = capsule.Capsule.CenterB;
				collider.Radius = capsule.Capsule.Radius;
				collider.Surface = capsule.Surface;
			}

			foreach ( var hull in part.Hulls )
			{
				var collider = go.AddComponent<HullCollider>();
				collider.Flags |= colliderFlags;
				collider.Type = HullCollider.PrimitiveType.Points;
				collider.Points = hull.GetPoints().ToList();
				collider.Surface = hull.Surface;
			}
		}
	}

	/// <summary>
	/// Destroy all the bodies, colliders and joints.
	/// </summary>
	private void DestroyPhysics()
	{
		if ( Renderer.IsValid() )
		{
			Renderer.ClearPhysicsBones();
		}

		BodyTransforms.Clear();

		if ( !PhysicsWereCreated )
			return;

		PhysicsDestroyCount++;

		foreach ( var joint in Joints )
		{
			if ( !joint.Component.IsValid() )
				continue;

			joint.Component.Destroy();

			Network?.Refresh( joint.Component );
		}

		foreach ( var collider in GetComponentsInChildren<Collider>( true ) )
		{
			if ( !collider.IsValid() ) continue;
			if ( !collider.GameObject.Flags.Contains( GameObjectFlags.PhysicsBone ) ) continue;

			collider.Destroy();

			Network?.Refresh( collider );
		}

		foreach ( var body in Bodies )
		{
			if ( !body.Component.IsValid() )
				continue;

			body.Component.GameObject.Flags &= ~GameObjectFlags.Absolute;
			body.Component.GameObject.Flags &= ~GameObjectFlags.PhysicsBone;
			body.Component.Destroy();

			Network?.Refresh( body.Component );
		}

		Bodies.Clear();
		Joints.Clear();

		PhysicsWereCreated = false;
	}

	internal void OnModelReloaded()
	{
		DestroyPhysics();
		CreatePhysics();
	}
}
