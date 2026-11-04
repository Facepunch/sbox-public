using System;

namespace SceneTests.Components;

public partial class PlayerControllerTest
{
	public sealed class DescendingStepPlatform : Component, IScenePhysicsEvents
	{
		public Rigidbody Body { get; set; }
		public bool Running { get; set; }
		public float Thickness { get; set; }
		public float Top { get; private set; } = 64;

		void IScenePhysicsEvents.PrePhysicsStep()
		{
			if ( !Running ) return;
			Top = MathF.Max( 8, Top - 20 * Time.Delta );
			var position = WorldPosition.WithZ( Top - Thickness * 0.5f );
			if ( Body.IsValid() ) Body.PhysicsBody.Move( new Transform( position, WorldRotation ), Time.Delta );
			else WorldPosition = position;
		}
	}

	[TestMethod]
	[DataRow( true, 2f, 110f, 0f, true )]
	[DataRow( false, 2f, 110f, 0f, true )]
	[DataRow( true, 2f, 320f, 0f, true )]
	[DataRow( false, 2f, 320f, 0f, true )]
	[DataRow( true, 16f, 110f, 0f, true )]
	[DataRow( false, 16f, 110f, 0f, true )]
	[DataRow( true, 2f, 110f, 25f, true )]
	[DataRow( false, 2f, 110f, 25f, true )]
	[DataRow( true, 2f, 110f, 0f, false )]
	[DataRow( false, 2f, 110f, 0f, false )]
	[DataRow( true, 2f, 320f, 0f, false )]
	[DataRow( false, 2f, 320f, 0f, false )]
	[DataRow( true, 16f, 110f, 0f, false )]
	[DataRow( false, 16f, 110f, 0f, false )]
	[DataRow( true, 2f, 110f, 25f, false )]
	[DataRow( false, 2f, 110f, 25f, false )]
	public void CanStepOntoDescendingPlatformAfterWaitingAgainstItsSide( bool approachWhileHigh, float thickness, float speed, float yaw, bool rigidbody )
	{
		var scene = new Scene();
		using var scope = scene.Push();
		CreateFloor( scene );
		var rotation = Rotation.FromYaw( yaw );
		var platform = scene.CreateObject();
		platform.WorldPosition = rotation * new Vector3( 152, 0, 64 - thickness * 0.5f );
		platform.WorldRotation = rotation;
		Rigidbody body = null;
		if ( rigidbody )
		{
			body = platform.Components.Create<Rigidbody>();
			body.MotionEnabled = false;
		}
		var collider = platform.Components.Create<BoxCollider>();
		collider.Scale = new Vector3( 208, 128, thickness );
		collider.Friction = 0.8f;
		var motion = platform.Components.Create<DescendingStepPlatform>();
		motion.Body = body;
		motion.Thickness = thickness;
		var pc = CreatePlayer( scene, Vector3.Up );
		TickUntilGrounded( scene, pc );
		Assert.IsTrue( pc.IsOnGround );
		Assert.AreEqual( PhysicsBodyType.Keyframed, collider.PhysicsBody.BodyType );
		motion.Running = true;
		var contactedWhileHigh = false;
		for ( int tick = 0; tick < 250 && pc.WorldPosition.Dot( rotation.Forward ) < 96; ++tick )
		{
			pc.WishVelocity = approachWhileHigh || motion.Top < 24 ? rotation.Forward * speed : Vector3.Zero;
			scene.GameTick( 0.02 );
			contactedWhileHigh |= motion.Top > pc.BodyHeight * 0.5f && pc.Body.PhysicsBody.IsTouching( collider.PhysicsBody, false );
			if ( motion.Top > 18.5f ) Assert.IsTrue( pc.WorldPosition.z < 2, $"Climbed a platform above step height: {pc.WorldPosition}" );
		}
		Assert.AreEqual( approachWhileHigh, contactedWhileHigh, "The early approach must contact the platform above the player's midpoint" );
		var probe = pc.TraceMovement( pc.WorldPosition, pc.WorldPosition + rotation.Forward * 2, includeGroundClearance: true );
		Assert.IsTrue( pc.WorldPosition.Dot( rotation.Forward ) >= 96,
			$"Blocked after descent: position {pc.WorldPosition}, velocity {pc.Body.Velocity}, top {motion.Top}, probe {probe.Hit}/{probe.StartedSolid}/{probe.Normal}, {DescribeGround( pc )}" );
		Assert.IsTrue( pc.IsOnGround );
		Assert.AreEqual( platform, pc.GroundObject );
		Assert.AreEqual( motion.Top, pc.WorldPosition.z, 1f );
	}
}
