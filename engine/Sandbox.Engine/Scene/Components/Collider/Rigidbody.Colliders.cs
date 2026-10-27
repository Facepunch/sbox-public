namespace Sandbox;

sealed public partial class Rigidbody : Component, Component.ExecuteInEditor, IGameObjectNetworkEvents, IScenePhysicsEvents
{
	readonly HashSet<Collider> Colliders = new();

	/// <summary>
	/// Tell all the downstream colliders that we exist. This gives them a chance to re-configure themselves.
	/// This doesn't meant they'll become shapes on our body, it just means they have potential to. They
	/// really just look for their nearest parent that has a Rigidbody on it, and then that becomes their boss.
	/// </summary>
	void BroadcastToColliders()
	{
		foreach ( var c in GetComponentsInChildren<Collider>( false, true ) )
		{
			c.OnRigidBodyEnabled( this );
		}
	}

	/// <summary>
	/// Tell all the colliders we're fucking off. Clear the list.
	/// </summary>
	void FreeColliders()
	{
		foreach ( var c in Colliders )
		{
			c.OnRigidBodyDisabled( this );
		}

		Colliders.Clear();
	}

	/// <summary>
	/// Called by a collider to tell the Rigidbody that it's part of it
	/// </summary>
	internal void OnColliderAdded( Collider collider )
	{
		EnsureBodyCreated();

		// when adding a collider we want to sync to the transform
		// so it won't be offset by a lerp amount in any way
		if ( _body.IsValid() )
		{
			_body.Transform = WorldTransform;
		}

		Colliders.Add( collider );

	}

	/// <summary>
	/// Called by a collider to tell the Rigidbody that it's no longer part of it
	/// </summary>
	internal void OnColliderRemoved( Collider collider )
	{
		Colliders.Remove( collider );
	}
}
