using System;

namespace SceneTests.Components;

/// <summary>
/// Keyframe colliders are only updated before the physics step until their body is on target, these pin that
/// they still follow their GameObject and get put back when something moves their body.
/// </summary>
[TestClass]
public class KeyframeColliderTests
{
	const double Dt = 1.0 / 60.0;

	sealed class OffsetProxy : TransformProxyComponent
	{
		public Vector3 Offset;
		global::Transform _local = global::Transform.Zero;

		public override global::Transform GetLocalTransform() => _local.WithPosition( _local.Position + Offset );
		public override void SetLocalTransform( in global::Transform value ) => _local = value;
	}

	static void WithKeyframed( Vector3 position, Action<Scene, BoxCollider> test )
	{
		// One physics step per tick
		var useFixedUpdate = ProjectSettings.Physics.UseFixedUpdate;
		ProjectSettings.Physics.UseFixedUpdate = false;

		try
		{
			var scene = new Scene();
			using var scope = scene.Push();

			var go = scene.CreateObject();
			go.WorldPosition = position;
			var collider = go.Components.Create<BoxCollider>();
			collider.Scale = 10;

			test( scene, collider );
		}
		finally
		{
			ProjectSettings.Physics.UseFixedUpdate = useFixedUpdate;
		}
	}

	static void Tick( Scene scene, int count )
	{
		for ( int i = 0; i < count; i++ ) scene.GameTick( Dt );
	}

	/// <summary>
	/// Tick until the body sleeps on its target, awake bodies report their moves and keep the collider queued.
	/// </summary>
	static void Rest( Scene scene, Collider collider )
	{
		Tick( scene, 60 );
		Assert.IsTrue( collider.KeyBody.Sleeping, "the body should have fallen asleep" );
		Assert.IsFalse( collider.KeyframeQueued, "a collider on its target shouldn't be updated" );
	}

	static void AssertAt( Collider collider, Vector3 position )
	{
		Assert.IsTrue( collider.KeyBody.Position.Distance( position ) < 0.05f, $"body at {collider.KeyBody.Position}, expected {position}" );
	}

	[TestMethod]
	public void RestsAndFollowsMovesFarFromTheOrigin()
	{
		var start = new Vector3( 5000, -3000, 2000 );
		WithKeyframed( start, ( scene, collider ) =>
		{
			Rest( scene, collider );

			collider.WorldPosition = start + new Vector3( 100, 0, 0 );
			Assert.IsTrue( collider.KeyframeQueued, "moving should queue an update" );

			Rest( scene, collider );
			AssertAt( collider, start + new Vector3( 100, 0, 0 ) );
		} );
	}

	[TestMethod]
	public void FollowsParentMoves()
	{
		WithKeyframed( Vector3.Zero, ( scene, collider ) =>
		{
			var parent = scene.CreateObject();
			collider.GameObject.SetParent( parent, false );
			Rest( scene, collider );

			parent.WorldPosition = new Vector3( 0, 50, 0 );
			Assert.IsTrue( collider.KeyframeQueued, "a parent move should queue an update" );

			Rest( scene, collider );
			AssertAt( collider, new Vector3( 0, 50, 0 ) );
		} );
	}

	[TestMethod]
	public void FollowsSmallMoves()
	{
		WithKeyframed( Vector3.Zero, ( scene, collider ) =>
		{
			Rest( scene, collider );

			collider.WorldPosition = new Vector3( 10, 0, 0 );
			Tick( scene, 2 );

			// Smaller than the arrival tolerance while the body is awake, still has to be followed
			collider.WorldPosition = new Vector3( 10.005f, 0, 0 );
			Rest( scene, collider );
			Assert.IsTrue( collider.KeyBody.Position.Distance( new Vector3( 10.005f, 0, 0 ) ) < 0.001f, $"body at {collider.KeyBody.Position}" );
		} );
	}

	[TestMethod]
	public void PutsBackATeleportedBody()
	{
		WithKeyframed( Vector3.Zero, ( scene, collider ) =>
		{
			Rest( scene, collider );

			collider.KeyBody.Position = new Vector3( 0, 300, 0 );
			Tick( scene, 2 );
			AssertAt( collider, Vector3.Zero );
		} );
	}

	[TestMethod]
	public void RestingPlatformStillPushesBodies()
	{
		WithKeyframed( Vector3.Zero, ( scene, collider ) =>
		{
			collider.Scale = 20;
			Rest( scene, collider );

			var box = scene.CreateObject();
			box.WorldPosition = new Vector3( 25, 0, 0 );
			box.Components.Create<BoxCollider>().Scale = 10;
			var rb = box.Components.Create<Rigidbody>();
			rb.Gravity = false;

			for ( int i = 1; i <= 20; i++ )
			{
				collider.WorldPosition = new Vector3( i, 0, 0 );
				Tick( scene, 1 );
			}

			Assert.IsTrue( box.WorldPosition.x > 35, $"the platform should have pushed the box, it's at {box.WorldPosition}" );
		} );
	}

	[TestMethod]
	public void FollowsRotations()
	{
		WithKeyframed( Vector3.Zero, ( scene, collider ) =>
		{
			Rest( scene, collider );

			var rotation = Rotation.From( 0, 170, 0 );
			collider.WorldRotation = rotation;
			Rest( scene, collider );
			Assert.IsTrue( collider.KeyBody.Rotation.Distance( rotation ) < 0.1f, $"body at {collider.KeyBody.Rotation.Angles()}" );
		} );
	}

	[TestMethod]
	public void FollowsMovesMadeWhileAnotherSceneIsActive()
	{
		WithKeyframed( Vector3.Zero, ( scene, collider ) =>
		{
			Rest( scene, collider );

			var other = new Scene();
			using ( other.Push() )
			{
				collider.WorldPosition = new Vector3( 0, 0, 80 );
			}

			Tick( scene, 3 );
			AssertAt( collider, new Vector3( 0, 0, 80 ) );
		} );
	}

	[TestMethod]
	public void FollowsMovesMadeWhileTheBodyIsDisabled()
	{
		WithKeyframed( Vector3.Zero, ( scene, collider ) =>
		{
			Rest( scene, collider );

			collider.KeyBody.Enabled = false;
			collider.WorldPosition = new Vector3( 60, 0, 0 );
			Tick( scene, 3 );

			collider.KeyBody.Enabled = true;
			Tick( scene, 3 );
			AssertAt( collider, new Vector3( 60, 0, 0 ) );
		} );
	}

	[TestMethod]
	public void PutsBackAPushedBody()
	{
		WithKeyframed( Vector3.Zero, ( scene, collider ) =>
		{
			Rest( scene, collider );

			// The pre-step update overwrites it, the body never leaves
			collider.KeyBody.Velocity = new Vector3( 500, 0, 0 );
			for ( int i = 0; i < 5; i++ )
			{
				Tick( scene, 1 );
				AssertAt( collider, Vector3.Zero );
			}
		} );
	}

	[TestMethod]
	public void FollowsMovesMadeWhilePaused()
	{
		WithKeyframed( Vector3.Zero, ( scene, collider ) =>
		{
			Rest( scene, collider );

			scene.TimeScale = 0;
			collider.WorldPosition = new Vector3( 0, 40, 0 );
			Tick( scene, 5 );

			scene.TimeScale = 1;
			Tick( scene, 3 );
			AssertAt( collider, new Vector3( 0, 40, 0 ) );
		} );
	}

	[TestMethod]
	public void FollowsAProxyAddedAfterResting()
	{
		WithKeyframed( Vector3.Zero, ( scene, collider ) =>
		{
			Rest( scene, collider );

			var proxy = collider.GameObject.Components.Create<OffsetProxy>();
			proxy.Offset = new Vector3( 0, 0, 25 );
			Tick( scene, 3 );
			AssertAt( collider, new Vector3( 0, 0, 25 ) );

			// Proxied colliders keep being updated, the proxy can move without a change notification
			proxy.Offset = new Vector3( 0, 0, 40 );
			Tick( scene, 3 );
			AssertAt( collider, new Vector3( 0, 0, 40 ) );

			proxy.Destroy();
			Tick( scene, 3 );
			AssertAt( collider, Vector3.Zero );
		} );
	}
}
