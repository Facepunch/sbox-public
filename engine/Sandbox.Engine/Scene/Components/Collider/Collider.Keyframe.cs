using System.Collections.Immutable;

namespace Sandbox;

public abstract partial class Collider
{
	protected PhysicsBody _keyframeBody;

	[Obsolete( "We no longer offer a way to get the KeyframeBody. What do you want to do?" )]
	public PhysicsBody KeyframeBody => KeyBody;

	internal PhysicsBody KeyBody => _keyframeBody;

	/// <summary>
	/// If we're a keyframe collider, this is the set of joints attached to us. If we're not then this won't ever
	/// return anything.
	/// </summary>
	public IReadOnlySet<Joint> Joints => KeyBody?.Joints ?? ((IReadOnlySet<Joint>)ImmutableHashSet<Joint>.Empty);

	void DestroyKeyframe()
	{
		if ( Scene?.Is2D == true )
			Scene?.GetSystem<ScenePhysics2dSystem>()?.RemoveKeyframe( this );
		else
			Scene?.GetSystem<ScenePhysicsSystem>()?.RemoveKeyframe( this );

		_keyframeBody?.Remove();
		_keyframeBody = null;
	}

	void CreateKeyframeBody()
	{
		if ( _keyframeBody.IsValid() )
			return;

		var isKeyframed = !Static && !Scene.IsEditor;

		var transform = GetTargetTransform();
		_keyframeBody = Scene.PhysicsWorld.CreateBody( new PhysicsBodySettings
		{
			BodyType = isKeyframed ? PhysicsBodyType.Keyframed : PhysicsBodyType.Static,
			Position = transform.Position,
			Rotation = transform.Rotation
		} );
		_keyframeBody.UseController = isKeyframed;

		_keyframeBody.Component = this;

		// Something else moved or re-enabled the body, put it back on our transform
		_keyframeBody._body.OnDirty = QueueKeyframeUpdate;

		if ( _collisionEvents is not null )
			_collisionEvents.Rebind( _keyframeBody );
		else
			_collisionEvents = new CollisionEventSystem( _keyframeBody );

		if ( isKeyframed )
		{
			if ( Scene.Is2D )
				Scene.GetSystem<ScenePhysics2dSystem>()?.AddKeyframe( this );
			else
				Scene.GetSystem<ScenePhysicsSystem>()?.AddKeyframe( this );
		}
	}

	void TeleportKeyframeBody( Transform transform )
	{
		if ( !_keyframeBody.IsValid() )
			return;

		_keyframeBody.Transform = transform;
		_keyframeBody.Velocity = 0;
		_keyframeBody.AngularVelocity = 0;
	}

	/// <summary>
	/// Called right before physics simulation to move the keyframebody to its new transform. Returns true once the body
	/// rests there, then it doesn't need updating until the transform changes or the body is moved.
	/// </summary>
	internal bool UpdateKeyframeTransform()
	{
		if ( Scene.IsEditor || Static )
			return false;

		if ( !_keyframeBody.IsValid() )
			return false;

		// if timeToArrive is longer than a physics frame delta, the objects that we push
		// will get pushed smoother, but will clip inside the collider more.
		// if it's shorter, the objects will be punched quicker than the collider is moving
		// so will tend to over-react to being touched.
		float timeToArrive = Time.Delta;

		var targetTransform = WorldTransform.WithScale( 1.0f );
		_keyframeBody.Move( targetTransform, timeToArrive );

		// Paused, the move did nothing yet. 2D updates every keyframe collider anyway.
		if ( timeToArrive <= 0 || Scene.Is2D )
			return false;

		// A real move wakes the body, so still asleep means we're on target. A proxy can move us without telling us.
		return Transform.Proxy is null && _keyframeBody.Sleeping;
	}

	/// <summary>
	/// Our target or our body may have moved, update the body before the next physics step.
	/// </summary>
	internal void QueueKeyframeUpdate()
	{
		// Moving colliders ask every step, keep that cheap
		if ( KeyframeQueued )
			return;

		if ( !Scene.Is2D && !Scene.IsEditor && _keyframeBody.IsValid() && !Static )
			Scene.GetSystem<ScenePhysicsSystem>()?.AddKeyframe( this );
	}

	/// <summary>
	/// In the physics system's keyframe update set.
	/// </summary>
	internal bool KeyframeQueued;
}
