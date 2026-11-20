/// <summary>
/// Only play the collision sound from this object's surface - not both the impacting surface and this surface.
/// </summary>
public sealed class SelfCollisionSound : Component, Component.ICollisionListener
{
	protected override void OnStart()
	{
		// Turn off default collision sounds
		foreach ( var body in GetComponents<Rigidbody>().Select( x => x.PhysicsBody ) )
		{
			body.EnableCollisionSounds = false;
		}
	}
	void ICollisionListener.OnCollisionStart( Sandbox.Collision collision )
	{
		Play( collision.Self.Shape, collision.Self.Surface, collision.Contact.Point, MathF.Abs( collision.Contact.NormalSpeed ) );
	}

	public void Play( PhysicsShape shape, Surface surface, in Vector3 position, float speed )
	{
		if ( !shape.IsValid() ) return;
		if ( !shape.Body.IsValid() ) return;
		if ( speed < 50.0f ) return;
		if ( surface == null ) return;

		surface.PlayCollisionSound( position, speed );
	}
}
