namespace Sandbox;

/// <summary>
/// This system exists to collect pending collision sounds and filter them into a unique set, to avoid
/// unnecessary sounds playing, when they're going to be making the same sound anyway.
/// </summary>
[Expose]
public sealed class CollisionSoundSystem : GameObjectSystem<CollisionSoundSystem>, ISceneCollisionEvents
{
	record struct PendingSound( Surface Surface, Vector3 Position, float Speed, bool Networked );

	private readonly List<PendingSound> Pending = [];

	public CollisionSoundSystem( Scene scene ) : base( scene )
	{
		Listen( Stage.FinishUpdate, 100, ProcessQueue, "CollisionSoundSystem Queue" );
	}

	/// <summary>
	/// Register this physics collision with the sound system
	/// </summary>
	public void RegisterCollision( in Collision collision )
	{
		var self = collision.Self;
		var other = collision.Other;

		if ( !self.Body.EnableCollisionSounds ) return;
		if ( !other.Body.EnableCollisionSounds ) return;

		// Assume networked if colliding with an object replicated to clients
		var networkRoot = other.GameObject?.NetworkRoot;
		var networked = networkRoot is { NetworkMode: NetworkMode.Object } && networkRoot.Network?.IsProxy == false;

		AddShapeCollision( other.Shape, other.Surface, collision.Contact, networked );
		AddShapeCollision( self.Shape, self.Surface, collision.Contact, networked );
	}

	void ISceneCollisionEvents.OnCollisionHit( Collision collision )
	{
		RegisterCollision( collision );
	}

	/// <summary>
	/// Add a collision sound for this shape
	/// </summary>
	public void AddShapeCollision( PhysicsShape shape, Surface surface, in Vector3 position, float speed, bool networked )
	{
		if ( !shape.IsValid() ) return;
		if ( !shape.Body.IsValid() ) return;
		if ( !shape.Body.EnableCollisionSounds ) return;
		if ( speed < 100.0f ) return;
		if ( surface is null ) return;

		// If we have more than 4, remove any that are slower/less significant
		if ( Pending.Count > 4 && Pending.RemoveAll( x => x.Speed < speed ) == 0 )
			return;

		// check for redundancies
		for ( int i = 0; i < Pending.Count; i++ )
		{
			if ( Pending[i].Surface.Index != surface.Index ) continue;
			if ( Pending[i].Speed >= speed ) return;

			Pending[i] = new PendingSound( surface, position, speed, networked );
			return;
		}

		Pending.Add( new PendingSound( surface, position, speed, networked ) );
	}

	public void AddShapeCollision( PhysicsShape shape, Surface surface, in PhysicsContact contact, bool networked )
	{
		AddShapeCollision( shape, surface, contact.Point, MathF.Abs( contact.NormalSpeed ), networked );
	}

	RealTimeSince lastRan = 0;

	/// <summary>
	/// Create the pending sounds
	/// </summary>
	void ProcessQueue()
	{
		if ( lastRan < 0.05f ) return;
		lastRan = 0;

		foreach ( var pending in Pending )
		{
			if ( pending.Surface is null ) continue;

			if ( pending.Networked )
			{
				PlayCollisionSound( (ushort)pending.Surface.Index, pending.Position, pending.Speed );
			}
			else
			{
				pending.Surface.PlayCollisionSound( pending.Position, pending.Speed );
			}
		}

		Pending.Clear();
	}

	[Rpc.Broadcast( NetFlags.Unreliable )]
	private void PlayCollisionSound( ushort index, Vector3 pos, float speed )
	{
		var surface = Surface.FindByIndex( index );
		if ( surface == null ) return;

		surface.PlayCollisionSound( pos, speed );
	}
}
