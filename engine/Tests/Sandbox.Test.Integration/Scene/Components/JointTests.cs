using System;
using PhysicsSpring = Sandbox.Physics.PhysicsSpring;

namespace SceneTests.Components;

/// <summary>
/// Pins the contract of the physics joint components (Joint base plus FixedJoint, SpringJoint,
/// HingeJoint, BallJoint, SliderJoint, WheelJoint, UprightJoint, ControlJoint and PhysicsFilter):
/// how the base resolves physics bodies, the joint lifecycle through enable/disable/break,
/// the cheap property surface, the constraint behavior under simulation, and json round trips.
/// </summary>
[TestClass]
public class PhysicsJointTest
{
	/// <summary>
	/// Creates a GameObject with a gravity-less dynamic Rigidbody and a 10 unit box collider
	/// at the given world position - the standard dynamic test body.
	/// </summary>
	static (GameObject go, Rigidbody rb) CreateDynamicBox( Scene scene, Vector3 position, bool gravity = false )
	{
		var go = scene.CreateObject();
		go.WorldPosition = position;

		var rb = go.Components.Create<Rigidbody>();
		rb.Gravity = gravity;

		var box = go.Components.Create<BoxCollider>();
		box.Scale = new Vector3( 10 );

		return (go, rb);
	}

	/// <summary>
	/// Creates a GameObject with a static 10 unit box collider and no rigidbody - joints
	/// resolve its body through Collider.KeyBody.
	/// </summary>
	static GameObject CreateStaticAnchor( Scene scene, Vector3 position )
	{
		var go = scene.CreateObject();
		go.WorldPosition = position;

		var box = go.Components.Create<BoxCollider>();
		box.Scale = new Vector3( 10 );
		box.Static = true;

		return go;
	}

	/// <summary>
	/// Builds the standard serialization rig: a root object with an "anchor" child and a
	/// "target" child, each carrying a gravity-less Rigidbody and box collider. The joint
	/// under test goes on the anchor and connects to the target.
	/// </summary>
	static (GameObject root, GameObject anchor, GameObject target) CreateJointRig( Scene scene )
	{
		var root = scene.CreateObject();
		root.Name = "rig";

		var anchor = scene.CreateObject();
		anchor.Name = "anchor";
		anchor.Parent = root;
		anchor.Components.Create<Rigidbody>().Gravity = false;
		anchor.Components.Create<BoxCollider>().Scale = new Vector3( 10 );

		var target = scene.CreateObject();
		target.Name = "target";
		target.Parent = root;
		target.WorldPosition = new Vector3( 0, 0, 40 );
		target.Components.Create<Rigidbody>().Gravity = false;
		target.Components.Create<BoxCollider>().Scale = new Vector3( 10 );

		return (root, anchor, target);
	}

	/// <summary>
	/// Serializes the rig root to json, destroys the original, deserializes it back into the
	/// scene, enables it and returns the single component of the requested type found in the
	/// clone's hierarchy.
	/// </summary>
	static T SerializeRigRoundTrip<T>( Scene scene, GameObject root ) where T : Component
	{
		var json = root.Serialize().ToJsonString();

		root.Destroy();
		scene.ProcessDeletes();

		var jsonObject = Json.ParseToJsonObject( json );
		SceneUtility.MakeIdGuidsUnique( jsonObject );

		var clone = new GameObject( false );
		clone.Deserialize( jsonObject );
		clone.Enabled = true;

		return clone.GetComponentsInChildren<T>( true ).Single();
	}

	/// <summary>
	/// The native joint is created by OnStart, not on component creation: before the first
	/// game tick Body1/Body2 are null, afterwards they resolve to the two rigidbody physics
	/// bodies, both bodies list the joint, Object1/Object2 point back at the GameObjects,
	/// and the runtime converts Auto attachment into baked LocalFrames.
	/// </summary>
	[TestMethod]
	public void CreationResolvesBodiesOnStart()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var (go1, rb1) = CreateDynamicBox( scene, Vector3.Zero );
		var (go2, rb2) = CreateDynamicBox( scene, new Vector3( 0, 0, 50 ) );

		var joint = go1.Components.Create<FixedJoint>();
		joint.Body = go2;

		Assert.IsNull( joint.Body1, "No native joint should exist before OnStart" );
		Assert.IsNull( joint.Body2 );
		Assert.AreEqual( 0, rb1.Joints.Count );
		Assert.AreEqual( Sandbox.Joint.AttachmentMode.Auto, joint.Attachment );

		scene.GameTick();

		Assert.AreEqual( rb1.PhysicsBody, joint.Body1, "Body1 should resolve to the joint's own rigidbody" );
		Assert.AreEqual( rb2.PhysicsBody, joint.Body2, "Body2 should resolve to the connected rigidbody" );
		Assert.AreEqual( go1, joint.Object1 );
		Assert.AreEqual( go2, joint.Object2 );
		Assert.IsTrue( rb1.Joints.Contains( joint ), "Body1's rigidbody should list the joint" );
		Assert.IsTrue( rb2.Joints.Contains( joint ), "Body2's rigidbody should list the joint" );
		Assert.AreEqual( joint.Body1, joint.Point1.Body );
		Assert.AreEqual( joint.Body2, joint.Point2.Body );
		Assert.IsFalse( joint.IsBroken );
		Assert.AreEqual( Sandbox.Joint.AttachmentMode.LocalFrames, joint.Attachment, "Runtime creation should bake Auto into LocalFrames" );
	}

	/// <summary>
	/// Disabling the joint component destroys the native joint (Body1 goes null and both
	/// rigidbodies forget it), re-enabling recreates it synchronously, and destroying the
	/// component cleans it up for good.
	/// </summary>
	[TestMethod]
	public void EnableDisableRecreatesJoint()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var (go1, rb1) = CreateDynamicBox( scene, Vector3.Zero );
		var (go2, rb2) = CreateDynamicBox( scene, new Vector3( 0, 0, 50 ) );

		var joint = go1.Components.Create<FixedJoint>();
		joint.Body = go2;
		scene.GameTick();

		Assert.IsNotNull( joint.Body1 );

		joint.Enabled = false;

		Assert.IsNull( joint.Body1, "Disabling should destroy the native joint" );
		Assert.IsNull( joint.Body2 );
		Assert.IsFalse( rb1.Joints.Contains( joint ) );
		Assert.IsFalse( rb2.Joints.Contains( joint ) );

		joint.Enabled = true;

		Assert.IsNotNull( joint.Body1, "Re-enabling should recreate the joint without waiting for a tick" );
		Assert.IsTrue( rb1.Joints.Contains( joint ) );
		Assert.IsTrue( rb2.Joints.Contains( joint ) );

		joint.Destroy();
		scene.ProcessDeletes();

		Assert.AreEqual( 0, rb1.Joints.Count, "Destroying the component should remove the joint from the body" );
		Assert.AreEqual( 0, rb2.Joints.Count );
	}

	/// <summary>
	/// FindPhysicsBody walks up the parent chain: a joint on a body-less child resolves
	/// Body1 from the parent's rigidbody. Setting AnchorBody re-creates the joint anchored
	/// to that object's body instead of the joint's own GameObject.
	/// </summary>
	[TestMethod]
	public void BodyResolutionWalksParentsAndAnchorBodyOverrides()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var (parentGo, parentRb) = CreateDynamicBox( scene, Vector3.Zero );

		var child = scene.CreateObject();
		child.Parent = parentGo;
		child.WorldPosition = new Vector3( 0, 0, 30 );

		var (targetGo, targetRb) = CreateDynamicBox( scene, new Vector3( 0, 0, 30 ) );
		var (overrideGo, overrideRb) = CreateDynamicBox( scene, new Vector3( 0, 0, 30 ) );

		var joint = child.Components.Create<BallJoint>();
		joint.Body = targetGo;
		scene.GameTick();

		Assert.AreEqual( parentRb.PhysicsBody, joint.Body1, "Body1 should be found by walking up to the parent's rigidbody" );
		Assert.AreEqual( parentGo, joint.Object1 );
		Assert.AreEqual( targetRb.PhysicsBody, joint.Body2 );

		joint.AnchorBody = overrideGo;

		Assert.AreEqual( overrideRb.PhysicsBody, joint.Body1, "AnchorBody should replace the joint's own GameObject as the anchor" );
		Assert.IsFalse( parentRb.Joints.Contains( joint ), "The old anchor body should forget the joint" );
		Assert.IsTrue( overrideRb.Joints.Contains( joint ) );
	}

	/// <summary>
	/// With no Body set the joint anchors to the physics world's reference body, so a
	/// FixedJoint with no target welds the object to the world: it holds its position
	/// against gravity.
	/// </summary>
	[TestMethod]
	public void WorldBodyFallbackWhenNoBodySet()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var (go, rb) = CreateDynamicBox( scene, new Vector3( 0, 0, 100 ), gravity: true );

		var joint = go.Components.Create<FixedJoint>();
		scene.GameTick();

		Assert.AreEqual( rb.PhysicsBody, joint.Body1 );
		Assert.AreEqual( scene.PhysicsWorld.Body, joint.Body2, "A missing Body should fall back to the world reference body" );

		rb.Sleeping = false;

		for ( int i = 0; i < 10; i++ ) scene.GameTick();

		Assert.AreEqual( 100f, go.WorldPosition.z, 20f, $"welded to the world the body must not fall: {go.WorldPosition}" );
	}

	/// <summary>
	/// A Body target with only a collider (no rigidbody) resolves to the collider's
	/// keyframed body, and the collider lists the joint through its Joints set.
	/// </summary>
	[TestMethod]
	public void KeyframedColliderBodyResolution()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var keyframedGo = scene.CreateObject();
		var keyframedCollider = keyframedGo.Components.Create<BoxCollider>();
		keyframedCollider.Scale = new Vector3( 10 );

		var (dynamicGo, rb) = CreateDynamicBox( scene, Vector3.Zero );

		var joint = dynamicGo.Components.Create<HingeJoint>();
		joint.Body = keyframedGo;
		scene.GameTick();

		Assert.AreEqual( rb.PhysicsBody, joint.Body1 );
		Assert.AreEqual( keyframedCollider.KeyBody, joint.Body2, "A collider-only target should resolve to its keyframed body" );
		Assert.IsTrue( keyframedCollider.Joints.Contains( joint ), "The keyframe collider should list the joint" );
	}

	/// <summary>
	/// Changing the Body property after the joint has started destroys the old native joint
	/// and creates a new one against the new target without waiting for a tick.
	/// </summary>
	[TestMethod]
	public void ChangingBodyRecreatesJointImmediately()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var (go, _) = CreateDynamicBox( scene, Vector3.Zero );
		var (targetA, rbA) = CreateDynamicBox( scene, new Vector3( 0, 0, 50 ) );
		var (targetB, rbB) = CreateDynamicBox( scene, new Vector3( 0, 0, -50 ) );

		var joint = go.Components.Create<FixedJoint>();
		joint.Body = targetA;
		scene.GameTick();

		Assert.AreEqual( rbA.PhysicsBody, joint.Body2 );

		joint.Body = targetB;

		Assert.AreEqual( rbB.PhysicsBody, joint.Body2, "The joint should rebuild against the new target" );
		Assert.AreEqual( targetB, joint.Object2 );
		Assert.IsFalse( rbA.Joints.Contains( joint ), "The old target should forget the joint" );
		Assert.IsTrue( rbB.Joints.Contains( joint ) );
	}

	/// <summary>
	/// A joint with a tiny BreakForce breaks when the constraint takes more impulse than
	/// its strength: the OnBreak callback fires, IsBroken flips and the native joint is
	/// gone from both bodies. The breaking impulse is spent stopping the body, so the
	/// launch velocity does not survive the break - but afterwards nothing constrains
	/// the body and a fresh velocity moves it freely.
	/// </summary>
	[TestMethod]
	public void BreakForceBreaksUnderImpulse()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var anchorGo = CreateStaticAnchor( scene, Vector3.Zero );
		var (go, rb) = CreateDynamicBox( scene, Vector3.Zero );

		var joint = go.Components.Create<FixedJoint>();
		joint.Body = anchorGo;
		joint.BreakForce = 1f;

		bool broke = false;
		joint.OnBreak = () => broke = true;

		scene.GameTick();
		Assert.IsNotNull( joint.Body1 );
		Assert.IsFalse( joint.IsBroken );

		for ( int i = 0; i < 10 && !joint.IsBroken; i++ )
		{
			rb.Sleeping = false;
			rb.Velocity = new Vector3( 5000, 0, 0 );
			scene.GameTick();
		}

		Assert.IsTrue( joint.IsBroken, "The joint should have broken under the impulse" );
		Assert.IsTrue( broke, "The OnBreak callback should have fired" );
		Assert.IsNull( joint.Body1, "A broken joint has no native joint" );
		Assert.AreEqual( 0f, joint.LinearStress, "A broken joint reports no stress" );
		Assert.AreEqual( 0, rb.Joints.Count );

		// The constraint absorbed the launch velocity while breaking, so prove the body is
		// free by teleporting it clear of the anchor and giving it a fresh velocity.
		go.WorldPosition = new Vector3( 500, 0, 0 );
		rb.Sleeping = false;
		rb.Velocity = new Vector3( 1000, 0, 0 );

		for ( int i = 0; i < 3; i++ ) scene.GameTick();

		Assert.IsTrue( go.WorldPosition.x > 550f, $"the freed body should move unconstrained: {go.WorldPosition}" );
	}

	/// <summary>
	/// Break() can be called manually: it invokes the OnBreak action, destroys the native
	/// joint and sets IsBroken. Unbreak() resurrects the joint against the same bodies.
	/// </summary>
	[TestMethod]
	public void ManualBreakAndUnbreak()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var (go1, rb1) = CreateDynamicBox( scene, Vector3.Zero );
		var (go2, rb2) = CreateDynamicBox( scene, new Vector3( 0, 0, 50 ) );

		var joint = go1.Components.Create<BallJoint>();
		joint.Body = go2;

		bool broke = false;
		joint.OnBreak = () => broke = true;

		scene.GameTick();
		Assert.IsNotNull( joint.Body1 );

		joint.Break();

		Assert.IsTrue( joint.IsBroken );
		Assert.IsTrue( broke, "Manual Break should invoke the OnBreak action" );
		Assert.IsNull( joint.Body1 );
		Assert.AreEqual( 0, rb1.Joints.Count );
		Assert.AreEqual( 0, rb2.Joints.Count );

		joint.Unbreak();

		Assert.IsFalse( joint.IsBroken );
		Assert.AreEqual( rb1.PhysicsBody, joint.Body1, "Unbreak should recreate the joint" );
		Assert.IsTrue( rb1.Joints.Contains( joint ) );
	}

	/// <summary>
	/// StartBroken makes the joint begin life broken: OnStart sets IsBroken and skips
	/// creating the native joint until Unbreak is called.
	/// </summary>
	[TestMethod]
	public void StartBrokenSkipsJointCreation()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var (go1, _) = CreateDynamicBox( scene, Vector3.Zero );
		var (go2, rb2) = CreateDynamicBox( scene, new Vector3( 0, 0, 60 ) );

		var joint = go1.Components.Create<SpringJoint>();
		joint.Body = go2;
		joint.StartBroken = true;

		scene.GameTick();

		Assert.IsTrue( joint.IsBroken, "StartBroken should leave the joint broken on start" );
		Assert.IsNull( joint.Body1, "No native joint should be created while broken" );
		Assert.AreEqual( 0, rb2.Joints.Count );

		joint.Unbreak();

		Assert.IsFalse( joint.IsBroken );
		Assert.IsNotNull( joint.Body1 );
		Assert.IsTrue( rb2.Joints.Contains( joint ) );
	}

	/// <summary>
	/// A FixedJoint welds two dynamic bodies into one rigid unit: pushing the top body
	/// sideways is an off-center push on the welded pair, so the pair's center of mass
	/// moves sideways (momentum is shared between both bodies) while the unit tumbles
	/// about it, all while falling under gravity. The weld preserves the separation
	/// distance and the relative orientation of the two bodies throughout.
	/// </summary>
	[TestMethod]
	public void FixedJointKeepsRelativeTransformUnderGravity()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var (goA, rbA) = CreateDynamicBox( scene, new Vector3( 0, 0, 200 ), gravity: true );
		var (goB, rbB) = CreateDynamicBox( scene, new Vector3( 0, 0, 100 ), gravity: true );

		var joint = goA.Components.Create<FixedJoint>();
		joint.Body = goB;
		scene.GameTick();

		rbA.Sleeping = false;
		rbB.Sleeping = false;
		rbA.Velocity = new Vector3( 100, 0, 0 );

		for ( int i = 0; i < 15; i++ ) scene.GameTick();

		Assert.IsTrue( goA.WorldPosition.z < 150f, $"the pair should be falling: {goA.WorldPosition}" );

		var center = (goA.WorldPosition + goB.WorldPosition) * 0.5f;
		Assert.IsTrue( center.x > 15f, $"the push should move the welded pair's center of mass sideways: {center}" );
		Assert.AreEqual( 0f, center.y, 10f, $"no out-of-plane drift: {center}" );

		var separation = goA.WorldPosition - goB.WorldPosition;
		Assert.AreEqual( 100f, separation.Length, 15f, $"the weld should keep the bodies 100 apart: {separation}" );
		Assert.IsTrue( goA.WorldRotation.Distance( goB.WorldRotation ) < 15f, "the weld should keep the relative orientation" );
		Assert.IsTrue( goA.WorldRotation.Distance( Rotation.Identity ) > 10f, "the off-center push should tumble the welded pair as one unit" );
	}

	/// <summary>
	/// A body hanging off a static anchor through a FixedJoint loads the constraint:
	/// the weld holds it in place against gravity and LinearStress reports a non-zero
	/// impulse while the joint carries the weight.
	/// </summary>
	[TestMethod]
	public void FixedJointReportsStressUnderLoad()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var anchorGo = CreateStaticAnchor( scene, Vector3.Zero );
		var (go, rb) = CreateDynamicBox( scene, new Vector3( 0, 0, -50 ), gravity: true );

		var joint = go.Components.Create<FixedJoint>();
		joint.Body = anchorGo;
		scene.GameTick();

		for ( int i = 0; i < 10; i++ ) scene.GameTick();

		Assert.AreEqual( -50f, go.WorldPosition.z, 20f, $"the weld should hold the hanging body: {go.WorldPosition}" );

		rb.Sleeping = false;
		scene.GameTick();

		Assert.IsTrue( joint.LinearStress > 0f, $"holding the weight should produce linear stress: {joint.LinearStress}" );

		joint.Enabled = false;
		Assert.AreEqual( 0f, joint.LinearStress, "no joint, no stress" );
	}

	/// <summary>
	/// A SpringJoint pulls a distant hanging body back toward its rest length: a body
	/// starting 200 units below a static anchor with MaxLength 100 and RestLength 50 is
	/// dragged up close to the rest length without sideways drift.
	/// </summary>
	[TestMethod]
	public void SpringJointPullsHangingBodyTowardRestLength()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var anchorGo = CreateStaticAnchor( scene, Vector3.Zero );
		var (go, rb) = CreateDynamicBox( scene, new Vector3( 0, 0, -200 ) );

		var joint = anchorGo.Components.Create<SpringJoint>();
		joint.Body = go;
		joint.Frequency = 8f;
		joint.Damping = 1f;
		joint.RestLength = 50f;
		joint.MinLength = 0f;
		joint.MaxLength = 100f;

		scene.GameTick();
		Assert.IsNotNull( joint.Body1 );
		Assert.AreEqual( rb.PhysicsBody, joint.Body2 );

		rb.Sleeping = false;

		for ( int i = 0; i < 80; i++ ) scene.GameTick();

		Assert.IsTrue( go.WorldPosition.z > -130f, $"the spring should have pulled the body within its max length: {go.WorldPosition}" );
		Assert.IsTrue( go.WorldPosition.z < -10f, $"the spring should not have launched the body past the anchor: {go.WorldPosition}" );
		Assert.AreEqual( 0f, go.WorldPosition.x, 10f );
		Assert.AreEqual( 0f, go.WorldPosition.y, 10f );

		// The cheap property surface holds exactly what was set
		Assert.AreEqual( 8f, joint.Frequency );
		Assert.AreEqual( 1f, joint.Damping );
		Assert.AreEqual( 50f, joint.RestLength );
		Assert.AreEqual( 100f, joint.MaxLength );
	}

	/// <summary>
	/// A HingeJoint between a static anchor and a co-located dynamic body pins all
	/// translation while leaving rotation about the hinge axis free: pushed sideways the
	/// body stays put, but a spin about the axis is allowed and shows up in Angle.
	/// </summary>
	[TestMethod]
	public void HingeJointPinsTranslationAndAllowsAxisSpin()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var anchorGo = CreateStaticAnchor( scene, Vector3.Zero );
		var (go, rb) = CreateDynamicBox( scene, Vector3.Zero );

		var joint = go.Components.Create<HingeJoint>();
		joint.Body = anchorGo;
		scene.GameTick();

		Assert.IsNotNull( joint.Body1 );
		Assert.IsTrue( joint.Axis.Dot( Vector3.Up ) > 0.99f, $"an unrotated auto hinge should spin around up: {joint.Axis}" );
		Assert.AreEqual( 0f, joint.Angle, 1f );

		for ( int i = 0; i < 5; i++ )
		{
			rb.Sleeping = false;
			rb.Velocity = new Vector3( 100, 0, 0 );
			scene.GameTick();
		}

		Assert.IsTrue( go.WorldPosition.Length < 15f, $"the hinge should pin translation: {go.WorldPosition}" );

		rb.Sleeping = false;
		rb.Velocity = Vector3.Zero;
		rb.AngularVelocity = new Vector3( 0, 0, 2 );

		for ( int i = 0; i < 5; i++ ) scene.GameTick();

		Assert.IsTrue( MathF.Abs( joint.Angle ) > 20f, $"spinning about the axis should be free: {joint.Angle}" );
		Assert.IsTrue( go.WorldPosition.Length < 15f, $"spinning must not translate the body: {go.WorldPosition}" );
	}

	/// <summary>
	/// MinAngle/MaxAngle clamp the hinge: a body spun hard about the hinge axis cannot
	/// swing past the configured limits.
	/// </summary>
	[TestMethod]
	public void HingeJointAngleLimitsClampSwing()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var anchorGo = CreateStaticAnchor( scene, Vector3.Zero );
		var (go, rb) = CreateDynamicBox( scene, Vector3.Zero );

		var joint = go.Components.Create<HingeJoint>();
		joint.Body = anchorGo;
		joint.MinAngle = -15f;
		joint.MaxAngle = 15f;
		scene.GameTick();

		for ( int i = 0; i < 10; i++ )
		{
			rb.Sleeping = false;
			rb.AngularVelocity = new Vector3( 0, 0, 2 );
			scene.GameTick();
		}

		Assert.IsTrue( MathF.Abs( joint.Angle ) < 25f, $"the limit should clamp the swing: {joint.Angle}" );
	}

	/// <summary>
	/// The hinge motor in TargetVelocity mode drives the joint: with a generous MaxTorque
	/// the body is spun about the hinge axis and the joint angle keeps growing.
	/// </summary>
	[TestMethod]
	public void HingeJointMotorDrivesTargetVelocity()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var anchorGo = CreateStaticAnchor( scene, Vector3.Zero );
		var (go, _) = CreateDynamicBox( scene, Vector3.Zero );

		var joint = go.Components.Create<HingeJoint>();
		joint.Body = anchorGo;
		joint.Motor = HingeJoint.MotorMode.TargetVelocity;
		joint.TargetVelocity = 90f;
		joint.MaxTorque = 100000f;
		scene.GameTick();

		Assert.IsNotNull( joint.Body1 );

		for ( int i = 0; i < 10; i++ ) scene.GameTick();

		Assert.IsTrue( MathF.Abs( joint.Angle ) > 30f, $"the motor should have swung the hinge: {joint.Angle}" );
		Assert.IsTrue( go.WorldPosition.Length < 15f, $"the motor must not translate the body: {go.WorldPosition}" );
	}

	/// <summary>
	/// A BallJoint pins relative position like a shoulder but leaves all rotation free:
	/// pushed sideways the co-located body stays at the anchor, while an arbitrary spin
	/// rotates it away from identity.
	/// </summary>
	[TestMethod]
	public void BallJointPinsPositionAllowsRotation()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var anchorGo = CreateStaticAnchor( scene, Vector3.Zero );
		var (go, rb) = CreateDynamicBox( scene, Vector3.Zero );

		var joint = go.Components.Create<BallJoint>();
		joint.Body = anchorGo;
		scene.GameTick();

		Assert.IsNotNull( joint.Body1 );

		for ( int i = 0; i < 5; i++ )
		{
			rb.Sleeping = false;
			rb.Velocity = new Vector3( 100, 0, 0 );
			scene.GameTick();
		}

		Assert.IsTrue( go.WorldPosition.Length < 15f, $"the ball joint should pin translation: {go.WorldPosition}" );

		rb.Sleeping = false;
		rb.Velocity = Vector3.Zero;
		rb.AngularVelocity = new Vector3( 1, 1, 1 );

		for ( int i = 0; i < 5; i++ ) scene.GameTick();

		Assert.IsTrue( go.WorldRotation.Distance( Rotation.Identity ) > 10f, "rotation should be free on an unlimited ball joint" );
		Assert.IsTrue( go.WorldPosition.Length < 15f, $"rotating must not translate the body: {go.WorldPosition}" );
	}

	/// <summary>
	/// A SliderJoint restricts the connected body to the axis between the two objects at
	/// creation: velocity perpendicular to the axis is cancelled while velocity along the
	/// axis slides the body.
	/// </summary>
	[TestMethod]
	public void SliderJointRestrictsMotionToAxis()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var anchorGo = CreateStaticAnchor( scene, Vector3.Zero );
		var (go, rb) = CreateDynamicBox( scene, new Vector3( 100, 0, 0 ) );

		var joint = go.Components.Create<SliderJoint>();
		joint.Body = anchorGo;
		joint.MinLength = -500f;
		joint.MaxLength = 500f;
		scene.GameTick();

		Assert.IsNotNull( joint.Body1 );

		for ( int i = 0; i < 5; i++ )
		{
			rb.Sleeping = false;
			rb.Velocity = new Vector3( 0, 80, 80 );
			scene.GameTick();
		}

		Assert.AreEqual( 0f, go.WorldPosition.y, 15f, $"off-axis motion should be blocked: {go.WorldPosition}" );
		Assert.AreEqual( 0f, go.WorldPosition.z, 15f, $"off-axis motion should be blocked: {go.WorldPosition}" );

		for ( int i = 0; i < 10; i++ )
		{
			rb.Sleeping = false;
			rb.Velocity = new Vector3( 200, 0, 0 );
			scene.GameTick();
		}

		Assert.IsTrue( go.WorldPosition.x > 150f, $"sliding along the axis should be free: {go.WorldPosition}" );
		Assert.AreEqual( 0f, go.WorldPosition.y, 15f, $"still no off-axis drift: {go.WorldPosition}" );
		Assert.AreEqual( 0f, go.WorldPosition.z, 15f, $"still no off-axis drift: {go.WorldPosition}" );

		// Cheap property surface
		Assert.AreEqual( -500f, joint.MinLength );
		Assert.AreEqual( 500f, joint.MaxLength );
	}

	/// <summary>
	/// WheelJoint creation pins the body order quirk - the connected Body becomes the
	/// native Body1 (the chassis) and the wheel's own body becomes Body2 - and the whole
	/// suspension/steering/motor property surface round-trips while the joint is live.
	/// </summary>
	[TestMethod]
	public void WheelJointCreationAndPropertyState()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var (wheelGo, wheelRb) = CreateDynamicBox( scene, Vector3.Zero );
		var (chassisGo, chassisRb) = CreateDynamicBox( scene, new Vector3( 0, 0, 50 ) );

		var joint = wheelGo.Components.Create<WheelJoint>();
		joint.Body = chassisGo;
		scene.GameTick();

		Assert.AreEqual( chassisRb.PhysicsBody, joint.Body1, "CreateWheel swaps the points - the chassis is Body1" );
		Assert.AreEqual( wheelRb.PhysicsBody, joint.Body2, "the wheel's own body is Body2" );

		joint.EnableSuspension = true;
		joint.EnableSuspensionLimit = true;
		joint.SuspensionLimits = new Vector2( -5, 5 );
		joint.SuspensionHertz = 15f;
		joint.SuspensionDampingRatio = 0.5f;
		joint.EnableSpinMotor = true;
		joint.MaxSpinTorque = 1000f;
		joint.SpinMotorSpeed = 10f;
		joint.EnableSteering = true;
		joint.SteeringHertz = 20f;
		joint.SteeringDampingRatio = 1.5f;
		joint.TargetSteeringAngle = 30f;
		joint.MaxSteeringTorque = 500f;
		joint.EnableSteeringLimit = true;
		joint.SteeringLimits = new Vector2( -45, 45 );

		Assert.IsTrue( joint.EnableSuspension );
		Assert.IsTrue( joint.EnableSuspensionLimit );
		Assert.AreEqual( new Vector2( -5, 5 ), joint.SuspensionLimits );
		Assert.AreEqual( 15f, joint.SuspensionHertz );
		Assert.AreEqual( 0.5f, joint.SuspensionDampingRatio );
		Assert.IsTrue( joint.EnableSpinMotor );
		Assert.AreEqual( 1000f, joint.MaxSpinTorque );
		Assert.AreEqual( 10f, joint.SpinMotorSpeed );
		Assert.IsTrue( joint.EnableSteering );
		Assert.AreEqual( 20f, joint.SteeringHertz );
		Assert.AreEqual( 1.5f, joint.SteeringDampingRatio );
		Assert.AreEqual( 30f, joint.TargetSteeringAngle );
		Assert.AreEqual( 500f, joint.MaxSteeringTorque );
		Assert.IsTrue( joint.EnableSteeringLimit );
		Assert.AreEqual( new Vector2( -45, 45 ), joint.SteeringLimits );

		scene.GameTick();

		Assert.IsNotNull( joint.Body1, "the joint should survive live property changes" );
		Assert.IsFalse( float.IsNaN( joint.SpinSpeed ) );
		Assert.IsFalse( float.IsNaN( joint.SpinTorque ) );
		Assert.IsFalse( float.IsNaN( joint.SteeringAngle ) );
		Assert.IsFalse( float.IsNaN( joint.SteeringTorque ) );
	}

	/// <summary>
	/// An UprightJoint with no Body constrains the object against the world. With the
	/// default MaxTorque of 0 the joint is completely inert - the native parallel joint
	/// solver only runs when maxTorque is greater than zero (b3SolveParallelJoint in
	/// parallel_joint.cpp), even though the property doc claims 0 means unlimited torque,
	/// so a tilted body stays tilted. Suspected engine bug: default-configured UprightJoint
	/// does nothing. Once MaxTorque is raised the spring restores the body upright, and
	/// the spring properties round-trip while live.
	/// </summary>
	[TestMethod]
	public void UprightJointRestoresUprightOrientation()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var (go, rb) = CreateDynamicBox( scene, Vector3.Zero );

		var joint = go.Components.Create<UprightJoint>();
		scene.GameTick();

		Assert.AreEqual( rb.PhysicsBody, joint.Body1 );
		Assert.AreEqual( scene.PhysicsWorld.Body, joint.Body2, "no Body means the joint anchors to the world" );

		go.WorldRotation = Rotation.FromRoll( 40 );
		rb.Sleeping = false;

		Assert.IsTrue( go.WorldRotation.Up.z < 0.85f, "the body should start tilted" );

		for ( int i = 0; i < 10; i++ ) scene.GameTick();

		Assert.IsTrue( go.WorldRotation.Up.z < 0.85f, $"with the default MaxTorque of 0 the joint applies no torque at all: {go.WorldRotation.Angles()}" );

		joint.MaxTorque = 1000000f;
		rb.Sleeping = false;

		for ( int i = 0; i < 60; i++ ) scene.GameTick();

		Assert.IsTrue( go.WorldRotation.Up.z > 0.9f, $"with torque available the joint should restore the body upright: {go.WorldRotation.Angles()}" );

		joint.Hertz = 4f;
		joint.DampingRatio = 1.2f;
		joint.MaxTorque = 500f;

		Assert.AreEqual( 4f, joint.Hertz );
		Assert.AreEqual( 1.2f, joint.DampingRatio );
		Assert.AreEqual( 500f, joint.MaxTorque );
		Assert.IsNotNull( joint.Body1, "live property changes must not kill the joint" );
	}

	/// <summary>
	/// A ControlJoint's velocity motor moves the connected body relative to the anchor:
	/// with a linear velocity target and a generous force limit the dynamic body is driven
	/// away from its co-located keyframed anchor, and the spring/motor property surface
	/// round-trips while live.
	/// </summary>
	[TestMethod]
	public void ControlJointVelocityMotorMovesBody()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var anchorGo = scene.CreateObject();
		var anchorCollider = anchorGo.Components.Create<BoxCollider>();
		anchorCollider.Scale = new Vector3( 10 );

		var (go, rb) = CreateDynamicBox( scene, Vector3.Zero );

		var joint = anchorGo.Components.Create<ControlJoint>();
		joint.Body = go;
		joint.LinearVelocity = new Vector3( 50, 0, 0 );
		joint.MaxVelocityForce = 1000000f;

		scene.GameTick();

		Assert.AreEqual( anchorCollider.KeyBody, joint.Body1 );
		Assert.AreEqual( rb.PhysicsBody, joint.Body2 );

		rb.Sleeping = false;

		for ( int i = 0; i < 20; i++ ) scene.GameTick();

		Assert.IsTrue( go.WorldPosition.Length > 10f, $"the velocity motor should have moved the body: {go.WorldPosition}" );

		joint.AngularVelocity = new Vector3( 0, 0, 1 );
		joint.MaxVelocityTorque = 250f;
		joint.LinearSpring = new PhysicsSpring( 5f, 0.5f, 100f );
		joint.AngularSpring = new PhysicsSpring( 6f, 0.6f, 200f );

		Assert.AreEqual( new Vector3( 0, 0, 1 ), joint.AngularVelocity );
		Assert.AreEqual( 250f, joint.MaxVelocityTorque );
		Assert.AreEqual( new PhysicsSpring( 5f, 0.5f, 100f ), joint.LinearSpring );
		Assert.AreEqual( new PhysicsSpring( 6f, 0.6f, 200f ), joint.AngularSpring );
		Assert.IsNotNull( joint.Body1, "live property changes must not kill the joint" );
	}

	/// <summary>
	/// PhysicsFilter disables collision between its body and the target: a falling box
	/// filtered against the floor drops straight through it while an unfiltered control
	/// box comes to rest on top.
	/// </summary>
	[TestMethod]
	public void PhysicsFilterDisablesCollisionBetweenBodies()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var floor = scene.CreateObject();
		floor.WorldPosition = new Vector3( 0, 0, -10 );
		var floorBox = floor.Components.Create<BoxCollider>();
		floorBox.Scale = new Vector3( 1000, 1000, 20 );
		floorBox.Static = true;

		var (filtered, _) = CreateDynamicBox( scene, new Vector3( 0, 0, 30 ), gravity: true );
		var (control, _) = CreateDynamicBox( scene, new Vector3( 200, 0, 30 ), gravity: true );

		var filter = filtered.Components.Create<PhysicsFilter>();
		filter.Body = floor;

		for ( int i = 0; i < 30; i++ ) scene.GameTick();

		Assert.IsTrue( filtered.WorldPosition.z < -100f, $"the filtered box should fall through the floor: {filtered.WorldPosition}" );
		Assert.IsTrue( control.WorldPosition.z > 0f, $"the control box should rest on the floor: {control.WorldPosition}" );
		Assert.IsTrue( control.WorldPosition.z < 20f, $"the control box should rest on the floor: {control.WorldPosition}" );
	}

	/// <summary>
	/// Adding a PhysicsFilter at runtime breaks an existing resting contact: a box already
	/// settled on the floor falls through once the filter starts and its proxy is reset.
	/// </summary>
	[TestMethod]
	public void PhysicsFilterCreatedLiveBreaksRestingContact()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var floor = scene.CreateObject();
		floor.WorldPosition = new Vector3( 0, 0, -10 );
		var floorBox = floor.Components.Create<BoxCollider>();
		floorBox.Scale = new Vector3( 1000, 1000, 20 );
		floorBox.Static = true;

		var (go, rb) = CreateDynamicBox( scene, new Vector3( 0, 0, 30 ), gravity: true );

		for ( int i = 0; i < 30; i++ ) scene.GameTick();

		Assert.IsTrue( go.WorldPosition.z > 0f, $"the box should have settled on the floor: {go.WorldPosition}" );

		var filter = go.Components.Create<PhysicsFilter>();
		filter.Body = floor;
		rb.Sleeping = false;

		for ( int i = 0; i < 30; i++ ) scene.GameTick();

		Assert.IsTrue( go.WorldPosition.z < -50f, $"the live filter should let the box fall through: {go.WorldPosition}" );
	}

	/// <summary>
	/// A FixedJoint with non-default spring, breaking and attachment configuration survives
	/// a json round trip, including manually set LocalFrames and the resolved Body reference.
	/// </summary>
	[TestMethod]
	public void FixedJointSerializationRoundTrip()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var (root, anchor, target) = CreateJointRig( scene );

		var joint = anchor.Components.Create<FixedJoint>();
		joint.Body = target;
		joint.LinearFrequency = 20f;
		joint.LinearDamping = 2f;
		joint.AngularFrequency = 30f;
		joint.AngularDamping = 3f;
		joint.EnableCollision = true;
		joint.BreakForce = 500f;
		joint.BreakTorque = 600f;
		joint.StartBroken = true;
		joint.Attachment = Sandbox.Joint.AttachmentMode.LocalFrames;
		joint.LocalFrame1 = new Transform( new Vector3( 1, 2, 3 ), Rotation.FromYaw( 90 ) );
		joint.LocalFrame2 = new Transform( new Vector3( 4, 5, 6 ) );

		var loaded = SerializeRigRoundTrip<FixedJoint>( scene, root );

		Assert.AreEqual( 20f, loaded.LinearFrequency );
		Assert.AreEqual( 2f, loaded.LinearDamping );
		Assert.AreEqual( 30f, loaded.AngularFrequency );
		Assert.AreEqual( 3f, loaded.AngularDamping );
		Assert.IsTrue( loaded.EnableCollision );
		Assert.AreEqual( 500f, loaded.BreakForce );
		Assert.AreEqual( 600f, loaded.BreakTorque );
		Assert.IsTrue( loaded.StartBroken );
		Assert.AreEqual( Sandbox.Joint.AttachmentMode.LocalFrames, loaded.Attachment );
		Assert.IsTrue( loaded.LocalFrame1.Position.AlmostEqual( new Vector3( 1, 2, 3 ) ) );
		Assert.IsTrue( loaded.LocalFrame1.Rotation.Distance( Rotation.FromYaw( 90 ) ) < 0.01f );
		Assert.IsTrue( loaded.LocalFrame2.Position.AlmostEqual( new Vector3( 4, 5, 6 ) ) );
		Assert.IsNotNull( loaded.Body, "the Body reference should resolve inside the clone" );
		Assert.AreEqual( "target", loaded.Body.Name );
	}

	/// <summary>
	/// A SpringJoint's spring, length and force mode configuration survives a json round
	/// trip and the deserialized joint goes live against the cloned target on the next tick.
	/// </summary>
	[TestMethod]
	public void SpringJointSerializationRoundTrip()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var (root, anchor, target) = CreateJointRig( scene );

		var joint = anchor.Components.Create<SpringJoint>();
		joint.Body = target;
		joint.Frequency = 9f;
		joint.Damping = 0.5f;
		joint.MinLength = 10f;
		joint.MaxLength = 200f;
		joint.RestLength = 75f;
		joint.ForceMode = SpringJoint.SpringForceMode.Pull;

		var loaded = SerializeRigRoundTrip<SpringJoint>( scene, root );

		Assert.AreEqual( 9f, loaded.Frequency );
		Assert.AreEqual( 0.5f, loaded.Damping );
		Assert.AreEqual( 10f, loaded.MinLength );
		Assert.AreEqual( 200f, loaded.MaxLength );
		Assert.AreEqual( 75f, loaded.RestLength );
		Assert.AreEqual( SpringJoint.SpringForceMode.Pull, loaded.ForceMode );
		Assert.AreEqual( "target", loaded.Body.Name );

		scene.GameTick();

		Assert.IsNotNull( loaded.Body1, "the deserialized joint should go live on the next tick" );
		Assert.AreEqual( "target", loaded.Object2.Name );
	}

	/// <summary>
	/// A HingeJoint's limit and motor configuration survives a json round trip and the
	/// deserialized joint goes live against the cloned target on the next tick.
	/// </summary>
	[TestMethod]
	public void HingeJointSerializationRoundTrip()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var (root, anchor, target) = CreateJointRig( scene );

		var joint = anchor.Components.Create<HingeJoint>();
		joint.Body = target;
		joint.MinAngle = -30f;
		joint.MaxAngle = 60f;
		joint.Motor = HingeJoint.MotorMode.TargetVelocity;
		joint.TargetVelocity = 90f;
		joint.MaxTorque = 1000f;
		joint.Friction = 0.25f;
		joint.Frequency = 2f;
		joint.DampingRatio = 0.5f;

		var loaded = SerializeRigRoundTrip<HingeJoint>( scene, root );

		Assert.AreEqual( -30f, loaded.MinAngle );
		Assert.AreEqual( 60f, loaded.MaxAngle );
		Assert.AreEqual( HingeJoint.MotorMode.TargetVelocity, loaded.Motor );
		Assert.AreEqual( 90f, loaded.TargetVelocity );
		Assert.AreEqual( 1000f, loaded.MaxTorque );
		Assert.AreEqual( 0.25f, loaded.Friction );
		Assert.AreEqual( 2f, loaded.Frequency );
		Assert.AreEqual( 0.5f, loaded.DampingRatio );
		Assert.AreEqual( "target", loaded.Body.Name );

		scene.GameTick();

		Assert.IsNotNull( loaded.Body1, "the deserialized joint should go live on the next tick" );
	}

	/// <summary>
	/// A BallJoint's swing/twist limits and motor configuration survive a json round trip.
	/// </summary>
	[TestMethod]
	public void BallJointSerializationRoundTrip()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var (root, anchor, target) = CreateJointRig( scene );

		var joint = anchor.Components.Create<BallJoint>();
		joint.Body = target;
		joint.SwingLimitEnabled = true;
		joint.SwingLimit = new Vector2( 10, 80 );
		joint.TwistLimitEnabled = true;
		joint.TwistLimit = new Vector2( -20, 20 );
		joint.Motor = BallJoint.MotorMode.TargetRotation;
		joint.TargetRotation = Rotation.FromYaw( 45 );
		joint.Frequency = 3f;
		joint.DampingRatio = 0.8f;
		joint.TargetVelocity = new Vector3( 1, 2, 3 );
		joint.MaxTorque = 100f;
		joint.Friction = 0.25f;

		var loaded = SerializeRigRoundTrip<BallJoint>( scene, root );

		Assert.IsTrue( loaded.SwingLimitEnabled );
		Assert.AreEqual( new Vector2( 10, 80 ), loaded.SwingLimit );
		Assert.IsTrue( loaded.TwistLimitEnabled );
		Assert.AreEqual( new Vector2( -20, 20 ), loaded.TwistLimit );
		Assert.AreEqual( BallJoint.MotorMode.TargetRotation, loaded.Motor );
		Assert.IsTrue( loaded.TargetRotation.Distance( Rotation.FromYaw( 45 ) ) < 0.01f );
		Assert.AreEqual( 3f, loaded.Frequency );
		Assert.AreEqual( 0.8f, loaded.DampingRatio );
		Assert.AreEqual( new Vector3( 1, 2, 3 ), loaded.TargetVelocity );
		Assert.AreEqual( 100f, loaded.MaxTorque );
		Assert.AreEqual( 0.25f, loaded.Friction );
		Assert.AreEqual( "target", loaded.Body.Name );
	}

	/// <summary>
	/// A SliderJoint's length limits and friction survive a json round trip.
	/// </summary>
	[TestMethod]
	public void SliderJointSerializationRoundTrip()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var (root, anchor, target) = CreateJointRig( scene );

		var joint = anchor.Components.Create<SliderJoint>();
		joint.Body = target;
		joint.MinLength = 5f;
		joint.MaxLength = 80f;
		joint.Friction = 0.5f;

		var loaded = SerializeRigRoundTrip<SliderJoint>( scene, root );

		Assert.AreEqual( 5f, loaded.MinLength );
		Assert.AreEqual( 80f, loaded.MaxLength );
		Assert.AreEqual( 0.5f, loaded.Friction );
		Assert.AreEqual( "target", loaded.Body.Name );
	}

	/// <summary>
	/// A WheelJoint's suspension, motor and steering configuration survives a json round trip.
	/// </summary>
	[TestMethod]
	public void WheelJointSerializationRoundTrip()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var (root, anchor, target) = CreateJointRig( scene );

		var joint = anchor.Components.Create<WheelJoint>();
		joint.Body = target;
		joint.EnableSuspension = true;
		joint.EnableSuspensionLimit = true;
		joint.SuspensionLimits = new Vector2( -8, 8 );
		joint.SuspensionHertz = 12f;
		joint.SuspensionDampingRatio = 0.4f;
		joint.EnableSpinMotor = true;
		joint.MaxSpinTorque = 2000f;
		joint.SpinMotorSpeed = 25f;
		joint.EnableSteering = true;
		joint.SteeringHertz = 18f;
		joint.SteeringDampingRatio = 1.1f;
		joint.TargetSteeringAngle = 15f;
		joint.MaxSteeringTorque = 750f;
		joint.EnableSteeringLimit = true;
		joint.SteeringLimits = new Vector2( -30, 30 );

		var loaded = SerializeRigRoundTrip<WheelJoint>( scene, root );

		Assert.IsTrue( loaded.EnableSuspension );
		Assert.IsTrue( loaded.EnableSuspensionLimit );
		Assert.AreEqual( new Vector2( -8, 8 ), loaded.SuspensionLimits );
		Assert.AreEqual( 12f, loaded.SuspensionHertz );
		Assert.AreEqual( 0.4f, loaded.SuspensionDampingRatio );
		Assert.IsTrue( loaded.EnableSpinMotor );
		Assert.AreEqual( 2000f, loaded.MaxSpinTorque );
		Assert.AreEqual( 25f, loaded.SpinMotorSpeed );
		Assert.IsTrue( loaded.EnableSteering );
		Assert.AreEqual( 18f, loaded.SteeringHertz );
		Assert.AreEqual( 1.1f, loaded.SteeringDampingRatio );
		Assert.AreEqual( 15f, loaded.TargetSteeringAngle );
		Assert.AreEqual( 750f, loaded.MaxSteeringTorque );
		Assert.IsTrue( loaded.EnableSteeringLimit );
		Assert.AreEqual( new Vector2( -30, 30 ), loaded.SteeringLimits );
		Assert.AreEqual( "target", loaded.Body.Name );
	}

	/// <summary>
	/// An UprightJoint's spring configuration survives a json round trip.
	/// </summary>
	[TestMethod]
	public void UprightJointSerializationRoundTrip()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var (root, anchor, target) = CreateJointRig( scene );

		var joint = anchor.Components.Create<UprightJoint>();
		joint.Body = target;
		joint.Hertz = 8f;
		joint.DampingRatio = 1.2f;
		joint.MaxTorque = 333f;

		var loaded = SerializeRigRoundTrip<UprightJoint>( scene, root );

		Assert.AreEqual( 8f, loaded.Hertz );
		Assert.AreEqual( 1.2f, loaded.DampingRatio );
		Assert.AreEqual( 333f, loaded.MaxTorque );
		Assert.AreEqual( "target", loaded.Body.Name );
	}

	/// <summary>
	/// A ControlJoint's motor velocities, force limits and spring settings survive a json
	/// round trip.
	/// </summary>
	[TestMethod]
	public void ControlJointSerializationRoundTrip()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var (root, anchor, target) = CreateJointRig( scene );

		var joint = anchor.Components.Create<ControlJoint>();
		joint.Body = target;
		joint.LinearVelocity = new Vector3( 1, 2, 3 );
		joint.AngularVelocity = new Vector3( 4, 5, 6 );
		joint.MaxVelocityForce = 100f;
		joint.MaxVelocityTorque = 200f;
		joint.LinearSpring = new PhysicsSpring( 5f, 0.5f, 100f );
		joint.AngularSpring = new PhysicsSpring( 6f, 0.6f, 200f );

		var loaded = SerializeRigRoundTrip<ControlJoint>( scene, root );

		Assert.AreEqual( new Vector3( 1, 2, 3 ), loaded.LinearVelocity );
		Assert.AreEqual( new Vector3( 4, 5, 6 ), loaded.AngularVelocity );
		Assert.AreEqual( 100f, loaded.MaxVelocityForce );
		Assert.AreEqual( 200f, loaded.MaxVelocityTorque );
		Assert.AreEqual( new PhysicsSpring( 5f, 0.5f, 100f ), loaded.LinearSpring );
		Assert.AreEqual( new PhysicsSpring( 6f, 0.6f, 200f ), loaded.AngularSpring );
		Assert.AreEqual( "target", loaded.Body.Name );
	}

	/// <summary>
	/// A PhysicsFilter's Body reference survives a json round trip and resolves to the
	/// cloned target object.
	/// </summary>
	[TestMethod]
	public void PhysicsFilterSerializationRoundTrip()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var (root, anchor, target) = CreateJointRig( scene );

		var filter = anchor.Components.Create<PhysicsFilter>();
		filter.Body = target;

		var loaded = SerializeRigRoundTrip<PhysicsFilter>( scene, root );

		Assert.IsNotNull( loaded.Body, "the Body reference should resolve inside the clone" );
		Assert.AreEqual( "target", loaded.Body.Name );
	}
}
