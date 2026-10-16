using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text.Json;
using Sandbox.ModelEditor.Nodes;

namespace ResourceTests;

[TestClass]
public class ModelBuilderTests
{
	[DataTestMethod]
	[DataRow( 0 )]
	[DataRow( 1 )]
	[DataRow( 2 )]
	[DataRow( 3 )]
	[DataRow( 4 )]
	[DataRow( 5 )]
	[DataRow( 6 )]
	[DataRow( 7 )]
	[DataRow( 8 )]
	[DataRow( 9 )]
	[DataRow( 10 )]
	[DataRow( 11 )]
	[DataRow( 12 )]
	[DataRow( 13 )]
	[DataRow( 14 )]
	[DataRow( 15 )]
	[DataRow( 16 )]
	[DataRow( 17 )]
	[DataRow( 18 )]
	[DataRow( 19 )]
	public void JointBuildersRejectInvalidSettings( int invalid )
	{
		var builder = Model.Builder;
		var hinge = builder.AddHingeJoint( 0, 1 );
		var ball = builder.AddBallJoint( 0, 1 );
		var slider = builder.AddSliderJoint( 0, 1 );
		var weld = builder.AddFixedJoint( 0, 1 );
		PhysicsJointBuilder target = hinge;
		switch ( invalid )
		{
			case 0: hinge.Body1 = -1; break;
			case 1: hinge.Body2 = 2; break;
			case 2: hinge.Body1 = 65536; break;
			case 3: hinge.Frame1 = new Transform( new Vector3( float.NaN, 0, 0 ) ); break;
			case 4: hinge.Frame2 = new Transform( Vector3.Zero, default( Rotation ) ); break;
			case 5: hinge.LinearStrength = -1; break;
			case 6: hinge.AngularStrength = float.PositiveInfinity; break;
			case 7: hinge.WithTwistLimit( 20, -20 ); break;
			case 8: hinge.WithTargetAngle( float.NaN ); break;
			case 9: hinge.WithTargetVelocity( new Vector3( float.PositiveInfinity, 0, 0 ) ); break;
			case 10: hinge.WithTargetVelocity( Vector3.Up ).WithMaxTorque( -1 ); break;
			case 11: hinge.WithFrequency( float.NaN ); break;
			case 12: target = ball.WithTargetRotation( new Rotation( 0, 0, 0, 2 ) ); break;
			case 13: target = ball.WithFriction( -1 ); break;
			case 14: target = ball.WithSwingLimit( -1 ); break;
			case 15: target = slider.WithLimit( float.NaN, 20 ); break;
			case 16: target = slider.WithTargetPosition( float.NegativeInfinity ); break;
			case 17: target = slider.WithTargetVelocity( Vector3.Forward ).WithMaxForce( float.NaN ); break;
			case 18: target = slider.WithDampingRatio( -1 ); break;
			case 19: target = weld.WithLinearFrequency( -1 ); break;
		}
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => target.Validate( 2 ) );
	}

	[TestMethod]
	public void JointBuildersRejectSelfConnectionsAndPreserveSignedTargets()
	{
		var builder = Model.Builder;
		var hinge = builder.AddHingeJoint( 0, 1 ).WithTargetVelocity( Vector3.Up * -2 ).WithMaxTorque( 100 );
		var ball = builder.AddBallJoint( 0, 1 ).WithTargetRotation( Rotation.FromYaw( -30 ) );
		var slider = builder.AddSliderJoint( 0, 1 ).WithTargetPosition( -12 );
		hinge.Validate( 2 );
		ball.Validate( 2 );
		slider.Validate( 2 );
		slider.WithTargetVelocity( Vector3.Up * -100 ).WithMaxForce( 100 ).Validate( 2 );
		hinge.Body2 = 0;
		Assert.ThrowsException<ArgumentException>( () => hinge.Validate( 2 ) );
	}

	[DataTestMethod]
	[DataRow( 0 )]
	[DataRow( 1 )]
	[DataRow( 2 )]
	[DataRow( 3 )]
	[DataRow( 4 )]
	[DataRow( 5 )]
	[DataRow( 6 )]
	[DataRow( 7 )]
	public void NativeAggregatesConsumeJointBuilderSettings( int kind )
	{
		var builder = Model.Builder;
		builder.AddBody( 1 ).AddSphere( new Sphere( Vector3.Zero, 1 ) );
		builder.AddBody( 1 ).AddSphere( new Sphere( Vector3.Zero, 1 ) );
		switch ( kind )
		{
			case 0: builder.AddHingeJoint( 0, 1 ).WithTargetVelocity( Vector3.Up * -2 ).WithMaxTorque( 100000 ); break;
			case 1: builder.AddHingeJoint( 0, 1 ).WithTargetAngle( 30 ).WithFrequency( 5 ).WithDampingRatio( 1 ); break;
			case 2: builder.AddSliderJoint( 0, 1 ).WithTargetVelocity( Vector3.Up * -100 ).WithMaxForce( 100000 ); break;
			case 3: builder.AddSliderJoint( 0, 1 ).WithTargetPosition( -12 ).WithFrequency( 5 ).WithDampingRatio( 1 ); break;
			case 4: builder.AddBallJoint( 0, 1 ).WithTargetVelocity( Vector3.Up * -2 ).WithMaxTorque( 100000 ); break;
			case 5: builder.AddBallJoint( 0, 1 ).WithTargetRotation( Rotation.FromYaw( 30 ) ).WithFrequency( 5 ).WithDampingRatio( 1 ); break;
			case 6: builder.AddFixedJoint( 0, 1 ).WithLinearFrequency( 10 ).WithLinearDamping( 0.7f ).WithAngularFrequency( 20 ).WithAngularDamping( 0.8f ); break;
			case 7: builder.AddBallJoint( 0, 1 ).WithSwingLimit( 20 ).WithTwistLimit( 0, 0 ).WithFriction( 0 ); break;
		}
		var model = builder.Create();
		var world = new PhysicsWorld();
		try
		{
			world.Gravity = Vector3.Zero;
			world.SleepingEnabled = false;
			var group = world.SetupPhysicsFromModel( model, PhysicsMotionType.Dynamic );
			Assert.IsNotNull( group );
			Assert.AreEqual( 1, group.native.GetJointCount() );
			group.GetBody( 0 ).BodyType = PhysicsBodyType.Static;
			var body = group.GetBody( 1 );
			var joint = group.native.GetJointHandle( 0 );
			if ( kind == 7 ) body.AngularVelocity = new Vector3( 2, 3, 4 );
			for ( var i = 0; i < 240; i++ ) world.Step( 1f / 120 );
			switch ( kind )
			{
				case 0:
				case 4: Assert.AreEqual( -2, body.AngularVelocity.z, 0.01f ); break;
				case 1: Assert.AreEqual( 30, joint.Angle.RadianToDegree(), 0.5f ); break;
				case 2: Assert.AreEqual( -100, body.Velocity.z, 0.5f ); break;
				case 3: Assert.AreEqual( -12, body.Transform.Position.z, 0.5f ); break;
				case 5: Assert.IsTrue( body.Transform.Rotation.Distance( Rotation.FromYaw( 30 ) ) < 0.5f ); break;
				case 6:
					var linear = joint.native.GetLinearSpring();
					var angular = joint.native.GetAngularSpring();
					Assert.AreEqual( 10, linear.x );
					Assert.AreEqual( 0.7f, linear.y );
					Assert.AreEqual( 20, angular.x );
					Assert.AreEqual( 0.8f, angular.y );
					break;
				case 7:
					var rotation = body.Transform.Rotation;
					Assert.IsTrue( rotation.Up.Dot( Vector3.Up ) >= MathF.Cos( 21f.DegreeToRadian() ) );
					Assert.IsTrue( MathF.Abs( rotation.z ) < 0.01f );
					break;
			}
		}
		finally
		{
			world.Delete();
		}
	}

	[TestMethod]
	public void JointBuilderMotorModesPreserveDefaultsAndSwitchExplicitly()
	{
		var builder = Model.Builder;
		var hinge = builder.AddHingeJoint( 0, 1 );
		var ball = builder.AddBallJoint( 0, 1 );
		var slider = builder.AddSliderJoint( 0, 1 );
		Assert.AreEqual( HingeJoint.MotorMode.Disabled, hinge.Motor );
		Assert.AreEqual( BallJoint.MotorMode.Disabled, ball.Motor );
		Assert.AreEqual( SliderJoint.MotorMode.Disabled, slider.Motor );
		Assert.AreEqual( 0, hinge.Friction );
		Assert.AreEqual( 0.5f, ball.Friction );
		Assert.AreEqual( 0, slider.Friction );
		Assert.AreEqual( Rotation.Identity, ball.TargetRotation );
		Assert.AreEqual( 1, hinge.Frequency );
		Assert.AreEqual( 1, ball.DampingRatio );
		Assert.AreEqual( 1, slider.Frequency );

		hinge.WithFrequency( 7 ).WithDampingRatio( 0.2f ).WithTargetAngle( 40 );
		ball.WithFrequency( 8 ).WithDampingRatio( 0.4f ).WithTargetRotation( Rotation.Identity );
		slider.WithFrequency( 9 ).WithDampingRatio( 0.6f ).WithTargetPosition( -12 );
		hinge.EnableMotor = ball.EnableMotor = slider.EnableMotor = false;
		hinge.EnableMotor = ball.EnableMotor = slider.EnableMotor = true;
		Assert.AreEqual( HingeJoint.MotorMode.TargetAngle, hinge.Motor );
		Assert.AreEqual( BallJoint.MotorMode.TargetRotation, ball.Motor );
		Assert.AreEqual( SliderJoint.MotorMode.TargetPosition, slider.Motor );
		Assert.AreEqual( 7, hinge.Frequency );
		Assert.AreEqual( 0.4f, ball.DampingRatio );
		Assert.AreEqual( 9, slider.Frequency );

		hinge.WithTargetAngle( 40 ).WithTargetVelocity( Vector3.Up );
		ball.WithTargetRotation( Rotation.FromYaw( 30 ) ).WithTargetVelocity( Vector3.Up );
		slider.WithTargetPosition( -12 ).WithTargetVelocity( Vector3.Up );
		Assert.AreEqual( HingeJoint.MotorMode.TargetVelocity, hinge.Motor );
		Assert.AreEqual( BallJoint.MotorMode.TargetVelocity, ball.Motor );
		Assert.AreEqual( SliderJoint.MotorMode.TargetVelocity, slider.Motor );

		hinge.WithTargetAngle( 40 ).WithFriction( 0.2f );
		ball.WithTargetRotation( Rotation.FromYaw( 30 ) ).WithFriction( 0 );
		slider.WithTargetPosition( -12 ).WithFriction( 0.3f );
		Assert.IsFalse( hinge.EnableMotor || ball.EnableMotor || slider.EnableMotor );
		hinge.EnableMotor = ball.EnableMotor = slider.EnableMotor = true;
		Assert.AreEqual( HingeJoint.MotorMode.TargetVelocity, hinge.Motor );
		Assert.AreEqual( BallJoint.MotorMode.TargetVelocity, ball.Motor );
		Assert.AreEqual( SliderJoint.MotorMode.TargetVelocity, slider.Motor );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => hinge.Motor = (HingeJoint.MotorMode)99 );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => ball.Motor = (BallJoint.MotorMode)99 );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => slider.Motor = (SliderJoint.MotorMode)99 );
	}

	[DataTestMethod]
	[DataRow( 1f, 0 )]
	[DataRow( 1f, 1 )]
	[DataRow( 1f, 2 )]
	[DataRow( 2f, 0 )]
	[DataRow( 2f, 1 )]
	[DataRow( 2f, 2 )]
	public void AllJointBuilderSettingsReachResourcesAndComponents( float scale, int mode )
	{
		var builder = Model.Builder;
		builder.AddBone( "reference", Vector3.Zero, Rotation.Identity );
		builder.AddBone( "attached", Vector3.Up * 8, Rotation.Identity, "reference" );
		builder.AddBody( 1, boneName: "reference" ).AddSphere( new Sphere( Vector3.Zero, 1 ) );
		builder.AddBody( 1, boneName: "attached" ).SetBindPose( new Transform( Vector3.Up * 8 ) ).AddSphere( new Sphere( Vector3.Zero, 1 ) );
		var basis = Rotation.From( 15, 25, 35 );
		var frame1 = new Transform( Vector3.Up * 8, basis );
		var frame2 = new Transform( Vector3.Zero, basis );
		var hinge = builder.AddHingeJoint( 0, 1, frame1, frame2, true )
			.WithTwistLimit( 0, 0 ).WithTargetAngle( 360 ).WithFrequency( 7 ).WithDampingRatio( 0.2f ).WithMaxTorque( 11 );
		hinge.TargetVelocity = basis.Up * -2 + basis.Forward * 3;
		hinge.Friction = 0.3f;
		hinge.Motor = (HingeJoint.MotorMode)mode;
		var rotation = Rotation.From( 10, 20, 30 );
		var ball = builder.AddBallJoint( 0, 1, frame1, frame2, true )
			.WithSwingLimit( 35 ).WithTwistLimit( -20, 40 )
			.WithTargetRotation( rotation ).WithFrequency( 8 ).WithDampingRatio( 0.4f ).WithMaxTorque( 12 );
		ball.TargetVelocity = new Vector3( 1, 2, 3 );
		ball.Friction = 0;
		ball.Motor = (BallJoint.MotorMode)mode;
		var slider = builder.AddSliderJoint( 0, 1, frame1, frame2, true )
			.WithLimit( 0, 0 ).WithTargetPosition( -12 ).WithFrequency( 9 ).WithDampingRatio( 0.6f ).WithMaxForce( 13 );
		slider.TargetVelocity = basis.Up * -100 + basis.Forward * 20;
		slider.Friction = 0.5f;
		slider.Motor = (SliderJoint.MotorMode)mode;
		var weld = builder.AddFixedJoint( 0, 1, frame1, frame2, true )
			.WithLinearFrequency( 10 ).WithLinearDamping( 0.7f )
			.WithAngularFrequency( 20 ).WithAngularDamping( 0.8f );
		foreach ( var joint in new PhysicsJointBuilder[] { hinge, ball, slider, weld } )
		{
			joint.WithLinearStrength( 123 ).WithAngularStrength( 456 );
		}

		var model = builder.Create();
		var data = model.Physics.Joints.ToArray();
		Assert.AreEqual( 4, data.Length );
		foreach ( var joint in data )
		{
			Assert.AreEqual( frame1, joint.Frame1 );
			Assert.AreEqual( frame2, joint.Frame2 );
			Assert.IsTrue( joint.EnableCollision );
			Assert.AreEqual( 123, joint.LinearStrength );
			Assert.AreEqual( 456, joint.AngularStrength );
		}
		Assert.IsTrue( data[0].EnableTwistLimit );
		Assert.AreEqual( 0, data[0].TwistMin );
		Assert.AreEqual( 0, data[0].TwistMax );
		Assert.AreEqual( 360, data[0].AngularTargetAngle, 0.001f );
		Assert.AreEqual( mode != 0, data[0].EnableAngularMotor );
		Assert.AreEqual( mode == 1, data[0].AngularMotorIsSpring );
		Assert.AreEqual( 7, data[0].AngularFrequency );
		Assert.AreEqual( 0.2f, data[0].AngularDampingRatio );
		Assert.AreEqual( hinge.TargetVelocity, data[0].AngularTargetVelocity );
		Assert.IsTrue( data[0].Friction.HasValue );
		Assert.AreEqual( 0.3f, data[0].Friction );
		Assert.IsTrue( data[1].EnableSwingLimit && data[1].EnableTwistLimit );
		Assert.AreEqual( rotation, data[1].AngularTargetRotation );
		Assert.AreEqual( mode != 0, data[1].EnableAngularMotor );
		Assert.AreEqual( mode == 1, data[1].AngularMotorIsSpring );
		Assert.AreEqual( 8, data[1].AngularFrequency );
		Assert.AreEqual( 0.4f, data[1].AngularDampingRatio );
		Assert.IsTrue( data[1].Friction.HasValue );
		Assert.AreEqual( 0, data[1].Friction );
		Assert.IsTrue( data[2].EnableLinearLimit );
		Assert.AreEqual( 0, data[2].LinearMin );
		Assert.AreEqual( 0, data[2].LinearMax );
		Assert.AreEqual( -12, data[2].LinearTargetPosition );
		Assert.AreEqual( mode != 0, data[2].EnableLinearMotor );
		Assert.AreEqual( mode == 1, data[2].LinearMotorIsSpring );
		Assert.AreEqual( slider.TargetVelocity, data[2].LinearTargetVelocity );
		Assert.AreEqual( 9, data[2].LinearFrequency );
		Assert.AreEqual( 0.6f, data[2].LinearDampingRatio );
		Assert.AreEqual( 13, data[2].MaxForce );

		var scene = new Scene();
		try
		{
			using var scope = scene.Push();
			var go = scene.CreateObject();
			go.WorldScale = Vector3.One * scale;
			var physics = go.AddComponent<ModelPhysics>( false );
			physics.Model = model;
			physics.Enabled = true;
			var components = physics.Joints.Select( x => x.Component ).ToArray();
			var hingeComponent = (HingeJoint)components[0];
			var ballComponent = (BallJoint)components[1];
			var sliderComponent = (SliderJoint)components[2];
			var fixedComponent = (FixedJoint)components[3];
			Assert.AreEqual( true, hingeComponent.LimitEnabled );
			Assert.AreEqual( true, sliderComponent.LimitEnabled );
			Assert.AreEqual( (HingeJoint.MotorMode)mode, hingeComponent.Motor );
			Assert.AreEqual( (BallJoint.MotorMode)mode, ballComponent.Motor );
			Assert.AreEqual( (SliderJoint.MotorMode)mode, sliderComponent.Motor );
			Assert.AreEqual( 0.3f, hingeComponent.Friction );
			Assert.AreEqual( 0, ballComponent.Friction );
			Assert.AreEqual( 0.5f, sliderComponent.Friction );
			Assert.IsTrue( sliderComponent.LocalFrame1.Rotation.Forward.Distance( basis.Up ) < 0.001f );
			Assert.IsTrue( sliderComponent.LocalFrame2.Rotation.Forward.Distance( basis.Up ) < 0.001f );
			if ( mode == 1 )
			{
				Assert.AreEqual( 360, hingeComponent.TargetAngle, 0.001f );
				Assert.AreEqual( 7, hingeComponent.Frequency );
				Assert.AreEqual( 0.2f, hingeComponent.DampingRatio );
				Assert.AreEqual( rotation, ballComponent.TargetRotation );
				Assert.AreEqual( 8, ballComponent.Frequency );
				Assert.AreEqual( 0.4f, ballComponent.DampingRatio );
				Assert.AreEqual( -12, sliderComponent.TargetPosition );
				Assert.AreEqual( 9, sliderComponent.Frequency );
				Assert.AreEqual( 0.6f, sliderComponent.DampingRatio );
			}
			if ( mode == 2 )
			{
				Assert.AreEqual( (-2f).RadianToDegree(), hingeComponent.TargetVelocity, 0.001f );
				Assert.AreEqual( new Vector3( 1, 2, 3 ), ballComponent.TargetVelocity );
				Assert.AreEqual( -100, sliderComponent.TargetVelocity, 0.001f );
			}
			Assert.AreEqual( mode != 0 ? 11 : 0, hingeComponent.MaxTorque );
			Assert.AreEqual( mode != 0 ? 12 : 0, ballComponent.MaxTorque );
			Assert.AreEqual( mode != 0 ? 13 : 0, sliderComponent.MaxForce );
			Assert.AreEqual( 10, fixedComponent.LinearFrequency );
			Assert.AreEqual( 0.7f, fixedComponent.LinearDamping );
			Assert.AreEqual( 20, fixedComponent.AngularFrequency );
			Assert.AreEqual( 0.8f, fixedComponent.AngularDamping );
			foreach ( var component in components )
			{
				Assert.AreEqual( 123, component.BreakForce );
				Assert.AreEqual( 456, component.BreakTorque );
				Assert.IsTrue( component.EnableCollision );
			}
		}
		finally
		{
			scene.Destroy();
		}
	}

	[DataTestMethod]
	[DataRow( 0f, 0f, 1f )]
	[DataRow( 0.01f, 1.5f, 10f )]
	[DataRow( 0.01f, 1.5f, 0.5f )]
	public void ModelPhysicsAppliesBodyDampingAndInertia( float linear, float angular, float scale )
	{
		var builder = Model.Builder;
		builder.AddBone( "body", Vector3.Zero, Rotation.Identity );
		var body = builder.AddBody( 1, boneName: "body" ).AddSphere( new Sphere( Vector3.Zero, 1 ) );
		if ( linear != 0 || angular != 0 || scale != 1 )
			body.SetDamping( linear, angular ).SetInertiaScale( scale );
		var model = builder.Create();
		var part = model.Physics.Parts.Single();
		Assert.AreEqual( linear, part.LinearDamping );
		Assert.AreEqual( angular, part.AngularDamping );
		Assert.AreEqual( scale, part.InertiaScale );
		var scene = new Scene();
		try
		{
			using var scope = scene.Push();
			var physics = scene.CreateObject().AddComponent<ModelPhysics>( false );
			physics.Model = model;
			physics.Enabled = true;
			scene.GameTick();
			var rigidbody = physics.Bodies.Single().Component;
			Assert.AreEqual( linear, rigidbody.LinearDamping );
			Assert.AreEqual( angular, rigidbody.AngularDamping );
			Assert.AreEqual( scale, rigidbody.InertiaScale );
			Assert.AreEqual( linear, rigidbody.PhysicsBody.LinearDamping );
			Assert.AreEqual( angular, rigidbody.PhysicsBody.AngularDamping );
			Assert.IsTrue( rigidbody.PhysicsBody.Inertia.Distance( Vector3.One * (0.4f * scale) ) < 0.001f );
			rigidbody.MassOverride = 2;
			Assert.IsTrue( rigidbody.PhysicsBody.Inertia.Distance( Vector3.One * (0.8f * scale) ) < 0.001f );
			rigidbody.Enabled = false;
			rigidbody.Enabled = true;
			scene.GameTick();
			Assert.IsTrue( rigidbody.PhysicsBody.Inertia.Distance( Vector3.One * (0.8f * scale) ) < 0.001f );
		}
		finally
		{
			scene.Destroy();
		}
	}

	[TestMethod]
	public void ClassicBallJointLimitsRemainTheDefault()
	{
		var builder = Model.Builder;
		builder.AddBody( 1 ).AddSphere( new Sphere( Vector3.Zero, 1 ) );
		builder.AddBody( 1 ).AddSphere( new Sphere( Vector3.Zero, 1 ) );
		var target = builder.AddBallJoint( 0, 1 ).WithSwingLimit( 35 ).WithTwistLimit( -10, 20 );
		Assert.IsFalse( target.EnableMotor );
		var joint = builder.Create().Physics.Joints.Single();
		Assert.AreEqual( PhysicsGroupDescription.JointType.Ball, joint.Type );
		Assert.IsFalse( joint.EnableAngularMotor );
		Assert.IsFalse( joint.AngularMotorIsSpring );
		Assert.IsNull( joint.Friction );
		Assert.IsTrue( joint.EnableSwingLimit && joint.EnableTwistLimit );
		Assert.AreEqual( 35, joint.SwingMax, 0.001f );
		Assert.AreEqual( -10, joint.TwistMin, 0.001f );
		Assert.AreEqual( 20, joint.TwistMax, 0.001f );
	}

	[TestMethod]
	public void BallJointLimitsAndMotorRoundTrip()
	{
		var builder = Model.Builder;
		builder.AddBody( 1 ).AddSphere( new Sphere( Vector3.Zero, 1 ) );
		builder.AddBody( 2 ).AddSphere( new Sphere( Vector3.Zero, 2 ) );
		var frame1 = new Transform( new Vector3( 1, 2, 3 ), Rotation.FromAxis( Vector3.Up, 20 ) );
		var frame2 = new Transform( new Vector3( 4, 5, 6 ), Rotation.FromAxis( Vector3.Left, 30 ) );
		builder.AddBallJoint( 0, 1, frame1, frame2, true )
			.WithSwingLimit( 35 ).WithTwistLimit( -10, 15 )
			.WithTargetVelocity( new( 1, 2, 3 ) ).WithMaxTorque( 6 );
		var joint = builder.Create().Physics.Joints.Single();
		Assert.AreEqual( PhysicsGroupDescription.JointType.Ball, joint.Type );
		Assert.AreEqual( 0, joint.Body1 );
		Assert.AreEqual( 1, joint.Body2 );
		Assert.AreEqual( frame1, joint.Frame1 );
		Assert.AreEqual( frame2, joint.Frame2 );
		Assert.IsTrue( joint.EnableCollision );
		Assert.IsTrue( joint.EnableSwingLimit && joint.EnableTwistLimit && joint.EnableAngularMotor );
		Assert.AreEqual( 35, joint.SwingMax, 0.001f );
		Assert.AreEqual( -10, joint.TwistMin, 0.001f );
		Assert.AreEqual( 15, joint.TwistMax, 0.001f );
		Assert.AreEqual( new Vector3( 1, 2, 3 ), joint.AngularTargetVelocity );
		Assert.AreEqual( 6, joint.MaxTorque, 0.001f );
	}

	[DataTestMethod]
	[DataRow( 1.0f, false )]
	[DataRow( 2.0f, false )]
	[DataRow( 1.0f, true )]
	[DataRow( 2.0f, true )]
	public void ModelPhysicsCreatesStandardBallJointComponents( float scale, bool motor )
	{
		var builder = Model.Builder;
		builder.AddBone( "reference", Vector3.Zero, Rotation.Identity );
		builder.AddBone( "attached", Vector3.Up * 8, Rotation.Identity, "reference" );
		builder.AddBody( 1, boneName: "reference" ).AddSphere( new Sphere( Vector3.Zero, 1 ) );
		builder.AddBody( 1, boneName: "attached" ).SetBindPose( new Transform( Vector3.Up * 8 ) ).AddSphere( new Sphere( Vector3.Zero, 1 ) );
		var basis = Rotation.FromAxis( Vector3.Forward, -90 );
		var target = builder.AddBallJoint( 0, 1, new Transform( Vector3.Up * 8, basis ), new Transform( Vector3.Zero, basis ) )
			.WithSwingLimit( 35 ).WithTwistLimit( -20, 40 );
		if ( motor )
			target.WithTargetVelocity( Vector3.Zero ).WithMaxTorque( 3 );
		var scene = new Scene();
		try
		{
			using var scope = scene.Push();
			var go = scene.CreateObject();
			go.WorldScale = Vector3.One * scale;
			var physics = go.AddComponent<ModelPhysics>( false );
			physics.Model = builder.Create();
			physics.Enabled = true;
			Assert.AreEqual( 2, physics.Bodies.Count );
			var joint = physics.Joints.Single().Component as Sandbox.BallJoint;
			Assert.IsNotNull( joint );
			Assert.IsTrue( joint.SwingLimitEnabled && joint.TwistLimitEnabled );
			Assert.AreEqual( 35, joint.SwingLimit.y, 0.001f );
			Assert.AreEqual( -20, joint.TwistLimit.x, 0.001f );
			Assert.AreEqual( 40, joint.TwistLimit.y, 0.001f );
			Assert.AreEqual( motor ? Sandbox.BallJoint.MotorMode.TargetVelocity : Sandbox.BallJoint.MotorMode.Disabled, joint.Motor );
			Assert.AreEqual( Vector3.Zero, joint.TargetVelocity );
			Assert.AreEqual( motor ? 3 : 0, joint.MaxTorque, 0.001f );
			Assert.AreEqual( 0.5f, joint.Friction );
			scene.GameTick();
			Assert.IsNotNull( joint.Body1 );
			Assert.IsNotNull( joint.Body2 );
			Assert.IsTrue( joint.Point1.LocalPosition.Distance( joint.LocalFrame1.Position ) < 0.001f );
			Assert.IsTrue( joint.Point2.LocalPosition.Distance( joint.LocalFrame2.Position ) < 0.001f );
			Assert.IsTrue( joint.Point1.LocalRotation.Distance( joint.LocalFrame1.Rotation ) < 0.001f );
			Assert.IsTrue( joint.Point2.LocalRotation.Distance( joint.LocalFrame2.Rotation ) < 0.001f );
		}
		finally
		{
			scene.Destroy();
		}
	}

	[TestMethod]
	public void GameDataDrivesPropSettingsAndBreakPieces()
	{
		var model = Model.Builder
			.AddMesh( CreateBoundsMesh() )
			.WithData( new ModelPropData
			{
				Health = 50,
				Flammable = true,
				Explosive = true,
				ExplosionDamage = 80,
				ExplosionRadius = 256,
				ExplosionForce = 2,
				ImpactDamage = 10,
				MinImpactDamageSpeed = 200
			} )
			.WithData<ModelBreakPiece[]>( [
				new()
				{
					PieceName = "piece",
					Model = "mount://test/models/piece.vmdl",
					Offset = new Vector3( 4, 8, 16 ),
					FadeTime = 20,
					CollisionTags = "debris",
					IsClientOnly = true,
					PlacementBone = "root"
				}
			] )
			.Create();

		Assert.IsTrue( model.HasData<ModelPropData>() );
		Assert.AreEqual( 50f, model.Data.Health );
		Assert.IsTrue( model.Data.Flammable );
		Assert.IsTrue( model.Data.Explosive );
		Assert.AreEqual( 80f, model.Data.ExplosionDamage );
		Assert.AreEqual( 256f, model.Data.ExplosionRadius );
		Assert.AreEqual( 2f, model.Data.ExplosionForce );
		Assert.AreEqual( 10f, model.Data.ImpactDamage );
		Assert.AreEqual( 200f, model.Data.MinImpactDamageSpeed );
		Assert.IsTrue( model.HasData<ModelBreakPiece[]>() );
		var piece = model.GetData<ModelBreakPiece[]>().Single();
		Assert.AreEqual( "piece", piece.PieceName );
		Assert.AreEqual( "mount://test/models/piece.vmdl", piece.Model );
		Assert.AreEqual( new Vector3( 4, 8, 16 ), piece.Offset );
		Assert.AreEqual( 20f, piece.FadeTime );
		Assert.AreEqual( "debris", piece.CollisionTags );
		Assert.IsTrue( piece.IsClientOnly );
		Assert.AreEqual( "root", piece.PlacementBone );
	}

	[DataTestMethod]
	[DataRow( true )]
	[DataRow( false )]
	public void GameDataUsesNativeBakeLightingKey( bool bakeLighting )
	{
		var model = Model.Builder
			.AddMesh( CreateBoundsMesh() )
			.WithData( new ModelPropData { BakeLighting = bakeLighting } )
			.Create();

		using var data = JsonDocument.Parse( model.GetJson( "prop_data" ) );
		Assert.AreEqual( bakeLighting, data.RootElement.GetProperty( "bakelighting" ).GetBoolean() );
		Assert.IsFalse( data.RootElement.TryGetProperty( "bakeLighting", out _ ) );
		Assert.AreEqual( bakeLighting, model.GetData<ModelPropData>().BakeLighting );
	}

	[TestMethod]
	public void GameDataReplacementAndBuilderReuseKeepModelsIndependent()
	{
		var data = new ModelPropData { Health = 10 };
		var builder = Model.Builder.AddMesh( CreateBoundsMesh() ).WithData( data );
		data.Health = 20;
		var first = builder.Create();
		builder.WithData( new ModelPropData { Health = 30 } );
		var second = builder.Create();

		Assert.AreEqual( 20f, first.GetData<ModelPropData>().Health );
		Assert.AreEqual( 30f, second.GetData<ModelPropData>().Health );
	}

	[Sandbox.ModelEditor.GameData( "model_builder_test" )]
	public struct BuilderGameData
	{
		public string Label;
		public DayOfWeek Day;
	}

	[TestMethod]
	public void CustomGameDataPreservesFieldsAndEnums()
	{
		var model = Model.Builder
			.AddMesh( CreateBoundsMesh() )
			.WithData( new BuilderGameData { Label = "custom", Day = DayOfWeek.Friday } )
			.Create();

		Assert.IsTrue( model.TryGetData<BuilderGameData>( out var data ) );
		Assert.AreEqual( "custom", data.Label );
		Assert.AreEqual( DayOfWeek.Friday, data.Day );
	}

	[TestMethod]
	public void EmptyGameDataListsRemainPresent()
	{
		var model = Model.Builder.AddMesh( CreateBoundsMesh() ).WithData<ModelBreakPiece[]>( [] ).Create();

		Assert.IsTrue( model.HasData<ModelBreakPiece[]>() );
		Assert.AreEqual( 0, model.GetData<ModelBreakPiece[]>().Length );
	}

	[TestMethod]
	public void GameDataRejectsNullAndInvalidNodeShapes()
	{
		Assert.ThrowsException<ArgumentNullException>( () => Model.Builder.WithData<ModelPropData>( null ) );
		Assert.ThrowsException<ArgumentNullException>( () => Model.Builder.WithData<ModelBreakPiece[]>( null ) );
		Assert.ThrowsException<ArgumentException>( () => Model.Builder.WithData( 42 ) );
		Assert.ThrowsException<ArgumentException>( () => Model.Builder.WithData<ModelPropData[]>( [] ) );
		Assert.ThrowsException<ArgumentException>( () => Model.Builder.WithData( new ModelBreakPiece() ) );
	}

	[DataTestMethod]
	[DataRow( null, "sbox_procedural_model.vmdl" )]
	[DataRow( "", "sbox_procedural_model.vmdl" )]
	[DataRow( "model_builder_named", "model_builder_named.vmdl" )]
	[DataRow( "scenes/world/aggregate_0.vmdl", "scenes/world/aggregate_0.vmdl" )]
	[DataRow( "mounts/goldsrc/model.mdl", "mounts/goldsrc/model.vmdl" )]
	[DataRow( "mounts/quake/model.md5mesh", "mounts/quake/model.vmdl" )]
	[DataRow( "mount://goldsrc/models/barney.mdl.vmdl", "mount_/goldsrc/models/barney.mdl.vmdl" )]
	[DataRow( "mount://quake/id1/models/player.md5mesh.vmdl", "mount_/quake/id1/models/player.md5mesh.vmdl" )]
	[DataRow( "C:\\Models\\Builder\\Named.MDL", "c_/models/builder/named.vmdl" )]
	[DataRow( "vpk:models/named.vmdl", "vpk_models/named.vmdl" )]
	[DataRow( "/", "sbox_procedural_model.vmdl" )]
	[DataRow( "Models\\Builder\\MixedCase.MDL", "models/builder/mixedcase.vmdl" )]
	[DataRow( "/scenes/world/aggregate_0.vmdl_c", "scenes/world/aggregate_0.vmdl" )]
	public void WithNameNamesNativeModel( string name, string nativeName )
	{
		var model = Model.Builder.WithName( name ).AddMesh( CreateBoundsMesh() ).Create();

		Assert.IsNotNull( model );
		Assert.IsFalse( model.IsError );
		Assert.IsTrue( model.IsProcedural );
		Assert.AreEqual( name ?? nativeName, model.Name );
		Assert.AreEqual( Resource.FixPath( name ?? nativeName ), model.ResourcePath );
		Assert.AreEqual( nativeName, model.native.GetModelName() );
		Assert.AreEqual( nativeName, NativeGlue.Resources.GetModelResourceName( model.native ) );
	}

	[TestMethod]
	public void NamedBuildersRemainAnonymous()
	{
		var name = $"model_builder_{Guid.NewGuid():N}.vmdl";
		var first = Model.Builder.WithName( name ).AddMesh( CreateBoundsMesh() ).Create();
		var second = Model.Builder.WithName( name ).AddMesh( CreateBoundsMesh() ).Create();

		Assert.AreNotSame( first, second );
		Assert.AreNotEqual( first.native.GetBindingPtr(), second.native.GetBindingPtr() );
		Assert.AreEqual( Guid.Empty, first.Guid );
		Assert.AreEqual( Guid.Empty, second.Guid );
		Assert.AreEqual( name, first.native.GetModelName() );
		Assert.AreEqual( name, second.native.GetModelName() );
	}

	[StructLayout( LayoutKind.Sequential )]
	private struct BoundsVertex
	{
		[VertexLayout.Position] public Vector3 Position;
		[VertexLayout.BlendIndices] public Color32 BlendIndices;
		[VertexLayout.BlendWeight] public Color32 BlendWeights;
	}

	[TestMethod]
	public void RuntimeBoneBoundsDefaultToBoneOrigin()
	{
		var bounds = new BBox( new Vector3( -100, -60, -10 ), new Vector3( 110, 80, 120 ) );
		var world = new SceneWorld();
		try
		{
			foreach ( var skinned in new[] { false, true } )
			{
				var builder = Model.Builder.AddMesh( CreateBoundsMesh() ).WithViewBounds( bounds );
				if ( skinned )
					builder.AddBone( "root", new Vector3( 0, 0, 16 ), Rotation.Identity );
				var model = builder.Create();
				Assert.AreEqual( bounds, model.RenderBounds );
				var sceneModel = new SceneModel( world, model, Transform.Zero );
				foreach ( var transform in new[]
				{
					Transform.Zero,
					new Transform( new Vector3( 100, -200, 50 ), Rotation.From( 15, 70, 25 ), new Vector3( 2, 0.5f, 1.5f ) )
				} )
				{
					sceneModel.Transform = transform;
					var expected = skinned
						? new BBox( new Vector3( 0, 0, 16 ), new Vector3( 0, 0, 16 ) )
						: bounds;
					sceneModel.UpdateToBindPose();
					AssertSceneBounds( sceneModel, expected.Transform( transform ) );
					sceneModel.Update( 0.016f );
					AssertSceneBounds( sceneModel, expected.Transform( transform ) );
				}
				sceneModel.Delete();
			}
		}
		finally
		{
			world.Delete();
		}
	}

	[TestMethod]
	public void RuntimeBoneBoundsFollowPose()
	{
		var world = new SceneWorld();
		var modelBounds = new BBox( new Vector3( -100, -100, -100 ), new Vector3( 100, 100, 100 ) );
		try
		{
			foreach ( var bounds in new[] { new BBox( new Vector3( -8, -6, -4 ), new Vector3( 12, 10, 16 ) ), default } )
				for ( var api = 0; api < 3; ++api )
				{
					var builder = Model.Builder.AddMesh( CreateBoundsMesh() ).WithViewBounds( modelBounds );
					var bone = new ModelBuilder.Bone( "root", null, Vector3.Zero, Rotation.Identity ) { Bounds = bounds };
					if ( api == 0 ) builder.AddBone( bone );
					else if ( api == 1 ) builder.AddBones( [bone] );
					else builder.AddBone( bone.Name, bone.Position, bone.Rotation, bounds );
					var model = builder.Create();
					Assert.AreEqual( modelBounds, model.RenderBounds );
					var transform = new Transform( new Vector3( 100, -200, 50 ), Rotation.From( 15, 70, 25 ), 2 );
					var sceneModel = new SceneModel( world, model, transform );
					sceneModel.UpdateToBindPose();
					AssertSceneBounds( sceneModel, bounds.Transform( transform ) );
					var pose = new Transform( new Vector3( 200, 20, 30 ), Rotation.From( 20, 40, 60 ) );
					sceneModel.Update( 0.016f, () => sceneModel.SetParentSpaceBone( 0, pose ) );
					AssertSceneBounds( sceneModel, bounds.Transform( transform.ToWorld( pose ) ) );
					sceneModel.Delete();
				}
		}
		finally
		{
			world.Delete();
		}
	}

	[TestMethod]
	public void RuntimeBoneBoundsIncludeUnspecifiedBounds()
	{
		var bounds = new BBox( new Vector3( -100, -60, -10 ), new Vector3( 110, 80, 120 ) );
		var boneBounds = new BBox( new Vector3( -8, -6, -4 ), new Vector3( 12, 10, 16 ) );
		var builder = Model.Builder.AddMesh( CreateBoundsMesh() ).WithViewBounds( bounds );
		builder.AddBone( "bounded", new Vector3( 200, 0, 0 ), Rotation.Identity, boneBounds );
		builder.AddBone( "unspecified", Vector3.Zero, Rotation.Identity );
		var world = new SceneWorld();
		try
		{
			var sceneModel = new SceneModel( world, builder.Create(), Transform.Zero );
			var expected = boneBounds.Translate( new Vector3( 200, 0, 0 ) ).AddPoint( Vector3.Zero );
			sceneModel.UpdateToBindPose();
			AssertSceneBounds( sceneModel, expected );
			sceneModel.Update( 0.016f );
			AssertSceneBounds( sceneModel, expected );
		}
		finally
		{
			world.Delete();
		}
	}

	private static Mesh CreateBoundsMesh()
	{
		var mesh = new Mesh( Material.Load( "materials/default/white.vmat" ) );
		var vertices = new BoundsVertex[]
		{
			new() { Position = new Vector3( -16, -16, 0 ), BlendWeights = new Color32( 255, 0, 0, 0 ) },
			new() { Position = new Vector3( 16, -16, 0 ), BlendWeights = new Color32( 255, 0, 0, 0 ) },
			new() { Position = new Vector3( 0, 16, 32 ), BlendWeights = new Color32( 255, 0, 0, 0 ) }
		};
		mesh.CreateVertexBuffer( vertices.Length, vertices );
		mesh.CreateIndexBuffer( 3, new[] { 0, 1, 2 } );
		mesh.Bounds = new BBox( new Vector3( -16, -16, 0 ), new Vector3( 16, 16, 32 ) );
		return mesh;
	}

	private static void AssertSceneBounds( SceneModel sceneModel, BBox expected )
	{
		var actual = sceneModel.Bounds;
		Assert.AreEqual( expected.Mins.x, actual.Mins.x, 1.01f );
		Assert.AreEqual( expected.Mins.y, actual.Mins.y, 1.01f );
		Assert.AreEqual( expected.Mins.z, actual.Mins.z, 1.01f );
		Assert.AreEqual( expected.Maxs.x, actual.Maxs.x, 1.01f );
		Assert.AreEqual( expected.Maxs.y, actual.Maxs.y, 1.01f );
		Assert.AreEqual( expected.Maxs.z, actual.Maxs.z, 1.01f );
	}

	[TestMethod]
	public void CollisionShapesRoundTrip()
	{
		var model = Model.Builder
			.WithName( "builder_shapes" )
			.WithMass( 250 )
			.AddCollisionSphere( 16, new Vector3( 0, 0, 8 ) )
			.AddCollisionCapsule( new Vector3( 0, 0, -8 ), new Vector3( 0, 0, 8 ), 4 )
			.AddCollisionBox( new Vector3( 8, 8, 8 ) )
			.Create();

		Assert.IsNotNull( model );
		Assert.AreEqual( "builder_shapes", model.Name );

		var physics = model.Physics;
		Assert.IsNotNull( physics );

		var body = physics.Parts.Single();
		Assert.AreEqual( 250f, body.Mass );

		var sphere = body.Spheres.Single().Sphere;
		Assert.AreEqual( 16f, sphere.Radius );
		Assert.AreEqual( new Vector3( 0, 0, 8 ), sphere.Center );

		Assert.AreEqual( 4f, body.Capsules.Single().Capsule.Radius );

		// Boxes are built into convex hulls
		Assert.AreEqual( 1, body.Hulls.Count );
	}

	[TestMethod]
	public void PhysicsBoundsComputedFromCollision()
	{
		var model = Model.Builder
			.AddCollisionBox( new Vector3( 16, 16, 16 ) )
			.Create();

		var bounds = model.PhysicsBounds;
		Assert.AreEqual( -16f, bounds.Mins.x, 0.5f );
		Assert.AreEqual( -16f, bounds.Mins.y, 0.5f );
		Assert.AreEqual( -16f, bounds.Mins.z, 0.5f );
		Assert.AreEqual( 16f, bounds.Maxs.x, 0.5f );
		Assert.AreEqual( 16f, bounds.Maxs.y, 0.5f );
		Assert.AreEqual( 16f, bounds.Maxs.z, 0.5f );
	}

	[TestMethod]
	public void CollisionMeshRoundTrip()
	{
		List<Vector3> vertices = [new( 0, 0, 0 ), new( 32, 0, 0 ), new( 32, 32, 0 ), new( 0, 32, 0 )];
		List<int> indices = [0, 1, 2, 0, 2, 3];

		var model = Model.Builder
			.AddCollisionMesh( vertices, indices )
			.Create();

		var body = model.Physics.Parts.Single();
		Assert.AreEqual( 1, body.Meshes.Count );
	}

	[TestMethod]
	public void BodiesAndJointsRoundTrip()
	{
		var builder = Model.Builder;
		builder.AddBody( 10 ).AddSphere( new Sphere( Vector3.Zero, 8 ) );
		builder.AddBody( 20 ).AddBox( new Vector3( 4, 4, 4 ) );
		builder.AddHingeJoint( 0, 1 );

		var physics = builder.Create().Physics;

		Assert.AreEqual( 2, physics.Parts.Count );
		Assert.AreEqual( 10f, physics.Parts[0].Mass );
		Assert.AreEqual( 8f, physics.Parts[0].Spheres.Single().Sphere.Radius );
		Assert.AreEqual( 20f, physics.Parts[1].Mass );
		Assert.AreEqual( 1, physics.Parts[1].Hulls.Count );

		var joint = physics.Joints.Single();
		Assert.AreEqual( PhysicsGroupDescription.JointType.Hinge, joint.Type );
		Assert.AreEqual( 0, joint.Body1 );
		Assert.AreEqual( 1, joint.Body2 );
	}

	[TestMethod]
	public void HitboxesRoundTrip()
	{
		var builder = Model.Builder;
		builder.AddBone( "head", new Vector3( 0, 0, 64 ), Rotation.Identity );

		var set = builder.AddHitboxSet();
		set.AddBox( "head_box", "head", new BBox( new Vector3( -4, -4, -4 ), new Vector3( 4, 4, 4 ) ) )
			.WithSurface( "flesh" )
			.AddTag( "head" );
		set.AddSphere( "head_sphere", "head", new Sphere( Vector3.Zero, 4 ) );
		set.AddCapsule( "neck", "head", new Capsule( new Vector3( 0, 0, -8 ), Vector3.Zero, 2 ) );

		var hitboxes = builder.Create().HitboxSet.All;
		Assert.AreEqual( 3, hitboxes.Count );

		var box = hitboxes.Single( h => h.Name == "head_box" );
		Assert.IsInstanceOfType<BBox>( box.Shape );
		Assert.AreEqual( "flesh", box.SurfaceName );
		Assert.IsTrue( box.Tags.Has( "head" ) );
		Assert.AreEqual( "head", box.Bone?.Name );

		var sphere = hitboxes.Single( h => h.Name == "head_sphere" );
		Assert.AreEqual( 4f, ((Sphere)sphere.Shape).Radius );

		var capsule = hitboxes.Single( h => h.Name == "neck" );
		Assert.AreEqual( 2f, ((Capsule)capsule.Shape).Radius );
	}

	[TestMethod]
	public void LodLevelOutOfRangeThrows()
	{
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => Model.Builder.WithLodDistance( -1, 100 ) );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => Model.Builder.WithLodDistance( 8, 100 ) );
	}

	[TestMethod]
	public void CollisionMeshValidation()
	{
		List<Vector3> vertices = [new( 0, 0, 0 ), new( 32, 0, 0 ), new( 32, 32, 0 ), new( 0, 32, 0 )];

		// Indices must form triangles
		Assert.ThrowsException<ArgumentException>( () =>
			Model.Builder.AddCollisionMesh( vertices, [0, 1, 2, 3] ) );

		// Indices must be in range
		Assert.ThrowsException<ArgumentOutOfRangeException>( () =>
			Model.Builder.AddCollisionMesh( vertices, [0, 1, 4] ) );

		// Materials are per triangle, not per vertex
		Assert.ThrowsException<ArgumentException>( () =>
			Model.Builder.AddCollisionMesh( vertices, [0, 1, 2, 0, 2, 3], [0, 0, 0, 0] ) );
	}
}
