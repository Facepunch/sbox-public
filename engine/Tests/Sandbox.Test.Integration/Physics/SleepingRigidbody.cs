using System;

namespace PhysicsTests;

/// <summary>
/// Sleeping rigidbodies skip their per-step work, these are the changes that must still reach them.
/// </summary>
[TestClass]
public class SleepingRigidbodyTests
{
	const float Dt = 1.0f / 50.0f;

	sealed class OffsetProxy : TransformProxyComponent
	{
		public Transform? LastSet;

		public override Transform GetLocalTransform() => new( new Vector3( 500, 0, 0 ) );
		public override Transform GetWorldTransform() => GetLocalTransform();
		public override void SetWorldTransform( Transform value ) => LastSet = value;
	}

	static void WithSleepingBox( Action<Scene, Rigidbody> test )
	{
		var useFixedUpdate = ProjectSettings.Physics.UseFixedUpdate;
		ProjectSettings.Physics.UseFixedUpdate = false;

		var scene = new Scene();
		using ( scene.Push() )
		{
			try
			{
				var floor = scene.CreateObject();
				floor.WorldPosition = new Vector3( 0, 0, -10 );
				var floorBox = floor.Components.Create<BoxCollider>();
				floorBox.Scale = new Vector3( 1000, 1000, 20 );
				floorBox.Static = true;

				var go = scene.CreateObject();
				go.WorldPosition = new Vector3( 0, 0, 20 );
				var rb = go.Components.Create<Rigidbody>();
				go.Components.Create<BoxCollider>().Scale = new Vector3( 20, 20, 20 );

				for ( int i = 0; i < 600 && !rb.PhysicsBody.Sleeping; i++ )
					scene.GameTick( Dt );

				Assert.IsTrue( rb.PhysicsBody.Sleeping, "the box should fall asleep on the floor" );

				// Let the skip kick in
				for ( int i = 0; i < 5; i++ )
					scene.GameTick( Dt );

				test( scene, rb );
			}
			finally
			{
				ProjectSettings.Physics.UseFixedUpdate = useFixedUpdate;
				scene.Destroy();
			}
		}
	}

	[TestMethod]
	public void TeleportingASleepingBodyMovesItsGameObject()
	{
		WithSleepingBox( ( scene, rb ) =>
		{
			var target = rb.WorldPosition + new Vector3( 100, 0, 0 );
			rb.PhysicsBody.Position = target;
			scene.GameTick( Dt );

			Assert.IsTrue( rb.WorldPosition.Distance( target ) < 1, $"GameObject at {rb.WorldPosition}, body at {target}" );
		} );
	}

	[TestMethod]
	public void WakingVelocityIsSeenBeforeTheStep()
	{
		WithSleepingBox( ( scene, rb ) =>
		{
			rb.PhysicsBody.Velocity = new Vector3( 0, 0, 300 );
			scene.GameTick( Dt );

			Assert.IsTrue( rb.PreVelocity.z > 250, $"pre-step velocity {rb.PreVelocity}" );
		} );
	}

	[TestMethod]
	public void TransformProxyKeepsGettingTheBodyTransform()
	{
		WithSleepingBox( ( scene, rb ) =>
		{
			var proxy = rb.GameObject.Components.Create<OffsetProxy>();
			scene.GameTick( Dt );

			Assert.IsNotNull( proxy.LastSet, "the proxy should receive the body transform" );
			Assert.IsTrue( proxy.LastSet.Value.Position.Distance( rb.PhysicsBody.Position ) < 1 );
		} );
	}
}
