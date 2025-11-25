namespace Sandbox;

public abstract partial class Collider
{
	/// <summary>
	/// If this collider is part of a Rigidbody then this will return the component
	/// that it's attached to. If this is null it's usually a good indication that this
	/// collider is either static, world geometry, or a keyframe.
	/// </summary>
	public Rigidbody Rigidbody { get; private set; }

	/// <summary>
	/// Returns either the rigidbody's physics body or the keyframe's physics body.
	/// </summary>
	internal PhysicsBody PhysicsBody => Rigidbody.IsValid() ? Rigidbody.PhysicsBody : KeyBody;

	/// <summary>
	/// Called when a Rigidbody is enabled. It calls this on all downstream colliders. On our part, we look at who our nearest
	/// parent rigidbody is and add ourselves to that.
	/// </summary>
	internal void OnRigidBodyEnabled( Rigidbody rigidbody )
	{
		ChangeBody();
	}

	/// <summary>
	/// An upstream rigidbody has been disabled or destroyed. We look for the nearest parent rigidbody and add ourselves to that.
	/// If one doesn't exist we become a kinematic collider.
	/// </summary>
	internal void OnRigidBodyDisabled( Rigidbody rigidbody )
	{
		ChangeBody();
	}


	void ChangeBody()
	{
		var body = GetComponentInParent<Rigidbody>( false, true );
		bool wantsKeyframe = body is null;
		bool hasKeyframe = _keyframeBody != null;

		// This is already our body - no sweat
		if ( Rigidbody == body && wantsKeyframe == hasKeyframe ) return;

		// remove from the old body
		if ( Rigidbody is not null )
		{
			DisconnectBody();
		}

		Rigidbody = body;

		// add to the new body
		if ( Rigidbody is not null )
		{
			DestroyKeyframe();
			Rigidbody.OnColliderAdded( this );
		}

		// we want to be a keyframe
		if ( Rigidbody is null )
		{
			CreateKeyframeBody();
		}

		RebuildImmediately();
	}

	void DisconnectBody()
	{
		// destroy the shapes first
		DestroyShapes();

		if ( Rigidbody.IsValid() )
		{
			Rigidbody.OnColliderRemoved( this );
		}

		Rigidbody = default;

		DestroyKeyframe();
	}
}
