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
		ScenePhysicsSystem.Current?.RemoveKeyframe( this );

		_keyframeBody?.Remove();
		_keyframeBody = null;
	}

	void CreateKeyframeBody()
	{
		if ( _keyframeBody.IsValid() )
			return;

		var isKeyframed = !Static && !Scene.IsEditor;

		_keyframeBody = new PhysicsBody( Scene.PhysicsWorld )
		{
			BodyType = isKeyframed ? PhysicsBodyType.Keyframed : PhysicsBodyType.Static,
			Transform = GetTargetTransform().WithScale( 1.0f ),
			UseController = isKeyframed,
			GravityEnabled = false
		};

		_keyframeBody.Component = this;

		if ( _collisionEvents is not null )
			_collisionEvents.Rebind( _keyframeBody );
		else
			_collisionEvents = new CollisionEventSystem( _keyframeBody );

		if ( isKeyframed )
		{
			ScenePhysicsSystem.Current?.AddKeyframe( this );
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
	/// Called right before physics simulation to move the keyframebody to its new transform.
	/// </summary>
	internal void UpdateKeyframeTransform()
	{
		if ( Scene.IsEditor || Static )
			return;

		if ( !_keyframeBody.IsValid() )
			return;

		// if timeToArrive is longer than a physics frame delta, the objects that we push
		// will get pushed smoother, but will clip inside the collider more.
		// if it's shorter, the objects will be punched quicker than the collider is moving
		// so will tend to over-react to being touched.
		float timeToArrive = Time.Delta;

		var targetTransform = WorldTransform.WithScale( 1.0f );
		_keyframeBody.Move( targetTransform, timeToArrive );
	}
}
