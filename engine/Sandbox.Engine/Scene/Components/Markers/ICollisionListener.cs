namespace Sandbox;

public abstract partial class Component
{
	/// <summary>
	/// A <see cref="Component"/> with this interface can react to collisions.
	/// </summary>
	public interface ICollisionListener
	{
		/// <summary>
		/// Called when this collider/rigidbody starts touching another collider.
		/// </summary>
		void OnCollisionStart( Collision collision ) { }

		/// <summary>
		/// Called once per physics step for every collider being touched, while the collision is awake.
		/// Sleeping contacts don't send updates, use <see cref="Collider.Touching"/> to track resting contacts.
		/// </summary>
		void OnCollisionUpdate( Collision collision ) { }

		/// <summary>
		/// Called when this collider/rigidbody stops touching another collider.
		/// </summary>
		void OnCollisionStop( CollisionStop collision ) { }
	}
}

public readonly record struct Collision( CollisionSource Self, CollisionSource Other, PhysicsContact Contact );
public readonly record struct CollisionStop( CollisionSource Self, CollisionSource Other );

public readonly struct CollisionSource
{
	internal CollisionSource( PhysicsContact.Target target )
	{
		Body = target.Body;
		Shape = target.Shape;
		Surface = target.Surface;
		Collider = target.Shape.Collider;
		Component = Collider;
		GameObject = Collider.IsValid() ? Collider.GameObject : Body.GameObject;
	}

	public bool IsTrigger => Collider.IsValid() && Collider.IsTrigger;

	public readonly PhysicsBody Body;
	public readonly PhysicsShape Shape;
	public readonly Surface Surface;
	public readonly Collider Collider;
	public readonly GameObject GameObject;
	internal readonly Component Component;
}
